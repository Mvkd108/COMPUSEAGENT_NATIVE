namespace Compuse.Routing;

public sealed record WindowIdentity
{
    public WindowIdentity(
        int processId,
        nint hwnd,
        string processImageName,
        string? windowClass,
        string windowTitle,
        string runtimeIdKey)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                processId,
                "A window identity requires a nonzero process id.");
        }

        if (hwnd == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd), hwnd, "A window identity requires a nonzero window handle.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(processImageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdKey);

        ProcessId = processId;
        Hwnd = hwnd;
        ProcessImageName = processImageName;
        WindowClass = windowClass;
        WindowTitle = windowTitle ?? string.Empty;
        RuntimeIdKey = runtimeIdKey;
    }

    public int ProcessId { get; }

    public nint Hwnd { get; }

    public string ProcessImageName { get; }

    public string? WindowClass { get; }

    public string WindowTitle { get; }

    public string RuntimeIdKey { get; }

    public bool SameObjectAs(WindowIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return ProcessId == other.ProcessId
            && Hwnd == other.Hwnd
            && string.Equals(RuntimeIdKey, other.RuntimeIdKey, StringComparison.Ordinal);
    }
}
