using System.Runtime.InteropServices;

namespace RightClickDoctor.Interop;

internal static class NativeMethods
{
    public const int SOk = 0;
    public const int SFalse = 1;
    public const int GmemMoveable = 0x0002;
    public const int GmemZeroinit = 0x0040;
    public const short CfHdrop = 15;
    public const int DvAspectContent = 1;
    public const int TymedHglobal = 1;

    public const int DvEFormatEtc = unchecked((int)0x80040064);
    public const int ENotImpl = unchecked((int)0x80004001);
    public const int OleEAdviseNotSupported = unchecked((int)0x80040003);

    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(int flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr hGlobal);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(IntPtr hGlobal);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    public static bool Succeeded(int hresult)
    {
        return hresult >= 0;
    }

    public static void NotifyShellAssociationsChanged()
    {
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
    }
}
