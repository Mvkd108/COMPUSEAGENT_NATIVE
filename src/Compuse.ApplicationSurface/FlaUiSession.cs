using System.Diagnostics;
using Compuse.Requests;
using Compuse.Routing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Compuse.ApplicationSurface;

public sealed partial class FlaUiSession : IUiaSession
{
    private const string MenuPopupWindowTitle = "Pop-up";
    private const int PollIntervalMs = 100;

    private readonly UIA3Automation _automation;

    public FlaUiSession()
    {
        _automation = new UIA3Automation();
    }

    public void Dispose() => _automation.Dispose();

    public WindowResolution ResolveWindow(ApplicationSurfaceTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        string processName = Path.GetFileNameWithoutExtension(target.ProcessImageName);
        if (target.HwndHint is ulong hint && hint != 0)
        {
            AutomationElement? element = FromHandleSafely(new nint((long)hint));
            if (element is null || !ProcessMatches(element, processName))
            {
                return WindowResolution.ForNotFound();
            }

            UiaWindow? window = ToUiaWindow(element, isDialog: false);
            return window is null ? WindowResolution.ForNotFound() : WindowResolution.ForWindow(window);
        }

        List<UiaWindow> matches = [];
        foreach (int pid in CandidateProcessIds(processName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.ProcessIdHint is uint pidHint && pid != (int)pidHint)
            {
                continue;
            }

            foreach (nint hwnd in EnumTopLevelWindows(pid))
            {
                AutomationElement? element = FromHandleSafely(hwnd);
                UiaWindow? window = element is null ? null : ToUiaWindow(element, isDialog: false);
                if (window is null)
                {
                    continue;
                }

                if (target.WindowClass is not null
                    && !string.Equals(window.ClassName, target.WindowClass, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (target.WindowTitle is not null
                    && !string.Equals(window.Title, target.WindowTitle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matches.Add(window);
            }
        }

        return matches.Count switch
        {
            0 => WindowResolution.ForNotFound(),
            1 => WindowResolution.ForWindow(matches[0]),
            _ => WindowResolution.ForAmbiguous()
        };
    }

    public UiaWindow? AttachWindow(WindowIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        AutomationElement? element = FromHandleSafely(identity.Hwnd);
        if (element is null)
        {
            return null;
        }

        if (element.Properties.ProcessId.Value != identity.ProcessId
            || !string.Equals(RuntimeIdKey(element), identity.RuntimeIdKey, StringComparison.Ordinal))
        {
            return null;
        }

        return ToUiaWindow(element, isDialog: false);
    }

    public bool WindowStillValid(UiaWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();

        AutomationElement? element = FromHandleSafely(window.Hwnd);
        if (element is null || element.Properties.ProcessId.Value != window.ProcessId)
        {
            return false;
        }

        if (!string.Equals(RuntimeIdKey(element), window.RuntimeIdKey, StringComparison.Ordinal))
        {
            return false;
        }

        return IsProcessAlive(window.ProcessId);
    }

    public bool ExpandMenuAndInvokeItem(
        UiaWindow window,
        string menuName,
        string itemName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(menuName);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        AutomationElement? mainWindow = FromHandleSafely(window.Hwnd);
        if (mainWindow is null)
        {
            return false;
        }

        AutomationElement? menu = mainWindow.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.MenuItem).And(cf.ByName(menuName)));
        if (menu is null || !menu.Patterns.ExpandCollapse.IsSupported)
        {
            return false;
        }

        menu.Patterns.ExpandCollapse.Pattern.Expand();
        cancellationToken.ThrowIfCancellationRequested();

        AutomationElement? popup = FindMenuPopup(window.ProcessId, itemName, cancellationToken);
        if (popup is null)
        {
            return false;
        }

        AutomationElement? item = popup.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.MenuItem).And(cf.ByName(itemName)));
        if (item is null || !item.Patterns.Invoke.IsSupported)
        {
            return false;
        }

        item.Patterns.Invoke.Pattern.Invoke();
        return true;
    }

    public UiaWindow? WaitForWindow(
        UiaWindow owner,
        string windowTitle,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            foreach (AutomationElement element in _automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByProcessId(owner.ProcessId).And(cf.ByControlType(ControlType.Window))))
            {
                UiaWindow? dialog = ToUiaWindow(element, isDialog: true);
                if (dialog is not null
                    && dialog.Hwnd != owner.Hwnd
                    && string.Equals(dialog.Title, windowTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return dialog;
                }
            }

            Thread.Sleep(PollIntervalMs);
        }

