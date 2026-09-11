using Compuse.Contracts;
using Compuse.Discovery;
using Compuse.Requests;

namespace Compuse.Routing.Tests;

[TestClass]
public sealed class DropFilesRouterTests
{
    [TestMethod]
    public void ApplicationSurfaceIsUnsupportedEvenIfSnapshotsLookHealthy()
    {
        RouteDecision decision = DropFilesRouter.Route(
            Request(TransferEffect.Copy, ApplicationTarget()),
            [FileSource(@"C:\src\a.txt")],
            new DestinationSnapshot(
                TargetSelectorKind.ApplicationSurface,
                DestinationStatus.ApplicationSurfaceUnsupported,
                identity: null,
                canAddFiles: false),
            []);
        AssertRefusal(decision, DropFilesRefusalCode.UnsupportedTargetKind);
    }

    [TestMethod]
    public void DestinationStatusesMapToRefusalCodes()
    {
        AssertRefusal(
            RouteDest(DestinationStatus.Missing),
            DropFilesRefusalCode.DestinationMissing);
        AssertRefusal(
            RouteDest(DestinationStatus.NotAContainer, identity: Id(@"C:\dst-file")),
            DropFilesRefusalCode.DestinationNotContainer);
        AssertRefusal(
            RouteDest(DestinationStatus.Inaccessible),
            DropFilesRefusalCode.DestinationInaccessible);
    }

    [TestMethod]
    public void DestinationRefusalWinsOverMissingSource()
    {
        RouteDecision decision = DropFilesRouter.Route(
            Request(TransferEffect.Copy, Container(@"C:\dst")),
            [new SourceSnapshot(@"C:\src\a.txt", SourceStatus.Missing, identity: null, byteLength: 0)],
            Dest(DestinationStatus.Missing),
            []);
        AssertRefusal(decision, DropFilesRefusalCode.DestinationMissing);
    }

    [TestMethod]
    public void SourceStatusesMapInRequestOrder()
    {
        AssertRefusal(
            RouteSources(new SourceSnapshot(@"C:\src\a.txt", SourceStatus.Missing, identity: null, 0)),
            DropFilesRefusalCode.SourceNotFound);
        AssertRefusal(
            RouteSources(new SourceSnapshot(@"C:\src\a.txt", SourceStatus.NotAFile, identity: null, 0)),
            DropFilesRefusalCode.SourceNotFile);
        AssertRefusal(
            RouteSources(new SourceSnapshot(@"C:\src\a.txt", SourceStatus.Inaccessible, identity: null, 0)),
            DropFilesRefusalCode.SourceInaccessible);
    }

    [TestMethod]
    public void FirstBadSourceWins()
    {
        RouteDecision decision = RouteSources(
            FileSource(@"C:\src\a.txt"),
            new SourceSnapshot(@"C:\src\b.txt", SourceStatus.Missing, identity: null, 0),
            new SourceSnapshot(@"C:\src\c.txt", SourceStatus.NotAFile, identity: null, 0));
        AssertRefusal(decision, DropFilesRefusalCode.SourceNotFound);
    }

    [TestMethod]
    public void DuplicateDestinationNamesAreCollision()
    {
        RouteDecision decision = DropFilesRouter.Route(
            Request(TransferEffect.Copy, Container(@"C:\dst"), @"C:\one\a.txt", @"C:\two\a.txt"),
            [FileSource(@"C:\one\a.txt"), FileSource(@"C:\two\a.txt")],
            Dest(DestinationStatus.FilesystemContainer, Id(@"C:\dst"), canAddFiles: true),
            [Missing(@"C:\dst\a.txt"), Missing(@"C:\dst\a.txt")]);
        AssertRefusal(decision, DropFilesRefusalCode.Collision);
    }

    [TestMethod]
    public void ExistingDestinationFileIsCollision()
    {
        FilesystemIdentity destFile = Id(@"C:\dst\a.txt");
        PathInspection existing = new(@"C:\dst\a.txt", PathPresence.File, destFile, byteLength: 4, canAddFiles: false);
        RouteDecision decision = DropFilesRouter.Route(
            Request(TransferEffect.Copy, Container(@"C:\dst"), @"C:\src\a.txt"),
            [FileSource(@"C:\src\a.txt")],
            Dest(DestinationStatus.FilesystemContainer, Id(@"C:\dst"), canAddFiles: true),
            [existing]);
        AssertRefusal(decision, DropFilesRefusalCode.Collision);
    }

