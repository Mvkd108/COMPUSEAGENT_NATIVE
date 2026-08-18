using System.Globalization;
using Compuse.Contracts;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using DomainOutcome = Compuse.Contracts.OperationOutcome;
using ProtoEnvelope = Compuse.Protocol.V1.OperationResultEnvelope;
using ProtoEvidence = Compuse.Protocol.V1.VerificationEvidence;
using ProtoEvidenceKind = Compuse.Protocol.V1.VerificationEvidenceKind;
using ProtoFailure = Compuse.Protocol.V1.FailureInfo;
using ProtoOutcome = Compuse.Protocol.V1.OperationOutcome;
using ProtoRefusal = Compuse.Protocol.V1.RefusalInfo;

namespace Compuse.Protocol.Tests;

[TestClass]
public sealed class OperationResultProtoMapperTests
{
    private const string CanonicalCorrelation = "abcdef01-2345-6789-abcd-ef0123456789";
    private static readonly DateTimeOffset UtcTimestamp = new(2026, 8, 15, 13, 26, 56, TimeSpan.Zero);

    [TestMethod]
    public void ToProtoRejectsNull() =>
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => OperationResultProtoMapper.ToProto(null!));

    [TestMethod]
    public void FromProtoRejectsNull() =>
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => OperationResultProtoMapper.FromProto(null!));

    [TestMethod]
    public void CommittedRoundTripPreservesValues()
    {
        OperationResult original = OperationResult.Committed(
            CorrelationId.Parse(CanonicalCorrelation),
            [Observation("drop_observed", "The drop completed."), Diagnostic()]);
        AssertRoundTrip(original);
    }

    [TestMethod]
    public void RefusedRoundTripPreservesValues()
    {
        OperationResult original = OperationResult.Refused(
            CorrelationId.Parse(CanonicalCorrelation),
            new RefusalInfo("policy_denied", "The operation was not attempted."),
            [OsApiReturn()]);
        AssertRoundTrip(original);
    }

    [TestMethod]
    public void FailedRoundTripPreservesTransientAndNonTransient()
    {
        OperationResult transient = OperationResult.Failed(
            CorrelationId.Parse(CanonicalCorrelation),
            new FailureInfo("timeout_expired", "The operation failed.", isTransient: true),
            [OsApiReturn()]);
        OperationResult persistent = OperationResult.Failed(
            CorrelationId.Parse(CanonicalCorrelation),
            new FailureInfo("provider_unavailable", "The provider did not respond.", isTransient: false));
        AssertRoundTrip(transient);
        AssertRoundTrip(persistent);
        Assert.IsTrue(OperationResultProtoMapper.ToProto(transient).Failure.IsTransient);
        Assert.IsFalse(OperationResultProtoMapper.ToProto(persistent).Failure.IsTransient);
    }

    [TestMethod]
    public void IndeterminateRoundTripAllowsEmptyEvidence()
    {
        OperationResult original = OperationResult.Indeterminate(CorrelationId.Parse(CanonicalCorrelation));
        AssertRoundTrip(original);
        Assert.AreEqual(0, OperationResultProtoMapper.ToProto(original).Evidence.Count);
    }

    [TestMethod]
    public void EmptyEvidenceIsAllowedForRefusedAndFailed()
    {
        OperationResult refused = OperationResult.Refused(
            CorrelationId.Parse(CanonicalCorrelation),
            new RefusalInfo("policy_denied", "The operation was not attempted."));
        OperationResult failed = OperationResult.Failed(
            CorrelationId.Parse(CanonicalCorrelation),
            new FailureInfo("timeout_expired", "The operation failed.", isTransient: false));

        AssertRoundTrip(refused);
        AssertRoundTrip(failed);
        Assert.AreEqual(0, OperationResultProtoMapper.ToProto(refused).Evidence.Count);
        Assert.AreEqual(0, OperationResultProtoMapper.ToProto(failed).Evidence.Count);
    }

    [TestMethod]
    public void EvidenceOrderAndArtifactPresenceRoundTrip()
    {
        VerificationEvidence first = Observation("first_observed", "First observation.");
        VerificationEvidence second = Diagnostic("second_diag", "Second diagnostic.", "diag://second");
        OperationResult original = OperationResult.Committed(
            CorrelationId.Parse(CanonicalCorrelation),
            [first, second]);

        ProtoEnvelope proto = OperationResultProtoMapper.ToProto(original);
        Assert.AreEqual(2, proto.Evidence.Count);
        Assert.IsFalse(proto.Evidence[0].HasArtifactReference);
        Assert.IsTrue(proto.Evidence[1].HasArtifactReference);
        Assert.AreEqual("diag://second", proto.Evidence[1].ArtifactReference);

        OperationResult restored = OperationResultProtoMapper.FromProto(proto);
        Assert.AreEqual(2, restored.Evidence.Count);
        Assert.IsNull(restored.Evidence[0].ArtifactReference);
        Assert.AreEqual("diag://second", restored.Evidence[1].ArtifactReference);
        Assert.AreEqual(first.Code, restored.Evidence[0].Code);
        Assert.AreEqual(second.Code, restored.Evidence[1].Code);
    }

    [TestMethod]
    public void CorrelationIsEmittedAndAcceptedOnlyAsLowercaseCanonicalForm()
    {
        OperationResult original = OperationResult.Indeterminate(
            CorrelationId.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789"));
        ProtoEnvelope proto = OperationResultProtoMapper.ToProto(original);
        Assert.AreEqual(CanonicalCorrelation, proto.CorrelationId);
        Assert.AreEqual(CanonicalCorrelation, OperationResultProtoMapper.FromProto(proto).CorrelationId.ToString());
    }

    [TestMethod]
    public void UppercasePaddedEmptyAndMalformedCorrelationIdsAreRejected()
    {
        AssertCorrelationRejected("ABCDEF01-2345-6789-ABCD-EF0123456789");
        AssertCorrelationRejected(" " + CanonicalCorrelation);
        AssertCorrelationRejected(CanonicalCorrelation + " ");
        AssertCorrelationRejected("\t" + CanonicalCorrelation);
        AssertCorrelationRejected(string.Empty);
        AssertCorrelationRejected("{abcdef01-2345-6789-abcd-ef0123456789}");
        AssertCorrelationRejected("(abcdef01-2345-6789-abcd-ef0123456789)");
        AssertCorrelationRejected("abcdef0123456789abcdef0123456789");
        AssertCorrelationRejected("not-a-guid");
        AssertCorrelationRejected("00000000-0000-0000-0000-000000000000");
    }

    [TestMethod]
    public void UnspecifiedAndUnknownOutcomesAreRejected()
    {
        ProtoEnvelope unspecified = CreateIndeterminate();
        unspecified.Outcome = ProtoOutcome.Unspecified;
        AssertMapped(unspecified, ProtocolContractErrorCode.UnsupportedOutcome, "outcome");

        ProtoEnvelope unknown = CreateIndeterminate();
        unknown.Outcome = (ProtoOutcome)99;
        AssertMapped(unknown, ProtocolContractErrorCode.UnsupportedOutcome, "outcome");
    }

    [TestMethod]
    public void OutcomeDetailMismatchesAreRejected()
    {
        ProtoEnvelope refusalOnFailed = CreateFailed();
        refusalOnFailed.Refusal = CreateRefusal();
        AssertMapped(refusalOnFailed, ProtocolContractErrorCode.OutcomeDetailMismatch, "detail");

        ProtoEnvelope failureOnRefused = CreateRefused();
        failureOnRefused.Failure = CreateFailure();
        AssertMapped(failureOnRefused, ProtocolContractErrorCode.OutcomeDetailMismatch, "detail");

        ProtoEnvelope detailOnCommitted = CreateCommitted();
        detailOnCommitted.Refusal = CreateRefusal();
        AssertMapped(detailOnCommitted, ProtocolContractErrorCode.OutcomeDetailMismatch, "detail");

        ProtoEnvelope detailOnIndeterminate = CreateIndeterminate();
        detailOnIndeterminate.Failure = CreateFailure();
        AssertMapped(detailOnIndeterminate, ProtocolContractErrorCode.OutcomeDetailMismatch, "detail");

        ProtoEnvelope missingRefusal = CreateRefused();
        missingRefusal.ClearDetail();
        AssertMapped(missingRefusal, ProtocolContractErrorCode.OutcomeDetailMismatch, "detail");

        ProtoEnvelope missingFailure = CreateFailed();
        missingFailure.ClearDetail();
        AssertMapped(missingFailure, ProtocolContractErrorCode.OutcomeDetailMismatch, "detail");
    }

    [TestMethod]
    public void InvalidRefusalValuesAreRejectedWithFieldPaths()
    {
        AssertMapped(
            WithRefusal("", "The operation was not attempted."),
            ProtocolContractErrorCode.InvalidRefusal,
            "refusal.code");
        AssertMapped(
            WithRefusal("Policy_denied", "The operation was not attempted."),
            ProtocolContractErrorCode.InvalidRefusal,
            "refusal.code");
        AssertMapped(
            WithRefusal("policy-denied", "The operation was not attempted."),
            ProtocolContractErrorCode.InvalidRefusal,
            "refusal.code");
        AssertMapped(
            WithRefusal("policy_dénied", "The operation was not attempted."),
            ProtocolContractErrorCode.InvalidRefusal,
            "refusal.code");
        AssertMapped(
            WithRefusal(new string('a', 65), "The operation was not attempted."),
            ProtocolContractErrorCode.InvalidRefusal,
            "refusal.code");
        AssertMapped(
            WithRefusal("policy_denied", ""),
            ProtocolContractErrorCode.InvalidRefusal,
            "refusal.message");
        AssertMapped(
            WithRefusal("policy_denied", "   "),
            ProtocolContractErrorCode.InvalidRefusal,
            "refusal.message");
        AssertMapped(
            WithRefusal("policy_denied", new string('m', 1025)),
            ProtocolContractErrorCode.InvalidRefusal,
            "refusal.message");
    }

    [TestMethod]
    public void InvalidFailureValuesAreRejectedWithFieldPaths()
    {
        AssertMapped(
            WithFailure("", "The operation failed."),
            ProtocolContractErrorCode.InvalidFailure,
            "failure.code");
        AssertMapped(
            WithFailure("timeout_expired", "   "),
            ProtocolContractErrorCode.InvalidFailure,
            "failure.message");
    }

    [TestMethod]
    public void ZeroAndUnknownEvidenceKindsAreRejected()
    {
        AssertMapped(
            WithEvidenceKind(ProtoEvidenceKind.Unspecified),
            ProtocolContractErrorCode.InvalidEvidence,
            "evidence[0].kind");
        AssertMapped(
            WithEvidenceKind((ProtoEvidenceKind)99),
            ProtocolContractErrorCode.InvalidEvidence,
            "evidence[0].kind");
    }

    [TestMethod]
    public void InvalidEvidenceStringsAndArtifactReferencesAreRejected()
    {
        AssertMapped(
            WithEvidence(code: "", description: "Observed."),
            ProtocolContractErrorCode.InvalidEvidence,
            "evidence[0].code");
        AssertMapped(
            WithEvidence(code: "drop_observed", description: "   "),
            ProtocolContractErrorCode.InvalidEvidence,
            "evidence[0].description");
        AssertMapped(
            WithArtifact(""),
            ProtocolContractErrorCode.InvalidEvidence,
            "evidence[0].artifact_reference");
        AssertMapped(
            WithArtifact("   "),
            ProtocolContractErrorCode.InvalidEvidence,
            "evidence[0].artifact_reference");
        AssertMapped(
            WithArtifact(new string('a', 2049)),
            ProtocolContractErrorCode.InvalidEvidence,
            "evidence[0].artifact_reference");
    }

    [TestMethod]
    public void MissingOutOfRangeAndSubTickTimestampsAreRejected()
    {
        ProtoEnvelope missing = CreateCommitted();
        missing.Evidence[0].ObservedAt = null!;
        AssertMapped(missing, ProtocolContractErrorCode.InvalidTimestamp, "evidence[0].observed_at");

        AssertMapped(
            WithTimestamp(-62_135_596_801, 0),
            ProtocolContractErrorCode.InvalidTimestamp,
            "evidence[0].observed_at");
        AssertMapped(
            WithTimestamp(253_402_300_800, 0),
            ProtocolContractErrorCode.InvalidTimestamp,
            "evidence[0].observed_at");
        AssertMapped(
            WithTimestamp(0, -1),
            ProtocolContractErrorCode.InvalidTimestamp,
            "evidence[0].observed_at");
        AssertMapped(
            WithTimestamp(0, 1_000_000_000),
            ProtocolContractErrorCode.InvalidTimestamp,
            "evidence[0].observed_at");
        AssertMapped(
            WithTimestamp(0, 50),
            ProtocolContractErrorCode.InvalidTimestamp,
            "evidence[0].observed_at");
    }

    [TestMethod]
    public void CommittedWithoutExternalObservationIsRejected()
    {
        AssertMapped(
            CommittedWithEvidence(),
            ProtocolContractErrorCode.MissingCommitmentEvidence,
            "evidence");
        AssertMapped(
            CommittedWithEvidence(OsApiProto()),
            ProtocolContractErrorCode.MissingCommitmentEvidence,
            "evidence");
        AssertMapped(
            CommittedWithEvidence(DiagnosticProto()),
            ProtocolContractErrorCode.MissingCommitmentEvidence,
            "evidence");
        AssertMapped(
            CommittedWithEvidence(OsApiProto(), DiagnosticProto()),
            ProtocolContractErrorCode.MissingCommitmentEvidence,
            "evidence");
    }

    [TestMethod]
    public void UnknownFieldsAreToleratedWhenKnownFieldsAreValid()
    {
        ProtoEnvelope proto = OperationResultProtoMapper.ToProto(
            OperationResult.Indeterminate(CorrelationId.Parse(CanonicalCorrelation)));
        byte[] withUnknown = [.. proto.ToByteArray(), .. Convert.FromHexString("980601")];
        ProtoEnvelope parsed = ProtoEnvelope.Parser.ParseFrom(withUnknown);
        OperationResult restored = OperationResultProtoMapper.FromProto(parsed);
        Assert.AreEqual(DomainOutcome.Indeterminate, restored.Outcome);
        Assert.AreEqual(CanonicalCorrelation, restored.CorrelationId.ToString());
    }

    [TestMethod]
    public void ErrorCodesAreDefinedAndHaveNoZeroMember()
    {
        Dictionary<string, int> expected = new()
        {
            ["UnsupportedOutcome"] = 1,
            ["InvalidCorrelationId"] = 2,
            ["OutcomeDetailMismatch"] = 3,
            ["InvalidRefusal"] = 4,
            ["InvalidFailure"] = 5,
            ["InvalidEvidence"] = 6,
            ["InvalidTimestamp"] = 7,
            ["MissingCommitmentEvidence"] = 8
        };

        string[] names = System.Enum.GetNames<ProtocolContractErrorCode>();
        Assert.AreEqual(expected.Count, names.Length);
        foreach (KeyValuePair<string, int> pair in expected)
        {
            ProtocolContractErrorCode parsed = System.Enum.Parse<ProtocolContractErrorCode>(pair.Key);
            Assert.AreEqual(pair.Value, Convert.ToInt32(parsed, CultureInfo.InvariantCulture));
        }

        ProtocolContractErrorCode unspecified =
            (ProtocolContractErrorCode)System.Enum.ToObject(typeof(ProtocolContractErrorCode), 0);
        Assert.IsFalse(System.Enum.IsDefined(unspecified));
    }

    private static void AssertRoundTrip(OperationResult original)
    {
        ProtoEnvelope proto = OperationResultProtoMapper.ToProto(original);
        OperationResult restored = OperationResultProtoMapper.FromProto(proto);
        Assert.AreEqual(original.CorrelationId, restored.CorrelationId);
        Assert.AreEqual(original.Outcome, restored.Outcome);
        Assert.AreEqual(original.Refusal, restored.Refusal);
        Assert.AreEqual(original.Failure, restored.Failure);
        Assert.AreEqual(original.Evidence.Count, restored.Evidence.Count);
        for (int index = 0; index < original.Evidence.Count; index++)
        {
            Assert.AreEqual(original.Evidence[index], restored.Evidence[index]);
        }

        ProtoEnvelope again = OperationResultProtoMapper.ToProto(restored);
        Assert.AreEqual(proto.ToByteString(), again.ToByteString());
    }

    private static void AssertCorrelationRejected(string correlationId)
    {
        ProtoEnvelope envelope = CreateIndeterminate();
        envelope.CorrelationId = correlationId;
        AssertMapped(envelope, ProtocolContractErrorCode.InvalidCorrelationId, "correlation_id");
    }

    private static void AssertMapped(
        ProtoEnvelope envelope,
        ProtocolContractErrorCode code,
        string fieldPath)
    {
        ProtocolContractException ex = Assert.ThrowsExactly<ProtocolContractException>(
            () => OperationResultProtoMapper.FromProto(envelope));
        Assert.AreEqual(code, ex.Code);
        Assert.AreEqual(fieldPath, ex.FieldPath);
    }

    private static ProtoEnvelope CreateIndeterminate() => new()
    {
        CorrelationId = CanonicalCorrelation,
        Outcome = ProtoOutcome.Indeterminate
    };

    private static ProtoEnvelope CreateRefused() => new()
    {
        CorrelationId = CanonicalCorrelation,
        Outcome = ProtoOutcome.Refused,
        Refusal = CreateRefusal()
    };

    private static ProtoEnvelope CreateFailed() => new()
    {
        CorrelationId = CanonicalCorrelation,
        Outcome = ProtoOutcome.Failed,
        Failure = CreateFailure()
    };

    private static ProtoEnvelope CreateCommitted()
    {
        ProtoEnvelope envelope = new()
        {
            CorrelationId = CanonicalCorrelation,
            Outcome = ProtoOutcome.Committed
        };
        envelope.Evidence.Add(ObservationProto());
        return envelope;
    }

    private static ProtoRefusal CreateRefusal() => new()
    {
        Code = "policy_denied",
        Message = "The operation was not attempted."
    };

    private static ProtoFailure CreateFailure() => new()
    {
        Code = "timeout_expired",
        Message = "The operation failed.",
        IsTransient = true
    };

    private static ProtoEnvelope WithRefusal(string code, string message)
    {
        ProtoEnvelope envelope = CreateRefused();
        envelope.Refusal = new ProtoRefusal { Code = code, Message = message };
        return envelope;
    }

    private static ProtoEnvelope WithFailure(string code, string message)
    {
        ProtoEnvelope envelope = CreateFailed();
        envelope.Failure = new ProtoFailure { Code = code, Message = message, IsTransient = false };
        return envelope;
    }

    private static ProtoEnvelope WithEvidenceKind(ProtoEvidenceKind kind)
    {
        ProtoEvidence evidence = ObservationProto();
        evidence.Kind = kind;
        return CommittedWithEvidence(evidence);
    }

    private static ProtoEnvelope WithEvidence(string code, string description)
    {
        ProtoEvidence evidence = ObservationProto();
        evidence.Code = code;
        evidence.Description = description;
        return CommittedWithEvidence(evidence);
    }

    private static ProtoEnvelope WithArtifact(string artifactReference)
    {
        ProtoEvidence evidence = ObservationProto();
        evidence.ArtifactReference = artifactReference;
        return CommittedWithEvidence(evidence);
    }

    private static ProtoEnvelope WithTimestamp(long seconds, int nanos)
    {
        ProtoEvidence evidence = ObservationProto();
        evidence.ObservedAt = new Timestamp { Seconds = seconds, Nanos = nanos };
        return CommittedWithEvidence(evidence);
    }

    private static ProtoEnvelope CommittedWithEvidence(params ProtoEvidence[] evidence)
    {
        ProtoEnvelope envelope = new()
        {
            CorrelationId = CanonicalCorrelation,
            Outcome = ProtoOutcome.Committed
        };
        envelope.Evidence.AddRange(evidence);
        return envelope;
    }

    private static ProtoEvidence ObservationProto() => new()
    {
        Kind = ProtoEvidenceKind.ExternalSideEffectObservation,
        Code = "drop_observed",
        Description = "The drop completed.",
        ObservedAt = Timestamp.FromDateTimeOffset(UtcTimestamp)
    };

    private static ProtoEvidence OsApiProto() => new()
    {
        Kind = ProtoEvidenceKind.OsApiReturn,
        Code = "api_succeeded",
        Description = "The API returned success.",
        ObservedAt = Timestamp.FromDateTimeOffset(UtcTimestamp)
    };

    private static ProtoEvidence DiagnosticProto() => new()
    {
        Kind = ProtoEvidenceKind.DiagnosticArtifact,
        Code = "trace_captured",
        Description = "A diagnostic artifact was captured.",
        ObservedAt = Timestamp.FromDateTimeOffset(UtcTimestamp),
        ArtifactReference = "diag://trace"
    };

    private static VerificationEvidence Observation(
        string code = "drop_observed",
        string description = "The drop completed.") =>
        new(VerificationEvidenceKind.ExternalSideEffectObservation, code, description, UtcTimestamp);

    private static VerificationEvidence Diagnostic(
        string code = "trace_captured",
        string description = "A diagnostic artifact was captured.",
        string? artifact = "diag://trace") =>
        new(VerificationEvidenceKind.DiagnosticArtifact, code, description, UtcTimestamp, artifact);

    private static VerificationEvidence OsApiReturn() =>
        new(VerificationEvidenceKind.OsApiReturn, "api_succeeded", "The API returned success.", UtcTimestamp);
}
