using Compuse.Contracts;
using Google.Protobuf.WellKnownTypes;
using ProtoFailureInfo = Compuse.Protocol.V1.FailureInfo;
using ProtoOperationOutcome = Compuse.Protocol.V1.OperationOutcome;
using ProtoOperationResultEnvelope = Compuse.Protocol.V1.OperationResultEnvelope;
using ProtoRefusalInfo = Compuse.Protocol.V1.RefusalInfo;
using ProtoVerificationEvidence = Compuse.Protocol.V1.VerificationEvidence;
using ProtoVerificationEvidenceKind = Compuse.Protocol.V1.VerificationEvidenceKind;

namespace Compuse.Protocol;

public static class OperationResultProtoMapper
{
    private const long UnixEpochTicks = 621_355_968_000_000_000;
    private const long MinBclTicks = 0;
    private const long MaxBclTicks = 3_155_378_975_999_999_999;
    private const long TicksPerSecond = 10_000_000;
    private const int NanosecondsPerTick = 100;
    private const int MaxNanos = 999_999_999;

    public static ProtoOperationResultEnvelope ToProto(OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ProtoOperationResultEnvelope envelope = new()
        {
            CorrelationId = result.CorrelationId.ToString(),
            Outcome = ToProtoOutcome(result.Outcome)
        };

        switch (result.Outcome)
        {
            case OperationOutcome.Refused:
                envelope.Refusal = ToProtoRefusal(result.Refusal);
                break;
            case OperationOutcome.Failed:
                envelope.Failure = ToProtoFailure(result.Failure);
                break;
            case OperationOutcome.Committed:
            case OperationOutcome.Indeterminate:
                break;
            default:
                throw new InvalidOperationException("Operation outcome must be a defined managed value.");
        }

        foreach (VerificationEvidence item in result.Evidence)
        {
            envelope.Evidence.Add(ToProtoEvidence(item));
        }

        return envelope;
    }

    public static OperationResult FromProto(ProtoOperationResultEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);

        CorrelationId correlationId = ParseCorrelationId(message.CorrelationId);
        OperationOutcome outcome = FromProtoOutcome(message.Outcome);
        EnsureDetailConsistency(outcome, message.DetailCase);

        List<VerificationEvidence> evidence = new(message.Evidence.Count);
        for (int index = 0; index < message.Evidence.Count; index++)
        {
            evidence.Add(FromProtoEvidence(message.Evidence[index], index));
        }