    [TestMethod]
    public void CopyPlanIsDeterministic()
    {
        RouteDecision first = Happy(TransferEffect.Copy);
        RouteDecision second = Happy(TransferEffect.Copy);
        Assert.IsTrue(first.IsPlan);
        Assert.AreEqual(first.Plan!.BackendId, second.Plan!.BackendId);
        Assert.AreEqual(ExecutionPlan.FilesystemBackendId, first.Plan.BackendId);
        Assert.AreEqual(ExecutionPlan.SizeAndFileIdVerification, first.Plan.VerificationStrategy);
        Assert.AreEqual(TransferEffect.Copy, first.Plan.Effect);
        Assert.AreEqual(@"C:\src\a.txt", first.Plan.Items[0].SourcePath);
        Assert.AreEqual(@"C:\dst\a.txt", first.Plan.Items[0].DestinationPath);
        Assert.AreEqual(@"C:\src\b.txt", first.Plan.Items[1].SourcePath);
        Assert.AreEqual(@"C:\dst\b.txt", first.Plan.Items[1].DestinationPath);
    }

    [TestMethod]
    public void MovePlanKeepsMoveEffect()
    {
        RouteDecision decision = Happy(TransferEffect.Move);
        Assert.IsTrue(decision.IsPlan);
        Assert.AreEqual(TransferEffect.Move, decision.Plan!.Effect);
    }

    [TestMethod]
    public void CombineDestinationHandlesDriveRoot()
    {
        Assert.AreEqual(@"C:\a.txt", DropFilesRouter.CombineDestination(@"C:\", @"C:\src\a.txt"));
        Assert.AreEqual(@"C:\dst\a.txt", DropFilesRouter.CombineDestination(@"C:\dst", @"C:\src\a.txt"));
    }

    private static RouteDecision Happy(TransferEffect effect) =>
        DropFilesRouter.Route(
            Request(effect, Container(@"C:\dst"), @"C:\src\a.txt", @"C:\src\b.txt"),
            [FileSource(@"C:\src\a.txt"), FileSource(@"C:\src\b.txt")],
            Dest(DestinationStatus.FilesystemContainer, Id(@"C:\dst"), canAddFiles: true),
            [Missing(@"C:\dst\a.txt"), Missing(@"C:\dst\b.txt")]);

    private static RouteDecision RouteDest(DestinationStatus status, FilesystemIdentity? identity = null) =>
        DropFilesRouter.Route(
            Request(TransferEffect.Copy, Container(@"C:\dst")),
            [FileSource(@"C:\src\a.txt")],
            Dest(status, identity),
            []);

    private static RouteDecision RouteSources(params SourceSnapshot[] sources)
    {
        string[] paths = [.. sources.Select(static snapshot => snapshot.RequestedPath)];
        return DropFilesRouter.Route(
            Request(TransferEffect.Copy, Container(@"C:\dst"), paths),
            sources,
            Dest(DestinationStatus.FilesystemContainer, Id(@"C:\dst"), canAddFiles: true),
            [.. sources.Select(static snapshot => Missing(@"C:\dst\" + Path.GetFileName(snapshot.RequestedPath)))]);
    }

    private static void AssertRefusal(RouteDecision decision, string code)
    {
        Assert.IsFalse(decision.IsPlan);
        Assert.IsNotNull(decision.Refusal);
        Assert.AreEqual(code, decision.Refusal.Code);
    }

    private static DropFilesRequest Request(TransferEffect effect, TargetSelector target, params string[] sources)
    {
        string[] paths = sources.Length == 0 ? [@"C:\src\a.txt"] : sources;
        return DropFilesRequest.Create(
            CorrelationId.Parse("abcdef01-2345-6789-abcd-ef0123456789"),
            paths.Select(static path => new SourceItem(new PhysicalFileSource(path))),
            effect,
            target);
    }

    private static TargetSelector Container(string path) =>
        TargetSelector.FromFilesystemContainer(new FilesystemContainerTarget(path));

    private static TargetSelector ApplicationTarget() =>
        TargetSelector.FromApplicationSurface(new ApplicationSurfaceTarget("notepad.exe"));

    private static SourceSnapshot FileSource(string path) =>
        new(path, SourceStatus.PhysicalFile, Id(path), byteLength: 5);

    private static DestinationSnapshot Dest(
        DestinationStatus status,
        FilesystemIdentity? identity = null,
        bool canAddFiles = false) =>
        new(TargetSelectorKind.FilesystemContainer, status, identity, canAddFiles);

    private static PathInspection Missing(string path) =>
        new(path, PathPresence.Missing, identity: null, byteLength: 0, canAddFiles: false);

    private static FilesystemIdentity Id(string path) => new(path, volumeSerialNumber: 1, fileIndex: 42);
}
