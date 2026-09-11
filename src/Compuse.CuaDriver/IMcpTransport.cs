namespace Compuse.CuaDriver;

internal interface IMcpTransport : IAsyncDisposable
{
    public int ProcessId { get; }

    public Task<byte[]?> ReadLineAsync(CancellationToken cancellationToken);

    public ValueTask WriteLineAsync(ReadOnlyMemory<byte> line, CancellationToken cancellationToken);

    public Task WaitForExitAsync(CancellationToken cancellationToken);

    public void CloseInput();

    public string GetDiagnostics();
}

