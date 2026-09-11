using Compuse.Contracts;

namespace Compuse.Runtime;

public sealed class OperationExecutionContext
{
    private readonly object _gate = new();
    private readonly List<Func<CancellationToken, ValueTask>> _cleanups = [];
    private bool _handlerCompleted;
    private bool _cleanupStarted;

    internal OperationExecutionContext(
        CorrelationId correlationId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset effectiveDeadlineUtc,
        IOperationClock clock)
    {
        CorrelationId = correlationId;
        StartedAtUtc = startedAtUtc;
        EffectiveDeadlineUtc = effectiveDeadlineUtc;
        Clock = clock;
    }

    public CorrelationId CorrelationId { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset EffectiveDeadlineUtc { get; }

    public IOperationClock Clock { get; }

    public void RegisterCleanup(Func<CancellationToken, ValueTask> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);

        lock (_gate)
        {
            if (_handlerCompleted || _cleanupStarted)
            {
                throw new InvalidOperationException(
                    "Cleanup cannot be registered after the handler has completed.");
            }

            _cleanups.Add(cleanup);
        }
    }

    internal void MarkHandlerCompleted()
    {
        lock (_gate)
        {
            _handlerCompleted = true;
        }
    }

    internal Func<CancellationToken, ValueTask>[] BeginCleanup()
    {
        lock (_gate)
        {
            _cleanupStarted = true;
            Func<CancellationToken, ValueTask>[] snapshot = new Func<CancellationToken, ValueTask>[_cleanups.Count];
            for (int index = 0; index < _cleanups.Count; index++)
            {
                snapshot[index] = _cleanups[_cleanups.Count - 1 - index];
            }

            return snapshot;
        }
    }
}
