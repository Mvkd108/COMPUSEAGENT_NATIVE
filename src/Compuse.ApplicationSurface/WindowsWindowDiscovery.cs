using Compuse.Contracts;
using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public sealed class WindowsWindowDiscovery : IWindowDiscovery
{
    private readonly IUiaSessionFactory _sessionFactory;

    public WindowsWindowDiscovery()
        : this(FlaUiSessionFactory.Instance)
    {
    }

    internal WindowsWindowDiscovery(IUiaSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public WindowDiscoveryResult Discover(ApplicationSurfaceTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        using IUiaSession session = _sessionFactory.Create();
        WindowResolution resolution = session.ResolveWindow(target, cancellationToken);
        return resolution.Status switch
        {
            WindowResolutionStatus.Found => WindowDiscoveryResult.ForWindow(resolution.Window!.ToIdentity(target.ProcessImageName)),
            WindowResolutionStatus.Ambiguous => WindowDiscoveryResult.ForRefusal(
                new RefusalInfo(
                    DropFilesRefusalCode.AmbiguousTarget,
                    "Multiple windows match the application-surface selector; add a window title, class, PID, or HWND hint.")),
            _ => WindowDiscoveryResult.ForRefusal(
                new RefusalInfo(
                    DropFilesRefusalCode.TargetNotFound,
                    "No window matched the application-surface selector."))
        };
    }
}
