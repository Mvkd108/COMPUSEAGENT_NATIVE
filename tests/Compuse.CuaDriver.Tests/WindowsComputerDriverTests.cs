using System.Text;

namespace Compuse.CuaDriver.Tests;

[TestClass]
public sealed class WindowsComputerDriverTests
{
    private static DriverConnectionOptions Options(string exe) => new(exe);

    private static DriverConnectionOptions DefaultOptions() => new(Environment.ProcessPath ?? "testhost.exe");

    private static WindowsComputerDriver CreateDriver(FakeMcpTransport transport) =>
        new(DefaultOptions(), () => transport);

    private static FakeMcpTransport ScriptedDriver(
        Func<byte[], byte[]?> handler)
    {
        FakeMcpTransport transport = new()
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
        return transport;
    }

    [TestMethod]
    public async Task MissingExecutableReturnsUnavailableDiagnostic()
    {
        await using WindowsComputerDriver driver = new(Options(@"C:\definitely\missing\cua-driver.exe"));

        _ = await Assert.ThrowsAsync<DriverUnavailableException>(
            () => driver.InitializeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task UnsupportedDriverVersionIsRefused()
    {
        FakeMcpTransport transport = new(request =>
            McpTestScript.GetMethod(request) == "initialize"
                ? McpTestScript.RespondTo(request, McpTestScript.InitializeResult("0.20.0"))
                : null);
        await using WindowsComputerDriver driver = CreateDriver(transport);

        _ = await Assert.ThrowsAsync<DriverIncompatibleException>(
            () => driver.InitializeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task BackgroundUnavailableRefusalIsTyped()
    {
        FakeMcpTransport transport = ScriptedDriver(_ =>
            McpTestScript.RespondTo(
                _,
                "background_unavailable: the target window cannot accept background input; use foreground delivery explicitly.",
                isError: true));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        DriverActionResult result = await driver.ClickAsync(
            new ClickRequest(pid: 42, x: 10, y: 10, deliveryMode: DriverDeliveryMode.Background),
            CancellationToken.None);

        Assert.AreEqual(DriverActionStatus.Refused, result.Status);
        Assert.AreEqual("background_unavailable", result.Code);
    }

    [TestMethod]
    public async Task GetWindowStateParsesElementsAndScreenshot()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "structuredContent": {
                "window_title": "Calculator",
                "app_name": "Calculator",
                "window_bounds": {"x": 0, "y": 0, "width": 400, "height": 500},
                "total_element_count": 2,
                "elements": [
                  {"element_index": 0, "role": "button", "label": "7", "value": null, "enabled": true, "selected": false, "actions": ["invoke"], "frame": {"x": 10, "y": 10, "width": 40, "height": 40}, "parent_index": null, "depth": 2},
                  {"element_index": 1, "role": "button", "label": "Equals", "value": null, "enabled": true, "selected": false, "actions": ["invoke"], "frame": {"x": 60, "y": 10, "width": 40, "height": 40}, "parent_index": 0, "depth": 3}
                ],
                "screenshot": "aGVsbG8="
              },
              "content": [{"type":"text","text":"tree markdown"}]
            }
            """));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        WindowStateSnapshot snapshot = await driver.GetWindowStateAsync(42, 100, cancellationToken: CancellationToken.None);

        Assert.AreEqual("Calculator", snapshot.WindowTitle);
        Assert.AreEqual(2, snapshot.TotalElementCount);
        Assert.AreEqual(2, snapshot.Elements.Count);
        Assert.AreEqual(0, snapshot.Elements[0].ElementIndex);
        Assert.AreEqual("button", snapshot.Elements[0].Role);
        Assert.AreEqual("invoke", snapshot.Elements[0].Actions[0]);
        Assert.AreEqual(0, snapshot.Elements[1].ParentIndex);
        Assert.IsNotNull(snapshot.Screenshot);
        Assert.AreEqual("hello", Encoding.UTF8.GetString(snapshot.Screenshot!.PngData!));
        Assert.IsNotNull(snapshot.Screenshot.PngData);
    }

    [TestMethod]
    public async Task ListWindowsParsesWindowRecords()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "structuredContent": {
                "windows": [
                  {"window_id": 1001, "pid": 42, "app_name": "notepad", "title": "Untitled - Notepad", "bounds": {"x": 1, "y": 2, "width": 800, "height": 600}, "minimized": false, "is_on_screen": true, "z_index": 5}
                ]
              }
            }
            """));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        IReadOnlyList<WindowInfo> windows = await driver.ListWindowsAsync(cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, windows.Count);
        Assert.AreEqual(1001UL, windows[0].WindowId);
        Assert.AreEqual(42u, windows[0].Pid);
        Assert.AreEqual("Untitled - Notepad", windows[0].Title);
        Assert.AreEqual(800, windows[0].Bounds.Width);
        Assert.IsTrue(windows[0].OnScreen);
        Assert.AreEqual(5, windows[0].ZIndex);
    }

