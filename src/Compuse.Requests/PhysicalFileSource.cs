namespace Compuse.Requests;

public sealed record PhysicalFileSource
{
    public PhysicalFileSource(string absolutePath)
    {
        AbsolutePath = WindowsPath.NormalizeAbsolute(absolutePath, nameof(absolutePath));
    }

    public string AbsolutePath { get; }
}
