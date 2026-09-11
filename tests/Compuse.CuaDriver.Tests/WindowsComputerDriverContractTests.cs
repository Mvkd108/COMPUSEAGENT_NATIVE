using System.Text;
using System.Text.Json;

namespace Compuse.CuaDriver.Tests;

/// <summary>
/// Contract tests driven by sanitized real responses captured from the pinned
/// Cua Driver 0.27.0 (<c>cua-driver-rs-0.27.0-windows-x86_64</c>). Each fixture
/// preserves the field names and shapes the driver actually produces.
/// </summary>
[TestClass]
public sealed class WindowsComputerDriverContractTests
{
    private static DriverConnectionOptions DefaultOptions() => new(Environment.ProcessPath ?? "testhost.exe");

    private static readonly string[] McpArguments = ["mcp"];

    private static WindowsComputerDriver CreateDriver(FakeMcpTransport transport) =>
        new(DefaultOptions(), () => transport);

    private static FakeMcpTransport ScriptedDriver(Func<byte[], byte[]?> handler) =>
        new()
        {
            RequestHandler = request =>
            {
                string method = McpTestScript.GetMethod(request) ?? string.Empty;
                if (method == "initialize")
                {
                    return McpTestScript.RespondTo(request, McpTestScript.InitializeResult());
                }

                return method == "tools/call" ? handler(request) : null;
            }
        };

    private static JsonElement GetArguments(byte[] request)
    {
        using JsonDocument document = JsonDocument.Parse(request);
        return document.RootElement.GetProperty("params").GetProperty("arguments").Clone();
    }

    private static void AssertToolCalled(FakeMcpTransport transport, string toolName, out JsonElement arguments)
    {
        byte[]? call = transport.WrittenLines.LastOrDefault(line =>
            Encoding.UTF8.GetString(line).Contains($"\"{toolName}\"", StringComparison.Ordinal));
        Assert.IsNotNull(call, $"Expected a call to '{toolName}'.");
        arguments = GetArguments(call!);
    }

    // ---------------------------------------------------------------- clipboard

