using Compuse.Contracts;

#pragma warning disable CA1068

namespace Compuse.Runtime;

public sealed class OperationRuntime : IAsyncDisposable
{
    private readonly IOperationClock _clock;
    private readonly OperationRuntimeOptions _options;
    private readonly Dictionary<Type, object> _handlers = [];
    private readonly Dictionary<CorrelationId, AdmittedOperation> _inFlight = [];
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _gate = new();
    private bool _stopping;
    private bool _disposed;
    private Task? _stopTask;
    private Task? _disposeTask;

    public OperationRuntime(IOperationClock clock, OperationRuntimeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _options = options ?? new OperationRuntimeOptions();
    }

    public void Register<TRequest>(IOperationHandler<TRequest> handler)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (_disposed || _stopping)
            {
                throw new InvalidOperationException(
                    "Handlers cannot be registered after the runtime has started stopping.");
            }

            if (!_handlers.TryAdd(typeof(TRequest), handler))
            {
                throw new InvalidOperationException(
                    "A handler is already registered for the request type.");
            }
        }
    }

    public Task<OperationResult> RunAsync<TRequest>(
        TRequest request,
        CorrelationId? correlationId,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken = default)
        where TRequest : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        if (deadlineUtc is DateTimeOffset deadline && deadline.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Request deadlines must use a UTC offset of zero and are not converted.",
                nameof(deadlineUtc));
        }

        return RunCoreAsync(request, correlationId, deadlineUtc, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        Task stopTask;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _stopping = true;
            _stopTask ??= StopCoreAsync();
            stopTask = _stopTask;
        }

        return stopTask;
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_gate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposeTask = DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private Task<OperationResult> RunCoreAsync<TRequest>(
        TRequest request,
        CorrelationId? correlationId,
        DateTimeOffset? deadlineUtc,
        CancellationToken cancellationToken)
        where TRequest : notnull
    {
        CorrelationId correlation = correlationId ?? CorrelationId.New();
        IOperationHandler<TRequest> handler;
        DateTimeOffset startedAtUtc;
        DateTimeOffset effectiveDeadlineUtc;
        AdmittedOperation admitted;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_stopping)
            {
                return Task.FromResult(
                    Refused(
                        correlation,
                        RuntimeOutcomeCode.RuntimeStopping,
                        "The runtime is stopping."));
            }

            if (!_handlers.TryGetValue(typeof(TRequest), out object? boxed))
            {
                return Task.FromResult(
                    Refused(
                        correlation,
                        RuntimeOutcomeCode.UnsupportedRequest,
                        "No handler is registered for the request type."));
            }

            handler = (IOperationHandler<TRequest>)boxed;

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(
                    Refused(
                        correlation,
                        RuntimeOutcomeCode.Cancelled,
                        "The operation was cancelled."));
            }

            startedAtUtc = _clock.UtcNow;
            effectiveDeadlineUtc = EffectiveDeadline(startedAtUtc, deadlineUtc);
            if (effectiveDeadlineUtc <= startedAtUtc)
            {
                return Task.FromResult(
                    Refused(
                        correlation,
                        RuntimeOutcomeCode.DeadlineExpired,
                        "The operation deadline expired."));
            }

            if (_inFlight.ContainsKey(correlation))
            {
                return Task.FromResult(
                    Refused(
                        correlation,
                        RuntimeOutcomeCode.DuplicateCorrelation,
                        "An operation with this correlation is already in flight."));
            }

            if (_inFlight.Count >= _options.MaxConcurrentOperations)
            {
                return Task.FromResult(
                    Refused(
                        correlation,
                        RuntimeOutcomeCode.RuntimeBusy,
                        "The runtime is at its concurrency limit."));
            }

            admitted = new AdmittedOperation(correlation);
            _inFlight.Add(correlation, admitted);
        }

        return ExecuteAdmittedAsync(
            handler,
            request,
            correlation,
            startedAtUtc,
            effectiveDeadlineUtc,
            admitted,
            cancellationToken);
    }

    private async Task<OperationResult> ExecuteAdmittedAsync<TRequest>(
        IOperationHandler<TRequest> handler,
        TRequest request,
        CorrelationId correlation,
        DateTimeOffset startedAtUtc,
        DateTimeOffset effectiveDeadlineUtc,
        AdmittedOperation admitted,
        CancellationToken cancellationToken)
        where TRequest : notnull
    {
        try
        {
            return await DispatchAsync(
                    handler,
                    request,
                    correlation,
                    startedAtUtc,
                    effectiveDeadlineUtc,
                    admitted,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _ = _inFlight.Remove(correlation);
            }

            _ = admitted.Finished.TrySetResult();
        }
    }

    private async Task<OperationResult> DispatchAsync<TRequest>(
        IOperationHandler<TRequest> handler,
        TRequest request,
        CorrelationId correlation,
        DateTimeOffset startedAtUtc,
        DateTimeOffset effectiveDeadlineUtc,
        AdmittedOperation admitted,
        CancellationToken callerToken)
        where TRequest : notnull
    {
        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            callerToken,
            _shutdownCts.Token);
        OperationExecutionContext context = new(
            correlation,
            startedAtUtc,
            effectiveDeadlineUtc,
            _clock);
        TaskCompletionSource<OperationResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        admitted.Completion = completion;
        admitted.LinkedCts = linkedCts;

        CancellationTokenRegistration callerRegistration = default;
        if (callerToken.CanBeCanceled)
        {
            callerRegistration = callerToken.Register(
                () =>
                {
                    if (TryComplete(
                            completion,
                            Indeterminate(
                                correlation,
                                RuntimeOutcomeCode.Cancelled,
                                "The operation was cancelled.")))
                    {
                        CancelQuietly(linkedCts);
                    }
                },
                useSynchronizationContext: false);
        }

        Task handlerTask = RunHandlerAsync(
            handler,
            request,
            context,
            linkedCts.Token,
            completion,
            correlation,
            callerToken,
            effectiveDeadlineUtc);
        Task deadlineTask = WatchDeadlineAsync(
            effectiveDeadlineUtc,
            linkedCts,
            completion,
            correlation);
        Observe(handlerTask);
        Observe(deadlineTask);

        OperationResult result = await completion.Task.ConfigureAwait(false);
        await callerRegistration.DisposeAsync().ConfigureAwait(false);
        await RunCleanupsAsync(context).ConfigureAwait(false);
        CancelQuietly(linkedCts);
        if (handlerTask.IsCompleted)
        {
            linkedCts.Dispose();
        }
        else
        {
            _ = handlerTask.ContinueWith(
                _ => linkedCts.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return result;
    }

    private async Task RunHandlerAsync<TRequest>(
        IOperationHandler<TRequest> handler,
        TRequest request,
        OperationExecutionContext context,
        CancellationToken linkedToken,
        TaskCompletionSource<OperationResult> completion,
        CorrelationId correlation,
        CancellationToken callerToken,
        DateTimeOffset effectiveDeadlineUtc)
        where TRequest : notnull
    {
        try
        {
            OperationResult result = await handler
                .ExecuteAsync(request, context, linkedToken)
                .ConfigureAwait(false);
            context.MarkHandlerCompleted();
            _ = TryComplete(completion, TranslateReturn(result, correlation));
        }
        catch (Exception ex)
        {
            context.MarkHandlerCompleted();
            _ = TryComplete(
                completion,
                TranslateException(ex, correlation, linkedToken, callerToken, effectiveDeadlineUtc));
        }
    }

    private async Task WatchDeadlineAsync(
        DateTimeOffset effectiveDeadlineUtc,
        CancellationTokenSource linkedCts,
        TaskCompletionSource<OperationResult> completion,
        CorrelationId correlation)
    {
        try
        {
            TimeSpan remaining = effectiveDeadlineUtc - _clock.UtcNow;
            await _clock.Delay(remaining, linkedCts.Token).ConfigureAwait(false);
            if (TryComplete(
                    completion,
                    Indeterminate(
                        correlation,
                        RuntimeOutcomeCode.DeadlineExpired,
                        "The operation deadline expired.")))
            {
                CancelQuietly(linkedCts);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunCleanupsAsync(OperationExecutionContext context)
    {
        Func<CancellationToken, ValueTask>[] cleanups = context.BeginCleanup();
        if (cleanups.Length == 0)
        {
            return;
        }

        using CancellationTokenSource cleanupCts = new();
        Task timeoutTask = _clock.Delay(_options.CleanupTimeout, CancellationToken.None).AsTask();
        Task cleanupTask = InvokeCleanupsAsync(cleanups, cleanupCts.Token);
        Task completed = await Task.WhenAny(cleanupTask, timeoutTask).ConfigureAwait(false);
        if (completed != cleanupTask)
        {
            CancelQuietly(cleanupCts);
            Observe(cleanupTask);
        }
        else
        {
            await cleanupTask.ConfigureAwait(false);
        }
    }

    private static async Task InvokeCleanupsAsync(
        Func<CancellationToken, ValueTask>[] cleanups,
        CancellationToken cancellationToken)
    {
        for (int index = 0; index < cleanups.Length; index++)
        {
            try
            {
                await cleanups[index](cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    private async Task StopCoreAsync()
    {
        await Task.Yield();
        CancelQuietly(_shutdownCts);

        AdmittedOperation[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _inFlight.Values];
        }

        if (snapshot.Length == 0)
        {
            return;
        }

        Task[] finished = new Task[snapshot.Length];
        for (int index = 0; index < snapshot.Length; index++)
        {
            finished[index] = snapshot[index].Finished.Task;
        }

        Task drained = Task.WhenAll(finished);
        Task timeout = _clock.Delay(_options.ShutdownDrain, CancellationToken.None).AsTask();
        Task completed = await Task.WhenAny(drained, timeout).ConfigureAwait(false);
        if (completed != drained)
        {
            for (int index = 0; index < snapshot.Length; index++)
            {
                AdmittedOperation operation = snapshot[index];
                if (operation.Completion is TaskCompletionSource<OperationResult> completion
                    && TryComplete(
                        completion,
                        Indeterminate(
                            operation.Correlation,
                            RuntimeOutcomeCode.ShutdownInterrupted,
                            "The runtime shutdown drain elapsed.")))
                {
                    if (operation.LinkedCts is CancellationTokenSource linked)
                    {
                        CancelQuietly(linked);
                    }
                }
            }
        }

        await Task.WhenAll(finished).ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _disposed = true;
            }

            _shutdownCts.Dispose();
        }
    }

    private DateTimeOffset EffectiveDeadline(DateTimeOffset startedAtUtc, DateTimeOffset? deadlineUtc)
    {
        DateTimeOffset hostDeadline = startedAtUtc + _options.MaxExecution;
        if (deadlineUtc is DateTimeOffset requestDeadline && requestDeadline < hostDeadline)
        {
            return requestDeadline;
        }

        return hostDeadline;
    }

    private OperationResult TranslateReturn(OperationResult? result, CorrelationId correlation)
    {
        if (result is null)
        {
            return Failed(
                correlation,
                RuntimeOutcomeCode.HandlerNullResult,
                "The handler returned a null result.");
        }

        if (!Equals(result.CorrelationId, correlation))
        {
            return Failed(
                correlation,
                RuntimeOutcomeCode.HandlerCorrelationMismatch,
                "The handler result correlation does not match the operation.");
        }

        return result;
    }

    private OperationResult TranslateException(
        Exception exception,
        CorrelationId correlation,
        CancellationToken linkedToken,
        CancellationToken callerToken,
        DateTimeOffset effectiveDeadlineUtc)
    {
        if (exception is AggregateException aggregate)
        {
            return TranslateAggregate(aggregate, correlation, linkedToken, callerToken, effectiveDeadlineUtc);
        }

        if (IsCancellation(exception))
        {
            if (linkedToken.IsCancellationRequested)
            {
                return TranslateLinkedCancellation(correlation, callerToken, effectiveDeadlineUtc);
            }

            return Failed(correlation, RuntimeOutcomeCode.HandlerFault, Sanitize(exception.Message));
        }

        return Failed(correlation, RuntimeOutcomeCode.HandlerFault, Sanitize(exception.Message));
    }

    private OperationResult TranslateAggregate(
        AggregateException aggregate,
        CorrelationId correlation,
        CancellationToken linkedToken,
        CancellationToken callerToken,
        DateTimeOffset effectiveDeadlineUtc)
    {
        AggregateException flattened = aggregate.Flatten();
        bool allCancellation = flattened.InnerExceptions.Count > 0;
        Exception? firstFault = null;
        for (int index = 0; index < flattened.InnerExceptions.Count; index++)
        {
            Exception inner = flattened.InnerExceptions[index];
            if (IsCancellation(inner))
            {
                continue;
            }

            allCancellation = false;
            firstFault ??= inner;
        }

        if (allCancellation && linkedToken.IsCancellationRequested)
        {
            return TranslateLinkedCancellation(correlation, callerToken, effectiveDeadlineUtc);
        }

        Exception fault = firstFault ?? flattened;
        return Failed(correlation, RuntimeOutcomeCode.HandlerFault, Sanitize(fault.Message));
    }

    private OperationResult TranslateLinkedCancellation(
        CorrelationId correlation,
        CancellationToken callerToken,
        DateTimeOffset effectiveDeadlineUtc)
    {
        if (_shutdownCts.IsCancellationRequested)
        {
            return Indeterminate(
                correlation,
                RuntimeOutcomeCode.ShutdownInterrupted,
                "The runtime shutdown drain elapsed.");
        }

        if (_clock.UtcNow >= effectiveDeadlineUtc)
        {
            return Indeterminate(
                correlation,
                RuntimeOutcomeCode.DeadlineExpired,
                "The operation deadline expired.");
        }

        if (callerToken.IsCancellationRequested)
        {
            return Indeterminate(
                correlation,
                RuntimeOutcomeCode.Cancelled,
                "The operation was cancelled.");
        }

        return Indeterminate(
            correlation,
            RuntimeOutcomeCode.Cancelled,
            "The operation was cancelled.");
    }

    private OperationResult Refused(CorrelationId correlation, string code, string message) =>
        OperationResult.Refused(correlation, new RefusalInfo(code, message), [Evidence(code, message)]);

    private OperationResult Failed(CorrelationId correlation, string code, string message) =>
        OperationResult.Failed(
            correlation,
            new FailureInfo(code, message, isTransient: false),
            [Evidence(code, message)]);

    private OperationResult Indeterminate(CorrelationId correlation, string code, string message) =>
        OperationResult.Indeterminate(correlation, [Evidence(code, message)]);

    private VerificationEvidence Evidence(string code, string message) =>
        new(VerificationEvidenceKind.DiagnosticArtifact, code, message, _clock.UtcNow);

    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 1024)
        {
            return "The handler faulted.";
        }

        return message;
    }

    private static bool IsCancellation(Exception exception) =>
        exception is OperationCanceledException;

    private static bool TryComplete(TaskCompletionSource<OperationResult> completion, OperationResult result) =>
        completion.TrySetResult(result);

    private static void CancelQuietly(CancellationTokenSource source)
    {
        try
        {
            if (!source.IsCancellationRequested)
            {
                source.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed class AdmittedOperation
    {
        internal AdmittedOperation(CorrelationId correlation)
        {
            Correlation = correlation;
            Finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal CorrelationId Correlation { get; }

        internal TaskCompletionSource Finished { get; }

        internal TaskCompletionSource<OperationResult>? Completion { get; set; }

        internal CancellationTokenSource? LinkedCts { get; set; }
    }
}
