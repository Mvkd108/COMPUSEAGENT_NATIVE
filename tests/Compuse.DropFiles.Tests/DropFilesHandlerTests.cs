using Compuse.Contracts;
using Compuse.Discovery;
using Compuse.Filesystem;
using Compuse.Requests;
using Compuse.Routing;
using Compuse.Runtime;

namespace Compuse.DropFiles.Tests;

[TestClass]
public sealed class DropFilesHandlerTests
{
    [TestMethod]
    public async Task CopyThroughRuntimeIsCommittedWithObservationEvidence()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        OperationResult result = await Run(TransferEffect.Copy, destDir, source);

        Assert.AreEqual(OperationOutcome.Committed, result.Outcome);
        Assert.IsTrue(result.Evidence.Any(static item =>
            item.Kind == VerificationEvidenceKind.ExternalSideEffectObservation
            && item.Code == "destination_file_observed"));
        Assert.IsTrue(result.Evidence.Any(static item => item.Kind == VerificationEvidenceKind.OsApiReturn));
        Assert.IsTrue(System.IO.File.Exists(source));
        Assert.AreEqual("payload", System.IO.File.ReadAllText(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task MoveThroughRuntimeRemovesSource()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        OperationResult result = await Run(TransferEffect.Move, destDir, source);

        Assert.AreEqual(OperationOutcome.Committed, result.Outcome);
        Assert.IsTrue(result.Evidence.Any(static item => item.Code == "source_removed_observed"));
        Assert.IsFalse(System.IO.File.Exists(source));
        Assert.AreEqual("payload", System.IO.File.ReadAllText(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task ApplicationSurfaceIsRefused()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt");
        DropFilesRequest request = DropFilesRequest.Create(
            CorrelationId.New(),
            [new SourceItem(new PhysicalFileSource(source))],
            TransferEffect.Copy,
            TargetSelector.FromApplicationSurface(new ApplicationSurfaceTarget("notepad.exe")));
        OperationResult result = await Run(request);
        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreEqual(DropFilesRefusalCode.UnsupportedTargetKind, result.Refusal!.Code);
    }

    [TestMethod]
    public async Task CollisionIsRefusedWithoutOverwrite()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "new");
        string destDir = tree.Dir("dst");
        System.IO.File.WriteAllText(Path.Combine(destDir, "a.txt"), "old");
        OperationResult result = await Run(TransferEffect.Copy, destDir, source);
        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreEqual(DropFilesRefusalCode.Collision, result.Refusal!.Code);
        Assert.AreEqual("old", System.IO.File.ReadAllText(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task MissingSourceIsRefused()
    {
        using TempTree tree = new();
        string missing = Path.Combine(tree.Root, "gone.txt");
        string destDir = tree.Dir("dst");
        OperationResult result = await Run(TransferEffect.Copy, destDir, missing);
        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreEqual(DropFilesRefusalCode.SourceNotFound, result.Refusal!.Code);
    }

    [TestMethod]
    public async Task MultiFileCopyIsAllOrNothingOnCollision()
    {
        using TempTree tree = new();
        string first = tree.File("a.txt", "one");
        string second = tree.File("b.txt", "two");
        string destDir = tree.Dir("dst");
        System.IO.File.WriteAllText(Path.Combine(destDir, "b.txt"), "existing");
        OperationResult result = await Run(TransferEffect.Copy, destDir, first, second);

        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreEqual(DropFilesRefusalCode.Collision, result.Refusal!.Code);
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(destDir, "a.txt")));
        Assert.AreEqual("existing", System.IO.File.ReadAllText(Path.Combine(destDir, "b.txt")));
    }

    [TestMethod]
    public async Task MultiFileCopyCommitsBothFiles()
    {
        using TempTree tree = new();
        string first = tree.File("a.txt", "one");
        string second = tree.File("b.txt", "two");
        string destDir = tree.Dir("dst");
        OperationResult result = await Run(TransferEffect.Copy, destDir, first, second);

        Assert.AreEqual(OperationOutcome.Committed, result.Outcome);
        Assert.AreEqual("one", System.IO.File.ReadAllText(Path.Combine(destDir, "a.txt")));
        Assert.AreEqual("two", System.IO.File.ReadAllText(Path.Combine(destDir, "b.txt")));
        Assert.AreEqual(
            2,
            result.Evidence.Count(static item => item.Code == "destination_file_observed"));
        Assert.IsTrue(result.Evidence.Any(static item =>
            item.Code == "destination_file_observed" && item.ArtifactReference is not null));
    }

    [TestMethod]
    public async Task MissingDestinationIsRefused()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt");
        string missing = Path.Combine(tree.Root, "no-dst");
        OperationResult result = await Run(TransferEffect.Copy, missing, source);
        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreEqual(DropFilesRefusalCode.DestinationMissing, result.Refusal!.Code);
    }

    [TestMethod]
    public async Task StaleSourceIdentityIsRefusedBeforeTransfer()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        MutatingIdentityDiscovery discovery = new();
        DropFilesHandler handler = new(discovery, new FilesystemTransferBackend(discovery));
        OperationResult result = await Run(
            DropFilesRequest.Create(
                CorrelationId.New(),
                [new SourceItem(new PhysicalFileSource(source))],
                TransferEffect.Copy,
                TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir))),
            handler);

        Assert.AreEqual(OperationOutcome.Refused, result.Outcome);
        Assert.AreEqual(DropFilesRefusalCode.StaleIdentity, result.Refusal!.Code);
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task IntegrityMismatchIsIndeterminateWhenDestinationLengthDiffers()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        string destFile = Path.Combine(destDir, "a.txt");
        WindowsFilesystemDiscovery discovery = new();
        SourceSnapshot snapshot = discovery.DiscoverSource(source, CancellationToken.None);
        FilesystemIdentity destIdentity = new(destFile, volumeSerialNumber: 1, fileIndex: 99);
        PathInspection destInspection = new(
            destFile,
            PathPresence.File,
            destIdentity,
            byteLength: 1,
            canAddFiles: false);
        PathInspection sourceAfter = discovery.Inspect(source, CancellationToken.None);
        ItemObservation observation = new(
            source,
            destFile,
            snapshot.ByteLength,
            destInspection,
            sourceAfter);
        ScriptedTransferBackend backend = new(new TransferExecution(0, anyAborted: false, [observation]));
        DropFilesHandler handler = new(discovery, backend);
        OperationResult result = await Run(
            DropFilesRequest.Create(
                CorrelationId.New(),
                [new SourceItem(new PhysicalFileSource(source))],
                TransferEffect.Copy,
                TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir))),
            handler);

        Assert.AreEqual(OperationOutcome.Indeterminate, result.Outcome);
        Assert.IsTrue(result.Evidence.Any(static item => item.Code == DropFilesRefusalCode.IntegrityMismatch));
        Assert.IsFalse(result.Evidence.Any(static item => item.Code == "destination_file_observed"));
    }

    [TestMethod]
    public async Task VerificationUnavailableFailsWhenDestinationCannotBeInspected()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        string destFile = Path.Combine(destDir, "a.txt");
        WindowsFilesystemDiscovery discovery = new();
        PathInspection destInspection = new(
            destFile,
            PathPresence.Inaccessible,
            identity: null,
            byteLength: 0,
            canAddFiles: false);
        PathInspection sourceAfter = discovery.Inspect(source, CancellationToken.None);
        ItemObservation observation = new(source, destFile, 7, destInspection, sourceAfter);
        ScriptedTransferBackend backend = new(new TransferExecution(0, anyAborted: false, [observation]));
        DropFilesHandler handler = new(discovery, backend);
        OperationResult result = await Run(
            DropFilesRequest.Create(
                CorrelationId.New(),
                [new SourceItem(new PhysicalFileSource(source))],
                TransferEffect.Copy,
                TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir))),
            handler);

        Assert.AreEqual(OperationOutcome.Failed, result.Outcome);
        Assert.AreEqual(DropFilesRefusalCode.VerificationUnavailable, result.Failure!.Code);
    }

    [TestMethod]
    public async Task HandlerExecuteAsyncIsIncompleteWhileBackendIsGated()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        GatedTransferBackend backend = new();
        WindowsFilesystemDiscovery discovery = new();
        DropFilesHandler handler = new(discovery, backend);
        DropFilesRequest request = CreateRequest(TransferEffect.Copy, destDir, source);
        OperationExecutionContext context = HandlerContext.Create(request.CorrelationId, new SystemOperationClock());

        ValueTask<OperationResult> pending = handler.ExecuteAsync(request, context, CancellationToken.None);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(pending.IsCompleted);

        backend.Complete(TransferObservation.Aborted(discovery, source, destDir));
        OperationResult result = await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(OperationOutcome.Indeterminate, result.Outcome);
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task DeadlineAfterDispatchDiscardsLateCommitted()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        DateTimeOffset start = new(2026, 8, 22, 0, 0, 0, TimeSpan.Zero);
        ControllableClock clock = new(start);
        GatedTransferBackend backend = new();
        WindowsFilesystemDiscovery discovery = new();
        DropFilesHandler handler = new(discovery, backend);
        DropFilesRequest request = CreateRequest(TransferEffect.Copy, destDir, source);
        await using OperationRuntime runtime = new(clock, new OperationRuntimeOptions(maxExecution: TimeSpan.FromSeconds(30)));
        runtime.Register(handler);

        Task<OperationResult> run = runtime.RunAsync(request, request.CorrelationId, start.AddSeconds(5));
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await clock.WaitForWaiter().WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        OperationResult result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(OperationOutcome.Indeterminate, result.Outcome);
        Assert.AreEqual(RuntimeOutcomeCode.DeadlineExpired, result.Evidence[0].Code);
        backend.Complete(TransferObservation.CommittedCopy(discovery, source, destDir));
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(destDir, "a.txt")));
    }

    [TestMethod]
    public async Task CallerCancellationAfterDispatchReachesBackend()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload");
        string destDir = tree.Dir("dst");
        GatedTransferBackend backend = new();
        WindowsFilesystemDiscovery discovery = new();
        DropFilesHandler handler = new(discovery, backend);
        DropFilesRequest request = CreateRequest(TransferEffect.Copy, destDir, source);
        await using OperationRuntime runtime = new(new SystemOperationClock(), new OperationRuntimeOptions());
        runtime.Register(handler);
        using CancellationTokenSource cts = new();

        Task<OperationResult> run = runtime.RunAsync(request, request.CorrelationId, deadlineUtc: null, cts.Token);
        await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(backend.ObservedToken.CanBeCanceled);
        Assert.IsFalse(backend.ObservedToken.IsCancellationRequested);
        cts.Cancel();
        OperationResult result = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(OperationOutcome.Indeterminate, result.Outcome);
        Assert.AreEqual(RuntimeOutcomeCode.Cancelled, result.Evidence[0].Code);
        Assert.IsTrue(backend.ObservedToken.IsCancellationRequested);
        backend.Complete(TransferObservation.CommittedCopy(discovery, source, destDir));
        Assert.IsFalse(System.IO.File.Exists(Path.Combine(destDir, "a.txt")));
    }

    private static DropFilesRequest CreateRequest(TransferEffect effect, string destDir, params string[] sources) =>
        DropFilesRequest.Create(
            CorrelationId.New(),
            sources.Select(static path => new SourceItem(new PhysicalFileSource(path))),
            effect,
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir)));

    private static Task<OperationResult> Run(TransferEffect effect, string destDir, params string[] sources) =>
        Run(DropFilesRequest.Create(
            CorrelationId.New(),
            sources.Select(static path => new SourceItem(new PhysicalFileSource(path))),
            effect,
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir))));

    private static Task<OperationResult> Run(DropFilesRequest request)
    {
        WindowsFilesystemDiscovery discovery = new();
        return Run(request, new DropFilesHandler(discovery, new FilesystemTransferBackend(discovery)));
    }

    private static async Task<OperationResult> Run(DropFilesRequest request, DropFilesHandler handler)
    {
        await using OperationRuntime runtime = new(new SystemOperationClock());
        runtime.Register(handler);
        return await runtime.RunAsync(request, request.CorrelationId, request.DeadlineUtc);
    }
}

