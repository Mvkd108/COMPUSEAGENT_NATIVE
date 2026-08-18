namespace Compuse.Requests;

public sealed record ApplicationSurfaceTarget
{
    public ApplicationSurfaceTarget(
        string processImageName,
        string? windowClass = null,
        string? windowTitle = null,
        ulong? hwndHint = null,
        uint? processIdHint = null)
    {
        WindowsPath.ValidateFileName(processImageName, nameof(processImageName));
        if (hwndHint is 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hwndHint),
                hwndHint,
                "A window handle hint cannot be zero when provided.");
        }

        if (processIdHint is 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processIdHint),
                processIdHint,
                "A process id hint cannot be zero when provided.");
        }

        ProcessImageName = processImageName;
        WindowClass = RequestValidation.RequireOptionalWindowText(windowClass, nameof(windowClass));
        WindowTitle = RequestValidation.RequireOptionalWindowText(windowTitle, nameof(windowTitle));
        HwndHint = hwndHint;
        ProcessIdHint = processIdHint;
    }

    public string ProcessImageName { get; }

    public string? WindowClass { get; }

    public string? WindowTitle { get; }

    public ulong? HwndHint { get; }

    public uint? ProcessIdHint { get; }
}
