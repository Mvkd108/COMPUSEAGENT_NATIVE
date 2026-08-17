using System.Globalization;

namespace Compuse.Contracts.Tests;

[TestClass]
public sealed class VerificationEvidenceTests
{
    private static readonly DateTimeOffset UtcTimestamp = new(2026, 8, 15, 6, 9, 0, TimeSpan.Zero);

    [TestMethod]
    public void ConstructorPreservesDefinedKindUtcTimestampAndStrings()
    {
        VerificationEvidence evidence = new(
            VerificationEvidenceKind.ExternalSideEffectObservation,
            "path_exists",
            "The destination path was observed after the call returned.",
            UtcTimestamp,
            "artifacts/observe.log");

        Assert.AreEqual(VerificationEvidenceKind.ExternalSideEffectObservation, evidence.Kind);
        Assert.AreEqual("path_exists", evidence.Code);
        Assert.AreEqual("The destination path was observed after the call returned.", evidence.Description);
        Assert.AreEqual(UtcTimestamp, evidence.ObservedAtUtc);
        Assert.AreEqual("artifacts/observe.log", evidence.ArtifactReference);
    }

    [TestMethod]
    public void EvidenceKindNumericValuesAreStableAndComplete()
    {
        Dictionary<string, int> expected = new()
        {
            ["OsApiReturn"] = 1,
            ["ExternalSideEffectObservation"] = 2,
            ["DiagnosticArtifact"] = 3
        };

        string[] names = Enum.GetNames<VerificationEvidenceKind>();
        Assert.AreEqual(expected.Count, names.Length);

        foreach (KeyValuePair<string, int> pair in expected)
        {
            VerificationEvidenceKind parsed = Enum.Parse<VerificationEvidenceKind>(pair.Key);
            Assert.AreEqual(pair.Value, Convert.ToInt32(parsed, CultureInfo.InvariantCulture));
        }

        VerificationEvidenceKind unspecified =
            (VerificationEvidenceKind)Enum.ToObject(typeof(VerificationEvidenceKind), 0);
        Assert.IsFalse(Enum.IsDefined(unspecified));
    }

    [TestMethod]
    public void NullArtifactReferenceIsAllowed()
    {
        VerificationEvidence evidence = Create(
            VerificationEvidenceKind.OsApiReturn,
            artifactReference: null);

        Assert.IsNull(evidence.ArtifactReference);
    }

    [TestMethod]
    public void DescriptionWhitespaceIsPreservedWhenNotWhitespaceOnly()
    {
        VerificationEvidence evidence = new(
            VerificationEvidenceKind.DiagnosticArtifact,
            "trace_capture",
            "  captured trace  ",
            UtcTimestamp);

        Assert.AreEqual("  captured trace  ", evidence.Description);
    }

    [TestMethod]
    public void ZeroEvidenceKindIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Create((VerificationEvidenceKind)0));
    }

    [TestMethod]
    public void UndefinedEvidenceKindIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => Create((VerificationEvidenceKind)99));
    }

    [TestMethod]
    public void NonUtcTimestampIsRejectedWithoutConversion()
    {
        DateTimeOffset localOffset = new(2026, 8, 15, 11, 39, 0, TimeSpan.FromHours(5.5));

        ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
            () => Create(VerificationEvidenceKind.OsApiReturn, observedAtUtc: localOffset));

        Assert.AreEqual("observedAtUtc", exception.ParamName);
        Assert.AreNotEqual(TimeSpan.Zero, localOffset.Offset);
        Assert.AreNotEqual(localOffset.UtcDateTime, localOffset.DateTime);
    }

    [TestMethod]
    public void NullCodeThrowsArgumentNullException()
    {
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => new VerificationEvidence(
                VerificationEvidenceKind.OsApiReturn,
                null!,
                "API returned success.",
                UtcTimestamp));
    }

    [TestMethod]
    public void NullDescriptionThrowsArgumentNullException()
    {
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => new VerificationEvidence(
                VerificationEvidenceKind.OsApiReturn,
                "api_return",
                null!,
                UtcTimestamp));
    }

    [TestMethod]
    public void InvalidCodeIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => Create(VerificationEvidenceKind.OsApiReturn, code: "API_RETURN"));
    }

    [TestMethod]
    public void WhitespaceOnlyDescriptionIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new VerificationEvidence(
                VerificationEvidenceKind.DiagnosticArtifact,
                "trace_capture",
                "   ",
                UtcTimestamp));
    }

    [TestMethod]
    public void EmptyArtifactReferenceIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => Create(VerificationEvidenceKind.DiagnosticArtifact, artifactReference: string.Empty));
    }

    [TestMethod]
    public void WhitespaceOnlyArtifactReferenceIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => Create(VerificationEvidenceKind.DiagnosticArtifact, artifactReference: "   "));
    }

    [TestMethod]
    public void OverlengthArtifactReferenceIsRejected()
    {
        string overlength = new('a', 2049);

        _ = Assert.ThrowsExactly<ArgumentException>(
            () => Create(VerificationEvidenceKind.DiagnosticArtifact, artifactReference: overlength));
    }

    [TestMethod]
    public void MaximumLengthArtifactReferenceIsAccepted()
    {
        string maximum = new('a', 2048);
        VerificationEvidence evidence = Create(
            VerificationEvidenceKind.DiagnosticArtifact,
            artifactReference: maximum);

        Assert.AreEqual(maximum, evidence.ArtifactReference);
    }

    private static VerificationEvidence Create(
        VerificationEvidenceKind kind,
        string code = "api_return",
        DateTimeOffset? observedAtUtc = null,
        string? artifactReference = null)
    {
        return new VerificationEvidence(
            kind,
            code,
            "Recorded evidence for contract tests.",
            observedAtUtc ?? UtcTimestamp,
            artifactReference);
    }
}
