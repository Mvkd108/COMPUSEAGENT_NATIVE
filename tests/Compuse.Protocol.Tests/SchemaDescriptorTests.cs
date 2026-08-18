using System.Globalization;
using Compuse.Protocol.V1;
using Google.Protobuf.Reflection;

namespace Compuse.Protocol.Tests;

[TestClass]
public sealed class SchemaDescriptorTests
{
    private static FileDescriptor File => OperationResultEnvelope.Descriptor.File;

    [TestMethod]
    public void PackageAndCsharpNamespaceAreExact()
    {
        Assert.AreEqual("compuse.v1", File.Package);
        Assert.AreEqual("Compuse.Protocol.V1", typeof(OperationResultEnvelope).Namespace);
        Assert.AreEqual("Compuse.Protocol.V1", File.GetOptions().CsharpNamespace);
    }

    [TestMethod]
    public void FileContainsNoServices() => Assert.AreEqual(0, File.Services.Count);

    [TestMethod]
    public void OperationOutcomeMembersAreExact()
    {
        EnumDescriptor descriptor = RequireEnum("OperationOutcome");
        AssertExactEnum(
            descriptor,
            ("OPERATION_OUTCOME_UNSPECIFIED", 0),
            ("OPERATION_OUTCOME_COMMITTED", 1),
            ("OPERATION_OUTCOME_REFUSED", 2),
            ("OPERATION_OUTCOME_FAILED", 3),
            ("OPERATION_OUTCOME_INDETERMINATE", 4));
    }

    [TestMethod]
    public void VerificationEvidenceKindMembersAreExact()
    {
        EnumDescriptor descriptor = RequireEnum("VerificationEvidenceKind");
        AssertExactEnum(
            descriptor,
            ("VERIFICATION_EVIDENCE_KIND_UNSPECIFIED", 0),
            ("VERIFICATION_EVIDENCE_KIND_OS_API_RETURN", 1),
            ("VERIFICATION_EVIDENCE_KIND_EXTERNAL_SIDE_EFFECT_OBSERVATION", 2),
            ("VERIFICATION_EVIDENCE_KIND_DIAGNOSTIC_ARTIFACT", 3));
    }

    [TestMethod]
    public void MessageNamesAreExact()
    {
        string[] names = [.. File.MessageTypes.Select(static descriptor => descriptor.Name).OrderBy(static name => name, StringComparer.Ordinal)];
        CollectionAssert.AreEqual(ExpectedMessageNames, names);
    }

    [TestMethod]
    public void OperationResultEnvelopeFieldsAreExact()
    {
        MessageDescriptor descriptor = RequireMessage("OperationResultEnvelope");
        Assert.AreEqual(5, descriptor.Fields.InDeclarationOrder().Count);
        AssertField(descriptor, 1, "correlation_id", FieldType.String, isRepeated: false);
        AssertField(descriptor, 2, "outcome", FieldType.Enum, isRepeated: false);
        AssertField(descriptor, 3, "refusal", FieldType.Message, isRepeated: false);
        AssertField(descriptor, 4, "failure", FieldType.Message, isRepeated: false);
        AssertField(descriptor, 5, "evidence", FieldType.Message, isRepeated: true);
        Assert.AreEqual("OperationOutcome", descriptor.FindFieldByNumber(2).EnumType.Name);
        Assert.AreEqual("RefusalInfo", descriptor.FindFieldByNumber(3).MessageType.Name);
        Assert.AreEqual("FailureInfo", descriptor.FindFieldByNumber(4).MessageType.Name);
        Assert.AreEqual("VerificationEvidence", descriptor.FindFieldByNumber(5).MessageType.Name);
    }

    [TestMethod]
    public void RefusalAndFailureFieldsAreExact()
    {
        MessageDescriptor refusal = RequireMessage("RefusalInfo");
        Assert.AreEqual(2, refusal.Fields.InDeclarationOrder().Count);
        AssertField(refusal, 1, "code", FieldType.String, isRepeated: false);
        AssertField(refusal, 2, "message", FieldType.String, isRepeated: false);

        MessageDescriptor failure = RequireMessage("FailureInfo");
        Assert.AreEqual(3, failure.Fields.InDeclarationOrder().Count);
        AssertField(failure, 1, "code", FieldType.String, isRepeated: false);
        AssertField(failure, 2, "message", FieldType.String, isRepeated: false);
        AssertField(failure, 3, "is_transient", FieldType.Bool, isRepeated: false);
    }

    [TestMethod]
    public void VerificationEvidenceFieldsAreExact()
    {
        MessageDescriptor descriptor = RequireMessage("VerificationEvidence");
        Assert.AreEqual(5, descriptor.Fields.InDeclarationOrder().Count);
        AssertField(descriptor, 1, "kind", FieldType.Enum, isRepeated: false);
        AssertField(descriptor, 2, "code", FieldType.String, isRepeated: false);
        AssertField(descriptor, 3, "description", FieldType.String, isRepeated: false);
        AssertField(descriptor, 4, "observed_at", FieldType.Message, isRepeated: false);
        AssertField(descriptor, 5, "artifact_reference", FieldType.String, isRepeated: false);
        Assert.AreEqual("VerificationEvidenceKind", descriptor.FindFieldByNumber(1).EnumType.Name);
        Assert.AreEqual("Timestamp", descriptor.FindFieldByNumber(4).MessageType.Name);
    }

