using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RightClickDoctor.Models;

namespace RightClickDoctor.Registry;

public sealed class ShellEntryScanner
{
    private static readonly Regex GuidRegex = new(@"\{?[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}?", RegexOptions.Compiled);

    private static readonly string[] ContextMenuHandlerRoots =
    {
        @"*\shellex\ContextMenuHandlers",
        @"AllFileSystemObjects\shellex\ContextMenuHandlers",
        @"Directory\shellex\ContextMenuHandlers",
        @"Directory\Background\shellex\ContextMenuHandlers",
        @"Folder\shellex\ContextMenuHandlers",
        @"Drive\shellex\ContextMenuHandlers",
        @"DesktopBackground\shellex\ContextMenuHandlers"
    };

    private static readonly string[] StaticVerbRoots =
    {
        @"*\shell",
        @"AllFileSystemObjects\shell",
        @"Directory\shell",
        @"Directory\Background\shell",
        @"Folder\shell",
        @"Drive\shell",
        @"DesktopBackground\shell"
    };

    public IReadOnlyList<ShellEntry> Scan()
    {
        var blocked = ReadBlockedClsids();
        var approved = ReadApprovedNames();
        var entries = new List<ShellEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var location in GetScanLocations())
        {
            foreach (var root in ContextMenuHandlerRoots)
            {
                using var key = OpenClassesSubKey(location, root);
                if (key is null)
                {
                    continue;
                }

                foreach (var subKeyName in SafeSubKeyNames(key))
                {
                    using var handlerKey = key.OpenSubKey(subKeyName);
                    if (handlerKey is null)
                    {
                        continue;
                    }

                    var defaultValue = handlerKey.GetValue(null)?.ToString() ?? string.Empty;
                    var clsid = TryExtractGuid(defaultValue) ?? TryExtractGuid(subKeyName);
                    var entry = new ShellEntry
                    {
                        Kind = ShellEntryKind.ContextMenuHandler,
                        Name = subKeyName,
                        DisplayName = subKeyName,
                        Context = ContextFromRoot(root),
                        Clsid = clsid,
                        RootHive = location.Hive,
                        RegistryView = location.View,
                        KeyPath = $@"Software\Classes\{root}\{subKeyName}",
                        IsBlocked = clsid.HasValue && blocked.Contains(clsid.Value)
                    };

                    EnrichComEntry(entry, approved);
                    AddEntry(entries, seen, entry);
                }
            }

            foreach (var root in StaticVerbRoots)
            {
                using var key = OpenClassesSubKey(location, root);
                if (key is null)
                {
                    continue;
                }

                foreach (var verbName in SafeSubKeyNames(key))
                {
                    using var verbKey = key.OpenSubKey(verbName);
                    if (verbKey is null)
                    {
                        continue;
                    }

                    var explorerCommandClsid = TryExtractGuid(verbKey.GetValue("ExplorerCommandHandler")?.ToString() ?? string.Empty);
                    var kind = explorerCommandClsid.HasValue ? ShellEntryKind.ExplorerCommandHandler : ShellEntryKind.StaticVerb;
                    var command = verbKey.OpenSubKey("command")?.GetValue(null)?.ToString() ?? verbKey.GetValue("DelegateExecute")?.ToString() ?? string.Empty;
                    var displayName = verbKey.GetValue("MUIVerb")?.ToString()
                        ?? verbKey.GetValue(null)?.ToString()
                        ?? verbName;

                    var entry = new ShellEntry
                    {
                        Kind = kind,
                        Name = verbName,
                        DisplayName = displayName,
                        Context = ContextFromRoot(root),
                        Clsid = explorerCommandClsid,
                        RootHive = location.Hive,
                        RegistryView = location.View,
                        KeyPath = $@"Software\Classes\{root}\{verbName}",
                        TargetPath = command,
                        IsBlocked = explorerCommandClsid.HasValue && blocked.Contains(explorerCommandClsid.Value),
                        IsLegacyDisabled = HasValue(verbKey, "LegacyDisable") || HasValue(verbKey, "ProgrammaticAccessOnly")
                    };

                    if (explorerCommandClsid.HasValue)
                    {
                        EnrichComEntry(entry, approved);
                    }
                    else
                    {
                        EnrichCommandEntry(entry);
                    }

                    AddEntry(entries, seen, entry);
                }
            }
        }

