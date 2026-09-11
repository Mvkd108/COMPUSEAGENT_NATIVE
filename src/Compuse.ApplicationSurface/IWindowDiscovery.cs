using Compuse.Requests;

namespace Compuse.ApplicationSurface;

public interface IWindowDiscovery
{
    public WindowDiscoveryResult Discover(ApplicationSurfaceTarget target, CancellationToken cancellationToken);
}
