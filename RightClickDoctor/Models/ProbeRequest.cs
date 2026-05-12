namespace RightClickDoctor.Models;

public sealed class ProbeRequest
{
    public Guid Clsid { get; set; }

    public string Name { get; set; } = string.Empty;

    public string[] SamplePaths { get; set; } = Array.Empty<string>();
}
