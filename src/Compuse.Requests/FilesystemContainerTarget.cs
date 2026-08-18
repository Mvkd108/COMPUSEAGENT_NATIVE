namespace Compuse.Requests;

public sealed record FilesystemContainerTarget
{
    public FilesystemContainerTarget(string absolutePath)
    {
        AbsolutePath = WindowsPath.NormalizeAbsolute(absolutePath, nameof(absolutePath));
    }

    public string AbsolutePath { get; }
}
