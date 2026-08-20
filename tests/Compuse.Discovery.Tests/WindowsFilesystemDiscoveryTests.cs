using Compuse.Requests;

namespace Compuse.Discovery.Tests;

[TestClass]
public sealed class WindowsFilesystemDiscoveryTests
{
    private readonly WindowsFilesystemDiscovery _discovery = new();

    [TestMethod]
    public void InspectsFileAndDirectory()
    {
        using TempTree tree = new();
        string file = tree.File("a.txt", "payload");
        string dir = tree.Dir("dst");

        PathInspection fileInspection = _discovery.Inspect(file, CancellationToken.None);
        PathInspection dirInspection = _discovery.Inspect(dir, CancellationToken.None);

        Assert.AreEqual(PathPresence.File, fileInspection.Presence);
        Assert.AreEqual("payload".Length, fileInspection.ByteLength);
        Assert.IsNotNull(fileInspection.Identity);
        Assert.AreEqual(PathPresence.Directory, dirInspection.Presence);
        Assert.IsTrue(dirInspection.CanAddFiles);
        Assert.IsNotNull(dirInspection.Identity);
    }

    [TestMethod]
    public void MissingPathIsMissing()
    {
        using TempTree tree = new();
        string missing = Path.Combine(tree.Root, "no-such.txt");
        PathInspection inspection = _discovery.Inspect(missing, CancellationToken.None);
        Assert.AreEqual(PathPresence.Missing, inspection.Presence);
        Assert.IsNull(inspection.Identity);
    }

    [TestMethod]
    public void FileAsDestinationIsNotAContainer()
    {
        using TempTree tree = new();
        string file = tree.File("not-a-dir.txt");
        DestinationSnapshot snapshot = _discovery.DiscoverDestination(
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(file)),
            CancellationToken.None);
        Assert.AreEqual(DestinationStatus.NotAContainer, snapshot.Status);
        Assert.AreEqual(TargetSelectorKind.FilesystemContainer, snapshot.RequestedKind);
    }

    [TestMethod]
    public void ApplicationSurfaceIsUnsupportedWithoutTouchingFilesystem()
    {
        DestinationSnapshot snapshot = _discovery.DiscoverDestination(
            TargetSelector.FromApplicationSurface(new ApplicationSurfaceTarget("notepad.exe")),
            CancellationToken.None);
        Assert.AreEqual(DestinationStatus.ApplicationSurfaceUnsupported, snapshot.Status);
        Assert.IsNull(snapshot.Identity);
        Assert.IsFalse(snapshot.CanAddFiles);
    }

    [TestMethod]
    public void IdentityIsStableAcrossInspects()
    {
        using TempTree tree = new();
        string file = tree.File("stable.txt", "same");
        PathInspection first = _discovery.Inspect(file, CancellationToken.None);
        PathInspection second = _discovery.Inspect(file, CancellationToken.None);
        Assert.IsTrue(first.Identity!.SameObjectAs(second.Identity!));
        Assert.AreEqual(first.ByteLength, second.ByteLength);
    }

    [TestMethod]
    public void DistinctFilesHaveDistinctIdentities()
    {
        using TempTree tree = new();
        string first = tree.File("first.txt", "one");
        string second = tree.File("second.txt", "two-two");
        FilesystemIdentity left = _discovery.Inspect(first, CancellationToken.None).Identity!;
        FilesystemIdentity right = _discovery.Inspect(second, CancellationToken.None).Identity!;
        Assert.IsFalse(left.SameObjectAs(right));
        Assert.AreEqual("one".Length, _discovery.Inspect(first, CancellationToken.None).ByteLength);
        Assert.AreEqual("two-two".Length, _discovery.Inspect(second, CancellationToken.None).ByteLength);
    }

    [TestMethod]
    public void DirectorySourceIsNotAFile()
    {
        using TempTree tree = new();
        string dir = tree.Dir("folder");
        SourceSnapshot snapshot = _discovery.DiscoverSource(dir, CancellationToken.None);
        Assert.AreEqual(SourceStatus.NotAFile, snapshot.Status);
    }

    [TestMethod]
    public void DiscoverSourcesPreservesOrder()
    {
        using TempTree tree = new();
        string first = tree.File("a.txt");
        string second = tree.File("b.txt");
        IReadOnlyList<SourceSnapshot> snapshots = _discovery.DiscoverSources([first, second], CancellationToken.None);
        Assert.AreEqual(2, snapshots.Count);
        Assert.AreEqual(first, snapshots[0].RequestedPath);
        Assert.AreEqual(second, snapshots[1].RequestedPath);
        Assert.AreEqual(SourceStatus.PhysicalFile, snapshots[0].Status);
        Assert.AreEqual(SourceStatus.PhysicalFile, snapshots[1].Status);
    }

    [TestMethod]
    public void CancellationIsObserved()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        _ = Assert.ThrowsExactly<OperationCanceledException>(
            () => _discovery.Inspect(@"C:\Windows", cts.Token));
    }
}
