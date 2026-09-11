namespace Compuse.ApplicationSurface.Tests;

internal sealed class TempTree : IDisposable
{
    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "compuse-appsurface-tests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string File(string name, string contents = "hello")
    {
        string path = Path.Combine(Root, name);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