    [TestMethod]
    public void ArtifactReferenceHasPresence()
    {
        FieldDescriptor field = RequireMessage("VerificationEvidence").FindFieldByNumber(5);
        Assert.IsTrue(field.HasPresence);
        Assert.IsFalse(field.IsRepeated);
    }

    [TestMethod]
    public void DetailOneofMembershipIsExact()
    {
        MessageDescriptor descriptor = RequireMessage("OperationResultEnvelope");
        Assert.AreEqual(1, descriptor.Oneofs.Count);
        OneofDescriptor detail = descriptor.Oneofs[0];
        Assert.AreEqual("detail", detail.Name);
        Assert.AreEqual(2, detail.Fields.Count);
        Assert.AreEqual("refusal", detail.Fields[0].Name);
        Assert.AreEqual(3, detail.Fields[0].FieldNumber);
        Assert.AreEqual("failure", detail.Fields[1].Name);
        Assert.AreEqual(4, detail.Fields[1].FieldNumber);
    }

    [TestMethod]
    public void FileContainsNoUnexpectedMembers()
    {
        Assert.AreEqual(2, File.EnumTypes.Count);
        Assert.AreEqual(4, File.MessageTypes.Count);
        Assert.AreEqual(0, File.Services.Count);
        foreach (MessageDescriptor message in File.MessageTypes)
        {
            Assert.AreEqual(0, message.NestedTypes.Count);
            Assert.AreEqual(0, message.EnumTypes.Count);
        }
    }

    [TestMethod]
    public void SemanticOutcomeNumbersMatchManagedContracts()
    {
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Contracts.OperationOutcome>("Committed"),
            RequireEnum("OperationOutcome").FindValueByName("OPERATION_OUTCOME_COMMITTED")!.Number);
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Contracts.OperationOutcome>("Refused"),
            RequireEnum("OperationOutcome").FindValueByName("OPERATION_OUTCOME_REFUSED")!.Number);
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Contracts.OperationOutcome>("Failed"),
            RequireEnum("OperationOutcome").FindValueByName("OPERATION_OUTCOME_FAILED")!.Number);
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Contracts.OperationOutcome>("Indeterminate"),
            RequireEnum("OperationOutcome").FindValueByName("OPERATION_OUTCOME_INDETERMINATE")!.Number);
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Contracts.VerificationEvidenceKind>("OsApiReturn"),
            RequireEnum("VerificationEvidenceKind").FindValueByName("VERIFICATION_EVIDENCE_KIND_OS_API_RETURN")!.Number);
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Contracts.VerificationEvidenceKind>("ExternalSideEffectObservation"),
            RequireEnum("VerificationEvidenceKind")
                .FindValueByName("VERIFICATION_EVIDENCE_KIND_EXTERNAL_SIDE_EFFECT_OBSERVATION")!.Number);
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Contracts.VerificationEvidenceKind>("DiagnosticArtifact"),
            RequireEnum("VerificationEvidenceKind")
                .FindValueByName("VERIFICATION_EVIDENCE_KIND_DIAGNOSTIC_ARTIFACT")!.Number);
    }

    private static readonly string[] ExpectedMessageNames =
    [
        "FailureInfo",
        "OperationResultEnvelope",
        "RefusalInfo",
        "VerificationEvidence"
    ];

    private static int ParseManagedNumber<TEnum>(string name)
        where TEnum : struct, System.Enum =>
        Convert.ToInt32(System.Enum.Parse<TEnum>(name), CultureInfo.InvariantCulture);

    private static EnumDescriptor RequireEnum(string name)
    {
        EnumDescriptor? descriptor = File.FindTypeByName<EnumDescriptor>(name);
        Assert.IsNotNull(descriptor);
        return descriptor;
    }

    private static MessageDescriptor RequireMessage(string name)
    {
        MessageDescriptor? descriptor = File.FindTypeByName<MessageDescriptor>(name);
        Assert.IsNotNull(descriptor);
        return descriptor;
    }

    private static void AssertExactEnum(EnumDescriptor descriptor, params (string Name, int Number)[] expected)
    {
        Assert.AreEqual(expected.Length, descriptor.Values.Count);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index].Name, descriptor.Values[index].Name);
            Assert.AreEqual(expected[index].Number, descriptor.Values[index].Number);
        }
    }

    private static void AssertField(
        MessageDescriptor message,
        int number,
        string name,
        FieldType type,
        bool isRepeated)
    {
        FieldDescriptor field = message.FindFieldByNumber(number);
        Assert.AreEqual(name, field.Name);
        Assert.AreEqual(number, field.FieldNumber);
        Assert.AreEqual(type, field.FieldType);
        Assert.AreEqual(isRepeated, field.IsRepeated);
    }
}
