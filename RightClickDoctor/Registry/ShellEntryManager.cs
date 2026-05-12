using System.Diagnostics;
using Microsoft.Win32;
using RightClickDoctor.Interop;
using RightClickDoctor.Models;

namespace RightClickDoctor.Registry;

public sealed class ShellEntryManager
{
    private const string BlockedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    public void Disable(ShellEntry entry)
    {
        if (entry.Kind == ShellEntryKind.ContextMenuHandler && entry.Clsid.HasValue)
        {
            BlockComHandler(entry);
            entry.IsBlocked = true;
            NativeMethods.NotifyShellAssociationsChanged();
            return;
        }

        SetLegacyDisable(entry, disabled: true);
        entry.IsLegacyDisabled = true;
        NativeMethods.NotifyShellAssociationsChanged();
    }

    public void Enable(ShellEntry entry)
    {
        if (entry.Kind == ShellEntryKind.ContextMenuHandler && entry.Clsid.HasValue)
        {
            UnblockComHandler(entry);
            entry.IsBlocked = false;
            NativeMethods.NotifyShellAssociationsChanged();
            return;
        }

        SetLegacyDisable(entry, disabled: false);
        entry.IsLegacyDisabled = false;
        NativeMethods.NotifyShellAssociationsChanged();
    }

    public void RestartExplorer()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                process.Kill();
                process.WaitForExit(5000);
            }
            catch
            {
                // Explorer can restart itself or refuse to exit; continue with the rest.
            }
            finally
            {
                process.Dispose();
            }
        }

        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    private static void BlockComHandler(ShellEntry entry)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(BlockedKeyPath, writable: true);
        key.SetValue(entry.ClsidText, $"Blocked by RightClickDoctor: {entry.DisplayName}", RegistryValueKind.String);
    }

    private static void UnblockComHandler(ShellEntry entry)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(BlockedKeyPath, writable: true);
        key?.DeleteValue(entry.ClsidText, throwOnMissingValue: false);
    }

    private static void SetLegacyDisable(ShellEntry entry, bool disabled)
    {
        using var baseKey = RegistryKey.OpenBaseKey(entry.RootHive, entry.RegistryView);
        using var key = baseKey.OpenSubKey(entry.KeyPath, writable: true)
            ?? throw new InvalidOperationException($"Registry key was not found: {entry.RegistryPath}");

        if (disabled)
        {
            key.SetValue("LegacyDisable", string.Empty, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("LegacyDisable", throwOnMissingValue: false);
        }
    }
}
