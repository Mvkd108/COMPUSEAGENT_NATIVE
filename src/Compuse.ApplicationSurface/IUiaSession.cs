using Compuse.Requests;
using Compuse.Routing;

namespace Compuse.ApplicationSurface;

public interface IUiaSession : IDisposable
{
    public WindowResolution ResolveWindow(ApplicationSurfaceTarget target, CancellationToken cancellationToken);

    public UiaWindow? AttachWindow(WindowIdentity identity, CancellationToken cancellationToken);

    public bool WindowStillValid(UiaWindow window, CancellationToken cancellationToken);

    public bool ExpandMenuAndInvokeItem(UiaWindow window, string menuName, string itemName, CancellationToken cancellationToken);

    public UiaWindow? WaitForWindow(UiaWindow owner, string windowTitle, TimeSpan timeout, CancellationToken cancellationToken);

    public bool SetValueOnEditByAutomationId(UiaWindow window, string automationId, string value, CancellationToken cancellationToken);

    public bool InvokeSplitButtonByAutomationIdAndName(UiaWindow window, string automationId, string buttonName, CancellationToken cancellationToken);

    public bool CloseDialogByButtonName(UiaWindow dialog, string buttonName, CancellationToken cancellationToken);

    public bool DismissModalDialogByButtonName(int processId, string dialogTitle, string buttonName, CancellationToken cancellationToken);

    public bool WaitForWindowClosed(UiaWindow window, TimeSpan timeout, CancellationToken cancellationToken);

    public string? ReadDocumentText(UiaWindow window, TimeSpan timeout, CancellationToken cancellationToken);
}
