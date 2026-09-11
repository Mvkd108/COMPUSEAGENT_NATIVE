using System.Collections.ObjectModel;
using Compuse.Discovery;
using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.Filesystem;

public sealed class FilesystemTransferBackend : ITransferBackend
{
    private readonly IFilesystemDiscovery _discovery;
    private readonly IShellOperationFactory _factory;

    public FilesystemTransferBackend(IFilesystemDiscovery discovery)
        : this(discovery, NativeShellOperationFactory.Instance)
    {
    }

    internal FilesystemTransferBackend(IFilesystemDiscovery discovery, IShellOperationFactory factory)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(factory);
        _discovery = discovery;
        _factory = factory;
    }

    public ValueTask<TransferExecution> ExecuteAsync(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(plan.BackendId, ExecutionPlan.FilesystemBackendId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Only the filesystem backend can execute this plan.", nameof(plan));
        }

        TaskCompletionSource<TransferExecution> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(static state =>
        {
            StaWork work = (StaWork)state!;
            try
            {
                work.Completion.SetResult(work.Backend.ExecuteSta(work.Plan, work.CancellationToken));
            }
            catch (Exception ex)
            {
                work.Completion.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false;
        thread.Name = "compuse-ifileoperation";
        thread.Start(new StaWork(this, plan, completion, cancellationToken));
        return new ValueTask<TransferExecution>(completion.Task);
    }

    private TransferExecution ExecuteSta(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        IShellOperation? operation = null;
        int hresult = 0;
        bool aborted = false;
        bool performed = false;
        uint adviseCookie = 0;
        bool advised = false;
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Finish(ShellNative.Abort, aborted: true, plan);
            }

            operation = _factory.Create();
            CancellationProgressSink sink = new(cancellationToken);
            int advise = operation.Advise(sink, out adviseCookie);
            if (advise < 0)
            {
                return Finish(advise, aborted: false, plan);
            }

            advised = true;
            hresult = operation.SetOperationFlags(ShellNative.OperationFlags);
            if (hresult < 0)
            {
                return Finish(hresult, aborted: false, plan);
            }

            int createDest = operation.CreateItemFromParsingName(
                plan.DestinationIdentity.NormalizedPath,
                out ShellNative.IShellItem? destinationFolder);
            if (createDest < 0 || destinationFolder is null)
            {
                return Finish(createDest < 0 ? createDest : ShellNative.Abort, aborted: false, plan);
            }

            for (int index = 0; index < plan.Items.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Finish(ShellNative.Abort, aborted: true, plan);
                }

                PlannedItem item = plan.Items[index];
                int createSource = operation.CreateItemFromParsingName(
                    item.SourcePath,
                    out ShellNative.IShellItem? sourceItem);
                if (createSource < 0 || sourceItem is null)
                {
                    return Finish(createSource < 0 ? createSource : ShellNative.Abort, aborted: false, plan);
                }

                string fileName = Path.GetFileName(item.DestinationPath);
                int queue = plan.Effect == TransferEffect.Move
                    ? operation.MoveItem(sourceItem, destinationFolder, fileName)
                    : operation.CopyItem(sourceItem, destinationFolder, fileName);
                if (queue < 0)
                {
                    return Finish(queue, aborted: false, plan);
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Finish(ShellNative.Abort, aborted: true, plan);
            }

            performed = true;
            hresult = operation.PerformOperations();
            int abortCode = operation.GetAnyOperationsAborted(out aborted);
            if (hresult >= 0 && abortCode < 0)
            {
                hresult = abortCode;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                aborted = true;
            }
        }
        catch (OperationCanceledException) when (performed)
        {
            hresult = ShellNative.Abort;
            aborted = true;
        }
        finally
        {
            if (advised && operation is not null)
            {
                try
                {
                    _ = operation.Unadvise(adviseCookie);
                }
                catch (Exception)
                {
                }
            }

            if (operation is not null)
            {
                try
                {
                    operation.Release();
                }
                catch (Exception)
                {
                }
            }
        }

        return Finish(hresult, aborted, plan);
    }

    private TransferExecution Finish(int hresult, bool aborted, ExecutionPlan plan)
    {
        List<ItemObservation> observations = new(plan.Items.Count);
        for (int index = 0; index < plan.Items.Count; index++)
        {
            PlannedItem item = plan.Items[index];
            PathInspection destination = _discovery.Inspect(item.DestinationPath, CancellationToken.None);
            PathInspection sourceAfter = _discovery.Inspect(item.SourcePath, CancellationToken.None);
            observations.Add(
                new ItemObservation(
                    item.SourcePath,
                    item.DestinationPath,
                    item.ByteLength,
                    destination,
                    sourceAfter));
        }

        return new TransferExecution(hresult, aborted, new ReadOnlyCollection<ItemObservation>(observations.ToArray()));
    }

    private sealed record StaWork(
        FilesystemTransferBackend Backend,
        ExecutionPlan Plan,
        TaskCompletionSource<TransferExecution> Completion,
        CancellationToken CancellationToken);
}

internal interface IShellOperationFactory
{
    public IShellOperation Create();
}

internal interface IShellOperation
{
    public int Advise(ShellNative.IFileOperationProgressSink sink, out uint cookie);

    public int Unadvise(uint cookie);

    public int SetOperationFlags(uint flags);

    public int CreateItemFromParsingName(string path, out ShellNative.IShellItem? item);

    public int CopyItem(ShellNative.IShellItem source, ShellNative.IShellItem destinationFolder, string fileName);

    public int MoveItem(ShellNative.IShellItem source, ShellNative.IShellItem destinationFolder, string fileName);

    public int PerformOperations();

    public int GetAnyOperationsAborted(out bool aborted);

    public void Release();
}

internal sealed class NativeShellOperationFactory : IShellOperationFactory
{
    internal static NativeShellOperationFactory Instance { get; } = new();

    public IShellOperation Create() => new NativeShellOperation();
}

internal sealed class NativeShellOperation : IShellOperation
{
    private readonly ShellNative.IFileOperation _operation;
    private readonly List<object> _com = [];

    internal NativeShellOperation()
    {
        _operation = (ShellNative.IFileOperation)new ShellNative.FileOperationCoclass();
        _com.Add(_operation);
    }

    public int Advise(ShellNative.IFileOperationProgressSink sink, out uint cookie) =>
        _operation.Advise(sink, out cookie);

    public int Unadvise(uint cookie) => _operation.Unadvise(cookie);

    public int SetOperationFlags(uint flags) => _operation.SetOperationFlags(flags);

    public int CreateItemFromParsingName(string path, out ShellNative.IShellItem? item)
    {
        int hresult = ShellNative.SHCreateItemFromParsingName(
            path,
            nint.Zero,
            ShellNative.ShellItemIid,
            out ShellNative.IShellItem created);
        item = created;
        if (hresult >= 0)
        {
            _com.Add(created);
        }

        return hresult;
    }

    public int CopyItem(ShellNative.IShellItem source, ShellNative.IShellItem destinationFolder, string fileName) =>
        _operation.CopyItem(source, destinationFolder, fileName, nint.Zero);

    public int MoveItem(ShellNative.IShellItem source, ShellNative.IShellItem destinationFolder, string fileName) =>
        _operation.MoveItem(source, destinationFolder, fileName, nint.Zero);

    public int PerformOperations() => _operation.PerformOperations();

    public int GetAnyOperationsAborted(out bool aborted) => _operation.GetAnyOperationsAborted(out aborted);

    public void Release()
    {
        for (int index = _com.Count - 1; index >= 0; index--)
        {
            ShellNative.Release(_com[index]);
        }

        _com.Clear();
    }
}
