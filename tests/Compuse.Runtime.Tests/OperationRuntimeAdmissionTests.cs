using Compuse.Contracts;

namespace Compuse.Runtime.Tests;

[TestClass]
public sealed class OperationRuntimeAdmissionTests
{
    [TestMethod]
    public async Task ProvidedCorrelationIsPreservedOnAdmissionRefusal()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        OperationResult result = await runtime.RunAsync(
            new object(),
            RuntimeHarness.Alpha,
            deadlineUtc: null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Refused,
            RuntimeOutcomeCode.UnsupportedRequest);
    }

    [TestMethod]
    public async Task NullCorrelationIsAssignedAndNonEmpty()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>(
            (request, context, token) => new ValueTask<OperationResult>(
                RuntimeHarness.RefusedResult(context.CorrelationId))));
        OperationResult first = await runtime.RunAsync(new object(), correlationId: null, deadlineUtc: null);
        OperationResult second = await runtime.RunAsync(new object(), correlationId: null, deadlineUtc: null);
        Assert.AreNotEqual(Guid.Empty, first.CorrelationId.Value);
        Assert.AreNotEqual(Guid.Empty, second.CorrelationId.Value);
        Assert.AreNotEqual(first.CorrelationId, second.CorrelationId);
        Assert.AreEqual(OperationOutcome.Refused, first.Outcome);
    }

    [TestMethod]
    public async Task AlreadyCancelledCallerTokenIsRefusedWithoutDispatch()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        ScriptedHandler<object> handler = new((_, _, _) => throw new InvalidOperationException("should not run"));
        runtime.Register(handler);
        using CancellationTokenSource cts = new();
        cts.Cancel();
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null, cts.Token);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Refused,
            RuntimeOutcomeCode.Cancelled);
        Assert.AreEqual(0, handler.Invocations);
    }

    [TestMethod]
    public async Task PastAndEqualDeadlinesAreRefusedWithoutDispatch()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        ScriptedHandler<object> handler = new((_, _, _) => throw new InvalidOperationException("should not run"));
        runtime.Register(handler);

        OperationResult past = await runtime.RunAsync(
            new object(),
            RuntimeHarness.Alpha,
            RuntimeHarness.Start.AddSeconds(-1));
        RuntimeHarness.AssertKernel(
            past,
            RuntimeHarness.Alpha,
            OperationOutcome.Refused,
            RuntimeOutcomeCode.DeadlineExpired);

        OperationResult equal = await runtime.RunAsync(
            new object(),
            RuntimeHarness.Beta,
            RuntimeHarness.Start);
        RuntimeHarness.AssertKernel(
            equal,
            RuntimeHarness.Beta,
            OperationOutcome.Refused,
            RuntimeOutcomeCode.DeadlineExpired);
        Assert.AreEqual(0, handler.Invocations);
    }

    [TestMethod]
    public async Task NonUtcDeadlineThrowsAndDoesNotInvokeHandler()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        ScriptedHandler<object> handler = new((_, _, _) => throw new InvalidOperationException("should not run"));
        runtime.Register(handler);
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(
            () => runtime.RunAsync(
                new object(),
                RuntimeHarness.Alpha,
                new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.FromHours(5.5))));
        Assert.AreEqual("deadlineUtc", ex.ParamName);
        Assert.AreEqual(0, handler.Invocations);
    }

    [TestMethod]
    public async Task NullRequestThrows()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => runtime.RunAsync<object>(null!, RuntimeHarness.Alpha, null));
    }

    [TestMethod]
    public void NullClockThrows() =>
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => new OperationRuntime(null!));

    [TestMethod]
    public async Task MissingHandlerIsUnsupportedRequest()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Refused,
            RuntimeOutcomeCode.UnsupportedRequest);
    }

    [TestMethod]
    public async Task DuplicateInFlightCorrelationIsRefusedThenReusable()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Register(new ScriptedHandler<object>(async (_, context, _) =>
        {
            _ = entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return RuntimeHarness.RefusedResult(context.CorrelationId);
        }));

        Task<OperationResult> first = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        await entered.Task.ConfigureAwait(false);
        OperationResult duplicate = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            duplicate,
            RuntimeHarness.Alpha,
            OperationOutcome.Refused,
            RuntimeOutcomeCode.DuplicateCorrelation);

        _ = release.TrySetResult();
        OperationResult firstResult = await first.ConfigureAwait(false);
        Assert.AreEqual(OperationOutcome.Refused, firstResult.Outcome);

        OperationResult reused = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Assert.AreEqual(OperationOutcome.Refused, reused.Outcome);
        Assert.AreEqual(RuntimeHarness.Alpha, reused.CorrelationId);
    }

    [TestMethod]
    public async Task ConcurrencyCapRefusesAdditionalWork()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions(maxConcurrentOperations: 1));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedHandler<object> handler = new(async (_, context, _) =>
        {
            _ = entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return RuntimeHarness.RefusedResult(context.CorrelationId);
        });
        runtime.Register(handler);

        Task<OperationResult> first = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        await entered.Task.ConfigureAwait(false);
        OperationResult busy = await runtime.RunAsync(new object(), RuntimeHarness.Beta, null);
        RuntimeHarness.AssertKernel(
            busy,
            RuntimeHarness.Beta,
            OperationOutcome.Refused,
            RuntimeOutcomeCode.RuntimeBusy);
        Assert.AreEqual(1, handler.Invocations);

        _ = release.TrySetResult();
        _ = await first.ConfigureAwait(false);
    }
}
