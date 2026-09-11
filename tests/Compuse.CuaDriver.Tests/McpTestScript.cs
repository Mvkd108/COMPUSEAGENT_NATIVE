using System.Text;
using System.Text.Json;

namespace Compuse.CuaDriver.Tests;

internal static class McpTestScript
{
    public static byte[] RespondTo(byte[] request, string resultJson, bool isError = false)
    {
        using JsonDocument document = JsonDocument.Parse(request);
        JsonElement root = document.RootElement;
        JsonElement id = root.GetProperty("id");
        string envelope = isError
            ? $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{{\"content\":[{{\"type\":\"text\",\"text\":{JsonSerializer.Serialize(resultJson)}}}],\"isError\":true}}}}"
            : $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";
        return Encoding.UTF8.GetBytes(envelope);
    }

    public static byte[] ErrorResponse(byte[] request, int code, string message)
    {
        using JsonDocument document = JsonDocument.Parse(request);
        JsonElement id = document.RootElement.GetProperty("id");
        string envelope = $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":{code},\"message\":{JsonSerializer.Serialize(message)}}}}}";
        return Encoding.UTF8.GetBytes(envelope);
    }

    public static string InitializeResult(string version = "0.27.0", string protocolVersion = "2025-06-18") =>
        $"{{\"capabilities\":{{\"tools\":{{}}}},\"protocolVersion\":\"{protocolVersion}\",\"serverInfo\":{{\"name\":\"cua-driver\",\"version\":\"{version}\"}}}}";

    public static string? GetMethod(byte[] request)
    {
        using JsonDocument document = JsonDocument.Parse(request);
        JsonElement root = document.RootElement;
        return root.TryGetProperty("method", out JsonElement method) ? method.GetString() : null;
    }

    public static string? GetParamsString(byte[] request, string path)
    {
        using JsonDocument document = JsonDocument.Parse(request);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("params", out JsonElement parameters))
        {
            return null;
        }

        if (!parameters.TryGetProperty(path, out JsonElement value))
        {
            return null;
        }

        return value.ToString();
    }

    public static bool WrittenLineContains(IEnumerable<byte[]> lines, string needle) =>
        lines.Any(line => Encoding.UTF8.GetString(line).Contains(needle, StringComparison.Ordinal));
}

