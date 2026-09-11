using Compuse.Contracts;
using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public sealed record WindowDiscoveryResult(WindowIdentity? Window, RefusalInfo? Refusal)
{
    public bool Found => Window is not null;

    public static WindowDiscoveryResult ForWindow(WindowIdentity window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new WindowDiscoveryResult(window, Refusal: null);
    }

    public static WindowDiscoveryResult ForRefusal(RefusalInfo refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new WindowDiscoveryResult(Window: null, Refusal: refusal);
    }
}
