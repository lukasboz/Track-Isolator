using System.Text.Json.Serialization;

namespace StemSeparator.Models;

public class SSEEvent
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("stems")]
    public Dictionary<string, string>? Stems { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("progress")]
    public int? Progress { get; set; }
}
