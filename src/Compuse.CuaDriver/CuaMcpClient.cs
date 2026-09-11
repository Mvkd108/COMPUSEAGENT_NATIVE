using System.Collections.Concurrent;
using System.Text.Json;

namespace Compuse.CuaDriver;

public sealed class McpInitializeResult
{
    public McpInitializeResult(string serverName, DriverVersion version, string protocolVersion, JsonElement capabilities)
    {
        ServerName = serverName;
        Version = version;
        ProtocolVersion = protocolVersion;
        Capabilities = capabilities;
    }

    public string ServerName { get; }

    public DriverVersion Version { get; }

    public string ProtocolVersion { get; }

    public JsonElement Capabilities { get; }
}

public sealed class McpImageContent
{
    public McpImageContent(byte[] data, string? mimeType)
    {
        Data = data;
        MimeType = mimeType;
    }

    public byte[] Data { get; }

    public string? MimeType { get; }
}

internal sealed class McpToolResult
{
    public McpToolResult(
        JsonElement structured,
        IReadOnlyList<string> textContent,
        IReadOnlyList<McpImageContent> imageContent,
        bool isError,
        string? errorText)
    {
        Structured = structured;
        TextContent = textContent;
        ImageContent = imageContent;
        IsError = isError;
        ErrorText = errorText;
    }

    public JsonElement Structured { get; }

    public IReadOnlyList<string> TextContent { get; }

    public IReadOnlyList<McpImageContent> ImageContent { get; }

    public bool IsError { get; }

    public string? ErrorText { get; }

    public string CombinedText => TextContent.Count == 0 ? string.Empty : string.Join(Environment.NewLine, TextContent);
}

internal sealed class CuaMcpClient : IAsyncDisposable
{
    private readonly IMcpTransport _transport;
    private readonly DriverConnectionOptions _options;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private long _nextId;
    private int _faulted;

    public CuaMcpClient(IMcpTransport transport, DriverConnectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        _transport = transport;
        _options = options;
        _ = RunReaderAsync();
    }

    public McpInitializeResult? InitializeResult { get; private set; }

    public string GetTransportDiagnostics() => _transport.GetDiagnostics();

    public async Task<McpInitializeResult> InitializeAsync(CancellationToken cancellationToken)
    {
        long id = NextId();
        object parameters = new
        {
            protocolVersion = _options.ProtocolVersion,
            capabilities = new { },
            clientInfo = new { name = "compuse", version = "0.1.0" }
        };
        byte[] request = McpWire.EncodeRequest(id, "initialize", parameters);
        JsonElement result = await SendAsync(id, request, cancellationToken).ConfigureAwait(false);

        string serverName = ReadString(result, "serverInfo", "name") ?? "cua-driver";
        string versionText = ReadString(result, "serverInfo", "version") ?? string.Empty;
        DriverVersion version;
        try
        {
            version = DriverVersion.Parse(versionText);
        }
        catch (FormatException ex)
        {
            throw new DriverIncompatibleException($"The driver reported an unparseable version '{versionText}': {ex.Message}");
        }

        string protocolVersion = ReadString(result, "protocolVersion") ?? string.Empty;
        JsonElement capabilities = result.TryGetProperty("capabilities", out JsonElement caps)
            ? caps.Clone()
            : default;

        await SendNotificationAsync("notifications/initialized", parameters: null, cancellationToken).ConfigureAwait(false);
        InitializeResult = new McpInitializeResult(serverName, version, protocolVersion, capabilities);
        return InitializeResult;
    }

    public async Task<McpToolResult> CallToolAsync(string toolName, object? arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        long id = NextId();
        object parameters = new { name = toolName, arguments = arguments ?? new { } };
        byte[] request = McpWire.EncodeRequest(id, "tools/call", parameters);
        JsonElement result = await SendAsync(id, request, cancellationToken).ConfigureAwait(false);

        bool isError = result.TryGetProperty("isError", out JsonElement isErrorElement)
            && isErrorElement.ValueKind == JsonValueKind.True;
        List<string> text = [];
        List<McpImageContent> images = [];
        string? errorText = null;
        if (result.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in content.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out JsonElement type))
                {
                    continue;
                }

