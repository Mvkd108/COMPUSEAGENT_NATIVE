namespace Compuse.CuaDriver;

public enum DriverActionStatus
{
    Succeeded = 1,
    Refused = 2,
    Failed = 3,
}

public enum DriverDeliveryMode
{
    Unknown = 0,
    Background = 1,
    Foreground = 2,
}

public enum DriverActionEffect
{
    Unknown = 0,
    Verified = 1,
    Unverifiable = 2,
    Refused = 3,
}

public sealed record DriverActionResult
{
    public DriverActionResult(
        DriverActionStatus status,
        string? code,
        string? message,
        DriverDeliveryMode deliveryMode,
        IReadOnlyList<string> rawText,
        string? structuredJson)
    {
        Status = status;
        Code = code;
        Message = message;
        DeliveryMode = deliveryMode;
        RawText = rawText ?? Array.Empty<string>();
        StructuredJson = structuredJson;
    }

    public DriverActionStatus Status { get; }

    public string? Code { get; }

    public string? Message { get; }

    public DriverDeliveryMode DeliveryMode { get; }

    public IReadOnlyList<string> RawText { get; }

    public string? StructuredJson { get; }

    public string? Route { get; init; }

    /// <summary>
    /// The driver's reported effect classification, when present: verified,
    /// unverifiable, or refused. A non-refused effect means dispatch may have
    /// occurred; it is never commitment evidence.
    /// </summary>
    public DriverActionEffect Effect { get; init; }

    public bool DispatchMayHaveOccurred => Status == DriverActionStatus.Succeeded && Effect != DriverActionEffect.Refused;
}

public sealed record ElementTarget
{
    public ElementTarget(uint pid, ulong windowId, int elementIndex, string? elementToken = null, string? snapshotId = null)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        if (windowId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowId), windowId, "A window id cannot be zero.");
        }

        if (elementIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elementIndex), elementIndex, "An element index cannot be negative.");
        }

        Pid = pid;
        WindowId = windowId;
        ElementIndex = elementIndex;
        ElementToken = elementToken;
        SnapshotId = snapshotId;
    }

    public uint Pid { get; }

    public ulong WindowId { get; }

    public int ElementIndex { get; }

    public string? ElementToken { get; }

    public string? SnapshotId { get; }

    /// <summary>
    /// Validates that the target carries one of the two supported Cua addressing
    /// forms and is consistent with the request scope:
    /// <list type="bullet">
    /// <item>an <c>element_token</c>, which self-describes the snapshot and window; or</item>
    /// <item>an <c>element_index</c> together with the matching <c>snapshot_id</c> and <c>window_id</c>.</item>
    /// </list>
    /// An element index without a snapshot id (or vice versa) is an incomplete,
    /// silently ambiguous target and is rejected before any dispatch.
    /// </summary>
    internal void ValidateForDispatch(uint requestPid, ulong requestWindowId)
    {
        if (Pid != requestPid)
        {
            throw new ArgumentException(
                $"The element target pid {Pid} does not match the request pid {requestPid}.");
        }

        if (!string.IsNullOrWhiteSpace(ElementToken))
        {
            if (requestWindowId != 0 && WindowId != requestWindowId)
            {
                throw new ArgumentException(
                    $"The element target window id {WindowId} does not match the request window id {requestWindowId}.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(SnapshotId))
        {
            throw new ArgumentException(
                "An element-indexed target requires the matching snapshot_id; an element_token is required otherwise.");
        }

        if (WindowId == 0)
        {
            throw new ArgumentException(
                "An element-indexed target requires the window_id that produced the snapshot.");
        }
    }

    internal Dictionary<string, object?> ToCuaArguments()
    {
        if (!string.IsNullOrWhiteSpace(ElementToken))
        {
            return new Dictionary<string, object?> { ["element_token"] = ElementToken };
        }

        return new Dictionary<string, object?>
        {
            ["element_index"] = ElementIndex,
            ["snapshot_id"] = SnapshotId,
            ["window_id"] = WindowId
        };
    }
}

public sealed record LaunchRequest
{
    public LaunchRequest(string? name = null, string? path = null, string? aumid = null, IReadOnlyList<string>? additionalArguments = null)
    {
        if (name is null && path is null && aumid is null)
        {
            throw new ArgumentException("A launch request requires a name, path, or AUMID.", nameof(name));
        }

        Name = name;
        Path = path;
        Aumid = aumid;
        AdditionalArguments = additionalArguments ?? Array.Empty<string>();
    }

    public string? Name { get; }

    public string? Path { get; }

    public string? Aumid { get; }

    public IReadOnlyList<string> AdditionalArguments { get; }
}

public sealed record TypeTextRequest
{
    public TypeTextRequest(uint pid, string text, ElementTarget? target = null, int delayMs = 30)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        ArgumentNullException.ThrowIfNull(text);
        if (delayMs is < 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(delayMs), delayMs, "delay_ms must be between 0 and 200.");
        }

        Pid = pid;
        Text = text;
        Target = target;
        DelayMs = delayMs;
    }

    public uint Pid { get; }

    public string Text { get; }

    public ElementTarget? Target { get; }

    public int DelayMs { get; }
}

