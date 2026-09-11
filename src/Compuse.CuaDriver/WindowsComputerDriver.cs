using System.Text.Json;

namespace Compuse.CuaDriver;

public sealed class WindowsComputerDriver : IWindowsComputerDriver
{
    private static readonly DriverVersion MinimumSupported = DriverVersion.Parse("0.27.0");
    private static readonly DriverVersion MaximumExclusive = DriverVersion.Parse("1.0.0");
    private const string ExistingProfileGrant = "--grant";
    private const string ExistingProfileValue = "existing-profile";

    private readonly DriverConnectionOptions _options;
    private readonly Lazy<CuaMcpClient> _client;
    private bool _started;
    private bool _disposed;

    public WindowsComputerDriver(DriverConnectionOptions options)
        : this(options, transportFactory: null)
    {
    }

    internal WindowsComputerDriver(DriverConnectionOptions options, Func<IMcpTransport>? transportFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _client = new Lazy<CuaMcpClient>(() =>
        {
            IMcpTransport transport = transportFactory is not null
                ? transportFactory()
                : new StdioMcpTransport(_options);
            return new CuaMcpClient(transport, _options);
        });
    }

    public string ExecutablePath => _options.ExecutablePath;

    public string EffectivePermissionMode => _options.EffectivePermissionModeDescription;

    public DriverVersion? Version => _client.IsValueCreated ? _client.Value.InitializeResult?.Version : null;

    public bool IsReady => _client.IsValueCreated && _client.Value.InitializeResult is not null;

