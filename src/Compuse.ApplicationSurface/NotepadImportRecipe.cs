using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public sealed class NotepadImportRecipe : IApplicationImportRecipe
{
    private static readonly string[] RecognizedProcessNames = ["notepad"];

    public string Name => "notepad";

    public bool Matches(WindowIdentity window)
    {
        ArgumentNullException.ThrowIfNull(window);
        string processName = Path.GetFileNameWithoutExtension(window.ProcessImageName);
        for (int index = 0; index < RecognizedProcessNames.Length; index++)
        {
            if (string.Equals(processName, RecognizedProcessNames[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public IApplicationImportBackend CreateBackend() => new NotepadImportBackend();
}
