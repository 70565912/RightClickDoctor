using System.Text;
using System.Text.Json;
using RightClickDoctor.Models;

namespace RightClickDoctor.Reports;

public static class ReportWriter
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Write(string path, IEnumerable<ShellEntry> entries)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            WriteCsv(path, entries);
            return;
        }

        WriteJson(path, entries);
    }

    public static void WriteJson(string path, IEnumerable<ShellEntry> entries)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions), Encoding.UTF8);
    }

    public static void WriteCsv(string path, IEnumerable<ShellEntry> entries)
    {
        var builder = new StringBuilder();
        WriteCsvRow(builder, new[]
        {
            "Risk",
            "State",
            "Kind",
            "Name",
            "Context",
            "TotalMs",
            "CreateInstanceMs",
            "InitializeMs",
            "QueryContextMenuMs",
            "MenuItems",
            "CLSID",
            "TargetPath",
            "Publisher",
            "CompanyName",
            "RegistryPath",
            "LastError"
        });

        foreach (var entry in entries)
        {
            WriteCsvRow(builder, new[]
            {
                entry.Risk,
                entry.StateText,
                entry.KindText,
                entry.DisplayName,
                entry.Context,
                entry.TotalMsText,
                entry.CreateInstanceMsText,
                entry.InitializeMsText,
                entry.QueryContextMenuMsText,
                entry.MenuItems?.ToString() ?? string.Empty,
                entry.ClsidText,
                entry.TargetPath,
                entry.Publisher,
                entry.CompanyName,
                entry.RegistryPath,
                entry.LastError
            });
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static void WriteCsvRow(StringBuilder builder, IEnumerable<string> values)
    {
        builder.AppendLine(string.Join(",", values.Select(EscapeCsv)));
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
