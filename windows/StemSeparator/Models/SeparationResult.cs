namespace StemSeparator.Models;

public class SeparationResult
{
    public string RequestId { get; set; } = string.Empty;
    public string LocalDirectory { get; set; } = string.Empty;
    public Dictionary<StemType, string> StemPaths { get; set; } = new();
}
