using System.Globalization;
using Compuse.Protocol.V1;
using Google.Protobuf.Reflection;

namespace Compuse.Protocol.Tests;

[TestClass]
public sealed class DropFilesSchemaDescriptorTests
{
    private static FileDescriptor File => DropFilesRequest.Descriptor.File;

    [TestMethod]
    public void PackageAndCsharpNamespaceAreExact()
    {
        Assert.AreEqual("compuse.v1", File.Package);
        Assert.AreEqual("Compuse.Protocol.V1", typeof(DropFilesRequest).Namespace);
        Assert.AreEqual("Compuse.Protocol.V1", File.GetOptions().CsharpNamespace);
    }

    [TestMethod]
    public void FileContainsNoServices() => Assert.AreEqual(0, File.Services.Count);

    [TestMethod]
    public void TransferEffectMembersAreExact()
    {
        EnumDescriptor descriptor = RequireEnum("TransferEffect");
        AssertExactEnum(
            descriptor,
            ("TRANSFER_EFFECT_UNSPECIFIED", 0),
            ("TRANSFER_EFFECT_COPY", 1),
            ("TRANSFER_EFFECT_MOVE", 2));
    }

    [TestMethod]
    public void MessageNamesAreExact()
    {
        string[] names = [.. File.MessageTypes.Select(static descriptor => descriptor.Name).OrderBy(static name => name, StringComparer.Ordinal)];
        CollectionAssert.AreEqual(ExpectedMessageNames, names);
    }

    [TestMethod]
    public void DropFilesRequestFieldsAreExact()
    {
        MessageDescriptor descriptor = RequireMessage("DropFilesRequest");
        Assert.AreEqual(5, descriptor.Fields.InDeclarationOrder().Count);
        AssertField(descriptor, 1, "correlation_id", FieldType.String, isRepeated: false);
        AssertField(descriptor, 2, "sources", FieldType.Message, isRepeated: true);
        AssertField(descriptor, 3, "effect", FieldType.Enum, isRepeated: false);
        AssertField(descriptor, 4, "target", FieldType.Message, isRepeated: false);
        AssertField(descriptor, 5, "deadline", FieldType.Message, isRepeated: false);
        Assert.AreEqual("SourceItem", descriptor.FindFieldByNumber(2).MessageType.Name);
        Assert.AreEqual("TransferEffect", descriptor.FindFieldByNumber(3).EnumType.Name);
        Assert.AreEqual("TargetSelector", descriptor.FindFieldByNumber(4).MessageType.Name);
        Assert.AreEqual("Timestamp", descriptor.FindFieldByNumber(5).MessageType.Name);
        Assert.IsTrue(descriptor.FindFieldByNumber(5).HasPresence);
    }

    [TestMethod]
    public void SourceAndPhysicalFileFieldsAreExact()
    {
        MessageDescriptor source = RequireMessage("SourceItem");
        Assert.AreEqual(1, source.Fields.InDeclarationOrder().Count);
        AssertField(source, 1, "physical_file", FieldType.Message, isRepeated: false);
        Assert.AreEqual("PhysicalFileSource", source.FindFieldByNumber(1).MessageType.Name);
        Assert.AreEqual(1, source.Oneofs.Count);
        Assert.AreEqual("identity", source.Oneofs[0].Name);

        MessageDescriptor physicalFile = RequireMessage("PhysicalFileSource");
        Assert.AreEqual(1, physicalFile.Fields.InDeclarationOrder().Count);
        AssertField(physicalFile, 1, "absolute_path", FieldType.String, isRepeated: false);
    }

    [TestMethod]
    public void TargetSelectorFieldsAreExact()
    {
        MessageDescriptor selector = RequireMessage("TargetSelector");
        Assert.AreEqual(2, selector.Fields.InDeclarationOrder().Count);
        AssertField(selector, 1, "filesystem_container", FieldType.Message, isRepeated: false);
        AssertField(selector, 2, "application_surface", FieldType.Message, isRepeated: false);
        Assert.AreEqual(1, selector.Oneofs.Count);
        Assert.AreEqual("selector", selector.Oneofs[0].Name);
        Assert.AreEqual("filesystem_container", selector.Oneofs[0].Fields[0].Name);
        Assert.AreEqual("application_surface", selector.Oneofs[0].Fields[1].Name);

        MessageDescriptor filesystem = RequireMessage("FilesystemContainerTarget");
        Assert.AreEqual(1, filesystem.Fields.InDeclarationOrder().Count);
        AssertField(filesystem, 1, "absolute_path", FieldType.String, isRepeated: false);
    }

    [TestMethod]
    public void ApplicationSurfaceFieldsAndPresenceAreExact()
    {
        MessageDescriptor descriptor = RequireMessage("ApplicationSurfaceTarget");
        Assert.AreEqual(5, descriptor.Fields.InDeclarationOrder().Count);
        AssertField(descriptor, 1, "process_image_name", FieldType.String, isRepeated: false);
        AssertField(descriptor, 2, "window_class", FieldType.String, isRepeated: false);
        AssertField(descriptor, 3, "window_title", FieldType.String, isRepeated: false);
        AssertField(descriptor, 4, "hwnd_hint", FieldType.UInt64, isRepeated: false);
        AssertField(descriptor, 5, "process_id_hint", FieldType.UInt32, isRepeated: false);
        Assert.IsTrue(descriptor.FindFieldByNumber(2).HasPresence);
        Assert.IsTrue(descriptor.FindFieldByNumber(3).HasPresence);
        Assert.IsTrue(descriptor.FindFieldByNumber(4).HasPresence);
        Assert.IsTrue(descriptor.FindFieldByNumber(5).HasPresence);
        Assert.IsFalse(descriptor.FindFieldByNumber(1).HasPresence);
    }

    [TestMethod]
    public void FileContainsNoUnexpectedMembers()
    {
        Assert.AreEqual(1, File.EnumTypes.Count);
        Assert.AreEqual(6, File.MessageTypes.Count);
        Assert.AreEqual(0, File.Services.Count);
        foreach (MessageDescriptor message in File.MessageTypes)
        {
            Assert.AreEqual(0, message.NestedTypes.Count);
            Assert.AreEqual(0, message.EnumTypes.Count);
        }
    }

    [TestMethod]
    public void SemanticTransferEffectNumbersMatchManagedRequests()
    {
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Requests.TransferEffect>("Copy"),
            RequireEnum("TransferEffect").FindValueByName("TRANSFER_EFFECT_COPY")!.Number);
        Assert.AreEqual(
            ParseManagedNumber<Compuse.Requests.TransferEffect>("Move"),
            RequireEnum("TransferEffect").FindValueByName("TRANSFER_EFFECT_MOVE")!.Number);
    }

    private static readonly string[] ExpectedMessageNames =
    [
        "ApplicationSurfaceTarget",
        "DropFilesRequest",
        "FilesystemContainerTarget",
        "PhysicalFileSource",
        "SourceItem",
        "TargetSelector"
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