public sealed record KeyRequest
{
    public KeyRequest(uint pid, string key, ElementTarget? target = null, ulong windowId = 0)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Pid = pid;
        Key = key;
        Target = target;
        WindowId = windowId;
    }

    public uint Pid { get; }

    public string Key { get; }

    public ElementTarget? Target { get; }

    public ulong WindowId { get; }
}

public sealed record HotkeyRequest
{
    public HotkeyRequest(uint pid, IReadOnlyList<string> keys, ElementTarget? target = null, ulong windowId = 0)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count < 2)
        {
            throw new ArgumentException("A hotkey requires at least two keys (modifier plus key).", nameof(keys));
        }

        Pid = pid;
        Keys = keys;
        Target = target;
        WindowId = windowId;
    }

    public uint Pid { get; }

    public IReadOnlyList<string> Keys { get; }

    public ElementTarget? Target { get; }

    public ulong WindowId { get; }
}

public enum ScrollDirection
{
    Up = 1,
    Down = 2,
    Left = 3,
    Right = 4,
}

public enum ScrollStep
{
    Line = 1,
    Page = 2,
}

public sealed record ScrollRequest
{
    public ScrollRequest(uint pid, ulong windowId, ScrollDirection direction, ScrollStep step, int amount = 1)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        if (windowId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowId), windowId, "A window id cannot be zero.");
        }

        if (direction == default || !Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Scroll direction must be defined.");
        }

        if (step == default || !Enum.IsDefined(step))
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "Scroll step must be defined.");
        }

        if (amount is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Scroll amount must be between 1 and 50.");
        }

        Pid = pid;
        WindowId = windowId;
        Direction = direction;
        Step = step;
        Amount = amount;
    }

    public uint Pid { get; }

    public ulong WindowId { get; }

    public ScrollDirection Direction { get; }

    public ScrollStep Step { get; }

    public int Amount { get; }
}

public sealed record ClickRequest
{
    public ClickRequest(
        uint? pid,
        ElementTarget? elementTarget = null,
        int? x = null,
        int? y = null,
        DriverDeliveryMode deliveryMode = DriverDeliveryMode.Background,
        string? button = "left",
        int count = 1)
    {
        if (elementTarget is null && (x is null || y is null))
        {
            throw new ArgumentException("A click requires an element target or x/y coordinates.", nameof(elementTarget));
        }

        if (elementTarget is not null && (x is not null || y is not null))
        {
            throw new ArgumentException("A click cannot mix element and coordinate addressing.", nameof(elementTarget));
        }

        if (elementTarget is not null && pid is null)
        {
            throw new ArgumentException("An element-targeted click requires a pid.", nameof(pid));
        }

        if (deliveryMode == default || !Enum.IsDefined(deliveryMode) || deliveryMode == DriverDeliveryMode.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(deliveryMode), deliveryMode, "Click delivery mode must be background or foreground.");
        }

