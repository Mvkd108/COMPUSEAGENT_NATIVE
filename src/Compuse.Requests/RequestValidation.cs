namespace Compuse.Requests;

internal static class RequestValidation
{
    internal static string? RequireOptionalWindowText(string? value, string paramName)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length is 0 or > DropFilesRequestLimits.MaxWindowTextLength
            || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A window class or title must be 1 to 1024 characters and must not be whitespace-only when provided.",
                paramName);
        }

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current is < ' ' or '\u007f')
            {
                throw new ArgumentException(
                    "A window class or title must be 1 to 1024 characters and must not be whitespace-only when provided.",
                    paramName);
            }
        }

        return value;
    }
}