internal sealed class ScriptedTransferBackend : ITransferBackend
{
    private readonly TransferExecution _execution;

    public ScriptedTransferBackend(TransferExecution execution)
    {
        _execution = execution;
    }

    public ValueTask<TransferExecution> ExecuteAsync(ExecutionPlan plan, CancellationToken cancellationToken)
    {
        _ = plan;
        _ = cancellationToken;
        return new ValueTask<TransferExecution>(_execution);
    }
}

internal sealed class MutatingIdentityDiscovery : IFilesystemDiscovery
{
    private readonly WindowsFilesystemDiscovery _inner = new();
    private int _sourcePasses;

    public PathInspection Inspect(string absolutePath, CancellationToken cancellationToken) =>
        _inner.Inspect(absolutePath, cancellationToken);

    public SourceSnapshot DiscoverSource(string absolutePath, CancellationToken cancellationToken)
    {
        SourceSnapshot snapshot = _inner.DiscoverSource(absolutePath, cancellationToken);
        _sourcePasses++;
        if (_sourcePasses >= 2 && snapshot.Identity is not null)
        {
            FilesystemIdentity mutated = new(
                snapshot.Identity.NormalizedPath,
                snapshot.Identity.VolumeSerialNumber,
                snapshot.Identity.FileIndex + 1);
            return new SourceSnapshot(snapshot.RequestedPath, snapshot.Status, mutated, snapshot.ByteLength);
        }

        return snapshot;
    }

    public IReadOnlyList<SourceSnapshot> DiscoverSources(
        IReadOnlyList<string> absolutePaths,
        CancellationToken cancellationToken)
    {
        List<SourceSnapshot> snapshots = new(absolutePaths.Count);
        for (int index = 0; index < absolutePaths.Count; index++)
        {
            snapshots.Add(DiscoverSource(absolutePaths[index], cancellationToken));
        }

        return snapshots;
    }

    public DestinationSnapshot DiscoverDestination(TargetSelector target, CancellationToken cancellationToken) =>
        _inner.DiscoverDestination(target, cancellationToken);
}
