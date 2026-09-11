namespace Compuse.ApplicationSurface;

public enum WindowResolutionStatus
{
    NotFound = 1,
    Ambiguous = 2,
    Found = 3,
}

public sealed record WindowResolution(WindowResolutionStatus Status, UiaWindow? Window)
{
    public static WindowResolution ForWindow(UiaWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new WindowResolution(WindowResolutionStatus.Found, window);
    }

    public static WindowResolution ForNotFound() =>
        new(WindowResolutionStatus.NotFound, Window: null);

    public static WindowResolution ForAmbiguous() =>
        new(WindowResolutionStatus.Ambiguous, Window: null);
}