    public async Task<McpInitializeResult> InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(_options.ExecutablePath))
        {
            throw new DriverUnavailableException(
                $"The Cua Driver executable '{_options.ExecutablePath}' was not found. Install it from the pinned release or configure its path.");
        }

        ValidatePermissionConfiguration();
        ApplyLaunchGrants();

        if (_started && IsReady)
        {
            return _client.Value.InitializeResult!;
        }

        CuaMcpClient client = _client.Value;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_options.StartTimeout);
        McpInitializeResult result;
        try
        {
            result = await client.InitializeAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DriverStartException(
                $"The Cua Driver did not complete the MCP handshake within {_options.StartTimeout.TotalSeconds} seconds.");
        }
        catch (DriverIncompatibleException)
        {
            throw;
        }
        catch (DriverProtocolException ex)
        {
            throw new DriverStartException($"The Cua Driver MCP handshake failed: {ex.Message}", ex);
        }
        catch (DriverDisconnectedException ex)
        {
            throw new DriverStartException($"The Cua Driver exited during startup: {ex.Message}", ex);
        }

        if (result.Version < MinimumSupported || result.Version >= MaximumExclusive)
        {
            throw new DriverIncompatibleException(
                $"Cua Driver version {result.Version} is not supported. This adapter is pinned and tested against {MinimumSupported} up to {MaximumExclusive.Major}.0.0.");
        }

        _started = true;
        return result;
    }

    public string GetDiagnostics()
    {
        string permission = $"permission_mode={EffectivePermissionMode}";
        if (!_client.IsValueCreated)
        {
            return permission;
        }

        string transport = _client.Value.GetTransportDiagnostics();
        return string.IsNullOrWhiteSpace(transport) ? permission : $"{permission}\n{transport}";
    }

    public async Task<DriverHealthReport> GetHealthAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        McpToolResult result = await CallToolAsync("health_report", new { }, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        string overall = DriverJson.GetString(structured, "overall") ?? "unknown";
        string version = DriverJson.GetString(structured, "driver_version") ?? Version?.ToString() ?? "unknown";
        string platform = DriverJson.GetString(structured, "platform") ?? "windows";
        List<HealthCheck> checks = [];
        if (structured.TryGetProperty("checks", out JsonElement checksElement) && checksElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement check in checksElement.EnumerateArray())
            {
                checks.Add(new HealthCheck(
                    DriverJson.GetString(check, "name") ?? "unknown",
                    DriverJson.GetString(check, "status") ?? "unknown",
                    DriverJson.GetString(check, "message"),
                    DriverJson.GetString(check, "hint")));
            }
        }

        return new DriverHealthReport(overall, version, platform, checks);
    }

    public async Task<IReadOnlyList<ApplicationInfo>> ListApplicationsAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        McpToolResult result = await CallToolAsync("list_apps", new { }, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        List<ApplicationInfo> apps = [];
        if (structured.TryGetProperty("apps", out JsonElement appsElement) && appsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement app in appsElement.EnumerateArray())
            {
                ApplicationInfo? parsed = TryParseApplication(app);
                if (parsed is not null)
                {
                    apps.Add(parsed);
                }
            }
        }

        return apps;
    }

    public async Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(
        uint? pid = null,
        bool onScreenOnly = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("pid", pid)
            .Add("on_screen_only", onScreenOnly ? true : null)
            .Build();
        McpToolResult result = await CallToolAsync("list_windows", arguments, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);
        return ParseWindows(result.Structured);
    }

    public async Task<WindowStateSnapshot> GetWindowStateAsync(
        uint pid,
        ulong windowId,
        WindowStateRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("pid", pid)
            .Add("window_id", windowId)
            .Add("include_accessibility_tree", true)
            .Add("include_screenshot", true)
            .Add("max_elements", request?.MaxElements)
            .Add("max_depth", request?.MaxDepth)
            .Add("max_dimension", request?.MaxDimension)
            .Add("query", request?.Query)
            .Build();
        McpToolResult result = await CallToolAsync("get_window_state", arguments, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        List<AccessibilityElementInfo> elements = [];
        if (structured.TryGetProperty("elements", out JsonElement elementsElement) && elementsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in elementsElement.EnumerateArray())
            {
                AccessibilityElementInfo? parsed = TryParseElement(element);
                if (parsed is not null)
                {
                    elements.Add(parsed);
                }
            }
        }

        ScreenCaptureInfo? screenshot = TryParseScreenshot(result);
        return new WindowStateSnapshot(
            pid,
            windowId,
            DriverJson.GetString(structured, "window_title") ?? string.Empty,
            DriverJson.GetString(structured, "app_name") ?? string.Empty,
            DriverJson.GetBounds(structured, "window_bounds"),
            elements,
            DriverJson.GetString(structured, "tree_markdown"),
            DriverJson.GetInt(structured, "total_element_count"),
            screenshot,
            DriverJson.GetString(structured, "capture_coverage"),
            DriverJson.GetString(structured, "snapshot_id"));
    }

    public async Task<LaunchResult> LaunchApplicationAsync(LaunchRequest request, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("name", request.Name)
            .Add("path", request.Path)
            .Add("aumid", request.Aumid)
            .Add("additional_arguments", request.AdditionalArguments.Count == 0 ? null : request.AdditionalArguments)
            .Build();
        McpToolResult result = await CallToolAsync("launch_app", arguments, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        uint pid = DriverJson.GetUInt(structured, "pid") ?? 0;
        if (pid == 0)
        {
            throw new DriverProtocolException("launch_app did not report a process id.");
        }

        return new LaunchResult(
            pid,
            DriverJson.GetString(structured, "name"),
            DriverJson.GetBool(structured, false, "active"),
            DriverJson.GetString(structured, "bundle_id"),
            ParseWindows(structured));
    }

    public Task<DriverActionResult> InvokeElementAsync(ElementTarget target, CancellationToken cancellationToken) =>
        ClickAsync(new ClickRequest(target.Pid, elementTarget: target, deliveryMode: DriverDeliveryMode.Background), cancellationToken);

    public async Task<DriverActionResult> SetValueAsync(ElementTarget target, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        target.ValidateForDispatch(target.Pid, target.WindowId);
        Dictionary<string, object?> arguments = ElementArguments(target, target.Pid);
        arguments["value"] = value;
        return await MapActionAsync("set_value", arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DriverActionResult> TypeTextAsync(TypeTextRequest request, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = ElementArguments(request.Target, request.Pid);
        arguments["text"] = request.Text;
        arguments["delay_ms"] = request.DelayMs;
        return await MapActionAsync("type_text", arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DriverActionResult> PressKeyAsync(KeyRequest request, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = ElementArguments(request.Target, request.Pid);
        arguments["key"] = request.Key;
        if (request.Target is null)
        {
            if (request.WindowId != 0)
            {
                arguments["window_id"] = request.WindowId;
            }
        }

        return await MapActionAsync("press_key", arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DriverActionResult> HotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = ElementArguments(request.Target, request.Pid);
        arguments["keys"] = request.Keys.ToArray();
        if (request.Target is null)
        {
            if (request.WindowId != 0)
            {
                arguments["window_id"] = request.WindowId;
            }
        }

        return await MapActionAsync("hotkey", arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DriverActionResult> ScrollAsync(ScrollRequest request, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        string direction = request.Direction switch
        {
            ScrollDirection.Up => "up",
            ScrollDirection.Down => "down",
            ScrollDirection.Left => "left",
            _ => "right"
        };
        string by = request.Step == ScrollStep.Page ? "page" : "line";
        object arguments = new
        {
            pid = request.Pid,
            window_id = request.WindowId,
            direction,
            by,
            amount = request.Amount
        };
        return await MapActionAsync("scroll", arguments, cancellationToken).ConfigureAwait(false);
    }

    public Task<DriverActionResult> ClickAsync(ClickRequest request, CancellationToken cancellationToken) =>
        ClickFamilyAsync("click", request, includeButtonAndCount: true, cancellationToken);

    public Task<DriverActionResult> RightClickAsync(ClickRequest request, CancellationToken cancellationToken) =>
        ClickFamilyAsync("right_click", request, includeButtonAndCount: false, cancellationToken);

    public Task<DriverActionResult> DoubleClickAsync(ClickRequest request, CancellationToken cancellationToken) =>
        ClickFamilyAsync("double_click", request, includeButtonAndCount: false, cancellationToken);

    public async Task<DriverActionResult> DragAsync(DragRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("pid", request.Pid)
            .Add("window_id", request.WindowId)
            .Add("from_x", request.FromX)
            .Add("from_y", request.FromY)
            .Add("to_x", request.ToX)
            .Add("to_y", request.ToY)
            .Add("button", request.Button)
            .Add("delivery_mode", request.DeliveryMode == DriverDeliveryMode.Foreground ? "foreground" : "background")
            .Add("duration_ms", request.DurationMs)
            .Add("steps", request.Steps)
            .Build();
        return await MapActionAsync("drag", arguments, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScreenCaptureInfo> CaptureWindowAsync(
        uint pid,
        ulong windowId,
        int? maxDimension = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("pid", pid)
            .Add("window_id", windowId)
            .Add("include_accessibility_tree", false)
            .Add("include_screenshot", true)
            .Add("max_dimension", maxDimension)
            .Build();
        McpToolResult result = await CallToolAsync("get_window_state", arguments, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);
        return TryParseScreenshot(result)
            ?? throw new DriverProtocolException("get_window_state did not return a screenshot.");
    }

    public async Task<DriverActionResult> ClipboardWriteAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await MapActionAsync("clipboard_write", new { text }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ClipboardReadAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        McpToolResult result = await CallToolAsync("clipboard_read", new { include_text = true }, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);
        string? structured = DriverJson.GetString(result.Structured, "text");
        if (structured is not null)
        {
            return structured;
        }

        if (result.Structured.ValueKind == JsonValueKind.Object
            && result.Structured.TryGetProperty("text", out JsonElement textElement)
            && textElement.ValueKind == JsonValueKind.String
            && textElement.GetString() is string textValue)
        {
            return textValue;
        }

        throw new DriverProtocolException("clipboard_read did not return text content.");
    }

    public async Task<DriverActionResult> BringToFrontAsync(uint pid, ulong windowId, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        return await MapActionAsync("bring_to_front", new { pid, window_id = windowId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CursorPosition> GetCursorPositionAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        McpToolResult result = await CallToolAsync("get_cursor_position", new { }, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);
        int? x = DriverJson.GetInt(result.Structured, "x");
        int? y = DriverJson.GetInt(result.Structured, "y");
        if (x is null || y is null)
        {
            throw new DriverProtocolException("get_cursor_position did not report x/y coordinates.");
        }

        return new CursorPosition(x.Value, y.Value);
    }

    public async Task<DesktopState> GetDesktopStateAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        McpToolResult result = await CallToolAsync("get_desktop_state", new { }, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        ScreenCaptureInfo screenshot = TryParseScreenshot(result)
            ?? throw new DriverProtocolException("get_desktop_state did not return a desktop screenshot.");

        return new DesktopState(
            screenshot,
            DriverJson.GetInt(structured, "screen_width"),
            DriverJson.GetInt(structured, "screen_height"),
            DriverJson.GetInt(structured, "screenshot_width"),
            DriverJson.GetInt(structured, "screenshot_height"),
            DriverJson.GetDouble(structured, "scale_factor"),
            DriverJson.GetString(structured, "display") ?? "primary",
            DriverJson.GetString(structured, "platform"));
    }

    public async Task<VerifyStateResult> VerifyStateAsync(VerifyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("pid", request.Pid)
            .Add("window_id", request.WindowId)
            .Add("expect", request.ToExpectArray())
            .Add("include_screenshot", request.IncludeScreenshot ? true : null)
            .Add("stable_samples", request.StableSamples)
            .Add("timeout_ms", request.TimeoutMs)
            .Build();
        McpToolResult result = await CallToolAsync("verify_state", arguments, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        string verdict = DriverJson.GetString(structured, "status")
            ?? DriverJson.GetString(structured, "verdict")
            ?? DriverJson.GetString(structured, "result")
            ?? "unknown";
        bool stable = DriverJson.GetBool(structured, false, "stable");
        int? samples = DriverJson.GetInt(structured, "samples");
        int? elapsedMs = DriverJson.GetInt(structured, "elapsed_ms");
        string detail = DriverJson.GetString(structured, "detail") ?? result.CombinedText;
        return new VerifyStateResult(verdict, detail, TryParseScreenshot(result), stable, samples ?? 0, elapsedMs);
    }

    public async Task<BrowserPrepareResult> PrepareBrowserAsync(BrowserPrepareRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        object? profile = null;
        if (request.IsolatedProfile)
        {
            profile = request.ProfileName is null
                ? new { mode = "isolated_new" }
                : new { mode = "isolated_named", name = request.ProfileName };
        }
        else if (request.Pid is not null)
        {
            profile = new { mode = "existing_profile" };
        }

        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("pid", request.Pid)
            .Add("window_id", request.WindowId)
            .Add("allow_launch", request.AllowLaunch ? true : null)
            .Add("profile", profile)
            .Add("session", ResolveSessionLabel(request.SessionLabel))
            .Build();
        McpToolResult result = await CallToolAsync("browser_prepare", arguments, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        return new BrowserPrepareResult(
            DriverJson.GetBool(structured, false, "prepared"),
            DriverJson.GetInt(structured, "prepared_pid"),
            DriverJson.GetInt(structured, "endpoint_ownership", "listener_pid"),
            DriverJson.GetString(structured, "status"),
            DriverJson.GetString(structured, "action"),
            DriverJson.GetString(structured, "message"),
            DriverJson.GetBool(structured, false, "side_effects", "created_profile"));
    }

    public async Task<BrowserBinding> BindBrowserAsync(BrowserBindRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("pid", request.Pid)
            .Add("window_id", request.WindowId)
            .Add("snapshot_format", "dom_refs_v1")
            .Add("session", ResolveSessionLabel(request.SessionLabel))
            .Build();
        McpToolResult result = await CallToolAsync("get_browser_state", arguments, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        string targetId = DriverJson.GetString(structured, "target_id")
            ?? throw new DriverProtocolException("get_browser_state (bind) did not report a target_id.");
        List<BrowserTab> tabs = [];
        if (structured.TryGetProperty("tabs", out JsonElement tabsElement) && tabsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement tab in tabsElement.EnumerateArray())
            {
                string? tabId = DriverJson.GetString(tab, "tab_id");
                if (tabId is not null)
                {
                    tabs.Add(new BrowserTab(
                        tabId,
                        DriverJson.GetString(tab, "title"),
                        DriverJson.GetString(tab, "url"),
                        DriverJson.GetBool(tab, false, "active")));
                }
            }
        }

        return new BrowserBinding(
            targetId,
            DriverJson.GetString(structured, "native_title"),
            DriverJson.GetString(structured, "binding_quality"),
            DriverJson.GetString(structured, "endpoint_access_class"),
            tabs,
            DriverJson.GetBool(structured, false, "mutation_allowed"));
    }

    public async Task<BrowserSnapshot> SnapshotBrowserAsync(BrowserTabTarget target, bool includeScreenshot = false, string? query = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("target_id", target.TargetId)
            .Add("tab_id", target.TabId)
            .Add("snapshot_format", "dom_refs_v1")
            .Add("include_screenshot", includeScreenshot ? true : null)
            .Add("query", query)
            .Add("session", ResolveSessionLabel(target.SessionLabel))
            .Build();
        McpToolResult result = await CallToolAsync("get_browser_state", arguments, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);

        JsonElement structured = result.Structured;
        List<BrowserPageRef> refs = [];
        if (structured.TryGetProperty("refs", out JsonElement refsElement) && refsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement reference in refsElement.EnumerateArray())
            {
                string? refValue = DriverJson.GetString(reference, "ref");
                if (refValue is not null)
                {
                    refs.Add(new BrowserPageRef(
                        refValue,
                        DriverJson.GetString(reference, "node"),
                        DriverJson.GetString(reference, "label"),
                        DriverJson.GetString(reference, "frame")));
                }
            }
        }

        return new BrowserSnapshot(
            target.TargetId,
            target.TabId,
            DriverJson.GetString(structured, "snapshot_id"),
            DriverJson.GetString(structured, "url"),
            refs,
            DriverJson.GetBool(structured, false, "truncated"),
            TryParseScreenshot(result));
    }

    public Task<DriverActionResult> NavigateBrowserAsync(BrowserNavigateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("target_id", request.Target.TargetId)
            .Add("tab_id", request.Target.TabId)
            .Add("url", request.Url)
            .Add("session", ResolveSessionLabel(request.Target.SessionLabel))
            .Build();
        return MapActionAsync("browser_navigate", arguments, cancellationToken);
    }

    public Task<DriverActionResult> BrowserClickAsync(BrowserClickRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("target_id", request.Target.TargetId)
            .Add("tab_id", request.Target.TabId)
            .Add("ref", request.Reference)
            .Add("x", request.X)
            .Add("y", request.Y)
            .Add("input_route", request.InputRoute)
            .Add("session", ResolveSessionLabel(request.Target.SessionLabel))
            .Build();
        return MapActionAsync("browser_click", arguments, cancellationToken);
    }

    public Task<DriverActionResult> BrowserTypeAsync(BrowserTypeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("target_id", request.Target.TargetId)
            .Add("tab_id", request.Target.TabId)
            .Add("ref", request.Reference)
            .Add("text", request.Text)
            .Add("replace", request.Replace ? true : null)
            .Add("session", ResolveSessionLabel(request.Target.SessionLabel))
            .Build();
        return MapActionAsync("browser_type", arguments, cancellationToken);
    }

    public Task<DriverActionResult> BrowserScrollAsync(BrowserScrollRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("target_id", request.Target.TargetId)
            .Add("tab_id", request.Target.TabId)
            .Add("action", "scroll")
            .Add("ref", request.Reference)
            .Add("x", request.X)
            .Add("y", request.Y)
            .Add("delta_x", request.DeltaX)
            .Add("delta_y", request.DeltaY)
            .Add("session", ResolveSessionLabel(request.Target.SessionLabel))
            .Build();
        return MapActionAsync("browser_pointer", arguments, cancellationToken);
    }

    public Task<DriverActionResult> BrowserSetInputFilesAsync(BrowserSetInputFilesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, object?> arguments = new CuaArgs()
            .Add("target_id", request.Target.TargetId)
            .Add("tab_id", request.Target.TabId)
            .Add("ref", request.Reference)
            .Add("files", request.Files.ToArray())
            .Add("session", ResolveSessionLabel(request.Target.SessionLabel))
            .Build();
        return MapActionAsync("browser_set_input_files", arguments, cancellationToken);
    }

    public async Task<RecordingState> StartRecordingAsync(string outputDirectory, bool recordVideo = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outputDirectory);
        string approved = ResolveRecordingDirectory(outputDirectory);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        McpToolResult result = await CallToolAsync(
            "start_recording",
            new { output_dir = approved, record_video = recordVideo },
            cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);
        return ParseRecordingState(result.Structured);
    }

    public async Task<RecordingState> GetRecordingStateAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        McpToolResult result = await CallToolAsync("get_recording_state", new { }, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);
        return ParseRecordingState(result.Structured);
    }

    public async Task<RecordingState> StopRecordingAsync(CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        McpToolResult result = await CallToolAsync("stop_recording", new { }, cancellationToken).ConfigureAwait(false);
        ThrowIfError(result);
        return ParseRecordingState(result.Structured);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_client.IsValueCreated)
        {
            return;
        }

        CuaMcpClient client = _client.Value;
        if (_disposed)
        {
            return;
        }

        await client.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (!IsReady)
        {
            _ = await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<McpToolResult> CallToolAsync(string toolName, object? arguments, CancellationToken cancellationToken) =>
        _client.Value.CallToolAsync(toolName, arguments, cancellationToken);

    /// <summary>
    /// Builds a tool-call argument object that omits unset fields entirely. The
    /// pinned driver treats an explicit JSON <c>null</c> as "missing required"
    /// for some strict fields, so optional arguments must be absent, not null.
    /// </summary>
    private sealed class CuaArgs
    {
        private readonly Dictionary<string, object?> _values = new();

        public CuaArgs Add(string name, object? value)
        {
            if (value is not null)
            {
                _values[name] = value;
            }

            return this;
        }

        public Dictionary<string, object?> Build() => _values;
    }

    private void ValidatePermissionConfiguration()
    {
        if (_options.ServerArguments is null || _options.ServerArguments.Length == 0)
        {
            throw new DriverStartException("DriverConnectionOptions.ServerArguments must include the 'mcp' subcommand.");
        }

        if (string.IsNullOrWhiteSpace(_options.ProtocolVersion))
        {
            throw new DriverStartException("DriverConnectionOptions.ProtocolVersion cannot be blank.");
        }

        if (_options.MaxMessageBytes < DriverConnectionOptions.MinimumMaxMessageBytes)
        {
            throw new DriverStartException(
                $"DriverConnectionOptions.MaxMessageBytes must be at least {DriverConnectionOptions.MinimumMaxMessageBytes}.");
        }

        if (_options.MaxDiagnosticBytes < DriverConnectionOptions.MinimumMaxDiagnosticBytes)
        {
            throw new DriverStartException(
                $"DriverConnectionOptions.MaxDiagnosticBytes must be at least {DriverConnectionOptions.MinimumMaxDiagnosticBytes}.");
        }

        if (_options.StartTimeout <= TimeSpan.Zero)
        {
            throw new DriverStartException("DriverConnectionOptions.StartTimeout must be positive.");
        }

        if (_options.StopGracePeriod <= TimeSpan.Zero)
        {
            throw new DriverStartException("DriverConnectionOptions.StopGracePeriod must be positive.");
        }

        if (_options.MaxRequestId < 1)
        {
            throw new DriverStartException("DriverConnectionOptions.MaxRequestId must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(_options.CapabilityManifestFile))
        {
            _options.CapabilityManifestFile = null;
        }

        if (_options.PermissionMode == DriverPermissionMode.Bounded && _options.CapabilityManifestFile is null)
        {
            throw new DriverUnavailableException(
                "Bounded permission mode requires a capability manifest. Set DriverConnectionOptions.CapabilityManifestFile to an approved YAML manifest.");
        }

        if (_options.CapabilityManifestFile is not null)
        {
            string fullPath = Path.GetFullPath(_options.CapabilityManifestFile);
            if (!File.Exists(fullPath))
            {
                throw new DriverUnavailableException(
                    $"The capability manifest '{fullPath}' was not found. Bounded mode fails closed without an existing, approved manifest.");
            }

            _options.ResolvedCapabilityManifestPath = fullPath;
        }

        if (_options.ApprovedRecordingRoot is not null)
        {
            string root = Path.GetFullPath(_options.ApprovedRecordingRoot);
            if (!Directory.Exists(root))
            {
                throw new DriverUnavailableException(
                    $"The approved recording root '{root}' does not exist. Configure an existing directory or remove ApprovedRecordingRoot.");
            }

            _options.ApprovedRecordingRoot = root;
        }
    }

    private void ApplyLaunchGrants() =>
        _options.ServerArguments = ApplyLaunchGrants(_options.ServerArguments, _options.GrantExistingBrowserProfile);

    internal static string[] ApplyLaunchGrants(string[] arguments, bool grantExistingBrowserProfile)
    {
        if (!grantExistingBrowserProfile)
        {
            return arguments;
        }

        for (int index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], ExistingProfileGrant, StringComparison.Ordinal)
                && index + 1 < arguments.Length
                && string.Equals(arguments[index + 1], ExistingProfileValue, StringComparison.OrdinalIgnoreCase))
            {
                return arguments;
            }
        }

        List<string> updated = ["mcp", ExistingProfileGrant, ExistingProfileValue];
        foreach (string argument in arguments)
        {
            if (!string.Equals(argument, "mcp", StringComparison.Ordinal))
            {
                updated.Add(argument);
            }
        }

        return updated.ToArray();
    }

    private string ResolveSessionLabel(string? requestLabel)
    {
        string? label = requestLabel ?? _options.BrowserSessionLabel;
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new DriverProtocolException(
                "The driver requires an explicit session label for browser operations. Configure DriverConnectionOptions.BrowserSessionLabel or pass one on the request.");
        }

        return label;
    }
    private string ResolveRecordingDirectory(string outputDirectory)
    {
        if (!Path.IsPathRooted(outputDirectory))
        {
            throw new ArgumentException("The recording output directory must be an absolute path.", nameof(outputDirectory));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(outputDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("The recording output directory is not a valid path.", nameof(outputDirectory), ex);
        }

        if (_options.ApprovedRecordingRoot is not null
            && !IsWithinDirectory(_options.ApprovedRecordingRoot, fullPath))
        {
            throw new ArgumentException(
                $"The recording output directory '{fullPath}' is outside the approved root '{_options.ApprovedRecordingRoot}'.",
                nameof(outputDirectory));
        }

        try
        {
            _ = Directory.CreateDirectory(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new ArgumentException("The recording output directory could not be created.", nameof(outputDirectory), ex);
        }

        return fullPath;
    }

    private static bool IsWithinDirectory(string root, string candidate)
    {
        string rootTrimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidateTrimmed = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidateTrimmed.StartsWith(rootTrimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidateTrimmed, rootTrimmed, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<DriverActionResult> ClickFamilyAsync(
        string toolName,
        ClickRequest request,
        bool includeButtonAndCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        object arguments = BuildClickArguments(request, includeButtonAndCount);
        return await MapActionAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, object?> BuildClickArguments(ClickRequest request, bool includeButtonAndCount)
    {
        Dictionary<string, object?> arguments = new()
        {
            ["delivery_mode"] = request.DeliveryMode == DriverDeliveryMode.Foreground ? "foreground" : "background"
        };

        if (request.Pid is not null)
        {
            arguments["pid"] = request.Pid;
        }

        if (request.ElementTarget is not null)
        {
            request.ElementTarget.ValidateForDispatch(request.Pid!.Value, request.ElementTarget.WindowId);
            foreach (KeyValuePair<string, object?> field in request.ElementTarget.ToCuaArguments())
            {
                arguments[field.Key] = field.Value;
            }
        }
        else
        {
            if (request.X is not null && request.Y is not null)
            {
                arguments["x"] = request.X;
                arguments["y"] = request.Y;
            }
        }

        if (includeButtonAndCount)
        {
            arguments["button"] = request.Button;
            arguments["count"] = request.Count;
        }

        return arguments;
    }

    private static Dictionary<string, object?> ElementArguments(ElementTarget? target, uint pid)
    {
        Dictionary<string, object?> arguments = new() { ["pid"] = pid };
        if (target is null)
        {
            return arguments;
        }

        target.ValidateForDispatch(pid, target.WindowId);
        if (!string.IsNullOrWhiteSpace(target.ElementToken))
        {
            arguments["element_token"] = target.ElementToken;
            return arguments;
        }

        arguments["element_index"] = target.ElementIndex;
        arguments["snapshot_id"] = target.SnapshotId;
        arguments["window_id"] = target.WindowId;
        return arguments;
    }

    private async Task<DriverActionResult> MapActionAsync(
        string toolName,
        object? arguments,
        CancellationToken cancellationToken)
    {
        McpToolResult result = await CallToolAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.IsError && result.Structured.ValueKind != JsonValueKind.Object)
        {
            // The pinned driver always reports structured action content for
            // successful dispatches. A missing payload must never be treated as a
            // successful dispatch.
            throw new DriverProtocolException(
                $"The driver did not return structured content for '{toolName}'; the action outcome is unknown.");
        }

        DriverDeliveryMode deliveryMode = DriverDeliveryMode.Unknown;
        string? route = null;
        DriverActionEffect effect = DriverActionEffect.Unknown;
        if (result.Structured.ValueKind == JsonValueKind.Object)
        {
            string mode = DriverJson.GetString(result.Structured, "delivery", "mode") ?? string.Empty;
            deliveryMode = mode switch
            {
                "foreground" => DriverDeliveryMode.Foreground,
                "background" => DriverDeliveryMode.Background,
                _ => DriverDeliveryMode.Unknown
            };
            route = DriverJson.GetString(result.Structured, "route");
            string effectText = DriverJson.GetString(result.Structured, "effect") ?? string.Empty;
            effect = effectText switch
            {
                "verified" => DriverActionEffect.Verified,
                "unverifiable" => DriverActionEffect.Unverifiable,
                "refused" => DriverActionEffect.Refused,
                _ => DriverActionEffect.Unknown
            };
        }

        if (!result.IsError && effect != DriverActionEffect.Refused)
        {
            return new DriverActionResult(
                DriverActionStatus.Succeeded,
                code: null,
                message: result.CombinedText,
                deliveryMode,
                result.TextContent,
                result.Structured.ToString())
            {
                Route = route,
                Effect = effect
            };
        }

        (DriverActionStatus status, string code, string message) = ClassifyError(result.Structured, result.ErrorText ?? result.CombinedText);
        DriverActionEffect finalEffect = effect == DriverActionEffect.Unknown ? DriverActionEffect.Refused : effect;
        return new DriverActionResult(
            status,
            code,
            message,
            deliveryMode,
            result.TextContent,
            result.Structured.ToString())
        {
            Route = route,
            Effect = finalEffect
        };
    }

    private static void ThrowIfError(McpToolResult result)
    {
        if (result.IsError)
        {
            throw new DriverProtocolException($"The driver refused the request: {result.ErrorText}");
        }
    }

    private static (DriverActionStatus, string, string) ClassifyError(JsonElement structured, string text)
    {
        string message = string.IsNullOrWhiteSpace(text) ? "The driver reported an error." : text;

        // Prefer the structured refusal code when the driver reports one; the text
        // payload is not a stable vocabulary.
        string? code = structured.ValueKind == JsonValueKind.Object
            ? DriverJson.GetString(structured, "code")
            : null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            (DriverActionStatus, string, string)? coded = ClassifyStructuredCode(code, message);
            if (coded is not null)
            {
                return coded.Value;
            }
        }

        if (text.Contains("background_unavailable", StringComparison.OrdinalIgnoreCase)
            || text.Contains("background_occluded", StringComparison.OrdinalIgnoreCase)
            || text.Contains("background delivery is not available", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "background_unavailable", message);
        }

        if (text.Contains("browser_input_trust_unavailable", StringComparison.OrdinalIgnoreCase)
            || text.Contains("browser_ref_stale", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ref is stale", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "browser_input_trust_unavailable", message);
        }

        if (text.Contains("ambiguous", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "ambiguous_target", message);
        }

        if (text.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no longer exists", StringComparison.OrdinalIgnoreCase)
            || text.Contains("stale", StringComparison.OrdinalIgnoreCase)
            || text.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "stale_identity", message);
        }

        if (text.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || text.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
            || text.Contains("not approved", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "driver_permission_denied", message);
        }

        return (DriverActionStatus.Failed, "driver_error", message);
    }

    private static (DriverActionStatus, string, string)? ClassifyStructuredCode(string code, string message)
    {
        if (code.Equals("background_unavailable", StringComparison.OrdinalIgnoreCase)
            || code.Equals("background_occluded", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "background_unavailable", message);
        }

        if (code.Equals("browser_input_trust_unavailable", StringComparison.OrdinalIgnoreCase)
            || code.Equals("browser_ref_stale", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "browser_input_trust_unavailable", message);
        }

        if (code.Equals("ambiguous_target", StringComparison.OrdinalIgnoreCase)
            || code.Equals("ambiguous", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "ambiguous_target", message);
        }

        if (code.Equals("stale_identity", StringComparison.OrdinalIgnoreCase)
            || code.Equals("stale", StringComparison.OrdinalIgnoreCase)
            || code.Equals("expired", StringComparison.OrdinalIgnoreCase)
            || code.Equals("window_target_not_found", StringComparison.OrdinalIgnoreCase)
            || code.Equals("target_not_found", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "stale_identity", message);
        }

        if (code.Equals("permission_denied", StringComparison.OrdinalIgnoreCase)
            || code.Equals("driver_permission_denied", StringComparison.OrdinalIgnoreCase)
            || code.Equals("not_authorized", StringComparison.OrdinalIgnoreCase))
        {
            return (DriverActionStatus.Refused, "driver_permission_denied", message);
        }

        return null;
    }

    private static ApplicationInfo? TryParseApplication(JsonElement app)
    {
        uint pid = DriverJson.GetUInt(app, "pid") ?? 0;
        string? name = DriverJson.GetString(app, "name");
        if (name is null)
        {
            return null;
        }

        return new ApplicationInfo(
            name,
            pid,
            DriverJson.GetBool(app, false, "running"),
            DriverJson.GetBool(app, false, "active"),
            DriverJson.GetString(app, "kind") ?? "desktop",
            DriverJson.GetString(app, "launch_path"),
            DriverJson.GetString(app, "aumid") ?? DriverJson.GetString(app, "bundle_id"));
    }

    private static WindowInfo? TryParseWindow(JsonElement window)
    {
        ulong windowId = DriverJson.GetULong(window, "window_id") ?? 0;
        uint pid = DriverJson.GetUInt(window, "pid") ?? 0;
        if (windowId == 0 || pid == 0)
        {
            return null;
        }

        WindowBounds bounds = DriverJson.GetBounds(window, "bounds") ?? new WindowBounds(
            DriverJson.GetInt(window, "x") ?? 0,
            DriverJson.GetInt(window, "y") ?? 0,
            DriverJson.GetInt(window, "width") ?? 0,
            DriverJson.GetInt(window, "height") ?? 0);
        string title = DriverJson.GetString(window, "title") ?? string.Empty;
        string appName = DriverJson.GetString(window, "app_name") ?? title;
        return new WindowInfo(
            windowId,
            pid,
            appName,
            title,
            bounds,
            DriverJson.GetBool(window, false, "minimized"),
            DriverJson.GetBool(window, false, "is_on_screen"),
            DriverJson.GetInt(window, "z_index"));
    }

    private static List<WindowInfo> ParseWindows(JsonElement structured)
    {
        List<WindowInfo> windows = [];
        foreach (string key in new[] { "windows", "_legacy_windows" })
        {
            if (structured.TryGetProperty(key, out JsonElement windowsElement) && windowsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement window in windowsElement.EnumerateArray())
                {
                    WindowInfo? parsed = TryParseWindow(window);
                    if (parsed is not null)
                    {
                        windows.Add(parsed);
                    }
                }
            }
        }

        return windows;
    }

    private static AccessibilityElementInfo? TryParseElement(JsonElement element)
    {
        int? index = DriverJson.GetInt(element, "element_index");
        if (index is null)
        {
            return null;
        }

        return new AccessibilityElementInfo(
            index.Value,
            DriverJson.GetString(element, "role"),
            DriverJson.GetString(element, "label"),
            DriverJson.GetString(element, "value"),
            DriverJson.GetBool(element, false, "enabled"),
            DriverJson.GetBool(element, false, "selected"),
            DriverJson.GetStringArray(element, "actions"),
            DriverJson.GetBounds(element, "frame"),
            DriverJson.GetInt(element, "parent_index"),
            DriverJson.GetInt(element, "depth") ?? 0,
            DriverJson.GetString(element, "element_token"));
    }

    private static ScreenCaptureInfo? TryParseScreenshot(JsonElement structured, IReadOnlyList<string> textContent)
    {
        _ = textContent;
        string? base64 = DriverJson.GetString(structured, "screenshot");
        if (base64 is null && structured.TryGetProperty("screenshot", out JsonElement screenshotElement)
            && screenshotElement.ValueKind == JsonValueKind.Object)
        {
            base64 = DriverJson.GetString(screenshotElement, "data");
        }

        if (base64 is null)
        {
            return null;
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }

        int? width = DriverJson.GetInt(structured, "screenshot_width");
        int? height = DriverJson.GetInt(structured, "screenshot_height");
        return new ScreenCaptureInfo(data, "image/png", width, height);
    }

    private static ScreenCaptureInfo? TryParseScreenshot(McpToolResult result)
    {
        ScreenCaptureInfo? fromStructured = TryParseScreenshot(result.Structured, result.TextContent);
        if (fromStructured is not null)
        {
            return fromStructured;
        }

        McpImageContent? image = result.ImageContent.Count > 0 ? result.ImageContent[0] : null;
        if (image is null)
        {
            return null;
        }

        int? width = DriverJson.GetInt(result.Structured, "screenshot_width");
        int? height = DriverJson.GetInt(result.Structured, "screenshot_height");
        return new ScreenCaptureInfo(image.Data, image.MimeType ?? "image/png", width, height);
    }

    private static RecordingState ParseRecordingState(JsonElement structured)
    {
        return new RecordingState(
            DriverJson.GetBool(structured, false, "enabled"),
            DriverJson.GetBool(structured, false, "recording"),
            DriverJson.GetBool(structured, false, "video_active"),
            NormalizeWindowsPath(DriverJson.GetString(structured, "output_dir")),
            DriverJson.GetString(structured, "owner"),
            DriverJson.GetInt(structured, "next_turn") ?? 0,
            DriverJson.GetString(structured, "last_error"),
            NormalizeWindowsPath(DriverJson.GetString(structured, "last_video_path")));
    }

    private static string? NormalizeWindowsPath(string? path)
    {
        if (path is null)
        {
            return null;
        }

        // The driver returns extended-length device paths (\\?\C:\...) for some
        // recording fields; strip the device prefix so product-owned metadata uses
        // ordinary absolute paths.
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }
}