        return entries
            .OrderByDescending(e => e.Kind == ShellEntryKind.ContextMenuHandler)
            .ThenBy(e => e.IsMicrosoft)
            .ThenBy(e => e.Context)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddEntry(ICollection<ShellEntry> entries, ISet<string> seen, ShellEntry entry)
    {
        var key = $"{entry.Kind}|{entry.RootHive}|{entry.RegistryView}|{entry.KeyPath}|{entry.ClsidText}";
        if (seen.Add(key))
        {
            entries.Add(entry);
        }
    }

    private static void EnrichComEntry(ShellEntry entry, IReadOnlyDictionary<Guid, string> approved)
    {
        if (!entry.Clsid.HasValue)
        {
            entry.LastError = "No CLSID was found in this registration.";
            return;
        }

        var server = ResolveComServer(entry.Clsid.Value, entry.RegistryView);
        if (server is null)
        {
            entry.LastError = "CLSID registration or InProcServer32 was not found.";
            if (approved.TryGetValue(entry.Clsid.Value, out var approvedName))
            {
                entry.DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? approvedName : entry.DisplayName;
            }

            return;
        }

        entry.TargetPath = server.Path;
        entry.ThreadingModel = server.ThreadingModel;
        if (!string.IsNullOrWhiteSpace(server.DisplayName) && entry.DisplayName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
        {
            entry.DisplayName = server.DisplayName;
        }

        if (approved.TryGetValue(entry.Clsid.Value, out var approvedDisplayName) && string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            entry.DisplayName = approvedDisplayName;
        }

        EnrichFileIdentity(entry, server.Path);
    }

    private static void EnrichCommandEntry(ShellEntry entry)
    {
        var path = ExtractExecutablePath(entry.TargetPath);
        if (!string.IsNullOrWhiteSpace(path))
        {
            EnrichFileIdentity(entry, path);
        }
    }

    private static void EnrichFileIdentity(ShellEntry entry, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                entry.CompanyName = info.CompanyName ?? string.Empty;

                try
                {
                    using var rawCertificate = X509Certificate.CreateFromSignedFile(path);
                    using var certificate = new X509Certificate2(rawCertificate);
                    entry.Publisher = certificate.GetNameInfo(X509NameType.SimpleName, false);
                }
                catch
                {
                    entry.Publisher = string.Empty;
                }
            }
        }
        catch
        {
            // Identity metadata is helpful but nonessential.
        }

