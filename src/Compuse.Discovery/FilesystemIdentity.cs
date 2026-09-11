namespace Compuse.Discovery;

public sealed record FilesystemIdentity
{
    public FilesystemIdentity(string normalizedPath, uint volumeSerialNumber, ulong fileIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);
        if (fileIndex == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileIndex), fileIndex, "A filesystem identity requires a nonzero file index.");
        }

        NormalizedPath = normalizedPath;
        VolumeSerialNumber = volumeSerialNumber;
        FileIndex = fileIndex;
    }

    public string NormalizedPath { get; }

    public uint VolumeSerialNumber { get; }

    public ulong FileIndex { get; }

    public bool SameObjectAs(FilesystemIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return VolumeSerialNumber == other.VolumeSerialNumber && FileIndex == other.FileIndex;
    }
}