        if (button is not null
            && !string.Equals(button, "left", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(button, "right", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(button, "middle", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(button), button, "Click button must be left, right, or middle.");
        }

        if (count is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Click count must be between 1 and 3.");
        }

        Pid = pid;
        ElementTarget = elementTarget;
        X = x;
        Y = y;
        DeliveryMode = deliveryMode;
        Button = button;
        Count = count;
    }

    public uint? Pid { get; }

    public ElementTarget? ElementTarget { get; }

    public int? X { get; }

    public int? Y { get; }

    public DriverDeliveryMode DeliveryMode { get; }

    public string? Button { get; }

    public int Count { get; }

    internal ulong RequiredWindowId
    {
        get
        {
            if (ElementTarget is not null)
            {
                return ElementTarget.WindowId;
            }

            return 0;
        }
    }
}

public sealed record DragRequest
{
    public DragRequest(
        uint pid,
        ulong windowId,
        int fromX,
        int fromY,
        int toX,
        int toY,
        DriverDeliveryMode deliveryMode = DriverDeliveryMode.Background,
        string? button = "left",
        int? durationMs = null,
        int? steps = null)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        if (windowId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowId), windowId, "A window id cannot be zero.");
        }

        if (deliveryMode == default || !Enum.IsDefined(deliveryMode) || deliveryMode == DriverDeliveryMode.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(deliveryMode), deliveryMode, "Drag delivery mode must be background or foreground.");
        }

        if (durationMs is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMs), durationMs, "Drag duration must be between 0 and 10000 ms.");
        }

        if (steps is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), steps, "Drag steps must be between 1 and 200.");
        }

        Pid = pid;
        WindowId = windowId;
        FromX = fromX;
        FromY = fromY;
        ToX = toX;
        ToY = toY;
        DeliveryMode = deliveryMode;
        Button = button;
        DurationMs = durationMs;
        Steps = steps;
    }

    public uint Pid { get; }

    public ulong WindowId { get; }

    public int FromX { get; }

    public int FromY { get; }

    public int ToX { get; }

    public int ToY { get; }

    public DriverDeliveryMode DeliveryMode { get; }

    public string? Button { get; }

    public int? DurationMs { get; }

    public int? Steps { get; }
}

public enum VerifyElementPresence
{
    Absent = 0,
    Present = 1,
    Unconstrained = 2,
}

public sealed record VerifyWindowPredicate
{
    public VerifyWindowPredicate(bool? exists = null, WindowBounds? bounds = null, int? tolerancePx = null)
    {
        if (tolerancePx is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerancePx), tolerancePx, "tolerance_px must be between 0 and 100.");
        }

        Exists = exists;
        Bounds = bounds;
        TolerancePx = tolerancePx;
    }

    public bool? Exists { get; }

    public WindowBounds? Bounds { get; }

    public int? TolerancePx { get; }
}

public sealed record VerifyElementPredicate
{
    public VerifyElementPredicate(
        string? role = null,
        string? labelContains = null,
        bool exists = false,
        string? valueEquals = null,
        bool? enabled = null,
        bool? selected = null)
    {
        if (role is not null && string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("A verify element role cannot be blank.", nameof(role));
        }

        if (labelContains is not null && string.IsNullOrWhiteSpace(labelContains))
        {
            throw new ArgumentException("A verify element label_contains cannot be blank.", nameof(labelContains));
        }

        Role = role;
        LabelContains = labelContains;
        Exists = exists;
        ValueEquals = valueEquals;
        Enabled = enabled;
        Selected = selected;
    }

    public string? Role { get; }

    public string? LabelContains { get; }

    public bool Exists { get; }

    public string? ValueEquals { get; }

    public bool? Enabled { get; }

    public bool? Selected { get; }

    internal object ToCuaObject()
    {
        object selector = new
        {
            role = Role,
            label_contains = LabelContains
        };
        object? element = Exists
            ? new
            {
                selector,
                exists = true,
                value_equals = ValueEquals,
                enabled = Enabled,
                selected = Selected
            }
            : new
            {
                selector,
                value_equals = ValueEquals,
                enabled = Enabled,
                selected = Selected
            };
        return element;
    }
}

