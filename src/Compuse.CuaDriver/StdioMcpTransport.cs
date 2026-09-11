using System.Diagnostics;
using System.Text;

namespace Compuse.CuaDriver;

internal sealed class StdioMcpTransport : IMcpTransport
{
    private const int ReadChunkBytes = 8192;
    private static readonly byte[] Newline = [(byte)'\n'];

    private readonly Process _process;
    private readonly int _maxMessageBytes;
    private readonly int _maxDiagnosticBytes;
    private readonly StringBuilder _diagnostics = new();
    private readonly object _diagnosticGate = new();
    private readonly Task _stderrTask;
    private readonly List<byte> _buffer = [];
    private bool _closed;

    public StdioMcpTransport(DriverConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ProcessStartInfo startInfo = new()
        {
            FileName = options.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in options.ServerArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        ApplyPermissionConfiguration(startInfo, options);

        _process = new Process { StartInfo = startInfo };
        _maxMessageBytes = options.MaxMessageBytes;
        _maxDiagnosticBytes = options.MaxDiagnosticBytes;
        try
        {
            if (!_process.Start())
            {
                throw new DriverStartException($"The driver executable '{options.ExecutablePath}' could not be started.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new DriverStartException($"The driver executable '{options.ExecutablePath}' could not be started: {ex.Message}", ex);
        }

        _stderrTask = DrainStandardErrorAsync();
    }

    public int ProcessId => _process.Id;

    public async Task<byte[]?> ReadLineAsync(CancellationToken cancellationToken)
    {
        Stream stream = _process.StandardOutput.BaseStream;
        byte[] chunk = new byte[ReadChunkBytes];
        while (true)
        {
            int newline = FindNewline();
            if (newline >= 0)
            {
                return TakeLine(newline);
            }

            if (_buffer.Count > _maxMessageBytes)
            {
                throw OversizedFrame();
            }

            int read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_buffer.Count == 0)
                {
                    return null;
                }

                if (_buffer.Count > _maxMessageBytes)
                {
                    throw OversizedFrame();
                }

                // EOF after a final unterminated frame: return the remainder as the final line.
                byte[] final = _buffer.ToArray();
                _buffer.Clear();
                return final;
            }

            _buffer.AddRange(chunk.AsSpan(0, read));
        }
    }

    public ValueTask WriteLineAsync(ReadOnlyMemory<byte> line, CancellationToken cancellationToken)
    {
        Stream stream = _process.StandardInput.BaseStream;
        return WriteLineCoreAsync(stream, line, cancellationToken);
    }

    private static async ValueTask WriteLineCoreAsync(Stream stream, ReadOnlyMemory<byte> line, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(line, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(Newline, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void CloseInput()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        try
        {
            _process.StandardInput.Close();
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    public string GetDiagnostics()
    {
        lock (_diagnosticGate)
        {
            return _diagnostics.ToString();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        CloseInput();
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
        }

        _process.Dispose();
        _ = _stderrTask;
    }

    private static void ApplyPermissionConfiguration(ProcessStartInfo startInfo, DriverConnectionOptions options)
    {
        // Never inherit conflicting ambient Cua permission variables. The adapter owns
        // the effective permission configuration for the child process.
        foreach (string ambient in AmbientPermissionVariables)
        {
            _ = startInfo.Environment.Remove(ambient);
        }

        switch (options.PermissionMode)
        {
            case DriverPermissionMode.Bounded:
                startInfo.Environment["CUA_DRIVER_PERMISSION_MODE"] = "bounded";
                startInfo.Environment["CUA_DRIVER_CAPABILITY_MANIFEST_FILE"] = options.ResolvedCapabilityManifestPath!;
                startInfo.Environment["CUA_DRIVER_CAPABILITY_MANIFEST_APPROVED"] = "1";
                break;
            case DriverPermissionMode.Standard:
                if (options.CapabilityManifestFile is not null)
                {
                    startInfo.Environment["CUA_DRIVER_CAPABILITY_MANIFEST_FILE"] = options.ResolvedCapabilityManifestPath!;
                    startInfo.Environment["CUA_DRIVER_CAPABILITY_MANIFEST_APPROVED"] = "1";
                }

                break;
            default:
                throw new DriverStartException($"Unsupported driver permission mode '{options.PermissionMode}'.");
        }
    }

    private static readonly string[] AmbientPermissionVariables =
    [
        "CUA_DRIVER_PERMISSION_MODE",
        "CUA_DRIVER_CAPABILITY_MANIFEST_FILE",
        "CUA_DRIVER_CAPABILITY_MANIFEST_APPROVED",
        "CUA_DRIVER_SESSION_POLICY_FILE",
        "CUA_DRIVER_SESSION_POLICY_APPROVED",
        "CUA_DRIVER_DANGEROUSLY_BYPASS_APPROVALS",
    ];

    private int FindNewline()
    {
        for (int index = 0; index < _buffer.Count; index++)
        {
            if (_buffer[index] == (byte)'\n')
            {
                return index;
            }
        }

        return -1;
    }

    private byte[] TakeLine(int newline)
    {
        int length = newline;
        if (length > 0 && _buffer[length - 1] == (byte)'\r')
        {
            length--;
        }

        byte[] line = new byte[length];
        if (length > 0)
        {
            _buffer.CopyTo(0, line, 0, length);
        }

        _buffer.RemoveRange(0, newline + 1);
        return line;
    }

    private DriverProtocolException OversizedFrame() =>
        new($"A driver message exceeded the maximum size of {_maxMessageBytes} bytes.");

    private async Task DrainStandardErrorAsync()
    {
        try
        {
            while (true)
            {
                string? line = await _process.StandardError.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                AppendDiagnostic(line);
            }
        }
        catch (Exception)
        {
        }
    }

    private void AppendDiagnostic(string line)
    {
        lock (_diagnosticGate)
        {
            if (_diagnostics.Length + line.Length + 1 > _maxDiagnosticBytes)
            {
                _ = _diagnostics.Remove(0, Math.Max(0, _diagnostics.Length + line.Length + 1 - _maxDiagnosticBytes));
            }

            if (_diagnostics.Length > 0)
            {
                _ = _diagnostics.Append('\n');
            }

            _ = _diagnostics.Append(line);
        }
    }
}
