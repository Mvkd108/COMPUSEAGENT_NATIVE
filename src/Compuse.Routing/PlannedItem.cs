using Compuse.Discovery;
using Compuse.Requests;

namespace Compuse.Routing;

public sealed class PlannedItem
{
    public PlannedItem(
        string sourcePath,
        string destinationPath,
        FilesystemIdentity sourceIdentity,
        long byteLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(sourceIdentity);
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Byte length cannot be negative.");
        }

        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        SourceIdentity = sourceIdentity;
        ByteLength = byteLength;
    }

    public string SourcePath { get; }

    public string DestinationPath { get; }

    public FilesystemIdentity SourceIdentity { get; }

    public long ByteLength { get; }
}