        entry.IsMicrosoft = ContainsMicrosoft(entry.CompanyName)
            || ContainsMicrosoft(entry.Publisher)
            || path.Contains(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsMicrosoft(string value)
    {
        return value.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
    }

    private static ComServerInfo? ResolveComServer(Guid clsid, RegistryView preferredView)
    {
        var classKeyPath = $@"Software\Classes\CLSID\{clsid:B}";
        foreach (var location in GetComLookupLocations(preferredView))
        {
            using var baseKey = OpenBaseKey(location.Hive, location.View);
            using var clsidKey = baseKey?.OpenSubKey(classKeyPath);
            using var inprocKey = clsidKey?.OpenSubKey("InProcServer32");
            var rawPath = inprocKey?.GetValue(null)?.ToString();
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            return new ComServerInfo(
                NormalizePath(rawPath),
                clsidKey?.GetValue(null)?.ToString() ?? string.Empty,
                inprocKey?.GetValue("ThreadingModel")?.ToString() ?? string.Empty);
        }

        return null;
    }

    private static IReadOnlyDictionary<Guid, string> ReadApprovedNames()
    {
        var result = new Dictionary<Guid, string>();
        foreach (var location in GetRegistryViewsForShellExtensions())
        {
            using var baseKey = OpenBaseKey(location.Hive, location.View);
            using var key = baseKey?.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
            if (key is null)
            {
                continue;
            }

            foreach (var valueName in key.GetValueNames())
            {
                var clsid = TryExtractGuid(valueName);
                if (clsid.HasValue && !result.ContainsKey(clsid.Value))
                {
                    result[clsid.Value] = key.GetValue(valueName)?.ToString() ?? string.Empty;
                }
            }
        }

        return result;
    }

    private static HashSet<Guid> ReadBlockedClsids()
    {
        var result = new HashSet<Guid>();
        foreach (var location in GetRegistryViewsForShellExtensions())
        {
            using var baseKey = OpenBaseKey(location.Hive, location.View);
            using var key = baseKey?.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked");
            if (key is null)
            {
                continue;
            }

            foreach (var valueName in key.GetValueNames())
            {
                var clsid = TryExtractGuid(valueName);
                if (clsid.HasValue)
                {
                    result.Add(clsid.Value);
                }
            }
        }

        return result;
    }

    private static RegistryKey? OpenClassesSubKey(RegistryLocation location, string subPath)
    {
        using var baseKey = OpenBaseKey(location.Hive, location.View);
        return baseKey?.OpenSubKey($@"Software\Classes\{subPath}");
    }

    private static RegistryKey? OpenBaseKey(RegistryHive hive, RegistryView view)
    {
        try
        {
            return RegistryKey.OpenBaseKey(hive, view);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<RegistryLocation> GetScanLocations()
    {
        var is64 = Environment.Is64BitOperatingSystem;
        yield return new RegistryLocation(RegistryHive.CurrentUser, is64 ? RegistryView.Registry64 : RegistryView.Registry32);
        yield return new RegistryLocation(RegistryHive.LocalMachine, is64 ? RegistryView.Registry64 : RegistryView.Registry32);

        if (is64)
        {
            yield return new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry32);
        }
    }

    private static IEnumerable<RegistryLocation> GetComLookupLocations(RegistryView preferredView)
    {
        var is64 = Environment.Is64BitOperatingSystem;
        var views = new List<RegistryView> { preferredView };
        if (is64)
        {
            views.Add(RegistryView.Registry64);
            views.Add(RegistryView.Registry32);
        }
        else
        {
            views.Add(RegistryView.Registry32);
        }

        foreach (var view in views.Distinct())
        {
            yield return new RegistryLocation(RegistryHive.CurrentUser, view);
            yield return new RegistryLocation(RegistryHive.LocalMachine, view);
        }
    }

    private static IEnumerable<RegistryLocation> GetRegistryViewsForShellExtensions()
    {
        var is64 = Environment.Is64BitOperatingSystem;
        yield return new RegistryLocation(RegistryHive.CurrentUser, is64 ? RegistryView.Registry64 : RegistryView.Registry32);
        yield return new RegistryLocation(RegistryHive.LocalMachine, is64 ? RegistryView.Registry64 : RegistryView.Registry32);

        if (is64)
        {
            yield return new RegistryLocation(RegistryHive.CurrentUser, RegistryView.Registry32);
            yield return new RegistryLocation(RegistryHive.LocalMachine, RegistryView.Registry32);
        }
    }

    private static IEnumerable<string> SafeSubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool HasValue(RegistryKey key, string valueName)
    {
        try
        {
            return key.GetValueNames().Any(name => name.Equals(valueName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static Guid? TryExtractGuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = GuidRegex.Match(value);
        return match.Success && Guid.TryParse(match.Value, out var guid) ? guid : null;
    }

    private static string ContextFromRoot(string root)
    {
        var index = root.IndexOf('\\');
        return index > 0 ? root[..index] : root;
    }

    private static string NormalizePath(string rawPath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim());
        if (File.Exists(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        var match = Regex.Match(expanded, "^\"?(.*?\\.(?:dll|exe))\"?(?:\\s|$)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var candidate = Environment.ExpandEnvironmentVariables(match.Groups[1].Value);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : candidate;
        }

        return expanded.Trim('"');
    }

    private static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        var quoted = Regex.Match(expanded, "^\"([^\"]+\\.(?:exe|dll))\"", RegexOptions.IgnoreCase);
        if (quoted.Success)
        {
            return quoted.Groups[1].Value;
        }

        var unquoted = Regex.Match(expanded, "^(.*?\\.(?:exe|dll))(?:\\s|$)", RegexOptions.IgnoreCase);
        return unquoted.Success ? unquoted.Groups[1].Value : string.Empty;
    }

    private sealed record RegistryLocation(RegistryHive Hive, RegistryView View);

    private sealed record ComServerInfo(string Path, string DisplayName, string ThreadingModel);
}
