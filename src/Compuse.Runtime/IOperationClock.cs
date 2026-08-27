namespace Compuse.Runtime;

public interface IOperationClock
{
    public DateTimeOffset UtcNow { get; }

    public ValueTask Delay(TimeSpan delay, CancellationToken cancellationToken);
}
