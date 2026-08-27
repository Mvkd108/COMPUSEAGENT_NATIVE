using Compuse.Routing;

namespace Compuse.Filesystem;

public interface ITransferBackend
{
    public ValueTask<TransferExecution> ExecuteAsync(
        ExecutionPlan plan,
        CancellationToken cancellationToken);
}
