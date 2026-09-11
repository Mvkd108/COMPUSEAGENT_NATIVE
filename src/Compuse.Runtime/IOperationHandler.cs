using Compuse.Contracts;

namespace Compuse.Runtime;

public interface IOperationHandler<in TRequest>
    where TRequest : notnull
{
    public ValueTask<OperationResult> ExecuteAsync(
        TRequest request,
        OperationExecutionContext context,
        CancellationToken cancellationToken);
}
