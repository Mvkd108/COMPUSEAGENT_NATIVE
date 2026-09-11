using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public interface IApplicationImportRecipe
{
    public string Name { get; }

    public bool Matches(WindowIdentity window);

    public IApplicationImportBackend CreateBackend();
}
