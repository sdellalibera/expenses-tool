using System.Text.Json.Serialization;

namespace ContentUnderstanding.Client.Models;

/// <summary>
/// Represents a request to analyze content using a specified analyzer.
/// </summary>
public class AnalyzeRequest
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}
