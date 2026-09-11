using Compuse.Contracts;
using Compuse.Discovery;
using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.ApplicationSurface.Tests;

[TestClass]
public sealed class ApplicationRouterTests
{
    private static readonly WindowIdentity NotepadIdentity = new(
        processId: 4242,
        hwnd: new nint(0xABCD),
        processImageName: "notepad.exe",
        windowClass: null,
        windowTitle: "Untitled - Notepad",
        runtimeIdKey: "42.1234");

    private static readonly WindowIdentity OtherIdentity = new(
        processId: 4242,
        hwnd: new nint(0xABCD),
        processImageName: "calc.exe",
        windowClass: null,
        windowTitle: "Calculator",
        runtimeIdKey: "42.1234");

    private static readonly ApplicationRecipeCatalog NotepadCatalog = new(new NotepadImportRecipe());

    [TestMethod]
    public void RefusesWhenWindowNotFound()
    {
        RouteDecision decision = ApplicationRouter.Route(
            TransferEffect.Copy,
            [PhysicalFile("a.txt", "payload")],
            WindowDiscoveryResult.ForRefusal(new RefusalInfo(DropFilesRefusalCode.TargetNotFound, "no window")),
            NotepadCatalog);

        Assert.IsFalse(decision.IsPlan);
        Assert.AreEqual(DropFilesRefusalCode.TargetNotFound, decision.Refusal!.Code);
    }

    [TestMethod]
    public void RefusesWhenWindowAmbiguous()
    {
        RouteDecision decision = ApplicationRouter.Route(
            TransferEffect.Copy,
            [PhysicalFile("a.txt", "payload")],
            WindowDiscoveryResult.ForRefusal(new RefusalInfo(DropFilesRefusalCode.AmbiguousTarget, "many windows")),
            NotepadCatalog);

        Assert.IsFalse(decision.IsPlan);
        Assert.AreEqual(DropFilesRefusalCode.AmbiguousTarget, decision.Refusal!.Code);
    }

    [TestMethod]
    public void RefusesMultipleSources()
    {
        RouteDecision decision = ApplicationRouter.Route(
            TransferEffect.Copy,
            [PhysicalFile("a.txt", "one"), PhysicalFile("b.txt", "two")],
            WindowDiscoveryResult.ForWindow(NotepadIdentity),
            NotepadCatalog);

        Assert.IsFalse(decision.IsPlan);
        Assert.AreEqual(DropFilesRefusalCode.SingleSourceRequired, decision.Refusal!.Code);
    }

    [TestMethod]
    public void RefusesMissingSource()
    {
        SourceSnapshot missing = new(
            @"C:\gone\a.txt",
            SourceStatus.Missing,
            identity: null,
            byteLength: 0);
        RouteDecision decision = ApplicationRouter.Route(
            TransferEffect.Copy,
            [missing],
            WindowDiscoveryResult.ForWindow(NotepadIdentity),
            NotepadCatalog);

        Assert.IsFalse(decision.IsPlan);
        Assert.AreEqual(DropFilesRefusalCode.SourceNotFound, decision.Refusal!.Code);
    }

    [TestMethod]
    public void RefusesUnsupportedApplicationRecipe()
    {
        RouteDecision decision = ApplicationRouter.Route(
            TransferEffect.Copy,
            [PhysicalFile("a.txt", "payload")],
            WindowDiscoveryResult.ForWindow(OtherIdentity),
            NotepadCatalog);

        Assert.IsFalse(decision.IsPlan);
        Assert.AreEqual(DropFilesRefusalCode.UnsupportedApplicationRecipe, decision.Refusal!.Code);
    }

    [TestMethod]
    public void RefusesWhenNoRecipeCatalogIsConfigured()
    {
        RouteDecision decision = ApplicationRouter.Route(
            TransferEffect.Copy,
            [PhysicalFile("a.txt", "payload")],
            WindowDiscoveryResult.ForWindow(NotepadIdentity),
            recipeCatalog: null);

        Assert.IsFalse(decision.IsPlan);
        Assert.AreEqual(DropFilesRefusalCode.UnsupportedApplicationRecipe, decision.Refusal!.Code);
    }

    [TestMethod]
    public void ProducesPlanWithExpectedTextTargetIdentityAndRecipe()
    {
        using TempTree tree = new();
        string path = tree.File("a.txt", "router payload");
        FilesystemIdentity fsIdentity = new(path, volumeSerialNumber: 1, fileIndex: 7);
        SourceSnapshot source = new(path, SourceStatus.PhysicalFile, fsIdentity, byteLength: "router payload".Length);

        RouteDecision decision = ApplicationRouter.Route(
            TransferEffect.Move,
            [source],
            WindowDiscoveryResult.ForWindow(NotepadIdentity),
            NotepadCatalog);

        Assert.IsTrue(decision.IsPlan);
        ApplicationImportPlan plan = decision.ApplicationPlan!;
        Assert.AreEqual("application_surface", plan.BackendId);
        Assert.AreEqual("document_text", plan.VerificationStrategy);
        Assert.AreEqual(TransferEffect.Move, plan.Effect);
        Assert.AreEqual(path, plan.SourcePath);
        Assert.AreEqual("router payload", plan.ExpectedText);
        Assert.IsTrue(plan.Target.SameObjectAs(NotepadIdentity));
        Assert.IsTrue(plan.SourceIdentity.SameObjectAs(fsIdentity));
        Assert.AreEqual("notepad", plan.RecipeName);
    }

    [TestMethod]
    public void NotepadRecipeMatchesNotepadWindowOnly()
    {
        NotepadImportRecipe recipe = new();
        Assert.IsTrue(recipe.Matches(NotepadIdentity));
        Assert.IsFalse(recipe.Matches(OtherIdentity));
    }

    private static SourceSnapshot PhysicalFile(string name, string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), "compuse-appsurface-router", name);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, contents);
        return new SourceSnapshot(
            path,
            SourceStatus.PhysicalFile,
            new FilesystemIdentity(path, volumeSerialNumber: 1, fileIndex: (ulong)name.Length + 1),
            contents.Length);
    }
}
