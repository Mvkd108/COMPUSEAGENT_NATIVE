namespace Compuse.Contracts.Tests;

[TestClass]
public sealed class CorrelationIdTests
{
    private const string CanonicalValue = "abcdef01-2345-6789-abcd-ef0123456789";
    private const string EmptyGuidValue = "00000000-0000-0000-0000-000000000000";

    [TestMethod]
    public void NewReturnsNonEmptyIdentifier()
    {
        CorrelationId first = CorrelationId.New();
        CorrelationId second = CorrelationId.New();

        Assert.AreNotEqual(Guid.Empty, first.Value);
        Assert.AreNotEqual(Guid.Empty, second.Value);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void FromRejectsEmptyGuid() => _ = Assert.ThrowsExactly<ArgumentException>(() => CorrelationId.From(Guid.Empty));

    [TestMethod]
    public void FromAcceptsNonEmptyGuid()
    {
        Guid value = Guid.Parse(CanonicalValue);
        CorrelationId correlationId = CorrelationId.From(value);

        Assert.AreEqual(value, correlationId.Value);
    }

    [TestMethod]
    public void ParseNullThrowsArgumentNullException() => _ = Assert.ThrowsExactly<ArgumentNullException>(() => CorrelationId.Parse(null!));

    [TestMethod]
    public void ParseWhitespaceThrowsFormatException() => _ = Assert.ThrowsExactly<FormatException>(() => CorrelationId.Parse("   "));

    [TestMethod]
    public void ParseInvalidThrowsFormatException() => _ = Assert.ThrowsExactly<FormatException>(() => CorrelationId.Parse("not-a-guid"));

    [TestMethod]
    public void ParseBracedFormThrowsFormatException()
    {
        _ = Assert.ThrowsExactly<FormatException>(
            () => CorrelationId.Parse("{abcdef01-2345-6789-abcd-ef0123456789}"));
    }

    [TestMethod]
    public void ParseParenthesizedFormThrowsFormatException()
    {
        _ = Assert.ThrowsExactly<FormatException>(
            () => CorrelationId.Parse("(abcdef01-2345-6789-abcd-ef0123456789)"));
    }

    [TestMethod]
    public void ParseCompactNFormThrowsFormatException()
    {
        _ = Assert.ThrowsExactly<FormatException>(
            () => CorrelationId.Parse("abcdef0123456789abcdef0123456789"));
    }

    [TestMethod]
    public void ParseEmptyGuidIsRejected() => _ = Assert.ThrowsExactly<ArgumentException>(() => CorrelationId.Parse(EmptyGuidValue));

    [TestMethod]
    public void ParseCanonicalDFormSucceeds()
    {
        CorrelationId correlationId = CorrelationId.Parse(CanonicalValue);

        Assert.AreEqual(CanonicalValue, correlationId.ToString());
    }

    [TestMethod]
    public void ToStringReturnsCanonicalLowercaseDForm()
    {
        CorrelationId correlationId = CorrelationId.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789");

        Assert.AreEqual(CanonicalValue, correlationId.ToString());
    }

    [TestMethod]
    public void CanonicalLowercaseDFormRoundTrips()
    {
        CorrelationId original = CorrelationId.Parse(CanonicalValue);
        CorrelationId roundTripped = CorrelationId.Parse(original.ToString());

        Assert.AreEqual(original, roundTripped);
        Assert.AreEqual(CanonicalValue, roundTripped.ToString());
    }

    [TestMethod]
    public void SameGuidValuesAreEqual()
    {
        Guid value = Guid.Parse(CanonicalValue);
        CorrelationId left = CorrelationId.From(value);
        CorrelationId right = CorrelationId.Parse(CanonicalValue);

        Assert.AreEqual(left, right);
        Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
    }

    [TestMethod]
    public void TryParseRejectsNullEmptyWhitespaceInvalidNonDAndEmptyGuid()
    {
        Assert.IsFalse(CorrelationId.TryParse(null, out CorrelationId? fromNull));
        Assert.IsNull(fromNull);

        Assert.IsFalse(CorrelationId.TryParse(string.Empty, out CorrelationId? fromEmpty));
        Assert.IsNull(fromEmpty);

        Assert.IsFalse(CorrelationId.TryParse("   ", out CorrelationId? fromWhitespace));
        Assert.IsNull(fromWhitespace);

        Assert.IsFalse(CorrelationId.TryParse("not-a-guid", out CorrelationId? fromInvalid));
        Assert.IsNull(fromInvalid);

        Assert.IsFalse(CorrelationId.TryParse("{abcdef01-2345-6789-abcd-ef0123456789}", out CorrelationId? fromBraced));
        Assert.IsNull(fromBraced);

        Assert.IsFalse(CorrelationId.TryParse("(abcdef01-2345-6789-abcd-ef0123456789)", out CorrelationId? fromParenthesized));
        Assert.IsNull(fromParenthesized);

        Assert.IsFalse(CorrelationId.TryParse("abcdef0123456789abcdef0123456789", out CorrelationId? fromN));
        Assert.IsNull(fromN);

        Assert.IsFalse(CorrelationId.TryParse(EmptyGuidValue, out CorrelationId? fromEmptyGuid));
        Assert.IsNull(fromEmptyGuid);
    }

    [TestMethod]
    public void TryParseAcceptsCanonicalDForm()
    {
        bool parsed = CorrelationId.TryParse(CanonicalValue, out CorrelationId? correlationId);

        Assert.IsTrue(parsed);
        Assert.IsNotNull(correlationId);
        Assert.AreEqual(CanonicalValue, correlationId.ToString());
    }

    [TestMethod]
    public void ParseRejectsWhitespaceAroundValidDForm()
    {
        _ = Assert.ThrowsExactly<FormatException>(() => CorrelationId.Parse(" " + CanonicalValue));
        _ = Assert.ThrowsExactly<FormatException>(() => CorrelationId.Parse(CanonicalValue + " "));
        _ = Assert.ThrowsExactly<FormatException>(() => CorrelationId.Parse(" " + CanonicalValue + " "));
        _ = Assert.ThrowsExactly<FormatException>(() => CorrelationId.Parse("\t" + CanonicalValue));
        _ = Assert.ThrowsExactly<FormatException>(() => CorrelationId.Parse(CanonicalValue + "\r\n"));
    }

    [TestMethod]
    public void TryParseRejectsWhitespaceAroundValidDForm()
    {
        AssertRejectedTryParse(" " + CanonicalValue);
        AssertRejectedTryParse(CanonicalValue + " ");
        AssertRejectedTryParse(" " + CanonicalValue + " ");
        AssertRejectedTryParse("\t" + CanonicalValue);
        AssertRejectedTryParse(CanonicalValue + "\n");
    }

    private static void AssertRejectedTryParse(string value)
    {
        bool parsed = CorrelationId.TryParse(value, out CorrelationId? correlationId);

        Assert.IsFalse(parsed);
        Assert.IsNull(correlationId);
    }
}
