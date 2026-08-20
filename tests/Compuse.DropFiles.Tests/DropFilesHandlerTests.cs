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

    private static Task<OperationResult> Run(TransferEffect effect, string destDir, params string[] sources) =>
        Run(DropFilesRequest.Create(
            CorrelationId.New(),
            sources.Select(static path => new SourceItem(new PhysicalFileSource(path))),
            effect,
            TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(destDir))));

    private static async Task<OperationResult> Run(DropFilesRequest request)
    {
        WindowsFilesystemDiscovery discovery = new();
        DropFilesHandler handler = new(discovery, new FilesystemTransferBackend(discovery));
        await using OperationRuntime runtime = new(new SystemOperationClock());
        runtime.Register(handler);
        return await runtime.RunAsync(request, request.CorrelationId, request.DeadlineUtc);
    }
}
