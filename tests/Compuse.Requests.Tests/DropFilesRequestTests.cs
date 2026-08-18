using Compuse.Contracts;

namespace Compuse.Requests.Tests;

[TestClass]
public sealed class DropFilesRequestTests
{
    private static readonly DateTimeOffset UtcDeadline = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CopyToFilesystemContainerPreservesOrderAndOmitsDeadline()
    {
        CorrelationId correlationId = CorrelationId.New();
        SourceItem first = File(@"C:\src\a.txt");
        SourceItem second = File(@"C:\src\b.txt");
        TargetSelector target = TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(@"C:\dst"));
        DropFilesRequest request = DropFilesRequest.Create(
            correlationId,
            [first, second],
            TransferEffect.Copy,
            target);

        Assert.AreEqual(correlationId, request.CorrelationId);
        Assert.AreEqual(TransferEffect.Copy, request.Effect);
        Assert.AreEqual(2, request.Sources.Count);
        Assert.AreEqual(first, request.Sources[0]);
        Assert.AreEqual(second, request.Sources[1]);
        Assert.AreEqual(TargetSelectorKind.FilesystemContainer, request.Target.Kind);
        Assert.AreEqual(@"C:\dst", request.Target.FilesystemContainer?.AbsolutePath);
        Assert.IsNull(request.Target.ApplicationSurface);
        Assert.IsNull(request.DeadlineUtc);
    }

    [TestMethod]
    public void MoveToApplicationSurfacePreservesOptionalHintsAndDeadline()
    {
        ApplicationSurfaceTarget surface = new(
            "WINWORD.EXE",
            "OpusApp",
            "Document1 - Word",
            hwndHint: 0x0000000000123456,
            processIdHint: 4242);
        DropFilesRequest request = DropFilesRequest.Create(
            CorrelationId.New(),
            [File(@"C:\src\memo.docx")],
            TransferEffect.Move,
            TargetSelector.FromApplicationSurface(surface),
            UtcDeadline);

        Assert.AreEqual(TransferEffect.Move, request.Effect);
        Assert.AreEqual(TargetSelectorKind.ApplicationSurface, request.Target.Kind);
        Assert.AreEqual("WINWORD.EXE", request.Target.ApplicationSurface?.ProcessImageName);
        Assert.AreEqual("OpusApp", request.Target.ApplicationSurface?.WindowClass);
        Assert.AreEqual("Document1 - Word", request.Target.ApplicationSurface?.WindowTitle);
        Assert.AreEqual(0x0000000000123456ul, request.Target.ApplicationSurface?.HwndHint);
        Assert.AreEqual(4242u, request.Target.ApplicationSurface?.ProcessIdHint);
        Assert.AreEqual(UtcDeadline, request.DeadlineUtc);
        Assert.IsNull(request.Target.FilesystemContainer);
    }

    [TestMethod]
    public void CallerCollectionMutationDoesNotMutateRequest()
    {
        List<SourceItem> sources = [File(@"C:\src\a.txt")];
        DropFilesRequest request = DropFilesRequest.Create(
            CorrelationId.New(),
            sources,
            TransferEffect.Copy,
            Container(@"C:\dst"));

        sources.Add(File(@"C:\src\b.txt"));

        Assert.AreEqual(1, request.Sources.Count);
        Assert.AreEqual(@"C:\src\a.txt", request.Sources[0].PhysicalFile.AbsolutePath);
    }

    [TestMethod]
    public void DuplicateSourcesAreRejectedCaseInsensitively()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => DropFilesRequest.Create(
                CorrelationId.New(),
                [File(@"C:\src\a.txt"), File(@"C:\SRC\A.TXT")],
                TransferEffect.Copy,
                Container(@"C:\dst")));
    }

    [TestMethod]
    public void EmptyAndOversizedSourceListsAreRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => DropFilesRequest.Create(
                CorrelationId.New(),
                [],
                TransferEffect.Copy,
                Container(@"C:\dst")));

        SourceItem[] tooMany = new SourceItem[DropFilesRequestLimits.MaxSourceCount + 1];
        for (int index = 0; index < tooMany.Length; index++)
        {
            tooMany[index] = File(@"C:\src\f" + index + ".txt");
        }

        _ = Assert.ThrowsExactly<ArgumentException>(
            () => DropFilesRequest.Create(
                CorrelationId.New(),
                tooMany,
                TransferEffect.Copy,
                Container(@"C:\dst")));
    }

    [TestMethod]
    public void NullArgumentsAndNullSourceElementsAreRejected()
    {
        SourceItem item = File(@"C:\src\a.txt");
        TargetSelector target = Container(@"C:\dst");

        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => DropFilesRequest.Create(null!, [item], TransferEffect.Copy, target));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => DropFilesRequest.Create(CorrelationId.New(), null!, TransferEffect.Copy, target));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => DropFilesRequest.Create(CorrelationId.New(), [item], TransferEffect.Copy, null!));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => DropFilesRequest.Create(
                CorrelationId.New(),
                [item, null!],
                TransferEffect.Copy,
                target));
        _ = Assert.ThrowsExactly<ArgumentNullException>(() => new SourceItem(null!));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => TargetSelector.FromFilesystemContainer(null!));
        _ = Assert.ThrowsExactly<ArgumentNullException>(
            () => TargetSelector.FromApplicationSurface(null!));
    }

    [TestMethod]
    public void UndefinedEffectAndNonUtcDeadlineAreRejected()
    {
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => DropFilesRequest.Create(
                CorrelationId.New(),
                [File(@"C:\src\a.txt")],
                (TransferEffect)0,
                Container(@"C:\dst")));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => DropFilesRequest.Create(
                CorrelationId.New(),
                [File(@"C:\src\a.txt")],
                (TransferEffect)99,
                Container(@"C:\dst")));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => DropFilesRequest.Create(
                CorrelationId.New(),
                [File(@"C:\src\a.txt")],
                TransferEffect.Copy,
                Container(@"C:\dst"),
                new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.FromHours(5.5))));
    }

    [TestMethod]
    public void ApplicationSurfaceRejectsInvalidHintsAndImageNames()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new ApplicationSurfaceTarget(""));
        _ = Assert.ThrowsExactly<ArgumentException>(() => new ApplicationSurfaceTarget(@"C:\Windows\notepad.exe"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => new ApplicationSurfaceTarget("notepad/exe"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => new ApplicationSurfaceTarget("CON.exe"));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new ApplicationSurfaceTarget("notepad.exe", windowClass: "   "));
        _ = Assert.ThrowsExactly<ArgumentException>(
            () => new ApplicationSurfaceTarget("notepad.exe", windowTitle: ""));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ApplicationSurfaceTarget("notepad.exe", hwndHint: 0));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new ApplicationSurfaceTarget("notepad.exe", processIdHint: 0));
    }

    [TestMethod]
    public void MaximumLegalSourceCountIsAccepted()
    {
        SourceItem[] sources = new SourceItem[DropFilesRequestLimits.MaxSourceCount];
        for (int index = 0; index < sources.Length; index++)
        {
            sources[index] = File(@"C:\src\f" + index + ".txt");
        }

        DropFilesRequest request = DropFilesRequest.Create(
            CorrelationId.New(),
            sources,
            TransferEffect.Copy,
            Container(@"C:\dst"));

        Assert.AreEqual(DropFilesRequestLimits.MaxSourceCount, request.Sources.Count);
    }

    private static SourceItem File(string path) => new(new PhysicalFileSource(path));

    private static TargetSelector Container(string path) =>
        TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(path));
}
