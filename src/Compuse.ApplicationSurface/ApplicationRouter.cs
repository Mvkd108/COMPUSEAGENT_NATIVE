using System.Text;
using Compuse.Contracts;
using Compuse.Discovery;
using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public static class ApplicationRouter
{
    public static RouteDecision Route(
        TransferEffect effect,
        IReadOnlyList<SourceSnapshot> sources,
        WindowDiscoveryResult discovery,
        IApplicationImportRecipeCatalog? recipeCatalog)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(discovery);

        if (discovery.Refusal is not null)
        {
            return RouteDecision.ForRefusal(discovery.Refusal);
        }

        if (sources.Count != 1)
        {
            return RouteDecision.ForRefusal(
                new RefusalInfo(
                    DropFilesRefusalCode.SingleSourceRequired,
                    "The application-surface prototype imports exactly one source file per operation."));
        }

        SourceSnapshot source = sources[0];
        RouteDecision? sourceRefusal = SourceRefusal(source);
        if (sourceRefusal is not null)
        {
            return sourceRefusal;
        }

        WindowIdentity window = discovery.Window!;
        IApplicationImportRecipe? recipe = recipeCatalog?.FindRecipe(window);
        if (recipe is null)
        {
            return RouteDecision.ForRefusal(
                new RefusalInfo(
                    DropFilesRefusalCode.UnsupportedApplicationRecipe,
                    $"No supported application recipe matches the target application '{window.ProcessImageName}'."));
        }

        string? expectedText = ReadExpectedText(source.RequestedPath);
        if (expectedText is null)
        {
            return RouteDecision.ForRefusal(
                new RefusalInfo(
                    DropFilesRefusalCode.SourceInaccessible,
                    "The source file content could not be read for verification."));
        }

        return RouteDecision.ForApplicationPlan(
            new ApplicationImportPlan(
                effect,
                window,
                source.RequestedPath,
                source.ByteLength,
                expectedText,
                source.Identity!,
                recipe.Name));
    }

    private static RouteDecision? SourceRefusal(SourceSnapshot source) =>
        source.Status switch
        {
            SourceStatus.PhysicalFile when source.Identity is not null => null,
            SourceStatus.Missing => RouteDecision.ForRefusal(
                new RefusalInfo(DropFilesRefusalCode.SourceNotFound, "A source file does not exist.")),
            SourceStatus.NotAFile => RouteDecision.ForRefusal(
                new RefusalInfo(DropFilesRefusalCode.SourceNotFile, "A source path exists and is not a file.")),
            _ => RouteDecision.ForRefusal(
                new RefusalInfo(DropFilesRefusalCode.SourceInaccessible, "A source file could not be opened."))
        };

    private static string? ReadExpectedText(string path)
    {
        try
        {
            using StreamReader reader = new(
                path,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
