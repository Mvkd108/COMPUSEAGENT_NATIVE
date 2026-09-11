using Compuse.Requests;

namespace Compuse.Discovery;

public interface IFilesystemDiscovery
{
    public PathInspection Inspect(string absolutePath, CancellationToken cancellationToken);

    public SourceSnapshot DiscoverSource(string absolutePath, CancellationToken cancellationToken);

    public IReadOnlyList<SourceSnapshot> DiscoverSources(
        IReadOnlyList<string> absolutePaths,
        CancellationToken cancellationToken);

    public DestinationSnapshot DiscoverDestination(TargetSelector target, CancellationToken cancellationToken);
}
