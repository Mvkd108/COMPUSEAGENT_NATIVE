namespace Compuse.CuaDriver;

public sealed record WindowBounds(int X, int Y, int Width, int Height);

public sealed record ApplicationInfo
{
    public ApplicationInfo(
        string name,
        uint pid,
        bool running,
        bool active,
        string kind,
        string? launchPath,
        string? aumid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Pid = pid;
        Running = running;
        Active = active;
        Kind = kind;
        LaunchPath = launchPath;
        Aumid = aumid;
    }

    public string Name { get; }

    public uint Pid { get; }

    public bool Running { get; }

    public bool Active { get; }

    public string Kind { get; }

    public string? LaunchPath { get; }

    public string? Aumid { get; }
}

public sealed record WindowInfo
{
    public WindowInfo(
        ulong windowId,
        uint pid,
        string appName,
        string title,
        WindowBounds bounds,
        bool minimized,
        bool onScreen,
        int? zIndex)
    {
        if (windowId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowId), windowId, "A window id cannot be zero.");
        }

        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        WindowId = windowId;
        Pid = pid;
        AppName = appName ?? string.Empty;
        Title = title ?? string.Empty;
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        Minimized = minimized;
        OnScreen = onScreen;
        ZIndex = zIndex;
    }

    public ulong WindowId { get; }

    public uint Pid { get; }

    public string AppName { get; }

    public string Title { get; }

    public WindowBounds Bounds { get; }

    public bool Minimized { get; }

    public bool OnScreen { get; }

    public int? ZIndex { get; }
}

public sealed record AccessibilityElementInfo
{
    public AccessibilityElementInfo(
        int elementIndex,
        string? role,
        string? label,
        string? value,
        bool enabled,
        bool selected,
        IReadOnlyList<string> actions,
        WindowBounds? frame,
        int? parentIndex,
        int depth,
        string? elementToken = null)
    {
        ElementIndex = elementIndex;
        Role = role;
        Label = label;
        Value = value;
        Enabled = enabled;
        Selected = selected;
        Actions = actions ?? Array.Empty<string>();
        Frame = frame;
        ParentIndex = parentIndex;
        Depth = depth;
        ElementToken = elementToken;
    }

    public int ElementIndex { get; }

    public string? Role { get; }

    public string? Label { get; }

    public string? Value { get; }

    public bool Enabled { get; }

    public bool Selected { get; }

    public IReadOnlyList<string> Actions { get; }

    public WindowBounds? Frame { get; }

    public int? ParentIndex { get; }

    public int Depth { get; }

    public string? ElementToken { get; }
}

public sealed record ScreenCaptureInfo
{
    public ScreenCaptureInfo(byte[]? pngData, string? mimeType, int? width, int? height)
    {
        PngData = pngData;
        MimeType = mimeType;
        Width = width;
        Height = height;
    }

    public byte[]? PngData { get; }

    public string? MimeType { get; }

    public int? Width { get; }

    public int? Height { get; }
}

public sealed record WindowStateSnapshot
{
    public WindowStateSnapshot(
        uint pid,
        ulong windowId,
        string windowTitle,
        string appName,
        WindowBounds? windowBounds,
        IReadOnlyList<AccessibilityElementInfo> elements,
        string? treeMarkdown,
        int? totalElementCount,
        ScreenCaptureInfo? screenshot,
        string? captureCoverage,
        string? snapshotId = null)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        if (windowId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowId), windowId, "A window id cannot be zero.");
        }

        Pid = pid;
        WindowId = windowId;
        WindowTitle = windowTitle ?? string.Empty;
        AppName = appName ?? string.Empty;
        WindowBounds = windowBounds;
        Elements = elements ?? Array.Empty<AccessibilityElementInfo>();
        TreeMarkdown = treeMarkdown;
        TotalElementCount = totalElementCount;
        Screenshot = screenshot;
        CaptureCoverage = captureCoverage;
        SnapshotId = snapshotId;
    }

    public uint Pid { get; }

    public ulong WindowId { get; }

    public string WindowTitle { get; }

    public string AppName { get; }

    public WindowBounds? WindowBounds { get; }

    public IReadOnlyList<AccessibilityElementInfo> Elements { get; }

    public string? TreeMarkdown { get; }

    public int? TotalElementCount { get; }

    public ScreenCaptureInfo? Screenshot { get; }

    public string? CaptureCoverage { get; }

    public string? SnapshotId { get; }
}

public sealed record LaunchResult
{
    public LaunchResult(uint pid, string? name, bool active, string? aumid, IReadOnlyList<WindowInfo> windows)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        Pid = pid;
        Name = name;
        Active = active;
        Aumid = aumid;
        Windows = windows ?? Array.Empty<WindowInfo>();
    }

    public uint Pid { get; }

    public string? Name { get; }

    public bool Active { get; }

    public string? Aumid { get; }

    public IReadOnlyList<WindowInfo> Windows { get; }
}

public sealed record CursorPosition(int X, int Y);

/// <summary>
/// Product-owned model of <c>get_desktop_state</c>: a full-display screenshot plus
/// display metadata. It is a capture of the desktop, never a list of application
/// windows.
/// </summary>
public sealed record DesktopState
{
    public DesktopState(
        ScreenCaptureInfo? screenshot,
        int? screenWidth,
        int? screenHeight,
        int? screenshotWidth,
        int? screenshotHeight,
        double? scaleFactor,
        string? display,
        string? platform)
    {
        if (screenshotWidth is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(screenshotWidth), screenshotWidth, "A screenshot width cannot be negative.");
        }

