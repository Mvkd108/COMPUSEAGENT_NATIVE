namespace Compuse.Runtime;

public sealed class SystemOperationClock : IOperationClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public ValueTask Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        return new ValueTask(Task.Delay(delay, cancellationToken));
    }
}
