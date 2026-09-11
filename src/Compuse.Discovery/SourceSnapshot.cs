namespace Compuse.Discovery;

public sealed class SourceSnapshot
{
    public SourceSnapshot(
        string requestedPath,
        SourceStatus status,
        FilesystemIdentity? identity,
        long byteLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        if (status == default || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Source status must be a defined nonzero value.");
        }

        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "Byte length cannot be negative.");
        }

        if (status == SourceStatus.PhysicalFile && identity is null)
        {
            throw new ArgumentException("A physical-file snapshot requires an identity.", nameof(identity));
        }

        RequestedPath = requestedPath;
        Status = status;
        Identity = identity;
        ByteLength = byteLength;
    }

    public string RequestedPath { get; }

    public SourceStatus Status { get; }

    public FilesystemIdentity? Identity { get; }

    public long ByteLength { get; }
}
