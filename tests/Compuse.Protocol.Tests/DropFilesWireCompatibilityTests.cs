using Compuse.Contracts;
using Compuse.Requests;
using Google.Protobuf;
using DomainRequest = Compuse.Requests.DropFilesRequest;
using ProtoFilesystemContainer = Compuse.Protocol.V1.FilesystemContainerTarget;
using ProtoPhysicalFile = Compuse.Protocol.V1.PhysicalFileSource;
using ProtoRequest = Compuse.Protocol.V1.DropFilesRequest;
using ProtoSourceItem = Compuse.Protocol.V1.SourceItem;
using ProtoTargetSelector = Compuse.Protocol.V1.TargetSelector;
using ProtoTransferEffect = Compuse.Protocol.V1.TransferEffect;

namespace Compuse.Protocol.Tests;

[TestClass]
public sealed class DropFilesWireCompatibilityTests
{
    private const string CanonicalCorrelation = "abcdef01-2345-6789-abcd-ef0123456789";
    private const string CanonicalHex =
        "0a2461626364656630312d323334352d363738392d616263642d65663031323334353637383912100a0e0a0c433a5c7372635c612e7478741801220a0a080a06433a5c647374";
    private const string UnknownVarintFieldHex = "980601";

    [TestMethod]
    public void CanonicalCopySerializesToExactBytes()
    {
        ProtoRequest message = CreateCanonicalCopy();
        Assert.AreEqual(CanonicalHex, Convert.ToHexString(message.ToByteArray()).ToLowerInvariant());
    }

    [TestMethod]
    public void DomainCopySerializesToCanonicalBytes()
    {
        ProtoRequest message = DropFilesRequestProtoMapper.ToProto(CreateCanonicalDomain());
        Assert.AreEqual(CanonicalHex, Convert.ToHexString(message.ToByteArray()).ToLowerInvariant());
    }

    [TestMethod]
    public void CanonicalBytesMapToCopyRequest()
    {
        ProtoRequest parsed = ProtoRequest.Parser.ParseFrom(Convert.FromHexString(CanonicalHex));
        DomainRequest request = DropFilesRequestProtoMapper.FromProto(parsed);

        Assert.AreEqual(CanonicalCorrelation, request.CorrelationId.ToString());
        Assert.AreEqual(TransferEffect.Copy, request.Effect);
        Assert.AreEqual(1, request.Sources.Count);
        Assert.AreEqual(@"C:\src\a.txt", request.Sources[0].PhysicalFile.AbsolutePath);
        Assert.AreEqual(TargetSelectorKind.FilesystemContainer, request.Target.Kind);
        Assert.AreEqual(@"C:\dst", request.Target.FilesystemContainer?.AbsolutePath);
        Assert.IsNull(request.DeadlineUtc);
    }

    [TestMethod]
    public void UnknownVarintFieldIsAcceptedWhilePreservingKnownValues()
    {
        byte[] withUnknown = Convert.FromHexString(CanonicalHex + UnknownVarintFieldHex);
        ProtoRequest parsed = ProtoRequest.Parser.ParseFrom(withUnknown);
        DomainRequest request = DropFilesRequestProtoMapper.FromProto(parsed);

        Assert.AreEqual(CanonicalCorrelation, parsed.CorrelationId);
        Assert.AreEqual(ProtoTransferEffect.Copy, parsed.Effect);
        Assert.AreEqual(CanonicalCorrelation, request.CorrelationId.ToString());
        Assert.AreEqual(TransferEffect.Copy, request.Effect);
        Assert.AreEqual(@"C:\src\a.txt", request.Sources[0].PhysicalFile.AbsolutePath);
    }

    private static ProtoRequest CreateCanonicalCopy()
    {
        ProtoRequest message = new()
        {
            CorrelationId = CanonicalCorrelation,
            Effect = ProtoTransferEffect.Copy,
            Target = new ProtoTargetSelector
            {
                FilesystemContainer = new ProtoFilesystemContainer
                {
                    AbsolutePath = @"C:\dst"
                }
            }
        };
        message.Sources.Add(new ProtoSourceItem
        {
            PhysicalFile = new ProtoPhysicalFile
            {
                AbsolutePath = @"C:\src\a.txt"
            }
        });
        return message;
    }

    private static DomainRequest CreateCanonicalDomain() =>
        DomainRequest.Create(
            CorrelationId.Parse(CanonicalCorrelation),
            [new SourceItem(new PhysicalFileSource(@"C:\src\a.txt"))],
            TransferEffect.Copy,
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(@"C:\dst")));
}
