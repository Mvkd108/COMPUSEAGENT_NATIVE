using Compuse.Discovery;
using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.Filesystem.Tests;

[TestClass]
public sealed class FilesystemTransferBackendTests
{
    [TestMethod]
    public async Task CopyObservesDestinationWithoutRemovingSource()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        TransferExecution execution = await ExecuteAsync(TransferEffect.Copy, destDir, CancellationToken.None, source);

        Assert.IsTrue(execution.ApiSucceeded, $"HRESULT 0x{execution.ApiHresult:X8}");
        Assert.AreEqual(1, execution.Observations.Count);
        Assert.IsTrue(execution.Observations[0].DestinationMatchesCopy);
        Assert.IsFalse(execution.Observations[0].SourceRemoved);
        Assert.IsTrue(System.IO.File.Exists(source));
        Assert.AreEqual("payload", System.IO.File.ReadAllText(execution.Observations[0].DestinationPath));
    }

    [TestMethod]
    public async Task MoveObservesDestinationAndRemovesSource()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        TransferExecution execution = await ExecuteAsync(TransferEffect.Move, destDir, CancellationToken.None, source);

        Assert.IsTrue(execution.ApiSucceeded, $"HRESULT 0x{execution.ApiHresult:X8}");
        Assert.IsTrue(execution.Observations[0].DestinationMatchesCopy);
        Assert.IsTrue(execution.Observations[0].SourceRemoved);
        Assert.IsFalse(System.IO.File.Exists(source));
        Assert.AreEqual("payload", System.IO.File.ReadAllText(execution.Observations[0].DestinationPath));
    }

    [TestMethod]
    public async Task MultiFileCopyObservesEachDestination()
    {
        using TempTree tree = new();
        string first = tree.File("a.txt", "one");
        string second = tree.File("b.txt", "two");
        string destDir = tree.Dir("dst");
        TransferExecution execution = await ExecuteAsync(TransferEffect.Copy, destDir, CancellationToken.None, first, second);

        Assert.IsTrue(execution.ApiSucceeded, $"HRESULT 0x{execution.ApiHresult:X8}");
        Assert.AreEqual(2, execution.Observations.Count);
        Assert.IsTrue(execution.Observations[0].DestinationMatchesCopy);
        Assert.IsTrue(execution.Observations[1].DestinationMatchesCopy);
        Assert.AreEqual("one", System.IO.File.ReadAllText(execution.Observations[0].DestinationPath));
        Assert.AreEqual("two", System.IO.File.ReadAllText(execution.Observations[1].DestinationPath));
    }

    [TestMethod]
    public async Task CancelledTokenDoesNotMutate()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        using CancellationTokenSource cts = new();
        cts.Cancel();
        TransferExecution execution = await ExecuteAsync(TransferEffect.Copy, destDir, cts.Token, source);
        Assert.AreEqual(ShellNative.Abort, execution.ApiHresult);
        Assert.IsTrue(execution.AnyAborted);
        Assert.IsTrue(System.IO.File.Exists(source));
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task ExecuteAsyncReturnsBeforeStaCompletes()
    {
        using TempTree tree = new();
        ExecutionPlan plan = CreatePlan(TransferEffect.Copy, tree.Dir("dst"), tree.File("a.txt", "payload"));
        ScriptedShellOperation shell = new() { BlockAdvise = true };
        FilesystemTransferBackend backend = new(new WindowsFilesystemDiscovery(), new ScriptedShellOperationFactory(shell));

        ValueTask<TransferExecution> pending = backend.ExecuteAsync(plan, CancellationToken.None);
        await shell.AdviseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(pending.IsCompleted);

        shell.AllowAdvise.SetResult();
        TransferExecution execution = await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, shell.UnadviseCalls);
        Assert.IsTrue(shell.Released);
        Assert.IsFalse(execution.ApiSucceeded);
        Assert.IsFalse(System.IO.File.Exists(plan.Items[0].DestinationPath));
    }

    [TestMethod]
    public async Task DedicatedStaIsUsedEvenWhenCallerIsSta()
    {
        using TempTree tree = new();
        ExecutionPlan plan = CreatePlan(TransferEffect.Copy, tree.Dir("dst"), tree.File("a.txt", "payload"));
        ScriptedShellOperation shell = new() { BlockAdvise = true };
        FilesystemTransferBackend backend = new(new WindowsFilesystemDiscovery(), new ScriptedShellOperationFactory(shell));
        TaskCompletionSource<bool> callerSawIncomplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Task<TransferExecution>> pendingHolder = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Thread caller = new(() =>
        {
            ValueTask<TransferExecution> pending = backend.ExecuteAsync(plan, CancellationToken.None);
            _ = callerSawIncomplete.TrySetResult(!pending.IsCompleted);
            _ = pendingHolder.TrySetResult(pending.AsTask());
        });
        caller.SetApartmentState(ApartmentState.STA);
        caller.IsBackground = true;
        caller.Start();

        await shell.AdviseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(await callerSawIncomplete.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        shell.AllowAdvise.SetResult();
        TransferExecution execution = await (await pendingHolder.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(execution.ApiSucceeded);
        Assert.AreEqual(1, shell.AdviseCalls);
        Assert.AreEqual(1, shell.UnadviseCalls);
    }

    [TestMethod]
    public async Task FailedAdviseDoesNotQueueOrPerformAndMutatesNothing()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        ExecutionPlan plan = CreatePlan(TransferEffect.Copy, destDir, source);
        const int adviseFailed = unchecked((int)0x80004005);
        ScriptedShellOperation shell = new() { AdviseHresult = adviseFailed };
        FilesystemTransferBackend backend = new(new WindowsFilesystemDiscovery(), new ScriptedShellOperationFactory(shell));

        TransferExecution execution = await backend.ExecuteAsync(plan, CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(adviseFailed, execution.ApiHresult);
        Assert.AreEqual(1, shell.AdviseCalls);
        Assert.AreEqual(0, shell.UnadviseCalls);
        Assert.AreEqual(0, shell.FlagsCalls);
        Assert.AreEqual(0, shell.CreateItemCalls);
        Assert.AreEqual(0, shell.QueueCalls);
        Assert.AreEqual(0, shell.PerformCalls);
        Assert.IsTrue(shell.Released);
        Assert.IsTrue(System.IO.File.Exists(source));
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task SuccessfulAdviseUnadvisesOnceWhenFlagsFail()
    {
        using TempTree tree = new();
        ExecutionPlan plan = CreatePlan(TransferEffect.Copy, tree.Dir("dst"), tree.File("a.txt", "payload"));
        const int flagsFailed = unchecked((int)0x80070005);
        ScriptedShellOperation shell = new() { FlagsHresult = flagsFailed };
        FilesystemTransferBackend backend = new(new WindowsFilesystemDiscovery(), new ScriptedShellOperationFactory(shell));

        TransferExecution execution = await backend.ExecuteAsync(plan, CancellationToken.None)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(flagsFailed, execution.ApiHresult);
        Assert.AreEqual(1, shell.AdviseCalls);
        Assert.AreEqual(1, shell.UnadviseCalls);
        Assert.AreEqual(1, shell.FlagsCalls);
        Assert.AreEqual(0, shell.CreateItemCalls);
        Assert.AreEqual(0, shell.QueueCalls);
        Assert.AreEqual(0, shell.PerformCalls);
        Assert.IsTrue(shell.Released);
        Assert.AreEqual(4, shell.Events.Count);
        Assert.AreEqual("advise", shell.Events[0]);
        Assert.AreEqual("flags", shell.Events[1]);
        Assert.AreEqual("unadvise", shell.Events[2]);
        Assert.AreEqual("release", shell.Events[3]);
        Assert.IsFalse(System.IO.File.Exists(plan.Items[0].DestinationPath));
    }

    private static Task<TransferExecution> ExecuteAsync(
        TransferEffect effect,
        string destDir,
        CancellationToken cancellationToken,
        params string[] sources)
    {
        ExecutionPlan plan = CreatePlan(effect, destDir, sources);
        FilesystemTransferBackend backend = new(new WindowsFilesystemDiscovery());
        return backend.ExecuteAsync(plan, cancellationToken).AsTask();
    }

    private static ExecutionPlan CreatePlan(TransferEffect effect, string destDir, params string[] sources)
    {
        WindowsFilesystemDiscovery discovery = new();
        DestinationSnapshot destination = discovery.DiscoverDestination(
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir)),
            CancellationToken.None);
        List<PlannedItem> items = new(sources.Length);
        for (int index = 0; index < sources.Length; index++)
        {
            SourceSnapshot sourceSnapshot = discovery.DiscoverSource(sources[index], CancellationToken.None);
            items.Add(new PlannedItem(
                sourceSnapshot.RequestedPath,
                DropFilesRouter.CombineDestination(destination.Identity!.NormalizedPath, sourceSnapshot.RequestedPath),
                sourceSnapshot.Identity!,
                sourceSnapshot.ByteLength));
        }

        return new ExecutionPlan(effect, destination.Identity!, items);
    }
}
