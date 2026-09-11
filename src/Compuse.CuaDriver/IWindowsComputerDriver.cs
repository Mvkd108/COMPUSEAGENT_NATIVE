namespace Compuse.CuaDriver;

public interface IWindowsComputerDriver : IAsyncDisposable
{
    public string ExecutablePath { get; }

    public string EffectivePermissionMode { get; }

    public DriverVersion? Version { get; }

    public bool IsReady { get; }

    public Task<McpInitializeResult> InitializeAsync(CancellationToken cancellationToken);

    public string GetDiagnostics();

    public Task<DriverHealthReport> GetHealthAsync(CancellationToken cancellationToken);

    public Task<IReadOnlyList<ApplicationInfo>> ListApplicationsAsync(CancellationToken cancellationToken);

    public Task<IReadOnlyList<WindowInfo>> ListWindowsAsync(
        uint? pid = null,
        bool onScreenOnly = false,
        CancellationToken cancellationToken = default);

    public Task<WindowStateSnapshot> GetWindowStateAsync(
        uint pid,
        ulong windowId,
        WindowStateRequest? request = null,
        CancellationToken cancellationToken = default);

    public Task<LaunchResult> LaunchApplicationAsync(LaunchRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> InvokeElementAsync(ElementTarget target, CancellationToken cancellationToken);

    public Task<DriverActionResult> SetValueAsync(ElementTarget target, string value, CancellationToken cancellationToken);

    public Task<DriverActionResult> TypeTextAsync(TypeTextRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> PressKeyAsync(KeyRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> HotkeyAsync(HotkeyRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> ScrollAsync(ScrollRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> ClickAsync(ClickRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> RightClickAsync(ClickRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> DoubleClickAsync(ClickRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> DragAsync(DragRequest request, CancellationToken cancellationToken);

    public Task<ScreenCaptureInfo> CaptureWindowAsync(
        uint pid,
        ulong windowId,
        int? maxDimension = null,
        CancellationToken cancellationToken = default);

    public Task<DriverActionResult> ClipboardWriteAsync(string text, CancellationToken cancellationToken);

    public Task<string> ClipboardReadAsync(CancellationToken cancellationToken);

    public Task<DriverActionResult> BringToFrontAsync(uint pid, ulong windowId, CancellationToken cancellationToken);

    public Task<CursorPosition> GetCursorPositionAsync(CancellationToken cancellationToken);

    public Task<DesktopState> GetDesktopStateAsync(CancellationToken cancellationToken);

    public Task<VerifyStateResult> VerifyStateAsync(VerifyRequest request, CancellationToken cancellationToken);

    public Task<BrowserPrepareResult> PrepareBrowserAsync(BrowserPrepareRequest request, CancellationToken cancellationToken);

    public Task<BrowserBinding> BindBrowserAsync(BrowserBindRequest request, CancellationToken cancellationToken);

    public Task<BrowserSnapshot> SnapshotBrowserAsync(
        BrowserTabTarget target,
        bool includeScreenshot = false,
        string? query = null,
        CancellationToken cancellationToken = default);

    public Task<DriverActionResult> NavigateBrowserAsync(BrowserNavigateRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> BrowserClickAsync(BrowserClickRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> BrowserTypeAsync(BrowserTypeRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> BrowserScrollAsync(BrowserScrollRequest request, CancellationToken cancellationToken);

    public Task<DriverActionResult> BrowserSetInputFilesAsync(BrowserSetInputFilesRequest request, CancellationToken cancellationToken);

    public Task<RecordingState> StartRecordingAsync(string outputDirectory, bool recordVideo = false, CancellationToken cancellationToken = default);

    public Task<RecordingState> GetRecordingStateAsync(CancellationToken cancellationToken);

    public Task<RecordingState> StopRecordingAsync(CancellationToken cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken);
}
