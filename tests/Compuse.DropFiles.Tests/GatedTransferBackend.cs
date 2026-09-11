using Compuse.Filesystem;
using Compuse.Routing;

namespace Compuse.DropFiles.Tests;

internal sealed class GatedTransferBackend : ITransferBackend
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource<TransferExecution> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal CancellationToken ObservedToken { get; private set; }

    public async ValueTask<TransferExecution> ExecuteAsync(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        _ = plan;
        ObservedToken = cancellationToken;
        _ = Entered.TrySetResult();
        return await Gate.Task.ConfigureAwait(false);
    }

    internal void Complete(TransferExecution execution) => _ = Gate.TrySetResult(execution);
}
