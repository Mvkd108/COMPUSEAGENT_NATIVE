using Compuse.Contracts;
using Compuse.Discovery;
using Compuse.Requests;

namespace Compuse.Routing;

public sealed class DropFilesRouter
{
    public static RouteDecision Route(
        DropFilesRequest request,
        IReadOnlyList<SourceSnapshot> sources,
        DestinationSnapshot destination,
        IReadOnlyList<PathInspection> destinationChildren)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(destinationChildren);

        if (sources.Count != request.Sources.Count)
        {
            throw new ArgumentException("Source snapshots must align with the request sources.", nameof(sources));
        }

        if (destination.Status == DestinationStatus.ApplicationSurfaceUnsupported
            || destination.RequestedKind == TargetSelectorKind.ApplicationSurface)
        {
            return Refuse(DropFilesRefusalCode.UnsupportedTargetKind, "Application-surface targets are not supported in the filesystem prototype.");
        }

        RouteDecision? destinationRefusal = DestinationRefusal(destination);
        if (destinationRefusal is not null)
        {
            return destinationRefusal;
        }

        RouteDecision? sourceRefusal = SourceRefusal(sources);
        if (sourceRefusal is not null)
        {
            return sourceRefusal;
        }

        if (destinationChildren.Count != sources.Count)
        {
            throw new ArgumentException(
                "Destination child inspections must align with the request sources when the destination is a container.",
                nameof(destinationChildren));
        }

        List<PlannedItem> items = new(sources.Count);
        HashSet<string> destinationNames = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < sources.Count; index++)
        {
            SourceSnapshot source = sources[index];
            string destinationPath = CombineDestination(destination.Identity!.NormalizedPath, source.RequestedPath);
            string destinationName = Path.GetFileName(destinationPath);
            if (!destinationNames.Add(destinationName))
            {
                return Refuse(DropFilesRefusalCode.Collision, "Two sources would write the same destination file name.");
            }

            PathInspection child = destinationChildren[index]
                ?? throw new ArgumentException("Destination child inspections cannot contain null elements.", nameof(destinationChildren));
            if (child.Presence is PathPresence.File or PathPresence.Directory)
            {
                return Refuse(DropFilesRefusalCode.Collision, "A destination file already exists and overwrite is refused.");
            }

            if (child.Presence == PathPresence.Inaccessible)
            {
                return Refuse(DropFilesRefusalCode.DestinationInaccessible, "A destination path could not be inspected.");
            }

            items.Add(new PlannedItem(source.RequestedPath, destinationPath, source.Identity!, source.ByteLength));
        }

        return RouteDecision.ForPlan(new ExecutionPlan(request.Effect, destination.Identity!, items));
    }

    private static RouteDecision? DestinationRefusal(DestinationSnapshot destination) =>
        destination.Status switch
        {
            DestinationStatus.Missing => Refuse(
                DropFilesRefusalCode.DestinationMissing,
                "The destination directory does not exist."),
            DestinationStatus.NotAContainer => Refuse(
                DropFilesRefusalCode.DestinationNotContainer,
                "The destination path exists and is not a directory."),
            DestinationStatus.Inaccessible => Refuse(
                DropFilesRefusalCode.DestinationInaccessible,
                "The destination directory cannot be written."),
            DestinationStatus.FilesystemContainer => null,
            _ => Refuse(
                DropFilesRefusalCode.DestinationInaccessible,
                "The destination could not be used.")
        };

    private static RouteDecision? SourceRefusal(IReadOnlyList<SourceSnapshot> sources)
    {
        for (int index = 0; index < sources.Count; index++)
        {
            SourceSnapshot source = sources[index]
                ?? throw new ArgumentException("Source snapshots cannot contain null elements.", nameof(sources));
            switch (source.Status)
            {
                case SourceStatus.Missing:
                    return Refuse(DropFilesRefusalCode.SourceNotFound, "A source file does not exist.");
                case SourceStatus.NotAFile:
                    return Refuse(DropFilesRefusalCode.SourceNotFile, "A source path exists and is not a file.");
                case SourceStatus.Inaccessible:
                    return Refuse(DropFilesRefusalCode.SourceInaccessible, "A source file could not be opened.");
                case SourceStatus.PhysicalFile:
                    break;
                default:
                    return Refuse(DropFilesRefusalCode.SourceInaccessible, "A source file could not be used.");
            }
        }

        return null;
    }

    public static string CombineDestination(string directoryPath, string sourcePath)
    {
        string fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ArgumentException("A source path must include a file name.", nameof(sourcePath));
        }

        if (directoryPath.EndsWith('\\'))
        {
            return directoryPath + fileName;
        }

        return directoryPath + '\\' + fileName;
    }

    private static RouteDecision Refuse(string code, string message) =>
        RouteDecision.ForRefusal(new RefusalInfo(code, message));
}
