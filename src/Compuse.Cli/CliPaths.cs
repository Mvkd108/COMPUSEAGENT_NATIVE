namespace Compuse.Cli;

internal static class CliPaths
{
    internal static string ResolveAbsolute(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (full.Length > 3 && full.EndsWith('\\'))
        {
            full = full.TrimEnd('\\');
        }

        return full;
    }
}
