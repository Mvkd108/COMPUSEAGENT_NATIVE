namespace Compuse.Contracts.Tests;

[TestClass]
public sealed class OperationResultTests
{
    private static readonly DateTimeOffset UtcTimestamp = new(2026, 8, 15, 6, 9, 0, TimeSpan.Zero);

    [TestMethod]
    public void CommittedShapeRequiresObservationAndOmitsTypedDetails()
    {
        CorrelationId correlationId = CorrelationId.New();
        VerificationEvidence observation = Observation();
        VerificationEvidence diagnostic = Diagnostic();
        OperationResult result = OperationResult.Committed(correlationId, [observation, diagnostic]);

        Assert.AreEqual(correlationId, result.CorrelationId);
        Assert.AreEqual(OperationOutcome.Committed, result.Outcome);
        Assert.IsNull(result.Refusal);
        Assert.IsNull(result.Failure);
        Assert.AreEqual(2, result.Evidence.Count);
        Assert.AreEqual(observation, result.Evidence[0]);
        Assert.AreEqual(diagnostic, result.Evidence[1]);
    }

    [TestMethod]
    public void RefusedShapeContainsOnlyRefusalDetail()
    {
        CorrelationId correlationId = CorrelationId.New();
        RefusalInfo refusal = new("policy_denied", "The operation was not attempted.");
        OperationResult result = OperationResult.Refused(correlationId, refusal);

        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreSame(refusal, result.Refusal);
        Assert.IsNull(result.Failure);
        Assert.AreEqual(0, result.Evidence.Count);
    }

    [TestMethod]
    public void FailedShapeContainsOnlyFailureDetail()
    {
        CorrelationId correlationId = CorrelationId.New();
        FailureInfo failure = new("timeout_expired", "The operation failed.", isTransient: true);
        VerificationEvidence apiReturn = OsApiReturn();
        OperationResult result = OperationResult.Failed(correlationId, failure, [apiReturn]);

        Assert.AreEqual(OperationOutcome.Failed, result.Outcome);
        Assert.IsNull(result.Refusal);
        Assert.AreSame(failure, result.Failure);
        Assert.AreEqual(1, result.Evidence.Count);
        Assert.AreEqual(apiReturn, result.Evidence[0]);
    }

    [TestMethod]
    public void IndeterminateShapeOmitsTypedDetails()
    {
        CorrelationId correlationId = CorrelationId.New();
        OperationResult result = OperationResult.Indeterminate(correlationId);

        Assert.AreEqual(OperationOutcome.Indeterminate, result.Outcome);
        Assert.IsNull(result.Refusal);
        Assert.IsNull(result.Failure);
        Assert.IsNotNull(result.Evidence);
        Assert.AreEqual(0, result.Evidence.Count);
    }

    [TestMethod]
    public void EveryFactoryRejectsNullCorrelationId()
    {
        RefusalInfo refusal = new("policy_denied", "The operation was not attempted.");
        FailureInfo failure = new("timeout_expired", "The operation failed.", isTransient: false);

        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => OperationResult.Committed(null!, [Observation()]));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => OperationResult.Refused(null!, refusal));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => OperationResult.Failed(null!, failure));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => OperationResult.Indeterminate(null!));
    }

    [TestMethod]
    public void NullEvidenceElementIsRejected()
    {
        VerificationEvidence?[] evidence = [Observation(), null];

        _ = Assert.ThrowsExactly<ArgumentException>(
            () => OperationResult.Committed(CorrelationId.New(), evidence!));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => OperationResult.Indeterminate(CorrelationId.New(), evidence!));
    }

    [TestMethod]
    public void CallerCollectionMutationDoesNotMutateResult()
    {
        List<VerificationEvidence> evidence = [Observation()];
        OperationResult result = OperationResult.Committed(CorrelationId.New(), evidence);

        evidence.Add(Diagnostic());

        Assert.AreEqual(1, result.Evidence.Count);
        Assert.AreEqual(VerificationEvidenceKind.ExternalSideEffectObservation, result.Evidence[0].Kind);
    }

    [TestMethod]
    public void CommittedWithEmptyEvidenceIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => OperationResult.Committed(CorrelationId.New(), []));
    }

    [TestMethod]
    public void CommittedWithOnlyOsApiReturnIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => OperationResult.Committed(CorrelationId.New(), [OsApiReturn()]));
    }

    [TestMethod]
    public void CommittedWithOnlyDiagnosticArtifactIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => OperationResult.Committed(CorrelationId.New(), [Diagnostic()]));
    }

    [TestMethod]
    public void CommittedWithMixtureLackingExternalObservationIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => OperationResult.Committed(CorrelationId.New(), [OsApiReturn(), Diagnostic()]));
    }

    [TestMethod]
    public void OsApiReturnAloneCannotProduceCommitted()
    {
        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => OperationResult.Committed(CorrelationId.New(), [OsApiReturn()]));

        StringAssert.Contains(exception.Message, "OS or API return");
    }

    [TestMethod]
    public void CommittedNullEvidenceEnumerableIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => OperationResult.Committed(CorrelationId.New(), null!));
    }

    [TestMethod]
    public void RefusedAndFailedRejectNullTypedDetails()
    {
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => OperationResult.Refused(CorrelationId.New(), null!));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => OperationResult.Failed(CorrelationId.New(), null!));
    }

    [TestMethod]
    public void EvidenceSnapshotIsNotTheCallerCollection()
    {
        List<VerificationEvidence> evidence = [Observation()];
        OperationResult result = OperationResult.Committed(CorrelationId.New(), evidence);

        Assert.AreNotSame(evidence, result.Evidence);
        _ = Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<VerificationEvidence>)result.Evidence).Add(Diagnostic()));
    }

    private static VerificationEvidence Observation()
    {
        return new VerificationEvidence(
            VerificationEvidenceKind.ExternalSideEffectObservation,
            "path_exists",
            "The requested external state was observed independently of the API return.",
            UtcTimestamp);
    }

    private static VerificationEvidence OsApiReturn()
    {
        return new VerificationEvidence(
            VerificationEvidenceKind.OsApiReturn,
            "api_return",
            "The OS API returned a success code.",
            UtcTimestamp);
    }

    private static VerificationEvidence Diagnostic()
    {
        return new VerificationEvidence(
            VerificationEvidenceKind.DiagnosticArtifact,
            "trace_capture",
            "A diagnostic artifact was captured.",
            UtcTimestamp,
            "artifacts/trace.log");
    }
}