        return outcome switch
        {
            OperationOutcome.Committed => CreateCommitted(correlationId, evidence),
            OperationOutcome.Refused => OperationResult.Refused(
                correlationId,
                FromProtoRefusal(message.Refusal),
                evidence),
            OperationOutcome.Failed => OperationResult.Failed(
                correlationId,
                FromProtoFailure(message.Failure),
                evidence),
            OperationOutcome.Indeterminate => OperationResult.Indeterminate(correlationId, evidence),
            _ => throw Create(
                ProtocolContractErrorCode.UnsupportedOutcome,
                "outcome",
                "The operation outcome is not a supported semantic value.")
        };
    }

    private static ProtoOperationOutcome ToProtoOutcome(OperationOutcome outcome)
    {
        return outcome switch
        {
            OperationOutcome.Committed => ProtoOperationOutcome.Committed,
            OperationOutcome.Refused => ProtoOperationOutcome.Refused,
            OperationOutcome.Failed => ProtoOperationOutcome.Failed,
            OperationOutcome.Indeterminate => ProtoOperationOutcome.Indeterminate,
            _ => throw new InvalidOperationException("Operation outcome must be a defined managed value.")
        };
    }

    private static OperationOutcome FromProtoOutcome(ProtoOperationOutcome outcome)
    {
        if (!System.Enum.IsDefined(outcome) || outcome == ProtoOperationOutcome.Unspecified)
        {
            throw Create(
                ProtocolContractErrorCode.UnsupportedOutcome,
                "outcome",
                "The operation outcome is not a supported semantic value.");
        }

        return outcome switch
        {
            ProtoOperationOutcome.Committed => OperationOutcome.Committed,
            ProtoOperationOutcome.Refused => OperationOutcome.Refused,
            ProtoOperationOutcome.Failed => OperationOutcome.Failed,
            ProtoOperationOutcome.Indeterminate => OperationOutcome.Indeterminate,
            _ => throw Create(
                ProtocolContractErrorCode.UnsupportedOutcome,
                "outcome",
                "The operation outcome is not a supported semantic value.")
        };
    }

    private static void EnsureDetailConsistency(
        OperationOutcome outcome,
        ProtoOperationResultEnvelope.DetailOneofCase detailCase)
    {
        bool valid = outcome switch
        {
            OperationOutcome.Committed => detailCase == ProtoOperationResultEnvelope.DetailOneofCase.None,
            OperationOutcome.Indeterminate => detailCase == ProtoOperationResultEnvelope.DetailOneofCase.None,
            OperationOutcome.Refused => detailCase == ProtoOperationResultEnvelope.DetailOneofCase.Refusal,
            OperationOutcome.Failed => detailCase == ProtoOperationResultEnvelope.DetailOneofCase.Failure,
            _ => false
        };

        if (!valid)
        {
            throw Create(
                ProtocolContractErrorCode.OutcomeDetailMismatch,
                "detail",
                "The outcome detail case does not match the operation outcome.");
        }
    }

    private static CorrelationId ParseCorrelationId(string value)
    {
        CorrelationId parsed;
        try
        {
            parsed = CorrelationId.Parse(value);
        }
        catch (ArgumentNullException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidCorrelationId,
                "correlation_id",
                "The correlation identifier is not canonical.",
                ex);
        }
        catch (FormatException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidCorrelationId,
                "correlation_id",
                "The correlation identifier is not canonical.",
                ex);
        }
        catch (ArgumentException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidCorrelationId,
                "correlation_id",
                "The correlation identifier is not canonical.",
                ex);
        }

        if (!string.Equals(value, parsed.ToString(), StringComparison.Ordinal))
        {
            throw Create(
                ProtocolContractErrorCode.InvalidCorrelationId,
                "correlation_id",
                "The correlation identifier is not canonical.");
        }

        return parsed;
    }

    private static ProtoRefusalInfo ToProtoRefusal(RefusalInfo? refusal)
    {
        if (refusal is null)
        {
            throw new InvalidOperationException("A refused result requires refusal information.");
        }

        return new ProtoRefusalInfo
        {
            Code = refusal.Code,
            Message = refusal.Message
        };
    }

    private static RefusalInfo FromProtoRefusal(ProtoRefusalInfo refusal)
    {
        try
        {
            return new RefusalInfo(refusal.Code, refusal.Message);
        }
        catch (ArgumentException ex) when (ex.ParamName == "code")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidRefusal,
                "refusal.code",
                "The refusal code is not a valid contract code.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "message")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidRefusal,
                "refusal.message",
                "The refusal message is not a valid contract message.",
                ex);
        }
    }

    private static ProtoFailureInfo ToProtoFailure(FailureInfo? failure)
    {
        if (failure is null)
        {
            throw new InvalidOperationException("A failed result requires failure information.");
        }

        return new ProtoFailureInfo
        {
            Code = failure.Code,
            Message = failure.Message,
            IsTransient = failure.IsTransient
        };
    }

    private static FailureInfo FromProtoFailure(ProtoFailureInfo failure)
    {
        try
        {
            return new FailureInfo(failure.Code, failure.Message, failure.IsTransient);
        }
        catch (ArgumentException ex) when (ex.ParamName == "code")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidFailure,
                "failure.code",
                "The failure code is not a valid contract code.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "message")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidFailure,
                "failure.message",
                "The failure message is not a valid contract message.",
                ex);
        }
    }

    private static ProtoVerificationEvidence ToProtoEvidence(VerificationEvidence evidence)
    {
        ProtoVerificationEvidence proto = new()
        {
            Kind = ToProtoKind(evidence.Kind),
            Code = evidence.Code,
            Description = evidence.Description,
            ObservedAt = Timestamp.FromDateTimeOffset(evidence.ObservedAtUtc)
        };

        if (evidence.ArtifactReference is not null)
        {
            proto.ArtifactReference = evidence.ArtifactReference;
        }

        return proto;
    }

    private static VerificationEvidence FromProtoEvidence(ProtoVerificationEvidence evidence, int index)
    {
        VerificationEvidenceKind kind = FromProtoKind(evidence.Kind, index);
        DateTimeOffset observedAtUtc = FromProtoTimestamp(evidence.ObservedAt, index);
        string? artifactReference = evidence.HasArtifactReference ? evidence.ArtifactReference : null;

        try
        {
            return new VerificationEvidence(
                kind,
                evidence.Code,
                evidence.Description,
                observedAtUtc,
                artifactReference);
        }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "kind")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidEvidence,
                $"evidence[{index}].kind",
                "The evidence kind is not a supported semantic value.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "code")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidEvidence,
                $"evidence[{index}].code",
                "The evidence code is not a valid contract code.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "description")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidEvidence,
                $"evidence[{index}].description",
                "The evidence description is not a valid contract message.",
                ex);
        }
        catch (ArgumentException ex) when (ex.ParamName == "artifactReference")
        {
            throw Create(
                ProtocolContractErrorCode.InvalidEvidence,
                $"evidence[{index}].artifact_reference",
                "The artifact reference is not a valid contract value.",
                ex);
        }
    }

    private static ProtoVerificationEvidenceKind ToProtoKind(VerificationEvidenceKind kind)
    {
        return kind switch
        {
            VerificationEvidenceKind.OsApiReturn => ProtoVerificationEvidenceKind.OsApiReturn,
            VerificationEvidenceKind.ExternalSideEffectObservation =>
                ProtoVerificationEvidenceKind.ExternalSideEffectObservation,
            VerificationEvidenceKind.DiagnosticArtifact => ProtoVerificationEvidenceKind.DiagnosticArtifact,
            _ => throw new InvalidOperationException("Evidence kind must be a defined managed value.")
        };
    }

    private static VerificationEvidenceKind FromProtoKind(ProtoVerificationEvidenceKind kind, int index)
    {
        if (!System.Enum.IsDefined(kind) || kind == ProtoVerificationEvidenceKind.Unspecified)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidEvidence,
                $"evidence[{index}].kind",
                "The evidence kind is not a supported semantic value.");
        }

        return kind switch
        {
            ProtoVerificationEvidenceKind.OsApiReturn => VerificationEvidenceKind.OsApiReturn,
            ProtoVerificationEvidenceKind.ExternalSideEffectObservation =>
                VerificationEvidenceKind.ExternalSideEffectObservation,
            ProtoVerificationEvidenceKind.DiagnosticArtifact => VerificationEvidenceKind.DiagnosticArtifact,
            _ => throw Create(
                ProtocolContractErrorCode.InvalidEvidence,
                $"evidence[{index}].kind",
                "The evidence kind is not a supported semantic value.")
        };
    }

    private static DateTimeOffset FromProtoTimestamp(Timestamp? timestamp, int index)
    {
        if (timestamp is null)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTimestamp,
                $"evidence[{index}].observed_at",
                "The evidence timestamp is missing or not exactly representable.");
        }

        if (timestamp.Nanos is < 0 or > MaxNanos || timestamp.Nanos % NanosecondsPerTick != 0)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTimestamp,
                $"evidence[{index}].observed_at",
                "The evidence timestamp is missing or not exactly representable.");
        }

        long tickOffset;
        try
        {
            checked
            {
                tickOffset = (timestamp.Seconds * TicksPerSecond) + (timestamp.Nanos / NanosecondsPerTick);
            }
        }
        catch (OverflowException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTimestamp,
                $"evidence[{index}].observed_at",
                "The evidence timestamp is missing or not exactly representable.",
                ex);
        }

        long ticks;
        try
        {
            checked
            {
                ticks = UnixEpochTicks + tickOffset;
            }
        }
        catch (OverflowException ex)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTimestamp,
                $"evidence[{index}].observed_at",
                "The evidence timestamp is missing or not exactly representable.",
                ex);
        }

        if (ticks is < MinBclTicks or > MaxBclTicks)
        {
            throw Create(
                ProtocolContractErrorCode.InvalidTimestamp,
                $"evidence[{index}].observed_at",
                "The evidence timestamp is missing or not exactly representable.");
        }

        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static OperationResult CreateCommitted(
        CorrelationId correlationId,
        IReadOnlyList<VerificationEvidence> evidence)
    {
        try
        {
            return OperationResult.Committed(correlationId, evidence);
        }
        catch (ArgumentException ex)
        {
            throw Create(
                ProtocolContractErrorCode.MissingCommitmentEvidence,
                "evidence",
                "A committed result requires at least one external side-effect observation.",
                ex);
        }
    }

    private static ProtocolContractException Create(
        ProtocolContractErrorCode code,
        string fieldPath,
        string message,
        Exception? innerException = null) =>
        new(code, fieldPath, message, innerException);
}
