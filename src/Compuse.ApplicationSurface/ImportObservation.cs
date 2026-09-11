namespace Compuse.ApplicationSurface;

public sealed class ImportObservation
{
    public ImportObservation(
        string sourcePath,
        string? documentText,
        bool documentTextMatches,
        bool sourceRemoved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        SourcePath = sourcePath;
        DocumentText = documentText;
        DocumentTextMatches = documentTextMatches;
        SourceRemoved = sourceRemoved;
    }

    public string SourcePath { get; }

    public string? DocumentText { get; }

    public bool DocumentTextMatches { get; }

    public bool SourceRemoved { get; }
}
