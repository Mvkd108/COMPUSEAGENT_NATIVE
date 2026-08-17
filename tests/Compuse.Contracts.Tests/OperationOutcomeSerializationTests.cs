using System.Globalization;
using System.Text.Json;

namespace Compuse.Contracts.Tests;

[TestClass]
public sealed class OperationOutcomeSerializationTests
{
    [TestMethod]
    public void NumericValuesAreStableAndComplete()
    {
        Dictionary<string, int> expected = new()
        {
            ["Committed"] = 1,
            ["Refused"] = 2,
            ["Failed"] = 3,
            ["Indeterminate"] = 4
        };

        string[] names = Enum.GetNames<OperationOutcome>();
        Assert.AreEqual(expected.Count, names.Length);

        foreach (KeyValuePair<string, int> pair in expected)
        {
            OperationOutcome parsed = Enum.Parse<OperationOutcome>(pair.Key);
            Assert.AreEqual(pair.Value, Convert.ToInt32(parsed, CultureInfo.InvariantCulture));
        }

        OperationOutcome unspecified = (OperationOutcome)Enum.ToObject(typeof(OperationOutcome), 0);
        Assert.IsFalse(Enum.IsDefined(unspecified));
        Assert.AreEqual(0, Convert.ToInt32(unspecified, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void CommittedSerializesToLowercaseCommitted() => Assert.AreEqual("\"committed\"", JsonSerializer.Serialize(OperationOutcome.Committed));

    [TestMethod]
    public void RefusedSerializesToLowercaseRefused() => Assert.AreEqual("\"refused\"", JsonSerializer.Serialize(OperationOutcome.Refused));

    [TestMethod]
    public void FailedSerializesToLowercaseFailed() => Assert.AreEqual("\"failed\"", JsonSerializer.Serialize(OperationOutcome.Failed));

    [TestMethod]
    public void IndeterminateSerializesToLowercaseIndeterminate() => Assert.AreEqual("\"indeterminate\"", JsonSerializer.Serialize(OperationOutcome.Indeterminate));

    [TestMethod]
    public void CommittedDeserializesFromExactToken() => Assert.AreEqual(OperationOutcome.Committed, JsonSerializer.Deserialize<OperationOutcome>("\"committed\""));

    [TestMethod]
    public void RefusedDeserializesFromExactToken() => Assert.AreEqual(OperationOutcome.Refused, JsonSerializer.Deserialize<OperationOutcome>("\"refused\""));

    [TestMethod]
    public void FailedDeserializesFromExactToken() => Assert.AreEqual(OperationOutcome.Failed, JsonSerializer.Deserialize<OperationOutcome>("\"failed\""));

    [TestMethod]
    public void IndeterminateDeserializesFromExactToken()
    {
        Assert.AreEqual(
            OperationOutcome.Indeterminate,
            JsonSerializer.Deserialize<OperationOutcome>("\"indeterminate\""));
    }

    [TestMethod]
    public void DefaultOutcomeCannotBeSerialized()
    {
        _ = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Serialize(default(OperationOutcome)));
    }

    [TestMethod]
    public void UndefinedCastValueCannotBeSerialized()
    {
        _ = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Serialize((OperationOutcome)99));
    }

    [TestMethod]
    public void UppercaseJsonTokenIsRejected()
    {
        _ = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<OperationOutcome>("\"Committed\""));
    }

    [TestMethod]
    public void MixedCaseJsonTokenIsRejected()
    {
        _ = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<OperationOutcome>("\"cOmMiTtEd\""));
    }

    [TestMethod]
    public void NumericJsonTokenIsRejected()
    {
        _ = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<OperationOutcome>("1"));
    }

    [TestMethod]
    public void NullJsonTokenIsRejected()
    {
        _ = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<OperationOutcome>("null"));
    }

    [TestMethod]
    public void UnknownJsonTokenIsRejected()
    {
        _ = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<OperationOutcome>("\"success\""));
    }

    [TestMethod]
    public void EmptyJsonStringTokenIsRejected()
    {
        _ = Assert.ThrowsExactly<JsonException>(
            () => JsonSerializer.Deserialize<OperationOutcome>("\"\""));
    }
}
