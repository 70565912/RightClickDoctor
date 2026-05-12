using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace RightClickDoctor.Interop;

internal sealed class ShellDataObject : System.Runtime.InteropServices.ComTypes.IDataObject
{
    private readonly string[] _paths;

    public ShellDataObject(IEnumerable<string> paths)
    {
        _paths = paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).ToArray();
    }

    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        if (QueryGetData(ref format) != NativeMethods.SOk)
        {
            throw new COMException("Unsupported clipboard format.", NativeMethods.DvEFormatEtc);
        }

        medium = new STGMEDIUM
        {
            tymed = (TYMED)NativeMethods.TymedHglobal,
            unionmember = BuildHdrop(),
            pUnkForRelease = null
        };
    }

    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
    {
        throw new COMException("GetDataHere is not implemented.", NativeMethods.ENotImpl);
    }

    public int QueryGetData(ref FORMATETC format)
    {
        var supportsHdrop = format.cfFormat == NativeMethods.CfHdrop
            && ((int)format.tymed & NativeMethods.TymedHglobal) != 0;

        return supportsHdrop ? NativeMethods.SOk : NativeMethods.DvEFormatEtc;
    }

    public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = IntPtr.Zero;
        return NativeMethods.SFalse;
    }

    public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
    {
        throw new COMException("SetData is not implemented.", NativeMethods.ENotImpl);
    }

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET)
        {
            throw new COMException("Only DATADIR_GET is supported.", NativeMethods.ENotImpl);
        }

        return new FormatEtcEnumerator(new[]
        {
            new FORMATETC
            {
                cfFormat = NativeMethods.CfHdrop,
                dwAspect = (DVASPECT)NativeMethods.DvAspectContent,
                lindex = -1,
                ptd = IntPtr.Zero,
                tymed = (TYMED)NativeMethods.TymedHglobal
            }
        });
    }

    public int DAdvise(ref FORMATETC format, ADVF advf, IAdviseSink adviseSink, out int connection)
    {
        connection = 0;
        return NativeMethods.OleEAdviseNotSupported;
    }

    public void DUnadvise(int connection)
    {
        throw new COMException("Advisory connections are not supported.", NativeMethods.OleEAdviseNotSupported);
    }

    public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
    {
        enumAdvise = null!;
        return NativeMethods.OleEAdviseNotSupported;
    }

    private IntPtr BuildHdrop()
    {
        var dropFilesSize = Marshal.SizeOf<DropFiles>();
        var payload = Encoding.Unicode.GetBytes(string.Join('\0', _paths) + "\0\0");
        var totalSize = checked(dropFilesSize + payload.Length);
        var handle = NativeMethods.GlobalAlloc(NativeMethods.GmemMoveable | NativeMethods.GmemZeroinit, (UIntPtr)totalSize);
        if (handle == IntPtr.Zero)
        {
            throw new OutOfMemoryException("GlobalAlloc failed while building CF_HDROP data.");
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            throw new OutOfMemoryException("GlobalLock failed while building CF_HDROP data.");
        }

        try
        {
            Marshal.StructureToPtr(new DropFiles
            {
                pFiles = (uint)dropFilesSize,
                fNC = 0,
                fWide = 1
            }, pointer, false);
            Marshal.Copy(payload, 0, IntPtr.Add(pointer, dropFilesSize), payload.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        return handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DropFiles
    {
        public uint pFiles;
        public int x;
        public int y;
        public int fNC;
        public int fWide;
    }
}
