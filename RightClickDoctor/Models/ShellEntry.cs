using Microsoft.Win32;

namespace RightClickDoctor.Models;

public sealed class ShellEntry
{
    public ShellEntryKind Kind { get; init; }

    public string Name { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Context { get; init; } = string.Empty;

    public Guid? Clsid { get; init; }

    public RegistryHive RootHive { get; init; }

    public RegistryView RegistryView { get; init; }

    public string KeyPath { get; init; } = string.Empty;

    public string TargetPath { get; set; } = string.Empty;

    public string ThreadingModel { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;

    public bool IsMicrosoft { get; set; }

    public bool IsBlocked { get; set; }

    public bool IsLegacyDisabled { get; set; }

    public ProbeStatus ProbeStatus { get; set; } = ProbeStatus.NotRun;

    public double? CreateInstanceMs { get; set; }

    public double? InitializeMs { get; set; }

    public double? QueryContextMenuMs { get; set; }

    public double? TotalMs { get; set; }

    public int? MenuItems { get; set; }

    public string LastError { get; set; } = string.Empty;

    public string ClsidText => Clsid?.ToString("B").ToUpperInvariant() ?? string.Empty;

    public string KindText => Kind switch
    {
        ShellEntryKind.ContextMenuHandler => "COM handler",
        ShellEntryKind.ExplorerCommandHandler => "Explorer command",
        _ => "Static verb"
    };

    public string HiveText => RootHive switch
    {
        RegistryHive.CurrentUser => "HKCU",
        RegistryHive.LocalMachine => "HKLM",
        _ => RootHive.ToString()
    };

    public string ViewText => RegistryView switch
    {
        RegistryView.Registry64 => "64-bit",
        RegistryView.Registry32 => "32-bit",
        _ => "Default"
    };

    public string RegistryPath => $@"{HiveText}\{KeyPath}";

    public string StateText
    {
        get
        {
            if (IsBlocked)
            {
                return "Blocked";
            }

            if (IsLegacyDisabled)
            {
                return "LegacyDisabled";
            }

            return "Enabled";
        }
    }

    public bool CanProbe => Kind == ShellEntryKind.ContextMenuHandler
        && Clsid.HasValue
        && !IsBlocked
        && RegistryView != RegistryView.Registry32;

    public string TotalMsText => TotalMs.HasValue ? TotalMs.Value.ToString("0.0") : string.Empty;

    public string CreateInstanceMsText => CreateInstanceMs.HasValue ? CreateInstanceMs.Value.ToString("0.0") : string.Empty;

    public string InitializeMsText => InitializeMs.HasValue ? InitializeMs.Value.ToString("0.0") : string.Empty;

    public string QueryContextMenuMsText => QueryContextMenuMs.HasValue ? QueryContextMenuMs.Value.ToString("0.0") : string.Empty;

    public string Risk
    {
        get
        {
            if (IsBlocked || IsLegacyDisabled)
            {
                return "Disabled";
            }

            if (ProbeStatus == ProbeStatus.Timeout)
            {
                return "Timeout";
            }

            if (ProbeStatus == ProbeStatus.Failed)
            {
                return "Error";
            }

            if (!TotalMs.HasValue)
            {
                return IsMicrosoft ? "System" : "Unknown";
            }

            return TotalMs.Value switch
            {
                >= 1000 => "Severe",
                >= 300 => "Slow",
                >= 75 => "Watch",
                _ => "OK"
            };
        }
    }

    public void ApplyProbe(ProbeResult result)
    {
        ProbeStatus = result.TimedOut ? ProbeStatus.Timeout : result.Success ? ProbeStatus.Success : ProbeStatus.Failed;
        CreateInstanceMs = result.CreateInstanceMs;
        InitializeMs = result.InitializeMs;
        QueryContextMenuMs = result.QueryContextMenuMs;
        TotalMs = result.TotalMs;
        MenuItems = result.MenuItems;
        LastError = result.Error ?? string.Empty;
    }
}
