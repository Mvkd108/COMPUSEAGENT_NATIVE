using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.ApplicationSurface.Tests;

internal sealed class ScriptedUiaSession : IUiaSession
{
    private readonly List<string> _calls = [];

    public WindowResolution ResolveResult { get; set; } = WindowResolution.ForNotFound();

    public UiaWindow? AttachResult { get; set; }

    public bool WindowValid { get; set; } = true;

    public bool MenuResult { get; set; }

    public UiaWindow? DialogResult { get; set; }

    public bool SetFileNameResult { get; set; }

    public bool InvokeOpenResult { get; set; }

    public bool CancelResult { get; set; } = true;

    public bool DismissResult { get; set; } = true;

    public bool DialogClosedResult { get; set; }

    public string? DocumentText { get; set; }

    public IReadOnlyList<string> Calls => _calls;

    public int CancelCalls { get; private set; }

    public int DismissCalls { get; private set; }

    public WindowResolution ResolveWindow(ApplicationSurfaceTarget target, CancellationToken cancellationToken)
    {
        _ = target;
        _calls.Add("resolve");
        return ResolveResult;
    }

    public UiaWindow? AttachWindow(WindowIdentity identity, CancellationToken cancellationToken)
    {
        _ = identity;
        _calls.Add("attach");
        return AttachResult;
    }

    public bool WindowStillValid(UiaWindow window, CancellationToken cancellationToken)
    {
        _ = window;
        _ = cancellationToken;
        _calls.Add("valid");
        return WindowValid;
    }

    public bool ExpandMenuAndInvokeItem(UiaWindow window, string menuName, string itemName, CancellationToken cancellationToken)
    {
        _ = window;
        _ = menuName;
        _ = itemName;
        _ = cancellationToken;
        _calls.Add("menu");
        return MenuResult;
    }

    public UiaWindow? WaitForWindow(UiaWindow owner, string windowTitle, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = owner;
        _ = windowTitle;
        _ = timeout;
        _ = cancellationToken;
        _calls.Add("wait-window");
        return DialogResult;
    }

    public bool SetValueOnEditByAutomationId(UiaWindow window, string automationId, string value, CancellationToken cancellationToken)
    {
        _ = window;
        _ = automationId;
        _ = value;
        _ = cancellationToken;
        _calls.Add("set-file");
        return SetFileNameResult;
    }

    public bool InvokeSplitButtonByAutomationIdAndName(UiaWindow window, string automationId, string buttonName, CancellationToken cancellationToken)
    {
        _ = window;
        _ = automationId;
        _ = buttonName;
        _ = cancellationToken;
        _calls.Add("invoke-open");
        return InvokeOpenResult;
    }

    public bool CloseDialogByButtonName(UiaWindow dialog, string buttonName, CancellationToken cancellationToken)
    {
        _ = dialog;
        _ = buttonName;
        _ = cancellationToken;
        _calls.Add("cancel");
        CancelCalls++;
        return CancelResult;
    }

    public bool DismissModalDialogByButtonName(int processId, string dialogTitle, string buttonName, CancellationToken cancellationToken)
    {
        _ = processId;
        _ = dialogTitle;
        _ = buttonName;
        _ = cancellationToken;
        _calls.Add("dismiss-error");
        DismissCalls++;
        return DismissResult;
    }

    public bool WaitForWindowClosed(UiaWindow window, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = window;
        _ = timeout;
        _ = cancellationToken;
        _calls.Add("wait-closed");
        return DialogClosedResult;
    }

    public string? ReadDocumentText(UiaWindow window, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = window;
        _ = timeout;
        _ = cancellationToken;
        _calls.Add("read-text");
        return DocumentText;
    }

    public void Dispose() => _calls.Add("dispose");
}

internal sealed class ScriptedSessionFactory : IUiaSessionFactory
{
    public ScriptedSessionFactory(ScriptedUiaSession session)
    {
        Session = session;
    }

    public ScriptedUiaSession Session { get; }

    public IUiaSession Create() => Session;
}
