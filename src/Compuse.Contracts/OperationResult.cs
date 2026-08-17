using System.Collections.ObjectModel;

namespace Compuse.Contracts;

public sealed class OperationResult
{
    private OperationResult(
        CorrelationId correlationId,
        OperationOutcome outcome,
        RefusalInfo? refusal,
        FailureInfo? failure,
        IReadOnlyList<VerificationEvidence> evidence)
    {
        CorrelationId = correlationId;
        Outcome = outcome;
        Refusal = refusal;
        Failure = failure;
        Evidence = evidence;
    }

    public CorrelationId CorrelationId { get; }

    public OperationOutcome Outcome { get; }

    public RefusalInfo? Refusal { get; }

    public FailureInfo? Failure { get; }

    public IReadOnlyList<VerificationEvidence> Evidence { get; }

    public static OperationResult Committed(
        CorrelationId correlationId,
        IEnumerable<VerificationEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(evidence);

        IReadOnlyList<VerificationEvidence> snapshot = SnapshotEvidence(evidence);
        if (snapshot.Count == 0)
        {
            throw new ArgumentException("A committed result requires a non-empty evidence snapshot.", nameof(evidence));
        }

        if (!ContainsExternalSideEffectObservation(snapshot))
        {
            throw new ArgumentException(
                "A committed result requires at least one ExternalSideEffectObservation. An OS or API return is not proof of an external side effect.",
                nameof(evidence));
        }

        return new OperationResult(
            correlationId,
            OperationOutcome.Committed,
            refusal: null,
            failure: null,
            snapshot);
    }

    public static OperationResult Refused(
        CorrelationId correlationId,
        RefusalInfo refusal,
        IEnumerable<VerificationEvidence>? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(refusal);

        return new OperationResult(
            correlationId,
            OperationOutcome.Refused,
            refusal,
            failure: null,
            SnapshotEvidence(evidence));
    }

    public static OperationResult Failed(
        CorrelationId correlationId,
        FailureInfo failure,
        IEnumerable<VerificationEvidence>? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(failure);

        return new OperationResult(
            correlationId,
            OperationOutcome.Failed,
            refusal: null,
            failure,
            SnapshotEvidence(evidence));
    }

    public static OperationResult Indeterminate(
        CorrelationId correlationId,
        IEnumerable<VerificationEvidence>? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(correlationId);

        return new OperationResult(
            correlationId,
            OperationOutcome.Indeterminate,
            refusal: null,
            failure: null,
            SnapshotEvidence(evidence));
    }

    private static IReadOnlyList<VerificationEvidence> SnapshotEvidence(
        IEnumerable<VerificationEvidence>? evidence)
    {
        if (evidence is null)
        {
            return Array.Empty<VerificationEvidence>();
        }

        List<VerificationEvidence> collected = [];
        foreach (VerificationEvidence? item in evidence)
        {
            if (item is null)
            {
                throw new ArgumentException("Evidence cannot contain null elements.", nameof(evidence));
            }

            collected.Add(item);
        }

        return new ReadOnlyCollection<VerificationEvidence>(collected.ToArray());
    }

    private static bool ContainsExternalSideEffectObservation(IReadOnlyList<VerificationEvidence> evidence)
    {
        for (int index = 0; index < evidence.Count; index++)
        {
            if (evidence[index].Kind == VerificationEvidenceKind.ExternalSideEffectObservation)
            {
                return true;
            }
        }

        return false;
    }
}
