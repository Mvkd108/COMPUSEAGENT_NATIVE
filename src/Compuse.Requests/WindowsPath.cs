namespace Compuse.Requests;

internal static class WindowsPath
{
    private static readonly string[] ReservedDeviceNames =
    [
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    ];

    internal static string NormalizeAbsolute(string path, string paramName)
    {
        ArgumentNullException.ThrowIfNull(path, paramName);

        if (path.Length is 0 or > DropFilesRequestLimits.MaxPathLength)
        {
            throw CreateInvalid(paramName);
        }

        if (char.IsWhiteSpace(path[0]) || char.IsWhiteSpace(path[^1]))
        {
            throw CreateInvalid(paramName);
        }

        char[] buffer = new char[path.Length];
        for (int index = 0; index < path.Length; index++)
        {
            char current = path[index];
            if (current is < ' ' or '\u007f')
            {
                throw CreateInvalid(paramName);
            }

            buffer[index] = current == '/' ? '\\' : current;
        }

        ReadOnlySpan<char> normalized = buffer;
        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal)
            || normalized.StartsWith(@"\\.\", StringComparison.Ordinal)
            || normalized.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            throw CreateInvalid(paramName);
        }

        if (normalized.Length >= 3
            && IsAsciiLetter(normalized[0])
            && normalized[1] == ':'
            && normalized[2] == '\\')
        {
            return NormalizeDriveAbsolute(normalized, paramName);
        }

        if (normalized.StartsWith(@"\\", StringComparison.Ordinal)
            && !normalized.StartsWith(@"\\\", StringComparison.Ordinal))
        {
            return NormalizeUnc(normalized, paramName);
        }

        throw CreateInvalid(paramName);
    }

    internal static bool IsReservedDeviceName(string component)
    {
        ReadOnlySpan<char> stem = component;
        int dot = component.IndexOf('.');
        if (dot >= 0)
        {
            stem = component.AsSpan(0, dot);
        }

        for (int index = 0; index < ReservedDeviceNames.Length; index++)
        {
            if (stem.Equals(ReservedDeviceNames[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static void ValidateFileName(string name, string paramName)
    {
        ArgumentNullException.ThrowIfNull(name, paramName);

        if (name.Length is 0 or > DropFilesRequestLimits.MaxProcessImageNameLength
            || name == "."
            || name == ".."
            || char.IsWhiteSpace(name[0])
            || char.IsWhiteSpace(name[^1])
            || name[^1] is '.' or ' '
            || IsReservedDeviceName(name))
        {
            throw new ArgumentException(
                "A process image name must be a file name of 1 to 255 characters without a directory.",
                paramName);
        }

        for (int index = 0; index < name.Length; index++)
        {
            char current = name[index];
            if (current is < ' ' or '\u007f' || IsIllegalNameChar(current) || current is '\\' or '/')
            {
                throw new ArgumentException(
                    "A process image name must be a file name of 1 to 255 characters without a directory.",
                    paramName);
            }
        }
    }

    private static string NormalizeDriveAbsolute(ReadOnlySpan<char> path, string paramName)
    {
        List<string> components = SplitAndCollapse(path[3..], paramName, allowEmptyResult: true);
        string result = components.Count == 0
            ? $"{path[0]}:\\"
            : $"{path[0]}:\\{string.Join('\\', components)}";
        return Finish(result, paramName);
    }

    private static string NormalizeUnc(ReadOnlySpan<char> path, string paramName)
    {
        List<string> parts = SplitRaw(path[2..], paramName);
        if (parts.Count < 2)
        {
            throw CreateInvalid(paramName);
        }

        ValidateComponent(parts[0], paramName, allowDotSegments: false);
        ValidateComponent(parts[1], paramName, allowDotSegments: false);

        List<string> tail = Collapse(parts.Skip(2), paramName, allowEmptyResult: true);
        string result = $@"\\{parts[0]}\{parts[1]}";
        if (tail.Count > 0)
        {
            result = $@"{result}\{string.Join('\\', tail)}";
        }

        return Finish(result, paramName);
    }

    private static List<string> SplitAndCollapse(ReadOnlySpan<char> remainder, string paramName, bool allowEmptyResult) =>
        Collapse(SplitRaw(remainder, paramName), paramName, allowEmptyResult);

    private static List<string> SplitRaw(ReadOnlySpan<char> remainder, string paramName)
    {
        List<string> parts = [];
        if (remainder.Length == 0)
        {
            return parts;
        }

        int start = 0;
        for (int index = 0; index <= remainder.Length; index++)
        {
            if (index < remainder.Length && remainder[index] != '\\')
            {
                continue;
            }

            ReadOnlySpan<char> slice = remainder[start..index];
            if (slice.Length == 0)
            {
                if (index == remainder.Length)
                {
                    break;
                }

                throw CreateInvalid(paramName);
            }

            parts.Add(slice.ToString());
            start = index + 1;
        }

        return parts;
    }

    private static List<string> Collapse(IEnumerable<string> parts, string paramName, bool allowEmptyResult)
    {
        List<string> components = [];
        foreach (string part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (components.Count == 0)
                {
                    throw CreateInvalid(paramName);
                }

                components.RemoveAt(components.Count - 1);
                continue;
            }

            ValidateComponent(part, paramName, allowDotSegments: false);
            components.Add(part);
        }

        if (components.Count == 0 && !allowEmptyResult)
        {
            throw CreateInvalid(paramName);
        }

        return components;
    }

    private static void ValidateComponent(string component, string paramName, bool allowDotSegments)
    {
        if (!allowDotSegments && (component == "." || component == ".."))
        {
            throw CreateInvalid(paramName);
        }

        if (component.Length is 0 or > DropFilesRequestLimits.MaxComponentLength
            || component[^1] is '.' or ' '
            || char.IsWhiteSpace(component[0])
            || IsReservedDeviceName(component))
        {
            throw CreateInvalid(paramName);
        }

        for (int index = 0; index < component.Length; index++)
        {
            char current = component[index];
            if (current is < ' ' or '\u007f' || IsIllegalNameChar(current))
            {
                throw CreateInvalid(paramName);
            }
        }
    }

    private static string Finish(string path, string paramName)
    {
        if (path.Length is 0 or > DropFilesRequestLimits.MaxPathLength)
        {
            throw CreateInvalid(paramName);
        }

        return path;
    }

    private static bool IsIllegalNameChar(char current) =>
        current is '<' or '>' or ':' or '"' or '|' or '?' or '*' or '\\' or '/';

    private static bool IsAsciiLetter(char current) =>
        current is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static ArgumentException CreateInvalid(string paramName) =>
        new(
            "A Windows path must be absolute, lexically normalized, at most 32767 characters, and must not use device or relative prefixes.",
            paramName);
}
