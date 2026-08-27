namespace Compuse.Runtime;

public static class RuntimeOutcomeCode
{
    public const string Cancelled = "cancelled";
    public const string DeadlineExpired = "deadline_expired";
    public const string DuplicateCorrelation = "duplicate_correlation";
    public const string HandlerCorrelationMismatch = "handler_correlation_mismatch";
    public const string HandlerFault = "handler_fault";
    public const string HandlerNullResult = "handler_null_result";
    public const string RuntimeBusy = "runtime_busy";
    public const string RuntimeStopping = "runtime_stopping";
    public const string ShutdownInterrupted = "shutdown_interrupted";
    public const string UnsupportedRequest = "unsupported_request";
}
