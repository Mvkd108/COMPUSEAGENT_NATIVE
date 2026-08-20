using System.Runtime.InteropServices;

namespace Compuse.Filesystem;

internal static class ShellNative
{
    internal const uint FofSilent = 0x0004;
    internal const uint FofNoConfirmation = 0x0010;
    internal const uint FofNoErrorUi = 0x0400;
    internal const uint FofNoConfirmMkdir = 0x0200;
    internal const uint FofxEarlyFailure = 0x00100000;
    internal const uint OperationFlags = FofSilent | FofNoConfirmation | FofNoErrorUi | FofNoConfirmMkdir | FofxEarlyFailure;

    internal static readonly Guid ShellItemIid = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
    internal static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        nint pbc,
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        [PreserveSig]
        public int BindToHandler(nint pbc, in Guid bhid, in Guid riid, out nint ppv);

        [PreserveSig]
        public int GetParent(out IShellItem ppsi);

        [PreserveSig]
        public int GetDisplayName(uint sigdnName, out nint ppszName);

        [PreserveSig]
        public int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        public int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        [PreserveSig]
        public int Advise(nint pfops, out uint pdwCookie);

        [PreserveSig]
        public int Unadvise(uint dwCookie);

        [PreserveSig]
        public int SetOperationFlags(uint dwOperationFlags);

        [PreserveSig]
        public int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

        [PreserveSig]
        public int SetProgressDialog(nint popd);

        [PreserveSig]
        public int SetProperties(nint pproparray);

        [PreserveSig]
        public int SetOwnerWindow(nint hwndOwner);

        [PreserveSig]
        public int ApplyPropertiesToItem(IShellItem psiItem);

        [PreserveSig]
        public int ApplyPropertiesToItems(nint punkItems);

        [PreserveSig]
        public int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, nint pfopsItem);

        [PreserveSig]
        public int RenameItems(nint pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig]
        public int MoveItem(
            IShellItem psiItem,
            IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            nint pfopsItem);

        [PreserveSig]
        public int MoveItems(nint punkItems, IShellItem psiDestinationFolder);

        [PreserveSig]
        public int CopyItem(
            IShellItem psiItem,
            IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName,
            nint pfopsItem);

        [PreserveSig]
        public int CopyItems(nint punkItems, IShellItem psiDestinationFolder);

        [PreserveSig]
        public int DeleteItem(IShellItem psiItem, nint pfopsItem);

        [PreserveSig]
        public int DeleteItems(nint punkItems);

        [PreserveSig]
        public int NewItem(
            IShellItem psiDestinationFolder,
            uint dwFileAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName,
            nint pfopsItem);

        [PreserveSig]
        public int PerformOperations();

        [PreserveSig]
        public int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    [ComImport]
    [Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class FileOperationCoclass
    {
    }

    internal static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            _ = Marshal.FinalReleaseComObject(comObject);
        }
    }
}
