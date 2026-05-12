namespace RightClickDoctor.Models;

public sealed class ProbeResult
{
    public Guid Clsid { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Success { get; set; }

    public bool TimedOut { get; set; }

    public int? ExitCode { get; set; }

    public double? CreateInstanceMs { get; set; }

    public double? InitializeMs { get; set; }

    public double? QueryContextMenuMs { get; set; }

    public double? TotalMs { get; set; }

    public int? InitializeHResult { get; set; }

    public int? QueryContextMenuHResult { get; set; }

    public int? MenuItems { get; set; }

    public string Phase { get; set; } = string.Empty;

    public string? Error { get; set; }
}
