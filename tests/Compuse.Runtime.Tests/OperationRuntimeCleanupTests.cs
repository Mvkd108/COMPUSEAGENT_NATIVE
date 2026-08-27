using Compuse.Contracts;

namespace Compuse.Runtime.Tests;

[TestClass]
public sealed class OperationRuntimeCleanupTests
{
    [TestMethod]
    public async Task CleanupsRunLifoAndFaultsDoNotReplaceResult()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        List<int> order = [];
        runtime.Register(new ScriptedHandler<object>((_, context, _) =>
        {
            context.RegisterCleanup(_ =>
            {
                order.Add(1);
                return ValueTask.CompletedTask;
            });
            context.RegisterCleanup(_ => throw new InvalidOperationException("cleanup-two"));
            context.RegisterCleanup(_ =>
            {
                order.Add(3);
                return ValueTask.CompletedTask;
            });
            return new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(context.CorrelationId));
        }));

        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreEqual(2, order.Count);
        Assert.AreEqual(3, order[0]);
        Assert.AreEqual(1, order[1]);
    }

    [TestMethod]
    public async Task CleanupHangIsBoundedByCleanupTimeout()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(cleanupTimeout: TimeSpan.FromSeconds(1)));
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Register(new ScriptedHandler<object>(async (_, context, _) =>
        {
            _ = entered.TrySetResult();
            await release.Task.ConfigureAwait(false);
            context.RegisterCleanup(async cleanupToken =>
            {
                _ = cleanupToken;
                await never.Task.ConfigureAwait(false);
            });
            return RuntimeHarness.FailedResult(context.CorrelationId);
        }));

        Task<OperationResult> run = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        await entered.Task.ConfigureAwait(false);
        await clock.WaitForWaiter().ConfigureAwait(false);
        Task cleanupWaiter = clock.WaitForNextWaiter();
        _ = release.TrySetResult();
        await cleanupWaiter.ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(1));
        OperationResult result = await run.ConfigureAwait(false);
        Assert.AreEqual(OperationOutcome.Failed, result.Outcome);
        Assert.AreEqual("provider_unavailable", result.Failure?.Code);
    }

    [TestMethod]
    public async Task RegisterCleanupAfterHandlerCompletionThrowsInsideLaterCall()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        OperationExecutionContext? captured = null;
        runtime.Register(new ScriptedHandler<object>((_, context, _) =>
        {
            captured = context;
            return new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(context.CorrelationId));
        }));
        _ = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Assert.IsNotNull(captured);
        _ = Assert.ThrowsExactly<InvalidOperationException>(
            () => captured!.RegisterCleanup(_ => ValueTask.CompletedTask));
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => captured!.RegisterCleanup(null!));
    }

    [TestMethod]
    public async Task CleanupsRunAfterCancellation()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        using CancellationTokenSource cts = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource cleaned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Register(new ScriptedHandler<object>(async (_, context, token) =>
        {
            context.RegisterCleanup(cleanupToken =>
            {
                _ = cleanupToken;
                _ = cleaned.TrySetResult();
                return ValueTask.CompletedTask;
            });
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
        await cleaned.Task.ConfigureAwait(false);
        Assert.AreEqual(OperationOutcome.Indeterminate, result.Outcome);
    }

    [TestMethod]
    public async Task CleanupCannotRegisterNestedCleanup()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        bool nestedThrew = false;
        runtime.Register(new ScriptedHandler<object>((_, context, _) =>
        {
            context.RegisterCleanup(cleanupToken =>
            {
                _ = cleanupToken;
                _ = Assert.ThrowsExactly<InvalidOperationException>(
                    () => context.RegisterCleanup(static nestedToken =>
                    {
                        _ = nestedToken;
                        return ValueTask.CompletedTask;
                    }));
                nestedThrew = true;
                return ValueTask.CompletedTask;
            });
            return new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(context.CorrelationId));
        }));

        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Assert.IsTrue(nestedThrew);
        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
    }

    [TestMethod]
    public async Task CleanupsRunAfterTimeout()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(maxExecution: TimeSpan.FromSeconds(4)));
        TaskCompletionSource cleaned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Register(new ScriptedHandler<object>(async (_, context, token) =>
        {
            context.RegisterCleanup(cleanupToken =>
            {
                _ = cleanupToken;
                _ = cleaned.TrySetResult();
                return ValueTask.CompletedTask;
            });
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (token.Register(() => canceled.TrySetCanceled(token)))
            {
                await canceled.Task.ConfigureAwait(false);
            }

            return RuntimeHarness.RefusedResult(context.CorrelationId);
        }));

        Task<OperationResult> run = runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        await clock.WaitForWaiter().ConfigureAwait(false);
        clock.Advance(TimeSpan.FromSeconds(4));
        OperationResult result = await run.ConfigureAwait(false);
        await cleaned.Task.ConfigureAwait(false);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Indeterminate,
            RuntimeOutcomeCode.DeadlineExpired);
    }
}
