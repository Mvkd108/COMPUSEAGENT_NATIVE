using Compuse.Contracts;

namespace Compuse.Runtime.Tests;

internal sealed class ScriptedHandler<TRequest> : IOperationHandler<TRequest>
    where TRequest : notnull
{
    internal ScriptedHandler(Func<TRequest, OperationExecutionContext, CancellationToken, ValueTask<OperationResult>> execute)
    {
        Execute = execute;
    }

    internal Func<TRequest, OperationExecutionContext, CancellationToken, ValueTask<OperationResult>> Execute { get; }

    internal int Invocations { get; private set; }

    internal TRequest? LastRequest { get; private set; }

    internal OperationExecutionContext? LastContext { get; private set; }

    internal CancellationToken LastToken { get; private set; }

    public ValueTask<OperationResult> ExecuteAsync(
        TRequest request,
        OperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        Invocations++;
        LastRequest = request;
        LastContext = context;
        LastToken = cancellationToken;
        return Execute(request, context, cancellationToken);
    }
}

internal static class RuntimeHarness
{
    internal static readonly DateTimeOffset Start = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    internal static ControllableOperationClock Clock() => new(Start);

    internal static OperationRuntimeOptions FastOptions(
        int maxConcurrentOperations = 32,
        TimeSpan? maxExecution = null,
        TimeSpan? shutdownDrain = null,
        TimeSpan? cleanupTimeout = null) =>
        new(
            maxConcurrentOperations,
            maxExecution ?? TimeSpan.FromSeconds(10),
            shutdownDrain ?? TimeSpan.FromSeconds(2),
            cleanupTimeout ?? TimeSpan.FromSeconds(1));

    internal static CorrelationId Id(string value) => CorrelationId.Parse(value);

    internal static CorrelationId Alpha { get; } = Id("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    internal static CorrelationId Beta { get; } = Id("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    internal static OperationResult RefusedResult(CorrelationId correlation) =>
        OperationResult.Refused(
            correlation,
            new RefusalInfo("policy_denied", "The operation was not attempted."));

    internal static OperationResult FailedResult(CorrelationId correlation) =>
        OperationResult.Failed(
            correlation,
            new FailureInfo("provider_unavailable", "The provider did not respond.", isTransient: false));

    internal static OperationResult CommittedResult(CorrelationId correlation, DateTimeOffset observedAtUtc) =>
        OperationResult.Committed(
            correlation,
            [
                new VerificationEvidence(
                    VerificationEvidenceKind.ExternalSideEffectObservation,
                    "drop_observed",
                    "The drop completed.",
                    observedAtUtc)
            ]);

    internal static void AssertKernel(
        OperationResult result,
        CorrelationId correlation,
        OperationOutcome outcome,
        string code)
    {
        Assert.AreEqual(correlation, result.CorrelationId);
        Assert.AreEqual(outcome, result.Outcome);
        Assert.AreEqual(1, result.Evidence.Count);
        Assert.AreEqual(VerificationEvidenceKind.DiagnosticArtifact, result.Evidence[0].Kind);
        Assert.AreEqual(code, result.Evidence[0].Code);
        if (outcome == OperationOutcome.Refused)
        {
            Assert.AreEqual(code, result.Refusal?.Code);
        }
        else if (outcome == OperationOutcome.Failed)
        {
            Assert.AreEqual(code, result.Failure?.Code);
            Assert.IsFalse(result.Failure!.IsTransient);
        }
    }
}
