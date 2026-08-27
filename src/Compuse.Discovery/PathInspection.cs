namespace Compuse.Discovery;

public sealed class PathInspection
{
    public PathInspection(
        string path,
        PathPresence presence,
        FilesystemIdentity? identity,
        long byteLength,
        bool canAddFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (presence == default || !Enum.IsDefined(presence))
        {
            throw new ArgumentOutOfRangeException(nameof(presence), presence, "Path presence must be a defined nonzero value.");
        }

        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Byte length cannot be negative.");
        }

        if (presence is PathPresence.File or PathPresence.Directory && identity is null)
        {
            throw new ArgumentException("An existing file or directory inspection requires an identity.", nameof(identity));
        }

        if (presence is PathPresence.Missing or PathPresence.Inaccessible && identity is not null)
        {
            throw new ArgumentException("Missing and inaccessible inspections cannot carry an identity.", nameof(identity));
        }

        if (presence != PathPresence.Directory && canAddFiles)
        {
            throw new ArgumentException("Only a directory inspection can report that files may be added.", nameof(canAddFiles));
        }

        Path = path;
        Presence = presence;
        Identity = identity;
        ByteLength = byteLength;
        CanAddFiles = canAddFiles;
    }

    public string Path { get; }

    public PathPresence Presence { get; }

    public FilesystemIdentity? Identity { get; }

    public long ByteLength { get; }

    public bool CanAddFiles { get; }
}
