using Compuse.Requests;

namespace Compuse.Discovery;

public sealed class DestinationSnapshot
{
    public DestinationSnapshot(
        TargetSelectorKind requestedKind,
        DestinationStatus status,
        FilesystemIdentity? identity,
        bool canAddFiles)
    {
        if (requestedKind == default || !Enum.IsDefined(requestedKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedKind),
                requestedKind,
                "Requested kind must be a defined nonzero value.");
        }

        if (status == default || !Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Destination status must be a defined nonzero value.");
        }

        if (status == DestinationStatus.FilesystemContainer && identity is null)
        {
            throw new ArgumentException("A filesystem-container snapshot requires an identity.", nameof(identity));
        }

        if (status == DestinationStatus.FilesystemContainer && !canAddFiles)
        {
            throw new ArgumentException("A filesystem-container snapshot must be able to add files.", nameof(canAddFiles));
        }

        if (status != DestinationStatus.FilesystemContainer && canAddFiles)
        {
            throw new ArgumentException("Only a filesystem-container snapshot can report that files may be added.", nameof(canAddFiles));
        }

        RequestedKind = requestedKind;
        Status = status;
        Identity = identity;
        CanAddFiles = canAddFiles;
    }

    public TargetSelectorKind RequestedKind { get; }

    public DestinationStatus Status { get; }

    public FilesystemIdentity? Identity { get; }

    public bool CanAddFiles { get; }
}