                if (type.GetString() == "text" && item.TryGetProperty("text", out JsonElement textElement))
                {
                    string? value = textElement.GetString();
                    if (value is not null)
                    {
                        text.Add(value);
                    }
                }
                else if (type.GetString() == "image" && item.TryGetProperty("data", out JsonElement dataElement))
                {
                    string? base64 = dataElement.GetString();
                    if (base64 is not null)
                    {
                        try
                        {
                            images.Add(new McpImageContent(
                                Convert.FromBase64String(base64),
                                item.TryGetProperty("mimeType", out JsonElement mime) ? mime.GetString() : "image/png"));
                        }
                        catch (FormatException)
                        {
                        }
                    }
                }
            }
        }

        if (isError)
        {
            errorText = text.Count > 0 ? string.Join(Environment.NewLine, text) : "The driver reported an error.";
        }

        JsonElement structured = result.TryGetProperty("structuredContent", out JsonElement structuredElement)
            ? structuredElement.Clone()
            : default;
        return new McpToolResult(structured, text, images, isError, errorText);
    }

    public ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        return _transport.DisposeAsync();
    }

    private async Task<JsonElement> SendAsync(long id, byte[] request, CancellationToken cancellationToken)
    {
        TaskCompletionSource<JsonElement> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new DriverProtocolException($"Duplicate MCP request id {id}.");
        }

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        try
        {
            await _transport.WriteLineAsync(request, linked.Token).ConfigureAwait(false);
            using CancellationTokenRegistration registration = linked.Token.Register(
                static state =>
                {
                    (long requestId, TaskCompletionSource<JsonElement> pending) = ((long, TaskCompletionSource<JsonElement>))state!;
                    _ = pending.TrySetCanceled();
                },
                (id, completion));
            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SendCancellationNotificationAsync(id).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _ = _pending.TryRemove(id, out _);
        }
    }

    private async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        byte[] notification = McpWire.EncodeNotification(method, parameters);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _transport.WriteLineAsync(notification, linked.Token).ConfigureAwait(false);
    }

    private async Task SendCancellationNotificationAsync(long requestId)
    {
        try
        {
            object parameters = new { requestId, reason = "caller_cancelled" };
            byte[] notification = McpWire.EncodeNotification("notifications/cancelled", parameters);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            await _transport.WriteLineAsync(notification, linked.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private long NextId()
    {
        long id = Interlocked.Increment(ref _nextId);
        if (id > _options.MaxRequestId)
        {
            throw new DriverProtocolException(
                $"Exceeded the driver request-id limit of {_options.MaxRequestId} requests.");
        }

        return id;
    }

    private async Task RunReaderAsync()
    {
        CancellationToken lifetime = _lifetime.Token;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                byte[]? line = await _transport.ReadLineAsync(lifetime).ConfigureAwait(false);
                if (line is null)
                {
                    Fault(DriverFaultReason.Closed);
                    return;
                }

                if (lifetime.IsCancellationRequested)
                {
                    return;
                }

                using JsonDocument document = McpWire.ParseMessage(line);
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.Number)
                {
                    long id = idElement.GetInt64();
                    if (root.TryGetProperty("result", out JsonElement resultElement))
                    {
                        Complete(id, resultElement.Clone());
                    }
                    else if (root.TryGetProperty("error", out JsonElement errorElement))
                    {
                        string message = errorElement.TryGetProperty("message", out JsonElement messageElement)
                            ? messageElement.GetString() ?? "Unknown MCP error."
                            : "Unknown MCP error.";
                        CompleteError(id, message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Fault(DriverFaultReason.Protocol, ex);
        }
    }

    private void Complete(long id, JsonElement result)
    {
        if (_pending.TryGetValue(id, out TaskCompletionSource<JsonElement>? completion))
        {
            _ = completion.TrySetResult(result);
        }
    }

    private void CompleteError(long id, string message)
    {
        if (_pending.TryGetValue(id, out TaskCompletionSource<JsonElement>? completion))
        {
            _ = completion.TrySetException(new DriverProtocolException($"The driver returned a JSON-RPC error: {message}"));
        }
    }

    private void Fault(DriverFaultReason reason, Exception? inner = null)
    {
        if (Interlocked.Exchange(ref _faulted, 1) != 0)
        {
            return;
        }

        DriverDisconnectedException fault = reason == DriverFaultReason.Closed
            ? new DriverDisconnectedException("The driver process closed its standard output.")
            : new DriverDisconnectedException("The driver connection faulted.", inner ?? new InvalidOperationException("protocol fault"));
        foreach (TaskCompletionSource<JsonElement> pending in _pending.Values)
        {
            _ = pending.TrySetException(fault);
        }
    }

    private static string? ReadString(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.TryGetProperty(path[index], out JsonElement next))
            {
                return null;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private enum DriverFaultReason
    {
        Closed,
        Protocol,
    }
}

