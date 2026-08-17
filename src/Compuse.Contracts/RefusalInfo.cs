namespace Compuse.Contracts;

public sealed record RefusalInfo
{
    public RefusalInfo(string code, string message)
    {
        Code = ContractValidation.RequireCode(code, nameof(code));
        Message = ContractValidation.RequireMessage(message, nameof(message));
    }

    public string Code { get; }

    public string Message { get; }
}
