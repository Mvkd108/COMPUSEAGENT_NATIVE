namespace Compuse.Contracts.Tests;

[TestClass]
public sealed class InformationTests
{
    private const string ValidCode = "policy_denied";
    private const string ValidMessage = "The operation was intentionally not attempted.";

    [TestMethod]
    public void RefusalInfoPreservesValidCodeAndMessage()
    {
        RefusalInfo info = new(ValidCode, ValidMessage);

        Assert.AreEqual(ValidCode, info.Code);
        Assert.AreEqual(ValidMessage, info.Message);
    }

    [TestMethod]
    public void FailureInfoPreservesValidValuesAndTransientFlag()
    {
        FailureInfo info = new("timeout_expired", ValidMessage, isTransient: true);

        Assert.AreEqual("timeout_expired", info.Code);
        Assert.AreEqual(ValidMessage, info.Message);
        Assert.IsTrue(info.IsTransient);
    }

    [TestMethod]
    public void FailureInfoPreservesNonTransientFlag()
    {
        FailureInfo info = new(ValidCode, ValidMessage, isTransient: false);

        Assert.IsFalse(info.IsTransient);
    }

    [TestMethod]
    public void ValidMessageWhitespaceIsPreservedWhenNotWhitespaceOnly()
    {
        const string message = "  leading and trailing  ";
        RefusalInfo refusal = new(ValidCode, message);
        FailureInfo failure = new(ValidCode, message, isTransient: false);

        Assert.AreEqual(message, refusal.Message);
        Assert.AreEqual(message, failure.Message);
    }

    [TestMethod]
    public void NullCodeThrowsArgumentNullException()
    {
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => new RefusalInfo(null!, ValidMessage));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => new FailureInfo(null!, ValidMessage, isTransient: false));
    }

    [TestMethod]
    public void NullMessageThrowsArgumentNullException()
    {
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => new RefusalInfo(ValidCode, null!));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => new FailureInfo(ValidCode, null!, isTransient: true));
    }

    [TestMethod]
    public void EmptyAndWhitespaceCodesAreRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo(string.Empty, ValidMessage));
        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo("   ", ValidMessage));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new FailureInfo(string.Empty, ValidMessage, isTransient: false));
    }

    [TestMethod]
    public void EmptyAndWhitespaceOnlyMessagesAreRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo(ValidCode, string.Empty));
        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo(ValidCode, "   "));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new FailureInfo(ValidCode, "\t", isTransient: true));
    }

    [TestMethod]
    public void OverlengthCodeIsRejected()
    {
        string overlength = "a" + new string('b', 64);

        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo(overlength, ValidMessage));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new FailureInfo(overlength, ValidMessage, isTransient: false));
    }

    [TestMethod]
    public void MaximumLengthCodeIsAccepted()
    {
        string maximum = "a" + new string('b', 63);
        RefusalInfo info = new(maximum, ValidMessage);

        Assert.AreEqual(maximum, info.Code);
        Assert.AreEqual(64, info.Code.Length);
    }

    [TestMethod]
    public void UppercaseCodeIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo("Policy_denied", ValidMessage));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new FailureInfo("Denied", ValidMessage, isTransient: false));
    }

    [TestMethod]
    public void PunctuationContainingCodeIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo("policy-denied", ValidMessage));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new FailureInfo("policy.denied", ValidMessage, isTransient: false));
    }

    [TestMethod]
    public void NonAsciiCodeIsRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo("policy_dénied", ValidMessage));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new FailureInfo("код", ValidMessage, isTransient: false));
    }

    [TestMethod]
    public void OverlengthMessageIsRejected()
    {
        string overlength = new('x', 1025);

        _ = Assert.ThrowsExactly<ArgumentException>(() => new RefusalInfo(ValidCode, overlength));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new FailureInfo(ValidCode, overlength, isTransient: true));
    }

    [TestMethod]
    public void MaximumLengthMessageIsAccepted()
    {
        string maximum = new('m', 1024);
        FailureInfo info = new(ValidCode, maximum, isTransient: false);

        Assert.AreEqual(maximum, info.Message);
        Assert.AreEqual(1024, info.Message.Length);
    }
}
