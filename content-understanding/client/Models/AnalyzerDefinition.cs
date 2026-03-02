using System.Text.Json.Serialization;

namespace ContentUnderstanding.Client.Models;

/// <summary>
/// Represents an analyzer configuration for the Content Understanding API.
/// </summary>
public class AnalyzerDefinition
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("scenario")]
    public string? Scenario { get; set; }

    [JsonPropertyName("fieldSchema")]
    public FieldSchema? FieldSchema { get; set; }

    [JsonPropertyName("config")]
    public AnalyzerConfig? Config { get; set; }
}

/// <summary>
/// Additional configuration options for an analyzer.
/// </summary>
public class AnalyzerConfig
{
    [JsonPropertyName("returnDetails")]
    public bool? ReturnDetails { get; set; }

    [JsonPropertyName("enableFace")]
    public bool? EnableFace { get; set; }

    [JsonPropertyName("enableOcr")]
    public bool? EnableOcr { get; set; }
}
