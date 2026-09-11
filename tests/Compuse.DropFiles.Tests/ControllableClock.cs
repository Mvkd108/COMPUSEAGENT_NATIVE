using Compuse.Runtime;

namespace Compuse.DropFiles.Tests;

internal sealed class ControllableClock : IOperationClock
{
    private readonly object _gate = new();
    private readonly List<Waiter> _waiters = [];
    private TaskCompletionSource? _waiterAdded;
    private DateTimeOffset _utcNow;

    internal ControllableClock(DateTimeOffset utcNow)
    {
        if (utcNow.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Request deadlines must use a UTC offset of zero and are not converted.",
                nameof(utcNow));
        }

        _utcNow = utcNow;
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    public ValueTask Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested)
        {
            _ = completion.TrySetCanceled(cancellationToken);
            return new ValueTask(completion.Task);
        }

        Waiter waiter;
        lock (_gate)
        {
            DateTimeOffset dueUtc = _utcNow + delay;
            waiter = new Waiter(dueUtc, completion);
            if (cancellationToken.CanBeCanceled)
            {
                waiter.Registration = cancellationToken.Register(
                    () => OnCanceled(waiter, cancellationToken),
                    useSynchronizationContext: false);
            }

            _waiters.Add(waiter);
            _ = _waiterAdded?.TrySetResult();
            _waiterAdded = null;
        }

        return new ValueTask(completion.Task);
    }

    internal Task WaitForWaiter()
    {
        lock (_gate)
        {
            if (_waiters.Count > 0)
            {
                return Task.CompletedTask;
            }

            _waiterAdded ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _waiterAdded.Task;
        }
    }

    internal void Advance(TimeSpan delta)
    {
        DateTimeOffset utcNow = UtcNow + delta;
        List<Waiter> due = [];
        lock (_gate)
        {
            _utcNow = utcNow;
            for (int index = _waiters.Count - 1; index >= 0; index--)
            {
                if (_waiters[index].DueUtc <= _utcNow)
                {
                    due.Add(_waiters[index]);
                    _waiters.RemoveAt(index);
                }
            }
        }

        for (int index = 0; index < due.Count; index++)
        {
            due[index].Registration.Dispose();
            _ = due[index].Completion.TrySetResult();
        }
    }

    private void OnCanceled(Waiter waiter, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _ = _waiters.Remove(waiter);
        }

        waiter.Registration.Dispose();
        _ = waiter.Completion.TrySetCanceled(cancellationToken);
    }

    private sealed class Waiter
    {
        internal Waiter(DateTimeOffset dueUtc, TaskCompletionSource completion)
        {
            DueUtc = dueUtc;
            Completion = completion;
        }

        internal DateTimeOffset DueUtc { get; }

        internal TaskCompletionSource Completion { get; }

        internal CancellationTokenRegistration Registration { get; set; }
    }
}
