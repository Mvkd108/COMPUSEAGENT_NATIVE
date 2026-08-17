namespace Compuse.Contracts;

public sealed record FailureInfo
{
    public FailureInfo(string code, string message, bool isTransient)
    {
        Code = ContractValidation.RequireCode(code, nameof(code));
        Message = ContractValidation.RequireMessage(message, nameof(message));
        IsTransient = isTransient;
    }

    public string Code { get; }

    public string Message { get; }

    public bool IsTransient { get; }
}
