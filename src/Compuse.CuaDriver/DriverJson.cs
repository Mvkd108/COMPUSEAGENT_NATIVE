using System.Text.Json;

namespace Compuse.CuaDriver;

internal static class DriverJson
{
    public static string? GetString(JsonElement element, params string[] path)
    {
        JsonElement current = element;
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

    public static bool GetBool(JsonElement element, bool fallback, params string[] path)
    {
        JsonElement current = element;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.TryGetProperty(path[index], out JsonElement next))
            {
                return fallback;
            }

            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    public static int? GetInt(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.TryGetProperty(path[index], out JsonElement next))
            {
                return null;
            }

            current = next;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out int value))
        {
            return value;
        }

        return null;
    }

    public static uint? GetUInt(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.TryGetProperty(path[index], out JsonElement next))
            {
                return null;
            }

            current = next;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetUInt32(out uint value))
        {
            return value;
        }

        return null;
    }

    public static ulong? GetULong(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.TryGetProperty(path[index], out JsonElement next))
            {
                return null;
            }

            current = next;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetUInt64(out ulong value))
        {
            return value;
        }

        return null;
    }

    public static WindowBounds? GetBounds(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.TryGetProperty(path[index], out JsonElement next))
            {
                return null;
            }

            current = next;
        }

        int? x = GetInt(current, "x");
        int? y = GetInt(current, "y");
        int? width = GetInt(current, "width") ?? GetInt(current, "w");
        int? height = GetInt(current, "height") ?? GetInt(current, "h");
        if (x is null || y is null || width is null || height is null)
        {
            return null;
        }

        return new WindowBounds(x.Value, y.Value, width.Value, height.Value);
    }

    public static double? GetDouble(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.TryGetProperty(path[index], out JsonElement next))
            {
                return null;
            }

            current = next;
        }

        if (current.ValueKind == JsonValueKind.Number && current.TryGetDouble(out double value))
        {
            return value;
        }

        return null;
    }

    public static List<string> GetStringArray(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        for (int index = 0; index < path.Length; index++)
        {
            if (!current.TryGetProperty(path[index], out JsonElement next))
            {
                return [];
            }

            current = next;
        }

        List<string> values = [];
        if (current.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in current.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is string value)
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }
}
