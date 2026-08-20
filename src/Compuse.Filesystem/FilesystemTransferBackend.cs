using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using Compuse.Discovery;
using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.Filesystem;

public sealed class FilesystemTransferBackend
{
    private readonly IFilesystemDiscovery _discovery;

    public FilesystemTransferBackend(IFilesystemDiscovery discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        _discovery = discovery;
    }

    public TransferExecution Execute(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(plan.BackendId, ExecutionPlan.FilesystemBackendId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Only the filesystem backend can execute this plan.", nameof(plan));
        }

        return Sta.Run(() => ExecuteSta(plan, cancellationToken));
    }

    private TransferExecution ExecuteSta(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<object> com = [];
        int hresult = 0;
        bool aborted = false;
        try
        {
            ShellNative.IFileOperation operation = (ShellNative.IFileOperation)new ShellNative.FileOperationCoclass();
            com.Add(operation);
            hresult = operation.SetOperationFlags(ShellNative.OperationFlags);
            if (hresult < 0)
            {
                return Finish(hresult, aborted: false, plan, cancellationToken);
            }

            int createDest = ShellNative.SHCreateItemFromParsingName(
                plan.DestinationIdentity.NormalizedPath,
                nint.Zero,
                ShellNative.ShellItemIid,
                out ShellNative.IShellItem destinationFolder);
            if (createDest < 0)
            {
                return Finish(createDest, aborted: false, plan, cancellationToken);
            }

            com.Add(destinationFolder);

            for (int index = 0; index < plan.Items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PlannedItem item = plan.Items[index];
                int createSource = ShellNative.SHCreateItemFromParsingName(
                    item.SourcePath,
                    nint.Zero,
                    ShellNative.ShellItemIid,
                    out ShellNative.IShellItem sourceItem);
                if (createSource < 0)
                {
                    return Finish(createSource, aborted: false, plan, cancellationToken);
                }

                com.Add(sourceItem);
                string fileName = Path.GetFileName(item.DestinationPath);
                int queue = plan.Effect == TransferEffect.Move
                    ? operation.MoveItem(sourceItem, destinationFolder, fileName, nint.Zero)
                    : operation.CopyItem(sourceItem, destinationFolder, fileName, nint.Zero);
                if (queue < 0)
                {
                    return Finish(queue, aborted: false, plan, cancellationToken);
                }
            }

            hresult = operation.PerformOperations();
            int abortCode = operation.GetAnyOperationsAborted(out aborted);
            if (hresult >= 0 && abortCode < 0)
            {
                hresult = abortCode;
            }
        }
        finally
        {
            for (int index = com.Count - 1; index >= 0; index--)
            {
                ShellNative.Release(com[index]);
            }
        }

        return Finish(hresult, aborted, plan, CancellationToken.None);
    }

    private TransferExecution Finish(int hresult, bool aborted, ExecutionPlan plan, CancellationToken cancellationToken)
    {
        List<ItemObservation> observations = new(plan.Items.Count);
        for (int index = 0; index < plan.Items.Count; index++)
        {
            PlannedItem item = plan.Items[index];
            PathInspection destination = _discovery.Inspect(item.DestinationPath, cancellationToken);
            PathInspection sourceAfter = _discovery.Inspect(item.SourcePath, cancellationToken);
            observations.Add(new ItemObservation(item.SourcePath, item.DestinationPath, item.ByteLength, destination, sourceAfter));
        }

        return new TransferExecution(hresult, aborted, new ReadOnlyCollection<ItemObservation>(observations.ToArray()));
    }
}

internal static class Sta
{
    internal static T Run<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            return func();
        }

        T? result = default;
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result!;
    }
}
