namespace Compuse.CuaDriver;

public enum DriverPermissionMode
{
    Standard = 0,
    Bounded = 1,
}

public sealed class DriverConnectionOptions
{
    public const string DefaultMcpProtocolVersion = "2025-06-18";
    public const int DefaultMaxMessageBytes = 16 * 1024 * 1024;
    public const int DefaultMaxDiagnosticBytes = 256 * 1024;
    public const long DefaultMaxRequestId = 1_000_000;
    public const int MinimumMaxMessageBytes = 256;
    public const int MinimumMaxDiagnosticBytes = 1024;

    private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DefaultStopGrace = TimeSpan.FromSeconds(3);

    public DriverConnectionOptions(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ExecutablePath = executablePath;
    }

    public string ExecutablePath { get; }

    public string ProtocolVersion { get; set; } = DefaultMcpProtocolVersion;

    public string[] ServerArguments { get; set; } = ["mcp"];

    public int MaxMessageBytes { get; set; } = DefaultMaxMessageBytes;

    public int MaxDiagnosticBytes { get; set; } = DefaultMaxDiagnosticBytes;

    public TimeSpan StartTimeout { get; set; } = DefaultStartTimeout;

    public TimeSpan StopGracePeriod { get; set; } = DefaultStopGrace;

    public long MaxRequestId { get; set; } = DefaultMaxRequestId;

    public DriverPermissionMode PermissionMode { get; set; } = DriverPermissionMode.Standard;

    public string? CapabilityManifestFile { get; set; }

    /// <summary>
    /// The capability manifest resolved to an absolute, existing path. Validated by
    /// <see cref="WindowsComputerDriver.InitializeAsync"/> before the child starts.
    /// </summary>
    public string? ResolvedCapabilityManifestPath { get; internal set; }

    public bool GrantExistingBrowserProfile { get; set; }

    /// <summary>
    /// Explicitly approved root directory. When set, <c>start_recording</c> output must
    /// resolve under this root; recording is refused otherwise.
    /// </summary>
    public string? ApprovedRecordingRoot { get; set; }

    /// <summary>
    /// Explicit session label required by the driver for browser operations. Browser
    /// methods refuse when neither this nor the request carries a label.
    /// </summary>
    public string? BrowserSessionLabel { get; set; }

    internal string EffectivePermissionModeDescription
    {
        get
        {
            if (PermissionMode == DriverPermissionMode.Bounded)
            {
                return "bounded";
            }

            return CapabilityManifestFile is null ? "standard" : "standard(manifest)";
        }
    }
}
