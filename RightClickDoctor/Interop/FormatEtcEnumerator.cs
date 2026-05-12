using System.Runtime.InteropServices.ComTypes;

namespace RightClickDoctor.Interop;

internal sealed class FormatEtcEnumerator : IEnumFORMATETC
{
    private readonly FORMATETC[] _formats;
    private int _index;

    public FormatEtcEnumerator(IEnumerable<FORMATETC> formats)
    {
        _formats = formats.ToArray();
    }

    public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
    {
        var fetched = 0;
        while (fetched < celt && _index < _formats.Length)
        {
            rgelt[fetched] = _formats[_index];
            fetched++;
            _index++;
        }

        if (pceltFetched is { Length: > 0 })
        {
            pceltFetched[0] = fetched;
        }

        return fetched == celt ? NativeMethods.SOk : NativeMethods.SFalse;
    }

    public int Skip(int celt)
    {
        _index = Math.Min(_index + celt, _formats.Length);
        return _index < _formats.Length ? NativeMethods.SOk : NativeMethods.SFalse;
    }

    public int Reset()
    {
        _index = 0;
        return NativeMethods.SOk;
    }

    public void Clone(out IEnumFORMATETC newEnum)
    {
        newEnum = new FormatEtcEnumerator(_formats)
        {
            _index = _index
        };
    }
}
