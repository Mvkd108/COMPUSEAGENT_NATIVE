using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Compuse.Requests;

namespace Compuse.Discovery;

public sealed class WindowsFilesystemDiscovery : IFilesystemDiscovery
{
    private const uint InspectAccess = NativeMethods.FileReadAttributes;
    private const uint AddFileAccess = NativeMethods.FileAddFile;
    private const uint ShareAll = NativeMethods.FileShareRead | NativeMethods.FileShareWrite | NativeMethods.FileShareDelete;
    private const uint OpenFlags = NativeMethods.FileFlagBackupSemantics | NativeMethods.FileFlagOpenReparsePoint;

    internal uint LastOpenedAccess { get; private set; }

    public PathInspection Inspect(string absolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryOpen(absolutePath, InspectAccess, out nint handle, out int error))
        {
            return MapOpenFailure(absolutePath, error);
        }

        try
        {
            if (!NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.ByHandleFileInformation info))
            {
                int infoError = Marshal.GetLastPInvokeError();
                return MapOpenFailure(absolutePath, infoError == 0 ? NativeMethods.ErrorAccessDenied : infoError);
            }

            if (info.FileIndex == 0)
            {
                return new PathInspection(absolutePath, PathPresence.Inaccessible, identity: null, byteLength: 0, canAddFiles: false);
            }

            FilesystemIdentity identity = new(absolutePath, info.VolumeSerialNumber, info.FileIndex);
            if (info.IsDirectory)
            {
                bool canAdd = CanAddFiles(absolutePath);
                return new PathInspection(absolutePath, PathPresence.Directory, identity, byteLength: 0, canAdd);
            }

            return new PathInspection(absolutePath, PathPresence.File, identity, info.FileSize, canAddFiles: false);
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }

    public SourceSnapshot DiscoverSource(string absolutePath, CancellationToken cancellationToken)
    {
        PathInspection inspection = Inspect(absolutePath, cancellationToken);
        return inspection.Presence switch
        {
            PathPresence.File => new SourceSnapshot(
                absolutePath,
                SourceStatus.PhysicalFile,
                inspection.Identity,
                inspection.ByteLength),
            PathPresence.Directory => new SourceSnapshot(absolutePath, SourceStatus.NotAFile, identity: null, byteLength: 0),
            PathPresence.Missing => new SourceSnapshot(absolutePath, SourceStatus.Missing, identity: null, byteLength: 0),
            PathPresence.Inaccessible => new SourceSnapshot(absolutePath, SourceStatus.Inaccessible, identity: null, byteLength: 0),
            _ => new SourceSnapshot(absolutePath, SourceStatus.Inaccessible, identity: null, byteLength: 0)
        };
    }

    public IReadOnlyList<SourceSnapshot> DiscoverSources(
        IReadOnlyList<string> absolutePaths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(absolutePaths);
        List<SourceSnapshot> snapshots = new(absolutePaths.Count);
        for (int index = 0; index < absolutePaths.Count; index++)
        {
            string? path = absolutePaths[index];
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Source paths cannot contain null or empty elements.", nameof(absolutePaths));
            }

            snapshots.Add(DiscoverSource(path, cancellationToken));
        }

        return new ReadOnlyCollection<SourceSnapshot>(snapshots.ToArray());
    }

    public DestinationSnapshot DiscoverDestination(TargetSelector target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (target.Kind == TargetSelectorKind.ApplicationSurface)
        {
            return new DestinationSnapshot(
                TargetSelectorKind.ApplicationSurface,
                DestinationStatus.ApplicationSurfaceUnsupported,
                identity: null,
                canAddFiles: false);
        }

        if (target.FilesystemContainer is null)
        {
            throw new ArgumentException("A filesystem-container selector requires a container target.", nameof(target));
        }

        PathInspection inspection = Inspect(target.FilesystemContainer.AbsolutePath, cancellationToken);
        return inspection.Presence switch
        {
            PathPresence.Directory when inspection.CanAddFiles => new DestinationSnapshot(
                TargetSelectorKind.FilesystemContainer,
                DestinationStatus.FilesystemContainer,
                inspection.Identity,
                canAddFiles: true),
            PathPresence.Directory => new DestinationSnapshot(
                TargetSelectorKind.FilesystemContainer,
                DestinationStatus.Inaccessible,
                identity: null,
                canAddFiles: false),
            PathPresence.File => new DestinationSnapshot(
                TargetSelectorKind.FilesystemContainer,
                DestinationStatus.NotAContainer,
                inspection.Identity,
                canAddFiles: false),
            PathPresence.Missing => new DestinationSnapshot(
                TargetSelectorKind.FilesystemContainer,
                DestinationStatus.Missing,
                identity: null,
                canAddFiles: false),
            _ => new DestinationSnapshot(
                TargetSelectorKind.FilesystemContainer,
                DestinationStatus.Inaccessible,
                identity: null,
                canAddFiles: false)
        };
    }

    private static PathInspection MapOpenFailure(string absolutePath, int error)
    {
        PathPresence presence = error is NativeMethods.ErrorFileNotFound or NativeMethods.ErrorPathNotFound
            ? PathPresence.Missing
            : PathPresence.Inaccessible;
        return new PathInspection(absolutePath, presence, identity: null, byteLength: 0, canAddFiles: false);
    }

    private bool TryOpen(string absolutePath, uint access, out nint handle, out int error)
    {
        LastOpenedAccess = access;
        handle = NativeMethods.CreateFileW(
            absolutePath,
            access,
            ShareAll,
            nint.Zero,
            NativeMethods.OpenExisting,
            OpenFlags,
            nint.Zero);
        if (handle == nint.Zero || handle == NativeMethods.InvalidHandleValue)
        {
            error = Marshal.GetLastPInvokeError();
            handle = nint.Zero;
            return false;
        }

        error = 0;
        return true;
    }

    private bool CanAddFiles(string directoryPath)
    {
        if (!TryOpen(directoryPath, AddFileAccess, out nint handle, out _))
        {
            return false;
        }

        _ = NativeMethods.CloseHandle(handle);
        return true;
    }
}
