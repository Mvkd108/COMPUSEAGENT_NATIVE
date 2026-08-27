using Compuse.Discovery;

namespace Compuse.Filesystem;

public sealed class ItemObservation
{
    public ItemObservation(string sourcePath, string destinationPath, long expectedLength, PathInspection destination, PathInspection sourceAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sourceAfter);
        if (expectedLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedLength), expectedLength, "Expected length cannot be negative.");
        }

        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        ExpectedLength = expectedLength;
        Destination = destination;
        SourceAfter = sourceAfter;
    }

    public string SourcePath { get; }

    public string DestinationPath { get; }

    public long ExpectedLength { get; }

    public PathInspection Destination { get; }

    public PathInspection SourceAfter { get; }

    public bool DestinationMatchesCopy =>
        Destination.Presence == PathPresence.File
        && Destination.ByteLength == ExpectedLength
        && Destination.Identity is not null;

    public bool SourceRemoved => SourceAfter.Presence == PathPresence.Missing;
}