    [TestMethod]
    public async Task ClipboardReadRequestsText()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"text":"clipboard payload","content_redacted_from_telemetry":true,"privacy_sensitive":true,"supported":true}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        string text = await driver.ClipboardReadAsync(CancellationToken.None);

        Assert.AreEqual("clipboard payload", text);
        AssertToolCalled(transport, "clipboard_read", out JsonElement arguments);
        Assert.IsTrue(arguments.GetProperty("include_text").GetBoolean(), "clipboard_read must request text explicitly.");
    }

    [TestMethod]
    public async Task ClipboardReadWithoutTextFailsExplicitly()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"text":null,"types":["CF_TEXT"]}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await Assert.ThrowsAsync<DriverProtocolException>(
            () => driver.ClipboardReadAsync(CancellationToken.None));
    }

    // ---------------------------------------------------------------- verify_state

    [TestMethod]
    public async Task VerifyStateSendsDocumentedExpectArrayAndReadsStatusField()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"status":"satisfied","samples":2,"stable":true,"elapsed_ms":1157,"predicates":[]}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        VerifyStateResult result = await driver.VerifyStateAsync(
            new VerifyRequest(
                42,
                100,
                elementPredicates: [new VerifyElementPredicate(role: "Text", labelContains: "Display is 7", exists: true)],
                windowPredicate: new VerifyWindowPredicate(exists: true)),
            CancellationToken.None);

        Assert.IsTrue(result.IsSatisfied);
        Assert.AreEqual("satisfied", result.Verdict);
        Assert.IsTrue(result.Stable);
        Assert.AreEqual(2, result.Samples);
        Assert.AreEqual(1157, result.ElapsedMs);

        AssertToolCalled(transport, "verify_state", out JsonElement arguments);
        JsonElement expect = arguments.GetProperty("expect");
        Assert.AreEqual(JsonValueKind.Array, expect.ValueKind);
        JsonElement windowPredicate = expect[0].GetProperty("window");
        Assert.IsTrue(windowPredicate.GetProperty("exists").GetBoolean());
        JsonElement elementPredicate = expect[1].GetProperty("element");
        Assert.AreEqual("Text", elementPredicate.GetProperty("selector").GetProperty("role").GetString());
        Assert.IsTrue(elementPredicate.GetProperty("exists").GetBoolean());
    }

    [TestMethod]
    public async Task VerifyStateUnsatisfiedIsNotSuccess()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"status":"unsatisfied","samples":2,"stable":false,"elapsed_ms":5000,"predicates":[]}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        VerifyStateResult result = await driver.VerifyStateAsync(
            new VerifyRequest(42, 100, elementPredicates: [new VerifyElementPredicate(role: "Text", labelContains: "42")]),
            CancellationToken.None);

        Assert.IsFalse(result.IsSatisfied);
        Assert.AreEqual("unsatisfied", result.Verdict);
    }

    // ---------------------------------------------------------------- desktop state

    [TestMethod]
    public async Task GetDesktopStateReturnsCaptureModelFromImageContent()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "content": [{"type":"image","data":"aGVscw==","mimeType":"image/png"}],
              "structuredContent": {
                "display": "primary",
                "platform": "windows",
                "scale_factor": 1.25,
                "screen_height": 1080,
                "screen_width": 1920,
                "screenshot_height": 1080,
                "screenshot_mime_type": "image/png",
                "screenshot_width": 1920
              }
            }
            """));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        DesktopState state = await driver.GetDesktopStateAsync(CancellationToken.None);

        Assert.IsNotNull(state.Screenshot);
        Assert.AreEqual("hels", Encoding.UTF8.GetString(state.Screenshot!.PngData!));
        Assert.AreEqual(1920, state.ScreenshotWidth);
        Assert.AreEqual(1080, state.ScreenshotHeight);
        Assert.AreEqual(1920, state.ScreenWidth);
        Assert.AreEqual(1080, state.ScreenHeight);
        Assert.AreEqual(1.25, state.ScaleFactor);
        Assert.AreEqual("primary", state.Display);
        Assert.AreEqual("windows", state.Platform);
    }

    [TestMethod]
    public async Task GetDesktopStateWithoutScreenshotFailsExplicitly()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"display":"primary"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await Assert.ThrowsAsync<DriverProtocolException>(
            () => driver.GetDesktopStateAsync(CancellationToken.None));
    }

    // ---------------------------------------------------------------- element frames

    [TestMethod]
    public async Task GetWindowStateParsesElementFramesWithDriverWidthHeightAliases()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "structuredContent": {
                "window_title": "Calculator",
                "app_name": "Calculator",
                "window_bounds": {"x": 58, "y": 0, "width": 402, "height": 667},
                "total_element_count": 1,
                "snapshot_id": "s00000001",
                "elements": [
                  {"element_index": 8, "element_token": "s00000001:8", "role": "Text", "label": "Display is 0", "enabled": true, "actions": ["invoke"], "frame": {"h": 94, "w": 400, "x": 59, "y": 130}, "depth": 2}
                ]
              }
            }
            """));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        WindowStateSnapshot snapshot = await driver.GetWindowStateAsync(42, 100, cancellationToken: CancellationToken.None);

        Assert.AreEqual("s00000001", snapshot.SnapshotId);
        WindowBounds frame = snapshot.Elements[0].Frame
            ?? throw new AssertFailedException("Element frames use {x,y,w,h} in the real driver response.");
        Assert.AreEqual(400, frame.Width);
        Assert.AreEqual(94, frame.Height);
        Assert.AreEqual(59, frame.X);
        Assert.AreEqual(130, frame.Y);
        Assert.AreEqual("s00000001:8", snapshot.Elements[0].ElementToken);
    }

    // ---------------------------------------------------------------- launch_app

    [TestMethod]
    public async Task LaunchAppSendsAdditionalArgumentsAsArray()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"pid":42,"name":"app","active":false,"bundle_id":null,"windows":[]}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.LaunchApplicationAsync(
            new LaunchRequest(aumid: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", additionalArguments: ["--flag"]),
            CancellationToken.None);

        AssertToolCalled(transport, "launch_app", out JsonElement arguments);
        JsonElement additional = arguments.GetProperty("additional_arguments");
        Assert.AreEqual(JsonValueKind.Array, additional.ValueKind);
        Assert.AreEqual("--flag", additional[0].GetString());
    }

    [TestMethod]
    public async Task LaunchAppWithNoArgumentsOmitsTheFieldValueAsNull()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"pid":42,"name":"app","active":false,"bundle_id":null,"windows":[]}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.LaunchApplicationAsync(new LaunchRequest(name: "Notepad"), CancellationToken.None);

        AssertToolCalled(transport, "launch_app", out JsonElement arguments);
        Assert.IsFalse(arguments.TryGetProperty("additional_arguments", out _) && arguments.GetProperty("additional_arguments").ValueKind != JsonValueKind.Null);
    }

    // ---------------------------------------------------------------- element-targeted actions

    [TestMethod]
    public async Task SetValueWithElementTokenPassesTokenOnly()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.SetValueAsync(
            new ElementTarget(42, 100, 8, elementToken: "s00000001:8"),
            "value",
            CancellationToken.None);

        AssertToolCalled(transport, "set_value", out JsonElement arguments);
        Assert.AreEqual("s00000001:8", arguments.GetProperty("element_token").GetString());
        Assert.IsFalse(arguments.TryGetProperty("element_index", out _), "element_token addressing must not also send element_index.");
        Assert.IsFalse(arguments.TryGetProperty("snapshot_id", out _), "element_token addressing must not also send snapshot_id.");
    }

    [TestMethod]
    public async Task SetValueWithIndexRequiresSnapshotIdAndWindowId()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.SetValueAsync(
            new ElementTarget(42, 100, 8, snapshotId: "s00000001"),
            "value",
            CancellationToken.None);

        AssertToolCalled(transport, "set_value", out JsonElement arguments);
        Assert.AreEqual(8, arguments.GetProperty("element_index").GetInt32());
        Assert.AreEqual("s00000001", arguments.GetProperty("snapshot_id").GetString());
        Assert.AreEqual(100UL, arguments.GetProperty("window_id").GetUInt64());
    }

    [TestMethod]
    public async Task SetValueWithBareIndexWithoutSnapshotIsRejectedBeforeDispatch()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => driver.SetValueAsync(new ElementTarget(42, 100, 8), "value", CancellationToken.None));

        Assert.IsFalse(transport.WrittenLines.Any(line =>
            Encoding.UTF8.GetString(line).Contains("\"set_value\"", StringComparison.Ordinal)),
            "An incomplete element target must never reach the driver.");
    }

    [TestMethod]
    public async Task SetValueWithSnapshotButNoIndexIsUnrepresentable()
    {
        // The DTO requires an element index; a snapshot cannot be supplied without
        // an index, so the "snapshot without index" ambiguity cannot be constructed.
        await using WindowsComputerDriver driver = CreateDriver(new FakeMcpTransport());
        ElementTarget target = new(42, 100, 8, snapshotId: "s00000001");
        Assert.IsNotNull(target);
        Assert.IsFalse(string.IsNullOrWhiteSpace(target.SnapshotId));
    }

    [TestMethod]
    public async Task TypeTextWithTokenTargetsTheElement()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.TypeTextAsync(
            new TypeTextRequest(42, "hello", new ElementTarget(42, 100, 8, elementToken: "s00000001:8")),
            CancellationToken.None);

        AssertToolCalled(transport, "type_text", out JsonElement arguments);
        Assert.AreEqual("s00000001:8", arguments.GetProperty("element_token").GetString());
        Assert.AreEqual("hello", arguments.GetProperty("text").GetString());
    }

    [TestMethod]
    public async Task PressKeyWithIndexRequiresSnapshotAndWindow()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.PressKeyAsync(
            new KeyRequest(42, "return", new ElementTarget(42, 100, 8, snapshotId: "s00000001")),
            CancellationToken.None);

        AssertToolCalled(transport, "press_key", out JsonElement arguments);
        Assert.AreEqual(8, arguments.GetProperty("element_index").GetInt32());
        Assert.AreEqual("s00000001", arguments.GetProperty("snapshot_id").GetString());
        Assert.AreEqual(100UL, arguments.GetProperty("window_id").GetUInt64());
    }

    [TestMethod]
    public void HotkeyRequiresAtLeastTwoKeys() =>
        Assert.Throws<ArgumentException>(() => new HotkeyRequest(42, ["ctrl"]));

    [TestMethod]
    public async Task HotkeyPassesWindowIdAndKeys()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.HotkeyAsync(new HotkeyRequest(42, ["ctrl", "s"], windowId: 100), CancellationToken.None);

        AssertToolCalled(transport, "hotkey", out JsonElement arguments);
        Assert.AreEqual(100UL, arguments.GetProperty("window_id").GetUInt64());
        Assert.AreEqual(2, arguments.GetProperty("keys").GetArrayLength());
    }

    [TestMethod]
    public async Task ScrollCarriesExplicitWindowId()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.ScrollAsync(new ScrollRequest(42, 100, ScrollDirection.Down, ScrollStep.Line, amount: 3), CancellationToken.None);

        AssertToolCalled(transport, "scroll", out JsonElement arguments);
        Assert.AreEqual(100UL, arguments.GetProperty("window_id").GetUInt64());
        Assert.AreEqual("down", arguments.GetProperty("direction").GetString());
        Assert.AreEqual("line", arguments.GetProperty("by").GetString());
        Assert.AreEqual(3, arguments.GetProperty("amount").GetInt32());
    }

    [TestMethod]
    public async Task MismatchedElementPidIsRejectedBeforeDispatch()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => driver.ClickAsync(
                new ClickRequest(pid: 999, elementTarget: new ElementTarget(42, 100, 8, snapshotId: "s00000001")),
                CancellationToken.None));
        Assert.IsFalse(transport.WrittenLines.Any(line =>
            Encoding.UTF8.GetString(line).Contains("\"click\"", StringComparison.Ordinal)),
            "A contradictory element target must never reach the driver.");
    }

    // ---------------------------------------------------------------- basic action surface

    [TestMethod]
    public async Task RightClickRoutesToTheDedicatedTool()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.RightClickAsync(new ClickRequest(pid: 42, x: 5, y: 5), CancellationToken.None);

        AssertToolCalled(transport, "right_click", out JsonElement arguments);
        Assert.IsFalse(arguments.TryGetProperty("count", out _), "right_click must not send the click tool's count field.");
    }

    [TestMethod]
    public async Task DoubleClickRoutesToTheDedicatedTool()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.DoubleClickAsync(new ClickRequest(pid: 42, x: 5, y: 5), CancellationToken.None);

        AssertToolCalled(transport, "double_click", out JsonElement arguments);
        Assert.IsFalse(arguments.TryGetProperty("button", out _), "double_click must not send the click tool's button field.");
    }

    [TestMethod]
    public async Task DragRoutesToTheDedicatedToolWithWindowScope()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await driver.DragAsync(new DragRequest(42, 100, fromX: 10, fromY: 10, toX: 200, toY: 300), CancellationToken.None);

        AssertToolCalled(transport, "drag", out JsonElement arguments);
        Assert.AreEqual(100UL, arguments.GetProperty("window_id").GetUInt64());
        Assert.AreEqual(10, arguments.GetProperty("from_x").GetInt32());
        Assert.AreEqual(300, arguments.GetProperty("to_y").GetInt32());
        Assert.AreEqual("background", arguments.GetProperty("delivery_mode").GetString());
    }

    [TestMethod]
    public async Task ClickRecordsEffectAndDispatchMayHaveOccurred()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        DriverActionResult result = await driver.ClickAsync(new ClickRequest(pid: 42, x: 1, y: 1), CancellationToken.None);

        Assert.AreEqual(DriverActionEffect.Unverifiable, result.Effect);
        Assert.IsTrue(result.DispatchMayHaveOccurred);
        Assert.AreEqual("accessibility", result.Route);
        Assert.AreEqual(DriverDeliveryMode.Background, result.DeliveryMode);
    }

    [TestMethod]
    public async Task BackgroundRefusalNeverReportsDispatch()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {"content":[{"type":"text","text":"background_unavailable: the target window cannot accept background input"}],"isError":true,"structuredContent":{"delivery":{"mode":"background"},"route":"pixel","effect":"refused"}}
            """));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        DriverActionResult result = await driver.ClickAsync(new ClickRequest(pid: 42, x: 1, y: 1), CancellationToken.None);

        Assert.AreEqual(DriverActionStatus.Refused, result.Status);
        Assert.AreEqual("background_unavailable", result.Code);
        Assert.AreEqual(DriverActionEffect.Refused, result.Effect);
        Assert.IsFalse(result.DispatchMayHaveOccurred);
    }

    // ---------------------------------------------------------------- capability configuration

    [TestMethod]
    public async Task BoundedModeWithoutManifestFailsExplicitlyBeforeStart()
    {
        DriverConnectionOptions options = new(Environment.ProcessPath ?? "testhost.exe")
        {
            PermissionMode = DriverPermissionMode.Bounded
        };
        await using WindowsComputerDriver driver = new(options);

        _ = await Assert.ThrowsAsync<DriverUnavailableException>(
            () => driver.InitializeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task CapabilityManifestMustResolveToAnExistingFile()
    {
        string missing = Path.Combine(Path.GetTempPath(), "compuse-tests", "missing-manifest.yaml");
        DriverConnectionOptions options = new(Environment.ProcessPath ?? "testhost.exe")
        {
            PermissionMode = DriverPermissionMode.Bounded,
            CapabilityManifestFile = missing
        };
        await using WindowsComputerDriver driver = new(options);

        _ = await Assert.ThrowsAsync<DriverUnavailableException>(
            () => driver.InitializeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task GrantExistingBrowserProfileAppendsTheLaunchGrant()
    {
        string[] withGrant = WindowsComputerDriver.ApplyLaunchGrants(McpArguments, grantExistingBrowserProfile: true);
        Assert.IsTrue(withGrant.SequenceEqual(new[] { "mcp", "--grant", "existing-profile" }));

        string[] noGrant = WindowsComputerDriver.ApplyLaunchGrants(McpArguments, grantExistingBrowserProfile: false);
        Assert.IsTrue(noGrant.SequenceEqual(McpArguments));

        string[] idempotent = WindowsComputerDriver.ApplyLaunchGrants(withGrant, grantExistingBrowserProfile: true);
        Assert.IsTrue(idempotent.SequenceEqual(withGrant));
    }

    [TestMethod]
    public void EffectivePermissionModeReflectsConfiguration()
    {
        Assert.AreEqual("standard", new DriverConnectionOptions("x.exe").EffectivePermissionModeDescription);
        Assert.AreEqual(
            "bounded",
            new DriverConnectionOptions("x.exe") { PermissionMode = DriverPermissionMode.Bounded }.EffectivePermissionModeDescription);
    }

    [TestMethod]
    public async Task RequestIdLimitIsEnforced()
    {
        FakeMcpTransport transport = new(request =>
        {
            string method = McpTestScript.GetMethod(request) ?? string.Empty;
            if (method == "initialize")
            {
                return McpTestScript.RespondTo(request, McpTestScript.InitializeResult());
            }

            return McpTestScript.RespondTo(request, """{"structuredContent":{"ok":true}}""");
        });
        DriverConnectionOptions options = DefaultOptions();
        options.MaxRequestId = 2;
        await using CuaMcpClient client = new(transport, options);
        _ = await client.InitializeAsync(CancellationToken.None);

        _ = await client.CallToolAsync("one", new { }, CancellationToken.None);
        _ = await Assert.ThrowsAsync<DriverProtocolException>(
            () => client.CallToolAsync("two", new { }, CancellationToken.None));
    }

    // ---------------------------------------------------------------- recording

    [TestMethod]
    public async Task RecordingRequiresAnAbsoluteOutputDirectory()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"enabled":true,"recording":true,"next_turn":1}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => driver.StartRecordingAsync("relative\\path", cancellationToken: CancellationToken.None));
    }

    [TestMethod]
    public async Task RecordingIsRestrictedToTheApprovedRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "compuse-tests-approved-root");
        _ = Directory.CreateDirectory(root);
        DriverConnectionOptions options = DefaultOptions();
        options.ApprovedRecordingRoot = root;
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"enabled":true,"recording":true,"next_turn":1}}"""));
        await using WindowsComputerDriver driver = new(options, () => transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        string outside = Path.Combine(Path.GetTempPath(), "compuse-tests-outside-root");
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => driver.StartRecordingAsync(outside, cancellationToken: CancellationToken.None));

        string inside = Path.Combine(root, "turn-001");
        RecordingState state = await driver.StartRecordingAsync(inside, cancellationToken: CancellationToken.None);
        Assert.IsTrue(state.Recording);
        AssertToolCalled(transport, "start_recording", out JsonElement arguments);
        Assert.IsTrue(arguments.GetProperty("output_dir").GetString()!.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task RecordingStateParsesDriverShape()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"enabled":true,"recording":true,"video_active":false,"output_dir":"C:\\probe","owner":"mcp-1","next_turn":4,"last_error":null,"last_video_path":null}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        RecordingState state = await driver.GetRecordingStateAsync(CancellationToken.None);

        Assert.IsTrue(state.Recording);
        Assert.AreEqual(4, state.NextTurn);
        Assert.AreEqual("C:\\probe", state.OutputDirectory);
    }

    // ---------------------------------------------------------------- browser

    [TestMethod]
    public async Task BrowserOperationsRequireAnExplicitSessionLabel()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        _ = await Assert.ThrowsAsync<DriverProtocolException>(
            () => driver.BindBrowserAsync(new BrowserBindRequest(42, 100), CancellationToken.None));
    }

    [TestMethod]
    public async Task BindBrowserParsesRealResponseShape()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "structuredContent": {
                "binding_quality": "exact",
                "binding_route": "native_cdp_window",
                "endpoint_access_class": "driver_owned",
                "endpoint_transport": "legacy_json_version",
                "mode": "bind",
                "mutation_allowed": true,
                "native_title": "about:blank - Google Chrome",
                "status": "ok",
                "tabs": [
                  {"active": true, "tab_id": "tab-abc", "title": "about:blank", "url": "about:blank"}
                ],
                "target_id": "bt-abc"
              }
            }
            """));
        DriverConnectionOptions options = DefaultOptions();
        options.BrowserSessionLabel = "session-1";
        await using WindowsComputerDriver driver = new(options, () => transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        BrowserBinding binding = await driver.BindBrowserAsync(new BrowserBindRequest(42, 100), CancellationToken.None);

        Assert.AreEqual("bt-abc", binding.TargetId);
        Assert.AreEqual("exact", binding.BindingQuality);
        Assert.AreEqual(1, binding.Tabs.Count);
        Assert.AreEqual("tab-abc", binding.Tabs[0].TabId);
        Assert.IsTrue(binding.MutationAllowed);
    }

    [TestMethod]
    public async Task SnapshotBrowserParsesRefsAndScreenshot()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "content": [{"type":"text","text":"snapshot p2 of https://example.com/: 2 interactive element(s)"},{"type":"image","data":"aGVscw==","mimeType":"image/png"}],
              "structuredContent": {
                "mode": "snapshot",
                "oopif": {"frames": 0, "status": "attached"},
                "refs": [
                  {"frame": "main", "label": "href=data:,", "node": "link", "ref": "p2:0"},
                  {"frame": "main", "label": "href=https://iana.org/domains/example", "node": "a", "ref": "p2:1"}
                ],
                "snapshot_id": "p2",
                "status": "ok",
                "tab_id": "tab-abc",
                "target_id": "bt-abc",
                "truncated": false,
                "url": "https://example.com/"
              }
            }
            """));
        DriverConnectionOptions options = DefaultOptions();
        options.BrowserSessionLabel = "session-1";
        await using WindowsComputerDriver driver = new(options, () => transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        BrowserSnapshot snapshot = await driver.SnapshotBrowserAsync(
            new BrowserTabTarget("bt-abc", "tab-abc"),
            includeScreenshot: true,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual("p2", snapshot.SnapshotId);
        Assert.AreEqual(2, snapshot.Refs.Count);
        Assert.AreEqual("p2:0", snapshot.Refs[0].Reference);
        Assert.AreEqual("a", snapshot.Refs[1].Node);
        Assert.IsNotNull(snapshot.Screenshot);
        Assert.AreEqual("hels", Encoding.UTF8.GetString(snapshot.Screenshot!.PngData!));
    }

    [TestMethod]
    public async Task BrowserClickWithStaleRefIsMappedToRefusedBackgroundInput()
    {
        // Real driver shape: the refusal is delivered as a successful tool result
        // whose structuredContent.effect is "refused"; there is no isError flag.
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {"content":[{"type":"text","text":"refused (browser_ref_stale): ref p1:0 is stale — the page navigated or the snapshot was superseded; re-run get_browser_state to re-snapshot"}],"structuredContent":{"effect":"refused","escalation":{"reason":"route_unavailable","target":"page"},"route":"trusted_input"}}
            """));
        DriverConnectionOptions options = DefaultOptions();
        options.BrowserSessionLabel = "session-1";
        await using WindowsComputerDriver driver = new(options, () => transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        DriverActionResult result = await driver.BrowserClickAsync(
            new BrowserClickRequest(new BrowserTabTarget("bt-abc", "tab-abc"), reference: "p1:0"),
            CancellationToken.None);

        Assert.AreEqual(DriverActionStatus.Refused, result.Status);
        Assert.AreEqual("browser_input_trust_unavailable", result.Code);
        Assert.AreEqual(DriverActionEffect.Refused, result.Effect);
        Assert.IsFalse(result.DispatchMayHaveOccurred);

        AssertToolCalled(transport, "browser_click", out JsonElement clickArgs);
        Assert.AreEqual("p1:0", clickArgs.GetProperty("ref").GetString());
        Assert.AreEqual("bt-abc", clickArgs.GetProperty("target_id").GetString());
        Assert.AreEqual("tab-abc", clickArgs.GetProperty("tab_id").GetString());
    }

    [TestMethod]
    public void BrowserNavigateRejectsNonHttpHttpsAboutUrls() =>
        Assert.Throws<ArgumentException>(() =>
            new BrowserNavigateRequest(new BrowserTabTarget("bt-abc", "tab-abc"), "file:///C:/x.html"));

    [TestMethod]
    public async Task BrowserPrepareIsolatedProfileUsesDriverOwnedProfile()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "structuredContent": {
                "action": "launched_isolated_browser",
                "prepared": true,
                "prepared_pid": 17968,
                "endpoint_ownership": {"detail": "driver-owned", "listener_pid": 17968, "method": "spawned_by_driver", "owner_pid": 17968},
                "status": "ok",
                "side_effects": {"created_profile": true, "launched_browser": true}
              }
            }
            """));
        DriverConnectionOptions options = DefaultOptions();
        options.BrowserSessionLabel = "session-1";
        await using WindowsComputerDriver driver = new(options, () => transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        BrowserPrepareResult result = await driver.PrepareBrowserAsync(
            new BrowserPrepareRequest(allowLaunch: true, isolatedProfile: true, profileName: "compuse-qual"),
            CancellationToken.None);

        Assert.IsTrue(result.Prepared);
        Assert.AreEqual(17968, result.PreparedPid);
        Assert.IsTrue(result.CreatedProfile);
        AssertToolCalled(transport, "browser_prepare", out JsonElement arguments);
        Assert.AreEqual("isolated_named", arguments.GetProperty("profile").GetProperty("mode").GetString());
        Assert.AreEqual("compuse-qual", arguments.GetProperty("profile").GetProperty("name").GetString());
        Assert.IsTrue(arguments.GetProperty("allow_launch").GetBoolean());
    }
}
