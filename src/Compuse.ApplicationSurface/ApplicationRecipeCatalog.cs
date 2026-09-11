using System.Collections.ObjectModel;
using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public sealed class ApplicationRecipeCatalog : IApplicationImportRecipeCatalog
{
    private readonly ReadOnlyCollection<IApplicationImportRecipe> _recipes;
    private readonly Dictionary<string, IApplicationImportRecipe> _byName;

    public ApplicationRecipeCatalog(params IApplicationImportRecipe[] recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);
        if (recipes.Length == 0)
        {
            throw new ArgumentException("A recipe catalog requires at least one recipe.", nameof(recipes));
        }

        _recipes = new ReadOnlyCollection<IApplicationImportRecipe>(recipes.ToArray());
        Dictionary<string, IApplicationImportRecipe> byName = new(StringComparer.Ordinal);
        for (int index = 0; index < recipes.Length; index++)
        {
            IApplicationImportRecipe recipe = recipes[index]
                ?? throw new ArgumentException("Recipe catalog entries cannot be null.", nameof(recipes));
            if (string.IsNullOrWhiteSpace(recipe.Name))
            {
                throw new ArgumentException("A recipe name cannot be empty.", nameof(recipes));
            }

            if (!byName.TryAdd(recipe.Name, recipe))
            {
                throw new ArgumentException($"Duplicate recipe name '{recipe.Name}'.", nameof(recipes));
            }
        }

        _byName = byName;
    }

    public IApplicationImportRecipe? FindRecipe(WindowIdentity window)
    {
        ArgumentNullException.ThrowIfNull(window);
        for (int index = 0; index < _recipes.Count; index++)
        {
            IApplicationImportRecipe recipe = _recipes[index];
            if (recipe.Matches(window))
            {
                return recipe;
            }
        }

        return null;
    }

    public IApplicationImportBackend CreateBackend(string recipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipeName);
        if (!_byName.TryGetValue(recipeName, out IApplicationImportRecipe? recipe))
        {
            throw new ArgumentOutOfRangeException(nameof(recipeName), recipeName, $"Unknown application recipe '{recipeName}'.");
        }

        return recipe.CreateBackend();
    }
}
