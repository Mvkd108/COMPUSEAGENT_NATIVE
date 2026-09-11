using System.Runtime.InteropServices;

namespace Compuse.Filesystem;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class CancellationProgressSink : ShellNative.IFileOperationProgressSink
{
    private readonly CancellationToken _cancellationToken;

    internal CancellationProgressSink(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    public int StartOperations() => Check();

    public int FinishOperations(int hrResult) => ShellNative.Ok;

    public int PreRenameItem(uint dwFlags, ShellNative.IShellItem? psiItem, string? pszNewName) => Check();

    public int PostRenameItem(
        uint dwFlags,
        ShellNative.IShellItem? psiItem,
        string? pszNewName,
        int hrRename,
        ShellNative.IShellItem? psiNewlyCreated) => ShellNative.Ok;

    public int PreMoveItem(
        uint dwFlags,
        ShellNative.IShellItem? psiItem,
        ShellNative.IShellItem? psiDestinationFolder,
        string? pszNewName) => Check();

    public int PostMoveItem(
        uint dwFlags,
        ShellNative.IShellItem? psiItem,
        ShellNative.IShellItem? psiDestinationFolder,
        string? pszNewName,
        int hrMove,
        ShellNative.IShellItem? psiNewlyCreated) => ShellNative.Ok;

    public int PreCopyItem(
        uint dwFlags,
        ShellNative.IShellItem? psiItem,
        ShellNative.IShellItem? psiDestinationFolder,
        string? pszNewName) => Check();

    public int PostCopyItem(
        uint dwFlags,
        ShellNative.IShellItem? psiItem,
        ShellNative.IShellItem? psiDestinationFolder,
        string? pszNewName,
        int hrCopy,
        ShellNative.IShellItem? psiNewlyCreated) => ShellNative.Ok;

    public int PreDeleteItem(uint dwFlags, ShellNative.IShellItem? psiItem) => Check();

    public int PostDeleteItem(uint dwFlags, ShellNative.IShellItem? psiItem, int hrDelete) => ShellNative.Ok;

    public int PreNewItem(uint dwFlags, ShellNative.IShellItem? psiDestinationFolder, string? pszNewName) =>
        Check();

    public int PostNewItem(
        uint dwFlags,
        ShellNative.IShellItem? psiDestinationFolder,
        string? pszNewName,
        string? pszTemplateName,
        uint dwFileAttributes,
        int hrNew,
        ShellNative.IShellItem? psiNewItem) => ShellNative.Ok;

    public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => Check();

    public int ResetTimer() => ShellNative.Ok;

    public int PauseTimer() => ShellNative.Ok;

    public int ResumeTimer() => ShellNative.Ok;

    private int Check()
    {
        try
        {
            return _cancellationToken.IsCancellationRequested ? ShellNative.Abort : ShellNative.Ok;
        }
        catch (Exception)
        {
            return ShellNative.Abort;
        }
    }
}
