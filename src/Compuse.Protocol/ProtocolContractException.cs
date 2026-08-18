namespace Compuse.Protocol;

public sealed class ProtocolContractException : Exception
{
    internal ProtocolContractException(
        ProtocolContractErrorCode code,
        string fieldPath,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "A protocol contract error code must be a defined nonzero value.");
        }

        ArgumentException.ThrowIfNullOrEmpty(fieldPath);

        Code = code;
        FieldPath = fieldPath;
    }

    public ProtocolContractErrorCode Code { get; }

    public string FieldPath { get; }
}
