using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public interface IApplicationImportRecipeCatalog
{
    public IApplicationImportRecipe? FindRecipe(WindowIdentity window);

    public IApplicationImportBackend CreateBackend(string recipeName);
}
