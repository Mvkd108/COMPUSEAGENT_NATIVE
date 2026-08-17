namespace Compuse.Contracts;

public sealed record VerificationEvidence
{
    public VerificationEvidence(
        VerificationEvidenceKind kind,
        string code,
        string description,
        DateTimeOffset observedAtUtc,
        string? artifactReference = null)
    {
        if (kind == default || !Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Evidence kind must be a defined nonzero value.");
        }

        if (observedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Evidence timestamps must use a UTC offset of zero and are not converted.",
                nameof(observedAtUtc));
        }

        Kind = kind;
        Code = ContractValidation.RequireCode(code, nameof(code));
        Description = ContractValidation.RequireMessage(description, nameof(description));
        ObservedAtUtc = observedAtUtc;
        ArtifactReference = ContractValidation.RequireArtifactReference(artifactReference, nameof(artifactReference));
    }

    public VerificationEvidenceKind Kind { get; }

    public string Code { get; }

    public string Description { get; }

    public DateTimeOffset ObservedAtUtc { get; }

    public string? ArtifactReference { get; }
}