    [TestMethod]
    public async Task ListApplicationsParsesAppRecords()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "structuredContent": {
                "apps": [
                  {"name": "Notepad", "pid": 42, "running": true, "active": false, "kind": "uwp", "launch_path": "shell:AppsFolder\\X!App", "aumid": "X!App"}
                ]
              }
            }
            """));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        IReadOnlyList<ApplicationInfo> apps = await driver.ListApplicationsAsync(CancellationToken.None);

        Assert.AreEqual(1, apps.Count);
        Assert.AreEqual("Notepad", apps[0].Name);
        Assert.AreEqual(42u, apps[0].Pid);
        Assert.IsTrue(apps[0].Running);
        Assert.AreEqual("uwp", apps[0].Kind);
    }

    [TestMethod]
    public async Task HealthReportParsesChecks()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """
            {
              "structuredContent": {
                "schema_version": "1",
                "platform": "win32",
                "driver_version": "0.27.0",
                "overall": "ok",
                "checks": [
                  {"name": "binary_version", "status": "pass", "message": "ok"},
                  {"name": "session_active", "status": "pass", "message": "ok"}
                ]
              }
            }
            """));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        DriverHealthReport health = await driver.GetHealthAsync(CancellationToken.None);

        Assert.AreEqual("ok", health.Overall);
        Assert.AreEqual("0.27.0", health.DriverVersion);
        Assert.AreEqual("win32", health.Platform);
        Assert.AreEqual(2, health.Checks.Count);
        Assert.AreEqual("pass", health.Checks[0].Status);
    }

    [TestMethod]
    public async Task SuccessfulClickIssuesExactlyOneToolCall()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"delivery":{"mode":"background"},"route":"accessibility","effect":"unverifiable"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);
        int callsBefore = transport.WrittenLines.Count(line =>
            Encoding.UTF8.GetString(line).Contains("\"click\"", StringComparison.Ordinal));

        DriverActionResult result = await driver.InvokeElementAsync(
            new ElementTarget(pid: 42, windowId: 100, elementIndex: 3, snapshotId: "s00000001"),
            CancellationToken.None);

        int callsAfter = transport.WrittenLines.Count(line =>
            Encoding.UTF8.GetString(line).Contains("\"click\"", StringComparison.Ordinal));
        Assert.AreEqual(DriverActionStatus.Succeeded, result.Status);
        Assert.AreEqual(DriverDeliveryMode.Background, result.DeliveryMode);
        Assert.AreEqual(callsBefore + 1, callsAfter, "The driver must not be called again automatically after a side-effect-producing action.");
    }

    [TestMethod]
    public async Task VerifyStateParsesVerdict()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"verdict":"satisfied","detail":"42 == 42"}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        VerifyStateResult result = await driver.VerifyStateAsync(
            new VerifyRequest(42, 100, elementPredicates: [new VerifyElementPredicate(role: "Text", labelContains: "42")]),
            CancellationToken.None);

        Assert.IsTrue(result.IsSatisfied);
        Assert.AreEqual("satisfied", result.Verdict);
    }

    [TestMethod]
    public async Task CaptureWindowReturnsScreenshot()
    {
        FakeMcpTransport transport = ScriptedDriver(request =>
            McpTestScript.RespondTo(request, """{"structuredContent":{"screenshot":"c2NyZWVuc2hvdA==","screenshot_width":320,"screenshot_height":200}}"""));
        await using WindowsComputerDriver driver = CreateDriver(transport);
        _ = await driver.InitializeAsync(CancellationToken.None);

        ScreenCaptureInfo capture = await driver.CaptureWindowAsync(42, 100, cancellationToken: CancellationToken.None);

        Assert.AreEqual("screenshot", Encoding.UTF8.GetString(capture.PngData!));
        Assert.AreEqual(320, capture.Width);
    }
}

