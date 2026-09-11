using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public sealed record UiaWindow
{
    public UiaWindow(
        int processId,
        nint hwnd,
        string title,
        string runtimeIdKey,
        bool isDialog,
        string? className)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "A window requires a nonzero process id.");
        }

        if (hwnd == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(hwnd), hwnd, "A window requires a nonzero handle.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdKey);

        ProcessId = processId;
        Hwnd = hwnd;
        Title = title ?? string.Empty;
        RuntimeIdKey = runtimeIdKey;
        IsDialog = isDialog;
        ClassName = className;
    }

    public int ProcessId { get; }

    public nint Hwnd { get; }

    public string Title { get; }

    public string RuntimeIdKey { get; }

    public bool IsDialog { get; }

    public string? ClassName { get; }

    public WindowIdentity ToIdentity(string processImageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processImageName);
        return new WindowIdentity(ProcessId, Hwnd, processImageName, ClassName, Title, RuntimeIdKey);
    }
}
