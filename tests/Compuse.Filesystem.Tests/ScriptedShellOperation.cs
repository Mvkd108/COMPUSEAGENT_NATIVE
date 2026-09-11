namespace Compuse.Filesystem.Tests;

internal sealed class ScriptedShellOperationFactory : IShellOperationFactory
{
    private readonly IShellOperation _operation;

    internal ScriptedShellOperationFactory(IShellOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _operation = operation;
    }

    public IShellOperation Create() => _operation;
}

internal sealed class ScriptedShellOperation : IShellOperation
{
    internal int AdviseHresult { get; init; }

    internal int FlagsHresult { get; init; }

    internal bool BlockAdvise { get; init; }

    internal TaskCompletionSource AdviseEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource AllowAdvise { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int AdviseCalls { get; private set; }

    internal int UnadviseCalls { get; private set; }

    internal int FlagsCalls { get; private set; }

    internal int CreateItemCalls { get; private set; }

    internal int QueueCalls { get; private set; }

    internal int PerformCalls { get; private set; }

    internal bool Released { get; private set; }

    internal List<string> Events { get; } = [];

    public int Advise(ShellNative.IFileOperationProgressSink sink, out uint cookie)
    {
        _ = sink;
        AdviseCalls++;
        Events.Add("advise");
        cookie = 1;
        _ = AdviseEntered.TrySetResult();
        if (BlockAdvise)
        {
            AllowAdvise.Task.GetAwaiter().GetResult();
        }

        return AdviseHresult;
    }

    public int Unadvise(uint cookie)
    {
        _ = cookie;
        UnadviseCalls++;
        Events.Add("unadvise");
        return 0;
    }

    public int SetOperationFlags(uint flags)
    {
        _ = flags;
        FlagsCalls++;
        Events.Add("flags");
        return FlagsHresult;
    }

    public int CreateItemFromParsingName(string path, out ShellNative.IShellItem? item)
    {
        _ = path;
        CreateItemCalls++;
        Events.Add("create-item");
        item = null;
        return unchecked((int)0x80004003);
    }

    public int CopyItem(ShellNative.IShellItem source, ShellNative.IShellItem destinationFolder, string fileName)
    {
        _ = source;
        _ = destinationFolder;
        _ = fileName;
        QueueCalls++;
        Events.Add("queue");
        return 0;
    }

    public int MoveItem(ShellNative.IShellItem source, ShellNative.IShellItem destinationFolder, string fileName)
    {
        _ = source;
        _ = destinationFolder;
        _ = fileName;
        QueueCalls++;
        Events.Add("queue");
        return 0;
    }

    public int PerformOperations()
    {
        PerformCalls++;
        Events.Add("perform");
        return 0;
    }

    public int GetAnyOperationsAborted(out bool aborted)
    {
        aborted = false;
        return 0;
    }

    public void Release()
    {
        Released = true;
        Events.Add("release");
    }
}