public sealed record VerifyRequest
{
    public VerifyRequest(
        uint pid,
        ulong windowId,
        IReadOnlyList<VerifyElementPredicate>? elementPredicates = null,
        VerifyWindowPredicate? windowPredicate = null,
        bool includeScreenshot = false,
        int? stableSamples = null,
        int? timeoutMs = null)
    {
        if (pid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pid), pid, "A process id cannot be zero.");
        }

        if (windowId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowId), windowId, "A window id cannot be zero.");
        }

        if (elementPredicates is null && windowPredicate is null)
        {
            throw new ArgumentException("A verify request requires at least one window or element predicate.", nameof(elementPredicates));
        }

        if (elementPredicates is { Count: > 8 })
        {
            throw new ArgumentOutOfRangeException(nameof(elementPredicates), elementPredicates, "A verify request supports at most eight predicates.");
        }

        if (stableSamples is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(stableSamples), stableSamples, "stable_samples must be between 1 and 5.");
        }

        if (timeoutMs is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "timeout_ms must be between 0 and 10000.");
        }

        Pid = pid;
        WindowId = windowId;
        ElementPredicates = elementPredicates ?? Array.Empty<VerifyElementPredicate>();
        WindowPredicate = windowPredicate;
        IncludeScreenshot = includeScreenshot;
        StableSamples = stableSamples;
        TimeoutMs = timeoutMs;
    }

    public uint Pid { get; }

    public ulong WindowId { get; }

    public IReadOnlyList<VerifyElementPredicate> ElementPredicates { get; }

    public VerifyWindowPredicate? WindowPredicate { get; }

    public bool IncludeScreenshot { get; }

    public int? StableSamples { get; }

    public int? TimeoutMs { get; }

    internal object[] ToExpectArray()
    {
        List<object> predicates = [];
        if (WindowPredicate is not null)
        {
            predicates.Add(new
            {
                window = new
                {
                    exists = WindowPredicate.Exists,
                    bounds = WindowPredicate.Bounds is null ? null : new
                    {
                        x = WindowPredicate.Bounds.X,
                        y = WindowPredicate.Bounds.Y,
                        width = WindowPredicate.Bounds.Width,
                        height = WindowPredicate.Bounds.Height,
                        tolerance_px = WindowPredicate.TolerancePx
                    }
                }
            });
        }

        foreach (VerifyElementPredicate element in ElementPredicates)
        {
            predicates.Add(new { element = element.ToCuaObject() });
        }

        return predicates.ToArray();
    }
}

public sealed record VerifyStateResult
{
    public VerifyStateResult(string verdict, string? detail, ScreenCaptureInfo? screenshot, bool stable, int samples, int? elapsedMs)
    {
        Verdict = verdict;
        Detail = detail;
        Screenshot = screenshot;
        Stable = stable;
        Samples = samples;
        ElapsedMs = elapsedMs;
    }

    public string Verdict { get; }

    public string? Detail { get; }

    public ScreenCaptureInfo? Screenshot { get; }

    public bool Stable { get; }

    public int Samples { get; }

    public int? ElapsedMs { get; }

    public bool IsSatisfied => string.Equals(Verdict, "satisfied", StringComparison.OrdinalIgnoreCase);
}

public sealed record WindowStateRequest
{
    public WindowStateRequest(int? maxElements = null, int? maxDepth = null, int? maxDimension = null, string? query = null)
    {
        if (maxElements is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxElements), maxElements, "max_elements must be positive.");
        }

        if (maxDepth is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "max_depth must be positive.");
        }

        if (maxDimension is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension), maxDimension, "max_dimension must be positive.");
        }

        MaxElements = maxElements;
        MaxDepth = maxDepth;
        MaxDimension = maxDimension;
        Query = query;
    }

    public int? MaxElements { get; }

    public int? MaxDepth { get; }

    public int? MaxDimension { get; }

    public string? Query { get; }
}

