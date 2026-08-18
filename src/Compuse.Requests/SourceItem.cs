namespace Compuse.Requests;

public sealed record SourceItem
{
    public SourceItem(PhysicalFileSource physicalFile)
    {
        ArgumentNullException.ThrowIfNull(physicalFile);
        PhysicalFile = physicalFile;
    }

    public PhysicalFileSource PhysicalFile { get; }
}
