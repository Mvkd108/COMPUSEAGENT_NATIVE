using Compuse.Contracts;

namespace Compuse.Runtime.Tests;

[TestClass]
public sealed class OperationRuntimeDeadlineTests
{
    [TestMethod]
    public async Task HostMaximumWinsOverLaterRequestDeadline()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(maxExecution: TimeSpan.FromSeconds(10)));
        runtime.Register(new ScriptedHandler<object>(async (_, context, token) =>
        {
            Assert.AreEqual(RuntimeHarness.Start.AddSeconds(10), context.EffectiveDeadlineUtc);
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (token.Register(() => canceled.TrySetCanceled(token)))
            {
                await canceled.Task.ConfigureAwait(false);
            }

            return RuntimeHarness.RefusedResult(context.CorrelationId);
        }));

        Task<OperationResult> run = runtime.RunAsync(
            new object(),
            RuntimeHarness.Alpha,
            RuntimeHarness.Start.AddHours(1));
        await clock.WaitForWaiter().ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(10));
        OperationResult result = await run.ConfigureAwait(false);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.DeadlineExpired);
    }

    [TestMethod]
    public async Task EarlierRequestDeadlineWinsOverHostMaximum()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(maxExecution: TimeSpan.FromSeconds(30)));
        runtime.Register(new ScriptedHandler<object>(async (_, context, token) =>
        {
            Assert.AreEqual(RuntimeHarness.Start.AddSeconds(5), context.EffectiveDeadlineUtc);
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (token.Register(() => canceled.TrySetCanceled(token)))
            {
                await canceled.Task.ConfigureAwait(false);
            }

            return RuntimeHarness.RefusedResult(context.CorrelationId);
        }));

        Task<OperationResult> run = runtime.RunAsync(
            new object(),
            RuntimeHarness.Alpha,
            RuntimeHarness.Start.AddSeconds(5));
        await clock.WaitForWaiter().ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(5));
        OperationResult result = await run.ConfigureAwait(false);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.DeadlineExpired);
    }

    [TestMethod]
    public async Task HostMaximumAppliesWhenRequestDeadlineIsAbsent()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(maxExecution: TimeSpan.FromSeconds(8)));
        runtime.Register(new ScriptedHandler<object>(async (_, context, token) =>
        {
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (token.Register(() => canceled.TrySetCanceled(token)))
            {
                await canceled.Task.ConfigureAwait(false);
            }

            return RuntimeHarness.RefusedResult(context.CorrelationId);
        }));

        Task<OperationResult> run = runtime.RunAsync(new object(), RuntimeHarness.Alpha, deadlineUtc: null);
        await clock.WaitForWaiter().ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(8));
        OperationResult result = await run.ConfigureAwait(false);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.DeadlineExpired);
    }

    [TestMethod]
    public async Task DeadlineAfterDispatchIsIndeterminateAndInvokesHandler()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(maxExecution: TimeSpan.FromSeconds(4)));
        ScriptedHandler<object> handler = new(async (_, context, token) =>
        {
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (token.Register(() => canceled.TrySetCanceled(token)))
            {
                await canceled.Task.ConfigureAwait(false);
            }

            return RuntimeHarness.RefusedResult(context.CorrelationId);
        });
        runtime.Register(handler);
        Task<OperationResult> run = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        await clock.WaitForWaiter().ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(4));
        OperationResult result = await run.ConfigureAwait(false);
        Assert.AreEqual(1, handler.Invocations);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.DeadlineExpired);
    }

    [TestMethod]
    public async Task CallerCancelAfterDispatchIsIndeterminate()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        using CancellationTokenSource cts = new();
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

        Task<OperationResult> run = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null, cts.Token);
        await entered.Task.ConfigureAwait(false);
        cts.Cancel();
        OperationResult result = await run.ConfigureAwait(false);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.Cancelled);
    }

    [TestMethod]
    public async Task HandlerResultWinsIfItCompletesBeforeWatcher()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>(
            (_, context, _) => new ValueTask<OperationResult>(RuntimeHarness.FailedResult(context.CorrelationId))));
        using CancellationTokenSource cts = new();
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null, cts.Token);
        Assert.AreEqual(OperationOutcome.Failed, result.Outcome);
        Assert.AreEqual("provider_unavailable", result.Failure?.Code);
    }

    [TestMethod]
    public async Task LateCommittedAfterTimeoutIsDiscarded()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(maxExecution: TimeSpan.FromSeconds(3)));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Register(new ScriptedHandler<object>(async (_, context, _) =>
        {
            _ = entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return RuntimeHarness.CommittedResult(context.CorrelationId, context.Clock.UtcNow);
        }));

        Task<OperationResult> run = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        await entered.Task.ConfigureAwait(false);
        await clock.WaitForWaiter().ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(3));
        OperationResult result = await run.ConfigureAwait(false);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.DeadlineExpired);
        _ = release.TrySetResult();
        await Task.Yield();
        Assert.AreEqual(OperationOutcome.Indeterminate, result.Outcome);
    }
}
