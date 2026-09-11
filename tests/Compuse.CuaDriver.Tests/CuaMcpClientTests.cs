using System.Text;

namespace Compuse.CuaDriver.Tests;

[TestClass]
public sealed class CuaMcpClientTests
{
    private static DriverConnectionOptions Options(string exe = "cua-driver.exe") =>
        new(exe);

    [TestMethod]
    public async Task InitializePerformsHandshakeAndSendsInitializedNotification()
    {
        FakeMcpTransport transport = new(request =>
            McpTestScript.GetMethod(request) == "initialize"
                ? McpTestScript.RespondTo(request, McpTestScript.InitializeResult("0.27.0"))
                : null);
        await using CuaMcpClient client = new(transport, Options());

        McpInitializeResult result = await client.InitializeAsync(CancellationToken.None);

        Assert.AreEqual("cua-driver", result.ServerName);
        Assert.AreEqual(DriverVersion.Parse("0.27.0"), result.Version);
        Assert.AreEqual("2025-06-18", result.ProtocolVersion);
        Assert.IsTrue(McpTestScript.WrittenLineContains(transport.WrittenLines, "notifications/initialized"));
    }

    [TestMethod]
    public async Task ToolCallCorrelatesResponsesAcrossConcurrentRequests()
    {
        FakeMcpTransport transport = new();
        await using CuaMcpClient client = new(transport, Options());
        byte[]? firstRequest = null;
        transport.RequestHandler = request =>
        {
            string method = McpTestScript.GetMethod(request) ?? string.Empty;
            if (method == "initialize")
            {
                return McpTestScript.RespondTo(request, McpTestScript.InitializeResult());
            }

            if (method == "tools/call")
            {
                string name = McpTestScript.GetParamsString(request, "name") ?? string.Empty;
                if (name == "first")
                {
                    // Hold the first response until the second request arrives.
                    firstRequest = request;
                    return null;
                }

                if (name == "second")
                {
                    // Respond out of order: enqueue the FIRST response, return the SECOND.
                    transport.EnqueueResponse(
                        Encoding.UTF8.GetString(McpTestScript.RespondTo(firstRequest!, "{\"content\":[{\"type\":\"text\",\"text\":\"first-ok\"}],\"structuredContent\":{\"value\":\"one\"}}")));
                    return McpTestScript.RespondTo(request, "{\"content\":[{\"type\":\"text\",\"text\":\"second-ok\"}],\"structuredContent\":{\"value\":\"two\"}}");
                }
            }

            return null;
        };

        _ = await client.InitializeAsync(CancellationToken.None);
        Task<McpToolResult> first = client.CallToolAsync("first", new { }, CancellationToken.None);
        Task<McpToolResult> second = client.CallToolAsync("second", new { }, CancellationToken.None);

        McpToolResult firstResult = await first;
        McpToolResult secondResult = await second;

        Assert.IsFalse(firstResult.IsError);
        Assert.IsFalse(secondResult.IsError);
        Assert.IsTrue(firstResult.CombinedText.Contains("first-ok", StringComparison.Ordinal));
        Assert.IsTrue(secondResult.CombinedText.Contains("second-ok", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MalformedJsonFaultsConnection()
    {
        FakeMcpTransport transport = new(request => McpTestScript.GetMethod(request) == "initialize"
            ? McpTestScript.RespondTo(request, McpTestScript.InitializeResult())
            : null);
        await using CuaMcpClient client = new(transport, Options());
        _ = await client.InitializeAsync(CancellationToken.None);

        Task<McpToolResult> pending = client.CallToolAsync("list_apps", new { }, CancellationToken.None);
        transport.EnqueueResponse("this is not json at all");

        _ = await Assert.ThrowsAsync<DriverDisconnectedException>(() => pending);
    }

    [TestMethod]
    public async Task JsonRpcErrorBecomesProtocolException()
    {
        FakeMcpTransport transport = new(request => McpTestScript.GetMethod(request) == "initialize"
            ? McpTestScript.RespondTo(request, McpTestScript.InitializeResult())
            : McpTestScript.ErrorResponse(request, -32601, "Method not found"));
        await using CuaMcpClient client = new(transport, Options());
        _ = await client.InitializeAsync(CancellationToken.None);

        Task<McpToolResult> pending = client.CallToolAsync("nope", new { }, CancellationToken.None);
        _ = await Assert.ThrowsAsync<DriverProtocolException>(() => pending);
    }

    [TestMethod]
    public async Task TransportExitFaultsPendingRequests()
    {
        FakeMcpTransport transport = new(request => McpTestScript.GetMethod(request) == "initialize"
            ? McpTestScript.RespondTo(request, McpTestScript.InitializeResult())
            : null);
        await using CuaMcpClient client = new(transport, Options());
        _ = await client.InitializeAsync(CancellationToken.None);

        Task<McpToolResult> pending = client.CallToolAsync("list_windows", new { }, CancellationToken.None);
        transport.SignalExit();

        _ = await Assert.ThrowsAsync<DriverDisconnectedException>(() => pending);
    }

    [TestMethod]
    public async Task CancellationSendsCancelledNotificationAndCompletesAsCanceled()
    {
        FakeMcpTransport transport = new(request => McpTestScript.GetMethod(request) == "initialize"
            ? McpTestScript.RespondTo(request, McpTestScript.InitializeResult())
            : null);
        await using CuaMcpClient client = new(transport, Options());
        _ = await client.InitializeAsync(CancellationToken.None);
        using CancellationTokenSource cts = new();

        Task<McpToolResult> pending = client.CallToolAsync("click", new { }, cts.Token);
        await Task.Yield();
        cts.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.IsTrue(McpTestScript.WrittenLineContains(transport.WrittenLines, "notifications/cancelled"));
    }

    [TestMethod]
    public async Task OversizedResponseLineFaultsConnection()
    {
        FakeMcpTransport transport = new(request => McpTestScript.GetMethod(request) == "initialize"
            ? McpTestScript.RespondTo(request, McpTestScript.InitializeResult())
            : null);
        DriverConnectionOptions options = Options();
        options.MaxMessageBytes = 128;
        await using CuaMcpClient client = new(transport, options);
        _ = await client.InitializeAsync(CancellationToken.None);

        Task<McpToolResult> pending = client.CallToolAsync("list_apps", new { }, CancellationToken.None);
        transport.EnqueueResponse(new string('a', 1024));

        _ = await Assert.ThrowsAsync<DriverDisconnectedException>(() => pending);
    }

    [TestMethod]
    public void McpClientAndWireTypesAreInternalToTheAdapter()
    {
        Assert.IsFalse(typeof(CuaMcpClient).IsPublic, "CuaMcpClient must stay behind the adapter.");
        Assert.IsFalse(typeof(McpWire).IsPublic, "MCP wire helpers must stay behind the adapter.");
        Assert.IsFalse(typeof(IMcpTransport).IsPublic, "The transport seam must stay behind the adapter.");
    }

    private static async Task WaitForWrittenCountAsync(FakeMcpTransport transport, int count)
    {
        while (transport.WrittenLines.Count < count)
        {
            await Task.Delay(10);
        }
    }
}

