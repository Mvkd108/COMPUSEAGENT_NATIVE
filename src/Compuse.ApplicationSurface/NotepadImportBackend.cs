using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public sealed class NotepadImportBackend : IApplicationImportBackend
{
    private const string FileMenuName = "File";
    private const string OpenItemName = "Open";
    private const string OpenDialogTitle = "Open";
    private const string FileNameEditAutomationId = "1148";
    private const string OpenButtonAutomationId = "1";
    private const string OpenButtonName = "Open";
    private const string CancelButtonName = "Cancel";
    private const string ErrorDialogTitle = "Notepad";
    private const string ErrorDialogButtonName = "OK";
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly IUiaSessionFactory _sessionFactory;

    public NotepadImportBackend()
        : this(FlaUiSessionFactory.Instance)
    {
    }

    internal NotepadImportBackend(IUiaSessionFactory sessionFactory)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        _sessionFactory = sessionFactory;
    }

    public ValueTask<ApplicationImportExecution> ExecuteAsync(
        ApplicationImportPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        TaskCompletionSource<ApplicationImportExecution> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(new ParameterizedThreadStart(static state =>
        {
            StaWork work = (StaWork)state!;
            try
            {
                using IUiaSession session = work.Factory.Create();
                work.Completion.SetResult(Execute(work.Plan, session, work.CancellationToken));
            }
            catch (Exception ex)
            {
                work.Completion.SetException(ex);
            }
        }))
        {
            IsBackground = false,
            Name = "compuse-uia-import"
        };
        thread.Start(new StaWork(_sessionFactory, plan, completion, cancellationToken));
        return new ValueTask<ApplicationImportExecution>(completion.Task);
    }

    private static ApplicationImportExecution Execute(
        ApplicationImportPlan plan,
        IUiaSession session,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ApplicationImportExecution(ApplicationImportPhase.None, targetAlive: false, observation: null, cancelled: true);
        }

        UiaWindow? window = session.AttachWindow(plan.Target, cancellationToken);
        if (window is null)
        {
            return new ApplicationImportExecution(
                ApplicationImportPhase.None,
                targetAlive: false,
                observation: null,
                cancellationToken.IsCancellationRequested);
        }

        if (!session.ExpandMenuAndInvokeItem(window, FileMenuName, OpenItemName, cancellationToken))
        {
            return Finish(ApplicationImportPhase.WindowResolved, session, window, cancellationToken);
        }

        UiaWindow? dialog = session.WaitForWindow(window, OpenDialogTitle, DialogTimeout, cancellationToken);
        if (dialog is null)
        {
            return Finish(ApplicationImportPhase.MenuInvoked, session, window, cancellationToken);
        }

        if (!session.SetValueOnEditByAutomationId(dialog, FileNameEditAutomationId, plan.SourcePath, cancellationToken))
        {
            _ = session.CloseDialogByButtonName(dialog, CancelButtonName, CancellationToken.None);
            _ = session.DismissModalDialogByButtonName(plan.Target.ProcessId, ErrorDialogTitle, ErrorDialogButtonName, CancellationToken.None);
            return Finish(ApplicationImportPhase.DialogShown, session, window, cancellationToken);
        }

        if (!session.InvokeSplitButtonByAutomationIdAndName(dialog, OpenButtonAutomationId, OpenButtonName, cancellationToken))
        {
            _ = session.CloseDialogByButtonName(dialog, CancelButtonName, CancellationToken.None);
            _ = session.DismissModalDialogByButtonName(plan.Target.ProcessId, ErrorDialogTitle, ErrorDialogButtonName, CancellationToken.None);
            return Finish(ApplicationImportPhase.FileNameSet, session, window, cancellationToken);
        }

        if (!session.WaitForWindowClosed(dialog, CloseTimeout, cancellationToken))
        {
            _ = session.DismissModalDialogByButtonName(plan.Target.ProcessId, ErrorDialogTitle, ErrorDialogButtonName, CancellationToken.None);
            return Finish(ApplicationImportPhase.OpenInvoked, session, window, cancellationToken);
        }

        string? text = session.ReadDocumentText(window, ReadTimeout, cancellationToken);
        bool matches = text is not null && Normalize(text) == Normalize(plan.ExpectedText);
        if (plan.Effect == TransferEffect.Move && matches)
        {
            bool removed = TryRemoveSource(plan.SourcePath);
            return new ApplicationImportExecution(
                removed ? ApplicationImportPhase.SourceRemoved : ApplicationImportPhase.DocumentRead,
                TargetAlive(session, window),
                new ImportObservation(plan.SourcePath, text, matches, removed),
                cancellationToken.IsCancellationRequested);
        }

        return new ApplicationImportExecution(
            ApplicationImportPhase.DocumentRead,
            TargetAlive(session, window),
            new ImportObservation(plan.SourcePath, text, matches, sourceRemoved: false),
            cancellationToken.IsCancellationRequested);
    }

    private static ApplicationImportExecution Finish(
        ApplicationImportPhase phase,
        IUiaSession session,
        UiaWindow window,
        CancellationToken cancellationToken) =>
        new(phase, TargetAlive(session, window), observation: null, cancellationToken.IsCancellationRequested);

    private static bool TargetAlive(IUiaSession session, UiaWindow window) =>
        session.WindowStillValid(window, CancellationToken.None);

    private static bool TryRemoveSource(string sourcePath)
    {
        try
        {
            System.IO.File.Delete(sourcePath);
            return !System.IO.File.Exists(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private sealed record StaWork(
        IUiaSessionFactory Factory,
        ApplicationImportPlan Plan,
        TaskCompletionSource<ApplicationImportExecution> Completion,
        CancellationToken CancellationToken);
}
