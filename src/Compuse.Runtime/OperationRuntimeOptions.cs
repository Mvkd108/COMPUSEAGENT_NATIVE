namespace Compuse.Runtime;

public sealed class OperationRuntimeOptions
{
    private const int DefaultMaxConcurrentOperations = 32;
    private static readonly TimeSpan DefaultMaxExecution = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultShutdownDrain = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultCleanupTimeout = TimeSpan.FromSeconds(1);
    private const int MinConcurrentOperations = 1;
    private const int MaxAllowedConcurrentOperations = 1024;
    private static readonly TimeSpan MaxAllowedDuration = TimeSpan.FromHours(1);

    public OperationRuntimeOptions(
        int maxConcurrentOperations = DefaultMaxConcurrentOperations,
        TimeSpan? maxExecution = null,
        TimeSpan? shutdownDrain = null,
        TimeSpan? cleanupTimeout = null)
    {
        if (maxConcurrentOperations is < MinConcurrentOperations or > MaxAllowedConcurrentOperations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentOperations),
                maxConcurrentOperations,
                "Max concurrent operations must be between 1 and 1024.");
        }

        MaxConcurrentOperations = maxConcurrentOperations;
        MaxExecution = Resolve(maxExecution, DefaultMaxExecution, nameof(maxExecution));
        ShutdownDrain = Resolve(shutdownDrain, DefaultShutdownDrain, nameof(shutdownDrain));
        CleanupTimeout = Resolve(cleanupTimeout, DefaultCleanupTimeout, nameof(cleanupTimeout));
    }

    public int MaxConcurrentOperations { get; }

    public TimeSpan MaxExecution { get; }

    public TimeSpan ShutdownDrain { get; }

    public TimeSpan CleanupTimeout { get; }

    private static TimeSpan Resolve(TimeSpan? value, TimeSpan fallback, string paramName)
    {
        TimeSpan resolved = value ?? fallback;
        if (resolved <= TimeSpan.Zero || resolved > MaxAllowedDuration)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                resolved,
                "A runtime duration must be greater than zero and at most one hour.");
        }

        return resolved;
    }
}
