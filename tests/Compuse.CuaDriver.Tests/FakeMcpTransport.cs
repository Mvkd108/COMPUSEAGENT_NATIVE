using System.Text.Json;
using System.Threading.Channels;

namespace Compuse.CuaDriver.Tests;

internal sealed class FakeMcpTransport : IMcpTransport
{
    private readonly Channel<byte[]> _toClient = Channel.CreateUnbounded<byte[]>();
    private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _closed;

    public FakeMcpTransport(Func<byte[], byte[]?>? requestHandler = null)
    {
        RequestHandler = requestHandler;
    }

    public int ProcessId => 9001;

    public List<byte[]> WrittenLines { get; } = [];

    public Func<byte[], byte[]?>? RequestHandler { get; set; }

    public bool InputClosed => Volatile.Read(ref _closed) != 0;

    public async Task<byte[]?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            byte[]? line = await _toClient.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return line;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ValueTask WriteLineAsync(ReadOnlyMemory<byte> line, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] copy = line.ToArray();
        WrittenLines.Add(copy);
        if (RequestHandler is not null && HasRequestId(copy))
        {
            byte[]? response = RequestHandler(copy);
            if (response is not null)
            {
                _ = _toClient.Writer.TryWrite(response);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static bool HasRequestId(byte[] line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.TryGetProperty("id", out _);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _exit.Task;
    }

    public void CloseInput() => Volatile.Write(ref _closed, 1);

    public string GetDiagnostics() => "fake-transport";

    public void EnqueueResponse(string json) => _ = _toClient.Writer.TryWrite(System.Text.Encoding.UTF8.GetBytes(json));

    public void SignalExit()
    {
        _ = _exit.TrySetResult();
        _ = _toClient.Writer.TryComplete();
    }

    public ValueTask DisposeAsync()
    {
        SignalExit();
        return ValueTask.CompletedTask;
    }
}

