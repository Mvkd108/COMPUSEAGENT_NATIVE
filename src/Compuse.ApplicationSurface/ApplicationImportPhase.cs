namespace Compuse.ApplicationSurface;

public enum ApplicationImportPhase
{
    None = 0,
    WindowResolved = 1,
    MenuInvoked = 2,
    DialogShown = 3,
    FileNameSet = 4,
    OpenInvoked = 5,
    DocumentRead = 6,
    SourceRemoved = 7,
}
