using Compuse.Contracts;
using Compuse.Requests;

namespace Compuse.Runtime.Tests;

[TestClass]
public sealed class OperationRuntimeShutdownTests
{
    [TestMethod]
    public async Task StopWhileIdleIsIdempotent()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        await runtime.StopAsync().ConfigureAwait(false);
        await runtime.StopAsync().ConfigureAwait(false);
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Refused,
            RuntimeOutcomeCode.RuntimeStopping);
    }

    [TestMethod]
    public async Task StopCancelsInFlightAndDrainYieldsShutdownInterrupted()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(shutdownDrain: TimeSpan.FromSeconds(2)));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Register(new ScriptedHandler<object>(async (_, context, _) =>
        {
            _ = entered.TrySetResult();
            TaskCompletionSource hang = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await hang.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            return RuntimeHarness.RefusedResult(context.CorrelationId);
        }));

        Task<OperationResult> run = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        await entered.Task.ConfigureAwait(false);
        Task nextWaiter = clock.WaitForNextWaiter();
        Task stop = runtime.StopAsync();
        await nextWaiter.ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(2));
        OperationResult result = await run.ConfigureAwait(false);
        await stop.ConfigureAwait(false);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.ShutdownInterrupted);
    }

    [TestMethod]
    public async Task StopCancelsInFlightAndHandlerObservesShutdown()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Register(new ScriptedHandler<object>(async (_, context, token) =>
        {
            _ = entered.TrySetResult();
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (token.Register(() => canceled.TrySetCanceled(token)))
            {
                await canceled.Task.ConfigureAwait(false);
            }

            return RuntimeHarness.RefusedResult(context.CorrelationId);
        }));

        Task<OperationResult> run = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        await entered.Task.ConfigureAwait(false);
        Task stop = runtime.StopAsync();
        OperationResult result = await run.ConfigureAwait(false);
        await stop.ConfigureAwait(false);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.ShutdownInterrupted);
    }

    [TestMethod]
    public async Task DisposeIsIdempotentAndRejectsLaterRunAndRegister()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        await runtime.DisposeAsync().ConfigureAwait(false);
        await runtime.DisposeAsync().ConfigureAwait(false);
        _ = Assert.ThrowsExactly<ObjectDisposedException>(
            () => runtime.RunAsync(new object(), RuntimeHarness.Alpha, null));
        _ = Assert.ThrowsExactly<InvalidOperationException>(
            () => runtime.Register(new ScriptedHandler<object>(
                (_, context, _) => new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(context.CorrelationId)))));
        _ = Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.StopAsync());
    }

    [TestMethod]
    public async Task RegisterAfterStopThrows()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        await runtime.StopAsync().ConfigureAwait(false);
        _ = Assert.ThrowsExactly<InvalidOperationException>(
            () => runtime.Register(new ScriptedHandler<object>(
                (_, context, _) => new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(context.CorrelationId)))));
    }

    [TestMethod]
    public async Task DuplicateHandlerRegistrationThrows()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        ScriptedHandler<object> handler = new(
            (_, context, _) => new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(context.CorrelationId)));
        runtime.Register(handler);
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => runtime.Register(handler));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => runtime.Register<string>(null!));
    }

    [TestMethod]
    public async Task DistinctCorrelationsDispatchConcurrently()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions(maxConcurrentOperations: 4));
        TaskCompletionSource enteredA = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource enteredB = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Register(new ScriptedHandler<object>(async (request, context, token) =>
        {
            _ = request;
            _ = token;
            if (Equals(context.CorrelationId, RuntimeHarness.Alpha))
            {
                _ = enteredA.TrySetResult();
            }
            else
            {
                _ = enteredB.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
            return RuntimeHarness.RefusedResult(context.CorrelationId);
        }));

        Task<OperationResult> first = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Task<OperationResult> second = runtime.RunAsync(new object(), RuntimeHarness.Beta, null);
        await Task.WhenAll(enteredA.Task, enteredB.Task).ConfigureAwait(false);
        _ = release.TrySetResult();
        OperationResult[] results = await Task.WhenAll(first, second).ConfigureAwait(false);
        Assert.AreEqual(OperationOutcome.Refused, results[0].Outcome);
        Assert.AreEqual(OperationOutcome.Refused, results[1].Outcome);
    }
}

[TestClass]
public sealed class OperationRuntimeDropFilesTests
{
    [TestMethod]
    public async Task DropFilesRequestPreservesObjectAndCorrelationWithoutCommitting()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        DropFilesRequest request = DropFilesRequest.Create(
            RuntimeHarness.Alpha,
            [new SourceItem(new PhysicalFileSource(@"C:\src\a.txt"))],
            TransferEffect.Copy,
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(@"C:\dst")),
            RuntimeHarness.Start.AddMinutes(5));
        ScriptedHandler<DropFilesRequest> handler = new((received, context, _) =>
        {
            Assert.AreSame(request, received);
            Assert.AreEqual(request.CorrelationId, context.CorrelationId);
            Assert.AreEqual(request.DeadlineUtc, RuntimeHarness.Start.AddMinutes(5));
            return new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(context.CorrelationId));
        });
        runtime.Register(handler);

        OperationResult result = await runtime.RunAsync(
            request,
            request.CorrelationId,
            request.DeadlineUtc);
        Assert.AreEqual(1, handler.Invocations);
        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreNotEqual(OperationOutcome.Committed, result.Outcome);
        Assert.AreEqual(request.CorrelationId, result.CorrelationId);
    }
}
