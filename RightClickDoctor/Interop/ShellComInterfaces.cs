using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace RightClickDoctor.Interop;

[ComImport]
[Guid("000214E8-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellExtInit
{
    [PreserveSig]
    int Initialize(IntPtr pidlFolder, System.Runtime.InteropServices.ComTypes.IDataObject dataObject, IntPtr hkeyProgId);
}

[ComImport]
[Guid("000214E4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    [PreserveSig]
    int QueryContextMenu(IntPtr menu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);

    [PreserveSig]
    int InvokeCommand(IntPtr commandInfo);

    [PreserveSig]
    int GetCommandString(UIntPtr idCmd, uint flags, IntPtr reserved, IntPtr commandString, int cch);
}