        return null;
    }

    public bool SetValueOnEditByAutomationId(
        UiaWindow window,
        string automationId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        AutomationElement? windowElement = FromHandleSafely(window.Hwnd);
        if (windowElement is null)
        {
            return false;
        }

        AutomationElement? edit = null;
        foreach (AutomationElement candidate in windowElement.FindAllDescendants(cf =>
            cf.ByAutomationId(automationId)))
        {
            if (candidate.Properties.ControlType.Value == ControlType.Edit)
            {
                edit = candidate;
                break;
            }
        }

        if (edit is null || !edit.Patterns.Value.IsSupported)
        {
            return false;
        }

        edit.Patterns.Value.Pattern.SetValue(value);
        return string.Equals(
            Safe(() => edit.Patterns.Value.Pattern.Value, null),
            value,
            StringComparison.Ordinal);
    }

    public bool InvokeSplitButtonByAutomationIdAndName(
        UiaWindow window,
        string automationId,
        string buttonName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonName);
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        AutomationElement? windowElement = FromHandleSafely(window.Hwnd);
        if (windowElement is null)
        {
            return false;
        }

        AutomationElement? primary = null;
        foreach (AutomationElement candidate in windowElement.FindAllDescendants(cf =>
            cf.ByControlType(ControlType.SplitButton)))
        {
            if (Safe(() => candidate.Name, string.Empty) == buttonName
                && Safe(() => candidate.Properties.AutomationId.Value, string.Empty) == automationId)
            {
                primary = candidate;
                break;
            }
        }

        if (primary is null)
        {
            return false;
        }

        if (primary.Patterns.Invoke.IsSupported)
        {
            primary.Patterns.Invoke.Pattern.Invoke();
            return true;
        }

        AutomationElement? child = primary.FindFirstChild(cf => cf.ByControlType(ControlType.SplitButton));
        if (child is not null && child.Patterns.Invoke.IsSupported)
        {
            child.Patterns.Invoke.Pattern.Invoke();
            return true;
        }

        return false;
    }

    public bool CloseDialogByButtonName(UiaWindow dialog, string buttonName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonName);
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        AutomationElement? dialogElement = FromHandleSafely(dialog.Hwnd);
        if (dialogElement is null)
        {
            return true;
        }

        AutomationElement? button = dialogElement.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName(buttonName)));
        if (button is not null && button.Patterns.Invoke.IsSupported)
        {
            button.Patterns.Invoke.Pattern.Invoke();
            return true;
        }

        return false;
    }

    public bool DismissModalDialogByButtonName(
        int processId,
        string dialogTitle,
        string buttonName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonName);
        if (processId <= 0 || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        foreach (AutomationElement element in _automation.GetDesktop()
            .FindAllDescendants(cf => cf.ByProcessId(processId).And(cf.ByControlType(ControlType.Window))))
        {
            if (Safe(() => element.Name, string.Empty) != dialogTitle)
            {
                continue;
            }

            AutomationElement? button = element.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName(buttonName)));
            if (button is not null && button.Patterns.Invoke.IsSupported)
            {
                button.Patterns.Invoke.Pattern.Invoke();
                return true;
            }
        }

        return false;
    }

    public bool WaitForWindowClosed(UiaWindow window, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            AutomationElement? element = FromHandleSafely(window.Hwnd);
            if (element is null || element.Properties.ProcessId.Value != window.ProcessId)
            {
                return true;
            }

            Thread.Sleep(PollIntervalMs);
        }

        return false;
    }

    public string? ReadDocumentText(UiaWindow window, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            AutomationElement? mainWindow = FromHandleSafely(window.Hwnd);
            AutomationElement? document = mainWindow?.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Document));
            if (document is not null)
            {
                if (document.Patterns.Text.IsSupported)
                {
                    return document.Patterns.Text.Pattern.DocumentRange.GetText(int.MaxValue);
                }

                if (document.Patterns.Value.IsSupported)
                {
                    return document.Patterns.Value.Pattern.Value;
                }

                return null;
            }

            Thread.Sleep(PollIntervalMs);
        }

        return null;
    }

    private AutomationElement? FindMenuPopup(int processId, string itemName, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            foreach (AutomationElement element in _automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByProcessId(processId).And(cf.ByControlType(ControlType.Window))))
            {
                if (Safe(() => element.Name, string.Empty) != MenuPopupWindowTitle)
                {
                    continue;
                }

                AutomationElement? item = element.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.MenuItem).And(cf.ByName(itemName)));
                if (item is not null)
                {
                    return element;
                }
            }

            Thread.Sleep(PollIntervalMs);
        }

        return null;
    }

    private AutomationElement? FromHandleSafely(nint hwnd)
    {
        if (hwnd == nint.Zero)
        {
            return null;
        }

        try
        {
            return _automation.FromHandle(hwnd);
        }
        catch (Exception ex) when (ex is FlaUI.Core.Exceptions.ElementNotAvailableException
            or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static IEnumerable<int> CandidateProcessIds(string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception)
        {
            yield break;
        }

        foreach (Process process in processes)
        {
            using (process)
            {
                if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return process.Id;
                }
            }
        }
    }

    private static List<nint> EnumTopLevelWindows(int processId)
    {
        List<nint> handles = [];
        _ = EnumWindows((hwnd, param) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out uint ownerProcessId);
            if (ownerProcessId == processId)
            {
                handles.Add(hwnd);
            }

            return true;
        }, nint.Zero);
        return handles;
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private static bool ProcessMatches(AutomationElement element, string processName)
    {
        int pid = element.Properties.ProcessId.Value;
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            return string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string RuntimeIdKey(AutomationElement element)
    {
        int[]? runtimeId = Safe(() => element.Properties.RuntimeId.Value, null);
        return runtimeId is null ? string.Empty : string.Join(".", runtimeId);
    }

    private static UiaWindow? ToUiaWindow(AutomationElement element, bool isDialog)
    {
        int pid = element.Properties.ProcessId.Value;
        int nativeHandle = Safe(() => (int)element.Properties.NativeWindowHandle.Value, 0);
        if (pid <= 0 || nativeHandle == 0)
        {
            return null;
        }

        string title = Safe(() => element.Name, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string runtimeIdKey = RuntimeIdKey(element);
        if (string.IsNullOrEmpty(runtimeIdKey))
        {
            return null;
        }

        return new UiaWindow(
            pid,
            new nint(nativeHandle),
            title,
            runtimeIdKey,
            isDialog,
            Safe(() => element.ClassName, null));
    }

    private static T Safe<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
