using Compuse.Discovery;
using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.Filesystem.Tests;

[TestClass]
public sealed class FilesystemTransferBackendTests
{
    [TestMethod]
    public void CopyObservesDestinationWithoutRemovingSource()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        TransferExecution execution = Execute(tree, TransferEffect.Copy, source, destDir);

        Assert.IsTrue(execution.ApiSucceeded, $"HRESULT 0x{execution.ApiHresult:X8}");
        Assert.AreEqual(1, execution.Observations.Count);
        Assert.IsTrue(execution.Observations[0].DestinationMatchesCopy);
        Assert.IsFalse(execution.Observations[0].SourceRemoved);
        Assert.IsTrue(System.IO.File.Exists(source));
        Assert.AreEqual("payload", System.IO.File.ReadAllText(execution.Observations[0].DestinationPath));
    }

    [TestMethod]
    public void MoveObservesDestinationAndRemovesSource()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        TransferExecution execution = Execute(tree, TransferEffect.Move, source, destDir);

        Assert.IsTrue(execution.ApiSucceeded, $"HRESULT 0x{execution.ApiHresult:X8}");
        Assert.IsTrue(execution.Observations[0].DestinationMatchesCopy);
        Assert.IsTrue(execution.Observations[0].SourceRemoved);
        Assert.IsFalse(System.IO.File.Exists(source));
        Assert.AreEqual("payload", System.IO.File.ReadAllText(execution.Observations[0].DestinationPath));
    }

    private static TransferExecution Execute(TempTree tree, TransferEffect effect, string source, string destDir)
    {
        WindowsFilesystemDiscovery discovery = new();
        SourceSnapshot sourceSnapshot = discovery.DiscoverSource(source, CancellationToken.None);
        DestinationSnapshot destination = discovery.DiscoverDestination(
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir)),
            CancellationToken.None);
        PlannedItem item = new(
            sourceSnapshot.RequestedPath,
            DropFilesRouter.CombineDestination(destination.Identity!.NormalizedPath, sourceSnapshot.RequestedPath),
            sourceSnapshot.Identity!,
            sourceSnapshot.ByteLength);
        ExecutionPlan plan = new(effect, destination.Identity, [item]);
        FilesystemTransferBackend backend = new(discovery);
        return backend.Execute(plan, CancellationToken.None);
    }
}
