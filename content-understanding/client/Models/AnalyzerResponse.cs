using System.Text.Json.Serialization;

namespace ContentUnderstanding.Client.Models;

/// <summary>
/// Represents the response returned when retrieving an analyzer.
/// </summary>
public class AnalyzerResponse
{
    [JsonPropertyName("analyzerId")]
    public string? AnalyzerId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("scenario")]
    public string? Scenario { get; set; }

    [JsonPropertyName("fieldSchema")]
    public FieldSchema? FieldSchema { get; set; }

    [JsonPropertyName("config")]
    public AnalyzerConfig? Config { get; set; }

    [JsonPropertyName("createdDateTime")]
    public string? CreatedDateTime { get; set; }

    [JsonPropertyName("lastUpdatedDateTime")]
    public string? LastUpdatedDateTime { get; set; }
}
