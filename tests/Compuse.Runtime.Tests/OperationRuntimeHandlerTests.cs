using Compuse.Contracts;

namespace Compuse.Runtime.Tests;

[TestClass]
public sealed class OperationRuntimeHandlerTests
{
    [TestMethod]
    public async Task HandlerFailedResultIsNotUpgraded()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>(
            (_, context, _) => new ValueTask<OperationResult>(RuntimeHarness.FailedResult(context.CorrelationId))));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Assert.AreEqual(OperationOutcome.Failed, result.Outcome);
        Assert.AreEqual("provider_unavailable", result.Failure?.Code);
        Assert.AreEqual(RuntimeHarness.Alpha, result.CorrelationId);
    }

    [TestMethod]
    public async Task HandlerCommittedWithObservationIsPassedThroughUnchanged()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        OperationResult committed = RuntimeHarness.CommittedResult(RuntimeHarness.Alpha, clock.UtcNow);
        runtime.Register(new ScriptedHandler<object>(
            (_, _, _) => new ValueTask<OperationResult>(committed)));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Assert.AreSame(committed, result);
        Assert.AreEqual(OperationOutcome.Committed, result.Outcome);
        Assert.AreEqual(1, result.Evidence.Count);
        Assert.AreEqual(VerificationEvidenceKind.ExternalSideEffectObservation, result.Evidence[0].Kind);
    }

    [TestMethod]
    public async Task HandlerNullResultIsFailed()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>(
            (_, _, _) => new ValueTask<OperationResult>((OperationResult)null!)));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Failed,
            RuntimeOutcomeCode.HandlerNullResult);
    }

    [TestMethod]
    public async Task HandlerCorrelationMismatchIsFailed()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>(
            (_, _, _) => new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(RuntimeHarness.Beta))));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Failed,
            RuntimeOutcomeCode.HandlerCorrelationMismatch);
    }

    [TestMethod]
    public async Task HandlerInvalidOperationExceptionIsHandlerFault()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>(
            (_, _, _) => throw new InvalidOperationException("boom")));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Failed,
            RuntimeOutcomeCode.HandlerFault);
        Assert.AreEqual("boom", result.Failure?.Message);
    }

    [TestMethod]
    public async Task HandlerCanceledWithoutTokenIsHandlerFault()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>((_, _, _) => throw new OperationCanceledException()));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Failed,
            RuntimeOutcomeCode.HandlerFault);
    }

    [TestMethod]
    public async Task AggregateExceptionFaultIsHandlerFault()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>(
            (_, _, _) => throw new AggregateException(new InvalidOperationException("inner-fault"))));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Failed,
            RuntimeOutcomeCode.HandlerFault);
        Assert.AreEqual("inner-fault", result.Failure?.Message);
    }

    [TestMethod]
    public async Task OsApiOnlyCommittedIsTranslatedToHandlerFault()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>((_, context, _) =>
        {
            _ = OperationResult.Committed(
                context.CorrelationId,
                [
                    new VerificationEvidence(
                        VerificationEvidenceKind.OsApiReturn,
                        "api_succeeded",
                        "The API returned success.",
                        context.Clock.UtcNow)
                ]);
            return new ValueTask<OperationResult>(RuntimeHarness.FailedResult(context.CorrelationId));
        }));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        RuntimeHarness.AssertKernel(
            result,
            RuntimeHarness.Alpha,
            OperationOutcome.Failed,
            RuntimeOutcomeCode.HandlerFault);
    }

    [TestMethod]
    public async Task LinkedTokenIsCancellableAndNotDisposedDuringExecute()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        bool observedOde = false;
        runtime.Register(new ScriptedHandler<object>((_, context, token) =>
        {
            Assert.IsTrue(token.CanBeCanceled);
            Assert.AreNotEqual(CancellationToken.None, token);
            try
            {
                using (token.Register(static () => { }))
                {
                    return new ValueTask<OperationResult>(RuntimeHarness.RefusedResult(context.CorrelationId));
                }
            }
            catch (ObjectDisposedException)
            {
                observedOde = true;
                throw;
            }
        }));
        OperationResult result = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Assert.IsFalse(observedOde);
        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
    }

    [TestMethod]
    public async Task WhitespaceAndOverlongFaultMessagesAreSanitized()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(clock, RuntimeHarness.FastOptions());
        runtime.Register(new ScriptedHandler<object>(
            (_, _, _) => throw new InvalidOperationException(new string('x', 1025))));
        OperationResult longResult = await runtime.RunAsync(new object(), RuntimeHarness.Alpha, null);
        Assert.AreEqual("The handler faulted.", longResult.Failure?.Message);

        await using OperationRuntime whitespaceRuntime = new(clock, RuntimeHarness.FastOptions());
        whitespaceRuntime.Register(new ScriptedHandler<object>(
            (_, _, _) => throw new InvalidOperationException("   ")));
        OperationResult whitespace = await whitespaceRuntime.RunAsync(new object(), RuntimeHarness.Beta, null);
        Assert.AreEqual("The handler faulted.", whitespace.Failure?.Message);
    }

    [TestMethod]
    public async Task HandlerCommittedWinsBeforeDeadlineWatcher()
    {
        ControllableOperationClock clock = RuntimeHarness.Clock();
        await using OperationRuntime runtime = new(
            clock,
            RuntimeHarness.FastOptions(maxExecution: TimeSpan.FromSeconds(30)));
        OperationResult committed = RuntimeHarness.CommittedResult(RuntimeHarness.Alpha, clock.UtcNow);
        runtime.Register(new ScriptedHandler<object>(
            (_, _, _) => new ValueTask<OperationResult>(committed)));
        OperationResult result = await runtime.RunAsync(
            new object(),
            RuntimeHarness.Alpha,
            RuntimeHarness.Start.AddMinutes(5));
        Assert.AreSame(committed, result);
        Assert.AreEqual(OperationOutcome.Committed, result.Outcome);
    }
}
