using System.Runtime.InteropServices;
using System.Text;

namespace Compuse.CuaDriver.Tests;

[TestClass]
[TestCategory("DriverIntegration")]
public sealed class CuaDriverIntegrationTests
{
    private static string? DriverPath => Environment.GetEnvironmentVariable("COMPUSE_CUA_DRIVER_EXE");

    private const string CalculatorAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    private const uint WmClose = 0x0010;

    [TestMethod]
    public async Task InitializeAndHealthAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        McpInitializeResult initialize = await driver.InitializeAsync(CancellationToken.None);
        Assert.AreEqual("cua-driver", initialize.ServerName);
        Assert.IsTrue(initialize.Version >= DriverVersion.Parse("0.27.0"), $"Driver version {initialize.Version} is older than the pinned tested version.");

        DriverHealthReport health = await driver.GetHealthAsync(CancellationToken.None);
        Assert.AreEqual("win32", health.Platform);
        Assert.AreNotEqual("failed", health.Overall, $"Driver health failed: {string.Join("; ", health.Checks.Select(c => $"{c.Name}={c.Status}"))}");
        Assert.IsTrue(health.Checks.Any(static c => c.Name == "binary_version"), "health_report must include binary_version.");
        Assert.IsTrue(driver.GetDiagnostics().Contains("permission_mode=standard", StringComparison.Ordinal), "Diagnostics must expose the effective permission mode.");
    }

    [TestMethod]
    public async Task DiscoversApplicationsAndWindowsAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        _ = await driver.InitializeAsync(CancellationToken.None);

        IReadOnlyList<ApplicationInfo> apps = await driver.ListApplicationsAsync(CancellationToken.None);
        Assert.IsTrue(apps.Count > 0, "list_apps must return installed/running applications on an interactive Windows session.");

        IReadOnlyList<WindowInfo> windows = await driver.ListWindowsAsync(cancellationToken: CancellationToken.None);
        Assert.IsTrue(windows.Count > 0, "list_windows must return top-level windows on an interactive Windows session.");

        CursorPosition cursor = await driver.GetCursorPositionAsync(CancellationToken.None);
        Assert.IsTrue(cursor.X >= 0 && cursor.Y >= 0);
    }

    [TestMethod]
    public async Task MissingDriverBinaryIsDetectedBeforeDispatch()
    {
        string exe = Path.Combine(Path.GetTempPath(), "compuse-missing-cua-driver", "cua-driver.exe");
        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe));

        _ = await Assert.ThrowsAsync<DriverUnavailableException>(
            () => driver.InitializeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task DesktopStateIsACaptureWithDisplayMetadata()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        _ = await driver.InitializeAsync(CancellationToken.None);

        DesktopState state = await driver.GetDesktopStateAsync(CancellationToken.None);

        Assert.IsNotNull(state.Screenshot, "get_desktop_state must return a desktop screenshot.");
        Assert.IsNotNull(state.Screenshot!.PngData);
        Assert.AreEqual("primary", state.Display);
        Assert.AreEqual("windows", state.Platform);
        Assert.IsTrue(state.ScreenWidth > 0 && state.ScreenHeight > 0, "get_desktop_state must report the true screen size.");
    }

    [TestMethod]
    public async Task ElementClickVerifyRoundTripOnCalculator()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        _ = await driver.InitializeAsync(CancellationToken.None);

        LaunchResult launched = await driver.LaunchApplicationAsync(new LaunchRequest(aumid: CalculatorAumid), CancellationToken.None);
        ulong windowId = launched.Windows.Count > 0 ? launched.Windows[0].WindowId : 0;
        if (windowId == 0)
        {
            // The launch can settle before a window materializes (transient, rare);
            // re-discover the window by pid before failing.
            await Task.Delay(1500);
            IReadOnlyList<WindowInfo> byPid = await driver.ListWindowsAsync(launched.Pid, cancellationToken: CancellationToken.None);
            windowId = byPid.Count > 0 ? byPid[0].WindowId : 0;
        }

        Assert.AreNotEqual(0UL, windowId, "launch_app must report a Calculator window.");
        await Task.Delay(2000);

        WindowStateSnapshot snapshot = await driver.GetWindowStateAsync(
            launched.Pid,
            windowId,
            new WindowStateRequest(maxElements: 100, maxDepth: 10),
            CancellationToken.None);
        Assert.IsNotNull(snapshot.SnapshotId, "get_window_state must report a snapshot id.");
        Assert.IsTrue(snapshot.Elements.Count > 0, "get_window_state must return elements.");

        AccessibilityElementInfo? display = snapshot.Elements.FirstOrDefault(e => e.Label is not null && e.Label.StartsWith("Display is", StringComparison.Ordinal));
        Assert.IsNotNull(display, "Calculator must expose its display text element.");
        string displayBefore = display!.Label ?? string.Empty;

        AccessibilityElementInfo? seven = snapshot.Elements.FirstOrDefault(e => e.Label == "Seven");
        Assert.IsNotNull(seven, "Calculator must expose the Seven button.");
        Assert.IsNotNull(seven!.ElementToken, "Calculator elements must carry element tokens.");

        DriverActionResult click = await driver.ClickAsync(
            new ClickRequest(launched.Pid, elementTarget: new ElementTarget(launched.Pid, windowId, seven.ElementIndex, elementToken: seven.ElementToken)),
            CancellationToken.None);
        Assert.AreEqual(DriverActionStatus.Succeeded, click.Status, click.Message);
        Assert.AreEqual(DriverDeliveryMode.Background, click.DeliveryMode, "A background element click must not switch to foreground.");
        Assert.AreEqual("accessibility", click.Route, "An element-token click must route through the accessibility channel.");

        await Task.Delay(700);
        WindowStateSnapshot after = await driver.GetWindowStateAsync(
            launched.Pid,
            windowId,
            new WindowStateRequest(maxElements: 100, maxDepth: 10),
            CancellationToken.None);
        AccessibilityElementInfo? displayAfter = after.Elements.FirstOrDefault(e => e.Label is not null && e.Label.StartsWith("Display is", StringComparison.Ordinal));
        Assert.IsNotNull(displayAfter);
        Assert.AreNotEqual(displayBefore, displayAfter!.Label, "The background click must change the displayed value.");
        Assert.IsTrue(displayAfter!.Label!.Contains('7'), $"The displayed value must contain the clicked digit; got '{displayAfter!.Label}'.");

        // A background accessibility click must not synthesize pointer movement or
        // steal the foreground. Measured tightly around the dispatch; the operator's
        // own desktop activity is tolerated with a small pointer allowance.
        // Use a token from the most recent snapshot: earlier tokens are stale by design.
        AccessibilityElementInfo? sevenFresh = after.Elements.FirstOrDefault(e => e.Label == "Seven");
        Assert.IsNotNull(sevenFresh, "Calculator must still expose the Seven button after the refresh.");
        CursorPosition pointerBefore = await driver.GetCursorPositionAsync(CancellationToken.None);
        nint foregroundBefore = GetForegroundWindow();
        DriverActionResult probe = await driver.ClickAsync(
            new ClickRequest(launched.Pid, elementTarget: new ElementTarget(launched.Pid, windowId, sevenFresh!.ElementIndex, elementToken: sevenFresh.ElementToken)),
            CancellationToken.None);
        CursorPosition pointerAfter = await driver.GetCursorPositionAsync(CancellationToken.None);
        nint foregroundAfter = GetForegroundWindow();
        Assert.AreEqual(DriverActionStatus.Succeeded, probe.Status, probe.Message);
        Assert.IsTrue(
            Math.Abs(pointerAfter.X - pointerBefore.X) <= 100 && Math.Abs(pointerAfter.Y - pointerBefore.Y) <= 100,
            $"A background click must not move the pointer more than incidental desktop activity; moved to ({pointerAfter.X},{pointerAfter.Y}) from ({pointerBefore.X},{pointerBefore.Y}).");
        Assert.AreEqual(foregroundBefore, foregroundAfter, "A background click must not change the foreground window.");

        VerifyStateResult verify = await driver.VerifyStateAsync(
            new VerifyRequest(
                launched.Pid,
                windowId,
                elementPredicates: [new VerifyElementPredicate(role: "Text", labelContains: "7", exists: true)]),
            CancellationToken.None);
        Assert.IsTrue(verify.IsSatisfied, $"verify_state must confirm the independent read-back; verdict was '{verify.Verdict}'.");
    }

    [TestMethod]
    public async Task StaleSnapshotIsRefusedBeforeDispatchAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        _ = await driver.InitializeAsync(CancellationToken.None);

        LaunchResult launched = await driver.LaunchApplicationAsync(new LaunchRequest(aumid: CalculatorAumid), CancellationToken.None);
        ulong windowId = launched.Windows.Count > 0 ? launched.Windows[0].WindowId : 0;
        if (windowId == 0)
        {
            await Task.Delay(1500);
            IReadOnlyList<WindowInfo> byPid = await driver.ListWindowsAsync(launched.Pid, cancellationToken: CancellationToken.None);
            windowId = byPid.Count > 0 ? byPid[0].WindowId : 0;
        }

        Assert.AreNotEqual(0UL, windowId, "launch_app must report a Calculator window.");
        await Task.Delay(1500);

        // A fabricated snapshot id is stale by definition; the driver must refuse it.
        DriverActionResult stale = await driver.ClickAsync(
            new ClickRequest(
                launched.Pid,
                elementTarget: new ElementTarget(launched.Pid, windowId, elementIndex: 26, snapshotId: "sdeadbeef")),
            CancellationToken.None);
        Assert.AreEqual(DriverActionStatus.Refused, stale.Status, $"A stale snapshot must refuse; message was '{stale.Message}'.");
        Assert.IsFalse(stale.DispatchMayHaveOccurred);
    }

    [TestMethod]
    public async Task BackgroundUnavailableDragIsRefusedAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        _ = await driver.InitializeAsync(CancellationToken.None);

        LaunchResult launched = await driver.LaunchApplicationAsync(new LaunchRequest(aumid: CalculatorAumid), CancellationToken.None);
        ulong windowId = launched.Windows.Count > 0 ? launched.Windows[0].WindowId : 0;
        if (windowId == 0)
        {
            await Task.Delay(1500);
            IReadOnlyList<WindowInfo> byPid = await driver.ListWindowsAsync(launched.Pid, cancellationToken: CancellationToken.None);
            windowId = byPid.Count > 0 ? byPid[0].WindowId : 0;
        }

        Assert.AreNotEqual(0UL, windowId, "launch_app must report a Calculator window.");
        await Task.Delay(1500);

        // A drag that starts on the caption/title bar cannot be delivered in the
        // background; the driver must refuse rather than silently escalating.
        DriverActionResult drag = await driver.DragAsync(
            new DragRequest(launched.Pid, windowId, fromX: 20, fromY: 20, toX: 100, toY: 100),
            CancellationToken.None);
        Assert.AreEqual(DriverActionStatus.Refused, drag.Status, $"A title-bar drag must refuse in the background; message was '{drag.Message}'.");
        Assert.AreEqual("background_unavailable", drag.Code);
        Assert.IsFalse(drag.DispatchMayHaveOccurred);
    }

    [TestMethod]
    public async Task CancellationLeavesNoPendingWorkAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        _ = await driver.InitializeAsync(CancellationToken.None);

        using CancellationTokenSource cts = new();
        cts.Cancel();
        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => driver.GetCursorPositionAsync(cts.Token));

        // The connection must remain usable after cancellation.
        CursorPosition cursor = await driver.GetCursorPositionAsync(CancellationToken.None);
        Assert.IsTrue(cursor.X >= 0 && cursor.Y >= 0);
    }

    [TestMethod]
    public async Task DisposableNotepadElementFlowAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        _ = await driver.InitializeAsync(CancellationToken.None);

        // Safety invariant: packaged Notepad launches with a file argument reuse an
        // existing Notepad window and REPLACE its document. Never run against an
        // already-open Notepad window (for example a user's own document).
        IReadOnlyList<WindowInfo> existingNotepad = await FindNotepadWindowsAsync(driver, CancellationToken.None);
        if (existingNotepad.Count > 0)
        {
            Assert.Inconclusive(
                $"Refusing to launch Notepad while {existingNotepad.Count} Notepad window(s) are open; a file launch would replace an existing document.");
        }

        // Disposable document; never the user's own Notepad window.
        string marker = "compuse-qual-" + Guid.NewGuid().ToString("N")[..8];
        string sourcePath = Path.Combine(Path.GetTempPath(), marker + ".txt");
        File.WriteAllText(sourcePath, "compuse disposable seed", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        nint? disposableHandle = null;
        try
        {
            LaunchResult launched = await driver.LaunchApplicationAsync(
                new LaunchRequest(aumid: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", additionalArguments: [sourcePath]),
                CancellationToken.None);
            await Task.Delay(2500);

            // Notepad launches route into the existing packaged process; identify
            // the marker window by its unique title and confirm it is genuinely new.
            IReadOnlyList<WindowInfo> notepadWindows = await FindNotepadWindowsAsync(driver, CancellationToken.None);
            WindowInfo? disposable = notepadWindows.FirstOrDefault(w => w.Title.Contains(marker, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(disposable, "The disposable Notepad window must be discoverable by its unique title.");
            Assert.AreNotEqual(
                existingNotepad.Count,
                notepadWindows.Count,
                "The launch must produce a new window, never replace an existing Notepad window.");

            ulong windowId = disposable!.WindowId;
            disposableHandle = (nint)(long)windowId;
            uint ownerPid = await FindWindowOwnerPidAsync(driver, windowId, CancellationToken.None);
            Assert.AreNotEqual(0u, ownerPid, "The disposable window must belong to a live process.");

            WindowStateSnapshot snapshot = await driver.GetWindowStateAsync(
                ownerPid,
                windowId,
                new WindowStateRequest(maxElements: 400, maxDepth: 14),
                CancellationToken.None);
            Assert.IsNotNull(snapshot.Screenshot, "Notepad must produce a window screenshot.");
            AccessibilityElementInfo? edit = snapshot.Elements.FirstOrDefault(e => e.Actions.Contains("set_value", StringComparer.Ordinal));
            Assert.IsNotNull(edit, "Notepad must expose its document edit with a set_value action.");
            Assert.IsNotNull(edit!.ElementToken, "The Notepad edit must carry an element token.");

            string expected = "disposable content " + marker;
            DriverActionResult setValue = await driver.SetValueAsync(
                new ElementTarget(ownerPid, windowId, edit.ElementIndex, elementToken: edit.ElementToken),
                expected,
                CancellationToken.None);
            Assert.AreEqual(DriverActionStatus.Succeeded, setValue.Status, setValue.Message);

            await Task.Delay(500);
            WindowStateSnapshot after = await driver.GetWindowStateAsync(
                ownerPid,
                windowId,
                new WindowStateRequest(maxElements: 400, maxDepth: 14),
                CancellationToken.None);
            AccessibilityElementInfo? editAfter = after.Elements.FirstOrDefault(e => e.Actions.Contains("set_value", StringComparer.Ordinal));
            Assert.IsNotNull(editAfter);
            Assert.AreEqual(expected, editAfter!.Value, "The background set_value must be readable back from the document edit.");
        }
        finally
        {
            if (disposableHandle is not null)
            {
                _ = PostMessage(disposableHandle.Value, WmClose, nint.Zero, nint.Zero);
                await Task.Delay(500);
            }

            try
            {
                File.Delete(sourcePath);
            }
            catch (Exception)
            {
            }
        }
    }

    private static async Task<IReadOnlyList<WindowInfo>> FindNotepadWindowsAsync(WindowsComputerDriver driver, CancellationToken cancellationToken)
    {
        IReadOnlyList<WindowInfo> all = await driver.ListWindowsAsync(cancellationToken: cancellationToken);
        return all.Where(w => w.Title.Contains("Notepad", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static async Task<uint> FindWindowOwnerPidAsync(WindowsComputerDriver driver, ulong windowId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApplicationInfo> apps = await driver.ListApplicationsAsync(cancellationToken);
        foreach (ApplicationInfo app in apps)
        {
            if (!app.Running)
            {
                continue;
            }

            IReadOnlyList<WindowInfo> windows = await driver.ListWindowsAsync(app.Pid, cancellationToken: cancellationToken);
            if (windows.Any(w => w.WindowId == windowId))
            {
                return app.Pid;
            }
        }

        return 0;
    }

    [TestMethod]
    public async Task ClassicWin32DiscoveryAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        await using WindowsComputerDriver driver = new(new DriverConnectionOptions(exe!));
        _ = await driver.InitializeAsync(CancellationToken.None);

        IReadOnlyList<ApplicationInfo> apps = await driver.ListApplicationsAsync(CancellationToken.None);
        ApplicationInfo? explorer = apps.FirstOrDefault(a => a.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(explorer, "File Explorer must be discoverable as a running desktop application.");

        IReadOnlyList<WindowInfo> windows = await driver.ListWindowsAsync(explorer!.Pid, cancellationToken: CancellationToken.None);
        Assert.IsTrue(windows.Count > 0, "File Explorer must own at least one top-level window.");
        Assert.IsTrue(windows[0].Title.Length > 0, "Explorer windows carry titles.");

        // Observation only: no input is dispatched to the user's Explorer windows.
        CursorPosition before = await driver.GetCursorPositionAsync(CancellationToken.None);
        Assert.IsTrue(before.X >= 0 && before.Y >= 0);
    }

    [TestMethod]
    public async Task BoundedModeDeniesToolsOutsideTheManifest()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        string manifest = WriteBoundedManifest();
        DriverConnectionOptions options = new(exe!)
        {
            PermissionMode = DriverPermissionMode.Bounded,
            CapabilityManifestFile = manifest
        };
        await using WindowsComputerDriver driver = new(options);
        _ = await driver.InitializeAsync(CancellationToken.None);
        Assert.AreEqual("bounded", driver.EffectivePermissionMode);
        Assert.IsTrue(driver.GetDiagnostics().Contains("permission_mode=bounded", StringComparison.Ordinal));

        DriverActionResult result = await driver.ClickAsync(new ClickRequest(pid: 1, x: 0, y: 0), CancellationToken.None);
        Assert.AreEqual(DriverActionStatus.Refused, result.Status, "A tool outside the manifest must be refused by the driver.");
        Assert.AreEqual("driver_permission_denied", result.Code);
    }

    [TestMethod]
    public async Task BrowserDisposableProfileFlowAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        DriverConnectionOptions options = new(exe!)
        {
            BrowserSessionLabel = "compuse-int-browser"
        };
        await using WindowsComputerDriver driver = new(options);
        _ = await driver.InitializeAsync(CancellationToken.None);

        // Driver-owned disposable profile; never an existing signed-in profile.
        BrowserPrepareResult prepared = await driver.PrepareBrowserAsync(
            new BrowserPrepareRequest(allowLaunch: true, isolatedProfile: true),
            CancellationToken.None);
        Assert.IsTrue(prepared.Prepared, prepared.Message);
        Assert.IsTrue(prepared.PreparedPid > 0, "browser_prepare must report the isolated browser pid.");
        await Task.Delay(2000);

        IReadOnlyList<WindowInfo> windows = await driver.ListWindowsAsync(prepared.PreparedPid > 0 ? (uint)prepared.PreparedPid : null, cancellationToken: CancellationToken.None);
        WindowInfo? browserWindow = windows.FirstOrDefault(w => w.Pid == (uint)prepared.PreparedPid);
        Assert.IsNotNull(browserWindow, "The disposable browser must own a top-level window.");

        BrowserBinding binding = await driver.BindBrowserAsync(
            new BrowserBindRequest((uint)prepared.PreparedPid, browserWindow!.WindowId),
            CancellationToken.None);
        Assert.AreEqual("exact", binding.BindingQuality, "An owned isolated browser must bind exactly.");
        Assert.IsTrue(binding.Tabs.Count > 0, "The disposable browser must expose at least one tab.");
        BrowserTab tab = binding.Tabs[0];

        DriverActionResult navigate = await driver.NavigateBrowserAsync(
            new BrowserNavigateRequest(new BrowserTabTarget(binding.TargetId, tab.TabId), "https://example.com"),
            CancellationToken.None);
        Assert.AreEqual(DriverActionStatus.Succeeded, navigate.Status, navigate.Message);
        await Task.Delay(2000);

        BrowserSnapshot snapshot = await driver.SnapshotBrowserAsync(
            new BrowserTabTarget(binding.TargetId, tab.TabId),
            cancellationToken: CancellationToken.None);
        Assert.AreEqual("https://example.com/", snapshot.Url);
        Assert.IsTrue(snapshot.Refs.Count > 0, "The example.com page must expose page refs.");

        // A stale ref from a previous snapshot must refuse, not dispatch.
        DriverActionResult stale = await driver.NavigateBrowserAsync(
            new BrowserNavigateRequest(new BrowserTabTarget(binding.TargetId, tab.TabId), "https://example.com/2"),
            CancellationToken.None);
        Assert.AreEqual(DriverActionStatus.Succeeded, stale.Status, stale.Message);
        DriverActionResult staleClick = await driver.BrowserClickAsync(
            new BrowserClickRequest(new BrowserTabTarget(binding.TargetId, tab.TabId), reference: snapshot.Refs[0].Reference),
            CancellationToken.None);
        Assert.AreEqual(
            DriverActionStatus.Refused,
            staleClick.Status,
            $"Stale page refs must refuse; message was '{staleClick.Message}' code='{staleClick.Code}' effect='{staleClick.Effect}'.");
        Assert.AreEqual("browser_input_trust_unavailable", staleClick.Code);
        Assert.IsFalse(staleClick.DispatchMayHaveOccurred);
    }

    [TestMethod]
    public async Task RecordingStartQueryStopAgainstRealDriver()
    {
        string? exe = DriverPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            Assert.Inconclusive("Set COMPUSE_CUA_DRIVER_EXE to the pinned cua-driver.exe path to run driver integration tests.");
        }

        string output = Path.Combine(Path.GetTempPath(), "compuse-int-recording", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(output);
        DriverConnectionOptions options = new(exe!)
        {
            ApprovedRecordingRoot = Path.GetTempPath()
        };
        await using WindowsComputerDriver driver = new(options);
        _ = await driver.InitializeAsync(CancellationToken.None);

        RecordingState started = await driver.StartRecordingAsync(output, cancellationToken: CancellationToken.None);
        Assert.IsTrue(started.Recording, "start_recording must enable recording.");
        Assert.AreEqual(output, started.OutputDirectory);

        RecordingState queried = await driver.GetRecordingStateAsync(CancellationToken.None);
        Assert.IsTrue(queried.Recording);

        RecordingState stopped = await driver.StopRecordingAsync(CancellationToken.None);
        Assert.IsFalse(stopped.Recording, "stop_recording must disable recording.");
        Assert.IsNull(stopped.OutputDirectory);
    }

    private static string WriteBoundedManifest()
    {
        string dir = Path.Combine(Path.GetTempPath(), "compuse-int-manifest");
        _ = Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "capabilities.yaml");
        string content = """
            version: 3
            expires_after: 8h
            idle_timeout: 30m

            allow:
              tools:
                - health_report
                - list_apps
                - list_windows
                - get_window_state
                - get_cursor_position

            resources:
              desktop:
                display: false
            """;
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
