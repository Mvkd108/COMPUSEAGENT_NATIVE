using Compuse.Discovery;
using Compuse.Filesystem;

namespace Compuse.DropFiles.Tests;

internal static class TransferObservation
{
    private const int Abort = unchecked((int)0x80004004);

    internal static TransferExecution Aborted(WindowsFilesystemDiscovery discovery, string source, string destDir)
    {
        string destFile = Path.Combine(destDir, Path.GetFileName(source));
        PathInspection destination = discovery.Inspect(destFile, CancellationToken.None);
        PathInspection sourceAfter = discovery.Inspect(source, CancellationToken.None);
        SourceSnapshot snapshot = discovery.DiscoverSource(source, CancellationToken.None);
        return new TransferExecution(
            Abort,
            anyAborted: true,
            [new ItemObservation(source, destFile, snapshot.ByteLength, destination, sourceAfter)]);
    }

    internal static TransferExecution CommittedCopy(WindowsFilesystemDiscovery discovery, string source, string destDir)
    {
        string destFile = Path.Combine(destDir, Path.GetFileName(source));
        SourceSnapshot snapshot = discovery.DiscoverSource(source, CancellationToken.None);
        PathInspection destination = new(
            destFile,
            PathPresence.File,
            snapshot.Identity,
            snapshot.ByteLength,
            canAddFiles: false);
        PathInspection sourceAfter = discovery.Inspect(source, CancellationToken.None);
        return new TransferExecution(
            0,
            anyAborted: false,
            [new ItemObservation(source, destFile, snapshot.ByteLength, destination, sourceAfter)]);
    }
}
