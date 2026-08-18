using Compuse.Contracts;
using Compuse.Requests;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using DomainRequest = Compuse.Requests.DropFilesRequest;
using ProtoApplicationSurface = Compuse.Protocol.V1.ApplicationSurfaceTarget;
using ProtoFilesystemContainer = Compuse.Protocol.V1.FilesystemContainerTarget;
using ProtoPhysicalFile = Compuse.Protocol.V1.PhysicalFileSource;
using ProtoRequest = Compuse.Protocol.V1.DropFilesRequest;
using ProtoSourceItem = Compuse.Protocol.V1.SourceItem;
using ProtoTargetSelector = Compuse.Protocol.V1.TargetSelector;
using ProtoTransferEffect = Compuse.Protocol.V1.TransferEffect;

namespace Compuse.Protocol.Tests;

[TestClass]
public sealed class DropFilesRequestProtoMapperTests
{
    private const string CanonicalCorrelation = "abcdef01-2345-6789-abcd-ef0123456789";
    private static readonly DateTimeOffset UtcDeadline = new(2026, 8, 18, 5, 21, 0, TimeSpan.Zero);

    [TestMethod]
    public void ToProtoRejectsNull() =>
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => DropFilesRequestProtoMapper.ToProto(null!));

    [TestMethod]
    public void FromProtoRejectsNull() =>
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => DropFilesRequestProtoMapper.FromProto(null!));

    [TestMethod]
    public void CopyToFilesystemRoundTripPreservesValues()
    {
        DomainRequest original = DomainRequest.Create(
            CorrelationId.Parse(CanonicalCorrelation),
            [File(@"C:\src\a.txt"), File(@"C:\src\b.txt")],
            TransferEffect.Copy,
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(@"C:\dst")));
        AssertRoundTrip(original);
    }

    [TestMethod]
    public void MoveToApplicationSurfaceRoundTripPreservesHintsAndDeadline()
    {
        DomainRequest original = DomainRequest.Create(
            CorrelationId.Parse(CanonicalCorrelation),
            [File(@"C:\src\memo.docx")],
            TransferEffect.Move,
            TargetSelector.FromApplicationSurface(
                new ApplicationSurfaceTarget(
                    "WINWORD.EXE",
                    "OpusApp",
                    "Document1 - Word",
                    hwndHint: 0x123456,
                    processIdHint: 4242)),
            UtcDeadline);
        AssertRoundTrip(original);

        ProtoRequest proto = DropFilesRequestProtoMapper.ToProto(original);
        Assert.AreEqual(ProtoTransferEffect.Move, proto.Effect);
        Assert.IsNotNull(proto.Deadline);
        Assert.IsTrue(proto.Target.ApplicationSurface.HasWindowClass);
        Assert.IsTrue(proto.Target.ApplicationSurface.HasHwndHint);
        Assert.IsTrue(proto.Target.ApplicationSurface.HasProcessIdHint);
    }

    [TestMethod]
    public void ApplicationSurfaceWithoutOptionalHintsRoundTripsAsAbsent()
    {
        DomainRequest original = DomainRequest.Create(
            CorrelationId.Parse(CanonicalCorrelation),
            [File(@"C:\src\a.txt")],
            TransferEffect.Copy,
            TargetSelector.FromApplicationSurface(new ApplicationSurfaceTarget("notepad.exe")));
        ProtoRequest proto = DropFilesRequestProtoMapper.ToProto(original);

        Assert.IsFalse(proto.Target.ApplicationSurface.HasWindowClass);
        Assert.IsFalse(proto.Target.ApplicationSurface.HasWindowTitle);
        Assert.IsFalse(proto.Target.ApplicationSurface.HasHwndHint);
        Assert.IsFalse(proto.Target.ApplicationSurface.HasProcessIdHint);
        Assert.IsNull(proto.Deadline);
        AssertRoundTrip(original);
    }

    [TestMethod]
    public void CorrelationIsEmittedAndAcceptedOnlyAsLowercaseCanonicalForm()
    {
        DomainRequest original = DomainRequest.Create(
            CorrelationId.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789"),
            [File(@"C:\src\a.txt")],
            TransferEffect.Copy,
            Container(@"C:\dst"));
        ProtoRequest proto = DropFilesRequestProtoMapper.ToProto(original);
        Assert.AreEqual(CanonicalCorrelation, proto.CorrelationId);
        Assert.AreEqual(
            CanonicalCorrelation,
            DropFilesRequestProtoMapper.FromProto(proto).CorrelationId.ToString());
    }

    [TestMethod]
    public void UppercasePaddedEmptyAndMalformedCorrelationIdsAreRejected()
    {
        AssertCorrelationRejected("ABCDEF01-2345-6789-ABCD-EF0123456789");
        AssertCorrelationRejected(" " + CanonicalCorrelation);
        AssertCorrelationRejected(CanonicalCorrelation + " ");
        AssertCorrelationRejected(string.Empty);
        AssertCorrelationRejected("00000000-0000-0000-0000-000000000000");
    }

    [TestMethod]
    public void UnspecifiedAndUnknownEffectsAreRejected()
    {
        ProtoRequest unspecified = CreateCanonicalCopy();
        unspecified.Effect = ProtoTransferEffect.Unspecified;
        AssertMapped(unspecified, ProtocolContractErrorCode.UnsupportedTransferEffect, "effect");

        ProtoRequest unknown = CreateCanonicalCopy();
        unknown.Effect = (ProtoTransferEffect)99;
        AssertMapped(unknown, ProtocolContractErrorCode.UnsupportedTransferEffect, "effect");
    }

    [TestMethod]
    public void MissingAndInvalidSourcesAreRejectedWithFieldPaths()
    {
        ProtoRequest empty = CreateCanonicalCopy();
        empty.Sources.Clear();
        AssertMapped(empty, ProtocolContractErrorCode.InvalidSource, "sources");

        ProtoRequest missingIdentity = CreateCanonicalCopy();
        missingIdentity.Sources[0] = new ProtoSourceItem();
        AssertMapped(missingIdentity, ProtocolContractErrorCode.InvalidSource, "sources[0].identity");

        ProtoRequest relative = CreateCanonicalCopy();
        relative.Sources[0].PhysicalFile.AbsolutePath = @"src\a.txt";
        AssertMapped(relative, ProtocolContractErrorCode.InvalidSource, "sources[0].physical_file.absolute_path");

        ProtoRequest duplicate = CreateCanonicalCopy();
        duplicate.Sources.Add(new ProtoSourceItem
        {
            PhysicalFile = new ProtoPhysicalFile { AbsolutePath = @"C:\SRC\A.TXT" }
        });
        AssertMapped(duplicate, ProtocolContractErrorCode.InvalidSource, "sources[1].physical_file.absolute_path");
    }

    [TestMethod]
    public void MissingAndInvalidTargetsAreRejectedWithFieldPaths()
    {
        ProtoRequest missing = CreateCanonicalCopy();
        missing.Target = null!;
        AssertMapped(missing, ProtocolContractErrorCode.InvalidTarget, "target");

        ProtoRequest empty = CreateCanonicalCopy();
        empty.Target = new ProtoTargetSelector();
        AssertMapped(empty, ProtocolContractErrorCode.InvalidTarget, "target");

        ProtoRequest relative = CreateCanonicalCopy();
        relative.Target.FilesystemContainer.AbsolutePath = @"dst";
        AssertMapped(relative, ProtocolContractErrorCode.InvalidTarget, "target.filesystem_container.absolute_path");

        ProtoRequest image = CreateCanonicalCopy();
        image.Target = new ProtoTargetSelector
        {
            ApplicationSurface = new ProtoApplicationSurface { ProcessImageName = @"C:\Windows\notepad.exe" }
        };
        AssertMapped(image, ProtocolContractErrorCode.InvalidTarget, "target.application_surface.process_image_name");

        ProtoRequest hwnd = CreateCanonicalCopy();
        hwnd.Target = new ProtoTargetSelector
        {
            ApplicationSurface = new ProtoApplicationSurface { ProcessImageName = "notepad.exe", HwndHint = 0 }
        };
        AssertMapped(hwnd, ProtocolContractErrorCode.InvalidTarget, "target.application_surface.hwnd_hint");

        ProtoRequest pid = CreateCanonicalCopy();
        pid.Target = new ProtoTargetSelector
        {
            ApplicationSurface = new ProtoApplicationSurface { ProcessImageName = "notepad.exe", ProcessIdHint = 0 }
        };
        AssertMapped(pid, ProtocolContractErrorCode.InvalidTarget, "target.application_surface.process_id_hint");

        ProtoRequest windowClass = CreateCanonicalCopy();
        windowClass.Target = new ProtoTargetSelector
        {
            ApplicationSurface = new ProtoApplicationSurface { ProcessImageName = "notepad.exe", WindowClass = "   " }
        };
        AssertMapped(windowClass, ProtocolContractErrorCode.InvalidTarget, "target.application_surface.window_class");
    }

    [TestMethod]
    public void MissingOutOfRangeAndSubTickDeadlinesAreRejected()
    {
        ProtoRequest subTick = CreateCanonicalCopy();
        subTick.Deadline = new Timestamp { Seconds = 0, Nanos = 50 };
        AssertMapped(subTick, ProtocolContractErrorCode.InvalidDeadline, "deadline");

        ProtoRequest negative = CreateCanonicalCopy();
        negative.Deadline = new Timestamp { Seconds = 0, Nanos = -1 };
        AssertMapped(negative, ProtocolContractErrorCode.InvalidDeadline, "deadline");

        ProtoRequest tooLarge = CreateCanonicalCopy();
        tooLarge.Deadline = new Timestamp { Seconds = 253_402_300_800, Nanos = 0 };
        AssertMapped(tooLarge, ProtocolContractErrorCode.InvalidDeadline, "deadline");
    }

    [TestMethod]
    public void ForwardSlashSourcePathIsNormalizedOnMap()
    {
        ProtoRequest proto = CreateCanonicalCopy();
        proto.Sources[0].PhysicalFile.AbsolutePath = @"C:/src/a.txt";
        DomainRequest restored = DropFilesRequestProtoMapper.FromProto(proto);
        Assert.AreEqual(@"C:\src\a.txt", restored.Sources[0].PhysicalFile.AbsolutePath);
    }

    [TestMethod]
    public void UnknownFieldsAreToleratedWhenKnownFieldsAreValid()
    {
        ProtoRequest proto = DropFilesRequestProtoMapper.ToProto(CreateCanonicalDomain());
        byte[] withUnknown = [.. proto.ToByteArray(), .. Convert.FromHexString("980601")];
        ProtoRequest parsed = ProtoRequest.Parser.ParseFrom(withUnknown);
        DomainRequest restored = DropFilesRequestProtoMapper.FromProto(parsed);
        Assert.AreEqual(TransferEffect.Copy, restored.Effect);
        Assert.AreEqual(@"C:\src\a.txt", restored.Sources[0].PhysicalFile.AbsolutePath);
    }

    private static void AssertRoundTrip(DomainRequest original)
    {
        ProtoRequest proto = DropFilesRequestProtoMapper.ToProto(original);
        DomainRequest restored = DropFilesRequestProtoMapper.FromProto(proto);
        Assert.AreEqual(original.CorrelationId, restored.CorrelationId);
        Assert.AreEqual(original.Effect, restored.Effect);
        Assert.AreEqual(original.DeadlineUtc, restored.DeadlineUtc);
        Assert.AreEqual(original.Sources.Count, restored.Sources.Count);
        for (int index = 0; index < original.Sources.Count; index++)
        {
            Assert.AreEqual(original.Sources[index], restored.Sources[index]);
        }

        Assert.AreEqual(original.Target.Kind, restored.Target.Kind);
        Assert.AreEqual(original.Target.FilesystemContainer, restored.Target.FilesystemContainer);
        Assert.AreEqual(original.Target.ApplicationSurface, restored.Target.ApplicationSurface);

        ProtoRequest again = DropFilesRequestProtoMapper.ToProto(restored);
        Assert.AreEqual(proto.ToByteString(), again.ToByteString());
    }

    private static void AssertCorrelationRejected(string correlationId)
    {
        ProtoRequest message = CreateCanonicalCopy();
        message.CorrelationId = correlationId;
        AssertMapped(message, ProtocolContractErrorCode.InvalidCorrelationId, "correlation_id");
    }

    private static void AssertMapped(
        ProtoRequest message,
        ProtocolContractErrorCode code,
        string fieldPath)
    {
        ProtocolContractException ex = Assert.ThrowsExactly<ProtocolContractException>(
            () => DropFilesRequestProtoMapper.FromProto(message));
        Assert.AreEqual(code, ex.Code);
        Assert.AreEqual(fieldPath, ex.FieldPath);
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
            [File(@"C:\src\a.txt")],
            TransferEffect.Copy,
            Container(@"C:\dst"));

    private static SourceItem File(string path) => new(new PhysicalFileSource(path));

    private static TargetSelector Container(string path) =>
        TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(path));
}
