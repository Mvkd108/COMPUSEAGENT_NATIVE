using System.Text;
using System.Text.Json;

namespace Compuse.CuaDriver;

internal static class McpWire
{
    private static readonly JsonSerializerOptions RequestJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] EncodeRequest(long id, string method, object? parameters)
    {
        object envelope = parameters is null
            ? new { jsonrpc = "2.0", id, method }
            : new { jsonrpc = "2.0", id, method, Params = parameters };
        string json = JsonSerializer.Serialize(envelope, RequestJson);
        return Encoding.UTF8.GetBytes(json);
    }

    public static byte[] EncodeNotification(string method, object? parameters)
    {
        object envelope = parameters is null
            ? new { jsonrpc = "2.0", method }
            : new { jsonrpc = "2.0", method, Params = parameters };
        string json = JsonSerializer.Serialize(envelope, RequestJson);
        return Encoding.UTF8.GetBytes(json);
    }

    public static JsonDocument ParseMessage(byte[] line)
    {
        try
        {
            return JsonDocument.Parse(line, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                MaxDepth = 64
            });
        }
        catch (JsonException ex)
        {
            throw new DriverProtocolException($"The driver sent a malformed JSON-RPC message: {ex.Message}", ex);
        }
    }
}

