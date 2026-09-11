using Compuse.Discovery;
using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.ApplicationSurface.Tests;

[TestClass]
public sealed class NotepadImportBackendTests
{
    private static readonly WindowIdentity Identity = new(
        processId: 4242,
        hwnd: new nint(0xABCD),
        processImageName: "notepad.exe",
        windowClass: null,
        windowTitle: "Untitled - Notepad",
        runtimeIdKey: "42.1234");

    private static readonly UiaWindow Window = new(
        processId: 4242,
        hwnd: new nint(0xABCD),
        title: "Untitled - Notepad",
        runtimeIdKey: "42.1234",
        isDialog: false,
        className: null);

    private static readonly UiaWindow Dialog = new(
        processId: 4242,
        hwnd: new nint(0xCDEF),
        title: "Open",
        runtimeIdKey: "42.5678",
        isDialog: true,
        className: null);

    [TestMethod]
    public async Task CopyImportCommitsWithDocumentTextObservation()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = true,
            InvokeOpenResult = true,
            DialogClosedResult = true,
            DocumentText = "payload one"
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.DocumentRead, execution.LastPhase);
        Assert.IsTrue(execution.TargetAlive);
        Assert.IsNotNull(execution.Observation);
        Assert.IsTrue(execution.Observation.DocumentTextMatches);
        Assert.IsFalse(execution.Observation.SourceRemoved);
        Assert.IsTrue(session.Calls.Contains("dispose"));
    }

    [TestMethod]
    public async Task MoveImportRemovesSourceAfterVerification()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = true,
            InvokeOpenResult = true,
            DialogClosedResult = true,
            DocumentText = "payload one"
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Move, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.SourceRemoved, execution.LastPhase);
        Assert.IsTrue(execution.Observation!.DocumentTextMatches);
        Assert.IsTrue(execution.Observation.SourceRemoved);
        Assert.IsFalse(System.IO.File.Exists(source));
    }

    [TestMethod]
    public async Task MoveImportKeepsSourceWhenRemovalFails()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = true,
            InvokeOpenResult = true,
            DialogClosedResult = true,
            DocumentText = "payload one"
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        // Lock the source file so deletion fails on Windows.
        ApplicationImportPlan plan = CreatePlan(TransferEffect.Move, source, expectedText: "payload one");
        using FileStream lockHandle = new(source, FileMode.Open, FileAccess.Read, FileShare.None);
        ApplicationImportExecution execution = await backend.ExecuteAsync(plan, CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.DocumentRead, execution.LastPhase);
        Assert.IsTrue(execution.Observation!.DocumentTextMatches);
        Assert.IsFalse(execution.Observation.SourceRemoved);
        Assert.IsTrue(System.IO.File.Exists(source));
    }

    [TestMethod]
    public async Task AttachFailureReportsNonePhaseAndTargetGone()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new() { AttachResult = null };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.None, execution.LastPhase);
        Assert.IsFalse(execution.TargetAlive);
        Assert.IsNull(execution.Observation);
        Assert.AreEqual(1, session.Calls.Count(static call => call == "attach"));
    }

    [TestMethod]
    public async Task MissingMenuActionReportsWindowResolved()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = false,
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.WindowResolved, execution.LastPhase);
        Assert.IsNull(execution.Observation);
        Assert.IsFalse(session.Calls.Contains("wait-dialog"));
    }

    [TestMethod]
    public async Task MissingDialogReportsMenuInvoked()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = null,
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.MenuInvoked, execution.LastPhase);
        Assert.IsNull(execution.Observation);
    }

    [TestMethod]
    public async Task FailedSetValueCancelsDialogAndReportsDialogShown()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = false,
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.DialogShown, execution.LastPhase);
        Assert.AreEqual(1, session.CancelCalls);
        Assert.IsFalse(session.Calls.Contains("invoke-open"));
    }

    [TestMethod]
    public async Task FailedInvokeCancelsDialogAndReportsFileNameSet()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = true,
            InvokeOpenResult = false,
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.FileNameSet, execution.LastPhase);
        Assert.AreEqual(1, session.CancelCalls);
        Assert.IsFalse(session.Calls.Contains("wait-closed"));
    }

    [TestMethod]
    public async Task DialogNotClosingReportsOpenInvoked()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = true,
            InvokeOpenResult = true,
            DialogClosedResult = false,
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.OpenInvoked, execution.LastPhase);
        Assert.IsNull(execution.Observation);
        Assert.IsFalse(session.Calls.Contains("read-text"));
    }

    [TestMethod]
    public async Task TextMismatchReportsDocumentReadWithoutMatch()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "expected content");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = true,
            InvokeOpenResult = true,
            DialogClosedResult = true,
            DocumentText = "different content",
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.DocumentRead, execution.LastPhase);
        Assert.IsFalse(execution.Observation!.DocumentTextMatches);
        Assert.AreEqual("different content", execution.Observation.DocumentText);
        Assert.IsFalse(execution.Observation.SourceRemoved);
    }

    [TestMethod]
    public async Task UnreadableTextReportsDocumentReadWithoutMatch()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "expected content");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = true,
            InvokeOpenResult = true,
            DialogClosedResult = true,
            DocumentText = null,
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.AreEqual(ApplicationImportPhase.DocumentRead, execution.LastPhase);
        Assert.IsFalse(execution.Observation!.DocumentTextMatches);
        Assert.IsNull(execution.Observation.DocumentText);
    }

    [TestMethod]
    public async Task TextComparisonNormalizesLineEndings()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "line one\nline two");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = true,
            DialogResult = Dialog,
            SetFileNameResult = true,
            InvokeOpenResult = true,
            DialogClosedResult = true,
            DocumentText = "line one\r\nline two\r\n",
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), CancellationToken.None);

        Assert.IsTrue(execution.Observation!.DocumentTextMatches);
    }

    [TestMethod]
    public async Task PreCancelledTokenDoesNotAttemptImport()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new() { AttachResult = Window };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));
        using CancellationTokenSource cts = new();
        cts.Cancel();

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), cts.Token);

        Assert.AreEqual(ApplicationImportPhase.None, execution.LastPhase);
        Assert.IsTrue(execution.Cancelled);
        Assert.IsFalse(session.Calls.Contains("menu"));
    }

    [TestMethod]
    public async Task CancellationDuringFlowMarksExecutionCancelled()
    {
        using TempTree tree = new();
        string source = tree.File("a.txt", "payload one");
        ScriptedUiaSession session = new()
        {
            AttachResult = Window,
            MenuResult = false,
        };
        NotepadImportBackend backend = new(new ScriptedSessionFactory(session));
        using CancellationTokenSource cts = new();
        cts.Cancel();

        ApplicationImportExecution execution = await backend.ExecuteAsync(CreatePlan(TransferEffect.Copy, source), cts.Token);

        Assert.AreEqual(ApplicationImportPhase.None, execution.LastPhase);
        Assert.IsTrue(execution.Cancelled);
    }

    private static ApplicationImportPlan CreatePlan(TransferEffect effect, string source, string? expectedText = null)
    {
        expectedText ??= System.IO.File.ReadAllText(source);
        FilesystemIdentity sourceIdentity = new(source, volumeSerialNumber: 1, fileIndex: 7);
        return new ApplicationImportPlan(
            effect,
            Identity,
            source,
            byteLength: expectedText.Length,
            expectedText,
            sourceIdentity,
            recipeName: "notepad");
    }
}