public sealed record BrowserPrepareRequest
{
    public BrowserPrepareRequest(bool allowLaunch = false, bool isolatedProfile = false, string? profileName = null, uint? pid = null, ulong? windowId = null, string? sessionLabel = null)
    {
        if (isolatedProfile && !string.IsNullOrWhiteSpace(profileName) && profileName!.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(profileName), profileName, "A browser profile name is limited to 64 path-safe ASCII characters.");
        }

        AllowLaunch = allowLaunch;
        IsolatedProfile = isolatedProfile;
        ProfileName = profileName;
        Pid = pid;
        WindowId = windowId;
        SessionLabel = sessionLabel;
    }

    public bool AllowLaunch { get; }

    public bool IsolatedProfile { get; }

    public string? ProfileName { get; }

    public uint? Pid { get; }

    public ulong? WindowId { get; }

    public string? SessionLabel { get; }
}

public sealed record BrowserBindRequest
{
    public BrowserBindRequest(uint pid, ulong windowId, string? sessionLabel = null)
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
        SessionLabel = sessionLabel;
    }

    public uint Pid { get; }

    public ulong WindowId { get; }

    public string? SessionLabel { get; }
}

public sealed record BrowserNavigateRequest
{
    public BrowserNavigateRequest(BrowserTabTarget target, string url)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps && !string.Equals(parsed.Scheme, "about", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("A browser navigation URL must be http, https, or about.", nameof(url));
        }

        Target = target;
        Url = url;
    }

    public BrowserTabTarget Target { get; }

    public string Url { get; }
}

public sealed record BrowserClickRequest
{
    public BrowserClickRequest(BrowserTabTarget target, string? reference = null, int? x = null, int? y = null, string? inputRoute = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (reference is null && (x is null || y is null))
        {
            throw new ArgumentException("A browser click requires a page ref or viewport x/y coordinates.", nameof(reference));
        }

        if (reference is not null && (x is not null || y is not null))
        {
            throw new ArgumentException("A browser click cannot mix ref and coordinate addressing.", nameof(reference));
        }

        if (inputRoute is not null
            && !string.Equals(inputRoute, "trusted", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(inputRoute, "dom_event", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(inputRoute), inputRoute, "A browser input route must be trusted or dom_event.");
        }

        Target = target;
        Reference = reference;
        X = x;
        Y = y;
        InputRoute = inputRoute;
    }

    public BrowserTabTarget Target { get; }

    public string? Reference { get; }

    public int? X { get; }

    public int? Y { get; }

    public string? InputRoute { get; }
}

public sealed record BrowserTypeRequest
{
    public BrowserTypeRequest(BrowserTabTarget target, string reference, string text, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(text);
        Target = target;
        Reference = reference;
        Text = text;
        Replace = replace;
    }

    public BrowserTabTarget Target { get; }

    public string Reference { get; }

    public string Text { get; }

    public bool Replace { get; }
}

public sealed record BrowserScrollRequest
{
    public BrowserScrollRequest(BrowserTabTarget target, double deltaX, double deltaY, string? reference = null, int? x = null, int? y = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (reference is null && (x is null || y is null))
        {
            throw new ArgumentException("A browser scroll requires a page ref or viewport x/y coordinates.", nameof(reference));
        }

        if (reference is not null && (x is not null || y is not null))
        {
            throw new ArgumentException("A browser scroll cannot mix ref and coordinate addressing.", nameof(reference));
        }

        Target = target;
        DeltaX = deltaX;
        DeltaY = deltaY;
        Reference = reference;
        X = x;
        Y = y;
    }

    public BrowserTabTarget Target { get; }

    public double DeltaX { get; }

    public double DeltaY { get; }

    public string? Reference { get; }

    public int? X { get; }

    public int? Y { get; }
}

public sealed record BrowserSetInputFilesRequest
{
    public BrowserSetInputFilesRequest(BrowserTabTarget target, string reference, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(files), files, "A browser file assignment requires 1 to 32 files.");
        }

        Target = target;
        Reference = reference;
        Files = files;
    }

    public BrowserTabTarget Target { get; }

    public string Reference { get; }

    public IReadOnlyList<string> Files { get; }
}
