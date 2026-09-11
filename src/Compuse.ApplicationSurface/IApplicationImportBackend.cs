using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public interface IApplicationImportBackend
{
    public ValueTask<ApplicationImportExecution> ExecuteAsync(
        ApplicationImportPlan plan,
        CancellationToken cancellationToken);
}
