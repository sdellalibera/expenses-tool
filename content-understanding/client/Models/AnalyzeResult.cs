using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentUnderstanding.Client.Models;

/// <summary>
/// Represents the result of a content analysis operation.
/// </summary>
public class AnalyzeResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("result")]
    public AnalyzeResultContent? Result { get; set; }

    [JsonPropertyName("error")]
    public AnalyzeError? Error { get; set; }
}

/// <summary>
/// The content portion of the analysis result containing extracted data.
/// </summary>
public class AnalyzeResultContent
{
    [JsonPropertyName("analyzerId")]
    public string? AnalyzerId { get; set; }

    [JsonPropertyName("apiVersion")]
    public string? ApiVersion { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("warnings")]
    public List<JsonElement>? Warnings { get; set; }

    [JsonPropertyName("contents")]
    public List<ContentItem>? Contents { get; set; }
}

/// <summary>
/// Represents a single item of analyzed content.
/// </summary>
public class ContentItem
{
    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, FieldValue>? Fields { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }
}

/// <summary>
/// Represents an extracted field value.
/// </summary>
public class FieldValue
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("valueString")]
    public string? ValueString { get; set; }

    [JsonPropertyName("valueNumber")]
    public double? ValueNumber { get; set; }

    [JsonPropertyName("valueBoolean")]
    public bool? ValueBoolean { get; set; }

    [JsonPropertyName("valueDate")]
    public string? ValueDate { get; set; }

    [JsonPropertyName("valueArray")]
    public List<FieldValue>? ValueArray { get; set; }

    [JsonPropertyName("valueObject")]
    public Dictionary<string, FieldValue>? ValueObject { get; set; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }
}

/// <summary>
/// Represents an error returned from the API.
/// </summary>
public class AnalyzeError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
