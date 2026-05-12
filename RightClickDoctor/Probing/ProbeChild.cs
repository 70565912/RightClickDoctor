using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using RightClickDoctor.Interop;
using RightClickDoctor.Models;
using RightClickDoctor.Reports;

namespace RightClickDoctor.Probing;

public static class ProbeChild
{
    public static int Run(string requestPath)
    {
        var result = new ProbeResult();
        var total = Stopwatch.StartNew();
        object? instance = null;
        IntPtr menu = IntPtr.Zero;

        try
        {
            if (string.IsNullOrWhiteSpace(requestPath) || !File.Exists(requestPath))
            {
                throw new FileNotFoundException("Probe request file was not found.", requestPath);
            }

            var request = JsonSerializer.Deserialize<ProbeRequest>(File.ReadAllText(requestPath), ReportWriter.JsonOptions)
                ?? throw new InvalidOperationException("Probe request was empty.");
            result.Clsid = request.Clsid;
            result.Name = request.Name;

            result.Phase = "CreateInstance";
            var phase = Stopwatch.StartNew();
            var type = Type.GetTypeFromCLSID(request.Clsid, throwOnError: true)
                ?? throw new InvalidOperationException($"Unable to resolve CLSID {request.Clsid:B}.");
            instance = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Unable to create CLSID {request.Clsid:B}.");
            result.CreateInstanceMs = phase.Elapsed.TotalMilliseconds;

            var dataObject = new ShellDataObject(request.SamplePaths);

            if (instance is IShellExtInit shellExtInit)
            {
                result.Phase = "IShellExtInit.Initialize";
                phase.Restart();
                var hr = shellExtInit.Initialize(IntPtr.Zero, dataObject, IntPtr.Zero);
                result.InitializeMs = phase.Elapsed.TotalMilliseconds;
                result.InitializeHResult = hr;
                if (!NativeMethods.Succeeded(hr))
                {
                    result.Error = $"IShellExtInit.Initialize failed with HRESULT 0x{hr:X8}.";
                    return WriteResult(result, total);
                }
            }

            if (instance is not IContextMenu contextMenu)
            {
                result.Error = "COM object does not implement IContextMenu.";
                return WriteResult(result, total);
            }

            result.Phase = "IContextMenu.QueryContextMenu";
            menu = NativeMethods.CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreatePopupMenu failed.");
            }

            phase.Restart();
            var queryHr = contextMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, 0);
            result.QueryContextMenuMs = phase.Elapsed.TotalMilliseconds;
            result.QueryContextMenuHResult = queryHr;
            result.MenuItems = NativeMethods.GetMenuItemCount(menu);
            if (!NativeMethods.Succeeded(queryHr))
            {
                result.Error = $"IContextMenu.QueryContextMenu failed with HRESULT 0x{queryHr:X8}.";
                return WriteResult(result, total);
            }

            result.Success = true;
            return WriteResult(result, total);
        }
        catch (Exception ex)
        {
            result.Error = ex is COMException comException
                ? $"{ex.Message} HRESULT=0x{comException.HResult:X8}"
                : ex.Message;
            return WriteResult(result, total);
        }
        finally
        {
            if (menu != IntPtr.Zero)
            {
                NativeMethods.DestroyMenu(menu);
            }

            if (instance is not null && Marshal.IsComObject(instance))
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }
    }

    private static int WriteResult(ProbeResult result, Stopwatch total)
    {
        result.TotalMs = total.Elapsed.TotalMilliseconds;
        Console.WriteLine(JsonSerializer.Serialize(result, ReportWriter.JsonOptions));
        return result.Success ? 0 : 2;
    }
}
