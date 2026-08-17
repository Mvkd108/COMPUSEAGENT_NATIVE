namespace Compuse.Contracts;

internal static class ContractValidation
{
    internal const int CodeMinLength = 1;
    internal const int CodeMaxLength = 64;
    internal const int MessageMinLength = 1;
    internal const int MessageMaxLength = 1024;
    internal const int ArtifactReferenceMinLength = 1;
    internal const int ArtifactReferenceMaxLength = 2048;

    internal static string RequireCode(string code, string paramName)
    {
        ArgumentNullException.ThrowIfNull(code, paramName);

        if (code.Length is < CodeMinLength or > CodeMaxLength || !IsValidCode(code))
        {
            throw new ArgumentException(
                "A contract code must be 1 to 64 ASCII characters, start with a-z, and contain only a-z, 0-9, or underscores.",
                paramName);
        }

        return code;
    }

    internal static string RequireMessage(string message, string paramName)
    {
        ArgumentNullException.ThrowIfNull(message, paramName);

        if (message.Length is < MessageMinLength or > MessageMaxLength
            || string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException(
                "A contract message must be 1 to 1024 characters and must not be whitespace-only.",
                paramName);
        }

        return message;
    }

    internal static string? RequireArtifactReference(string? artifactReference, string paramName)
    {
        if (artifactReference is null)
        {
            return null;
        }

        if (artifactReference.Length is < ArtifactReferenceMinLength or > ArtifactReferenceMaxLength
            || string.IsNullOrWhiteSpace(artifactReference))
        {
            throw new ArgumentException(
                "An artifact reference must be 1 to 2048 characters and must not be whitespace-only when provided.",
                paramName);
        }

        return artifactReference;
    }

    private static bool IsValidCode(string code)
    {
        if (code[0] is < 'a' or > 'z')
        {
            return false;
        }

        for (int index = 1; index < code.Length; index++)
        {
            char current = code[index];
            if (current is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