        if (screenshotHeight is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(screenshotHeight), screenshotHeight, "A screenshot height cannot be negative.");
        }

        Screenshot = screenshot;
        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        ScreenshotWidth = screenshotWidth;
        ScreenshotHeight = screenshotHeight;
        ScaleFactor = scaleFactor;
        Display = display;
        Platform = platform;
    }

    public ScreenCaptureInfo? Screenshot { get; }

    public int? ScreenWidth { get; }

    public int? ScreenHeight { get; }

    public int? ScreenshotWidth { get; }

    public int? ScreenshotHeight { get; }

    public double? ScaleFactor { get; }

    public string? Display { get; }

    public string? Platform { get; }
}

public sealed record BrowserTab
{
    public BrowserTab(string tabId, string? title, string? url, bool active)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        TabId = tabId;
        Title = title;
        Url = url;
        Active = active;
    }

    public string TabId { get; }

    public string? Title { get; }

    public string? Url { get; }

    public bool Active { get; }
}

public sealed record BrowserBinding
{
    public BrowserBinding(
        string targetId,
        string? nativeTitle,
        string? bindingQuality,
        string? endpointAccessClass,
        IReadOnlyList<BrowserTab> tabs,
        bool mutationAllowed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        TargetId = targetId;
        NativeTitle = nativeTitle;
        BindingQuality = bindingQuality;
        EndpointAccessClass = endpointAccessClass;
        Tabs = tabs ?? Array.Empty<BrowserTab>();
        MutationAllowed = mutationAllowed;
    }

    public string TargetId { get; }

    public string? NativeTitle { get; }

    public string? BindingQuality { get; }

    public string? EndpointAccessClass { get; }

    public IReadOnlyList<BrowserTab> Tabs { get; }

    public bool MutationAllowed { get; }
}

public sealed record BrowserPageRef
{
    public BrowserPageRef(string reference, string? node, string? label, string? frame)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        Reference = reference;
        Node = node;
        Label = label;
        Frame = frame;
    }

    public string Reference { get; }

    public string? Node { get; }

    public string? Label { get; }

    public string? Frame { get; }
}

public sealed record BrowserSnapshot
{
    public BrowserSnapshot(
        string targetId,
        string tabId,
        string? snapshotId,
        string? url,
        IReadOnlyList<BrowserPageRef> refs,
        bool truncated,
        ScreenCaptureInfo? screenshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        TargetId = targetId;
        TabId = tabId;
        SnapshotId = snapshotId;
        Url = url;
        Refs = refs ?? Array.Empty<BrowserPageRef>();
        Truncated = truncated;
        Screenshot = screenshot;
    }

    public string TargetId { get; }

    public string TabId { get; }

    public string? SnapshotId { get; }

    public string? Url { get; }

    public IReadOnlyList<BrowserPageRef> Refs { get; }

    public bool Truncated { get; }

    public ScreenCaptureInfo? Screenshot { get; }
}

public sealed record BrowserPrepareResult
{
    public BrowserPrepareResult(
        bool prepared,
        int? preparedPid,
        int? listenerPid,
        string? status,
        string? action,
        string? message,
        bool createdProfile)
    {
        Prepared = prepared;
        PreparedPid = preparedPid;
        ListenerPid = listenerPid;
        Status = status;
        Action = action;
        Message = message;
        CreatedProfile = createdProfile;
    }

    public bool Prepared { get; }

    public int? PreparedPid { get; }

    public int? ListenerPid { get; }

    public string? Status { get; }

    public string? Action { get; }

    public string? Message { get; }

    public bool CreatedProfile { get; }
}

public sealed record BrowserTabTarget
{
    public BrowserTabTarget(string targetId, string tabId, string? sessionLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        TargetId = targetId;
        TabId = tabId;
        SessionLabel = sessionLabel;
    }

    public string TargetId { get; }

    public string TabId { get; }

    public string? SessionLabel { get; }
}

public sealed record RecordingState
{
    public RecordingState(
        bool enabled,
        bool recording,
        bool videoActive,
        string? outputDirectory,
        string? owner,
        int nextTurn,
        string? lastError,
        string? lastVideoPath)
    {
        Enabled = enabled;
        Recording = recording;
        VideoActive = videoActive;
        OutputDirectory = outputDirectory;
        Owner = owner;
        NextTurn = nextTurn;
        LastError = lastError;
        LastVideoPath = lastVideoPath;
    }

    public bool Enabled { get; }

    public bool Recording { get; }

    public bool VideoActive { get; }

    public string? OutputDirectory { get; }

    public string? Owner { get; }

    public int NextTurn { get; }

    public string? LastError { get; }

    public string? LastVideoPath { get; }
}

public sealed record HealthCheck(string Name, string Status, string? Message, string? Hint);

public sealed record DriverHealthReport
{
    public DriverHealthReport(
        string overall,
        string driverVersion,
        string platform,
        IReadOnlyList<HealthCheck> checks)
    {
        Overall = overall;
        DriverVersion = driverVersion;
        Platform = platform;
        Checks = checks ?? Array.Empty<HealthCheck>();
    }

    public string Overall { get; }

    public string DriverVersion { get; }

    public string Platform { get; }

    public IReadOnlyList<HealthCheck> Checks { get; }
}
