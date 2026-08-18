namespace Compuse.Requests;

public sealed class TargetSelector
{
    private TargetSelector(
        TargetSelectorKind kind,
        FilesystemContainerTarget? filesystemContainer,
        ApplicationSurfaceTarget? applicationSurface)
    {
        Kind = kind;
        FilesystemContainer = filesystemContainer;
        ApplicationSurface = applicationSurface;
    }

    public TargetSelectorKind Kind { get; }

    public FilesystemContainerTarget? FilesystemContainer { get; }

    public ApplicationSurfaceTarget? ApplicationSurface { get; }

    public static TargetSelector FromFilesystemContainer(FilesystemContainerTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new TargetSelector(TargetSelectorKind.FilesystemContainer, target, applicationSurface: null);
    }

    public static TargetSelector FromApplicationSurface(ApplicationSurfaceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new TargetSelector(TargetSelectorKind.ApplicationSurface, filesystemContainer: null, target);
    }
}
