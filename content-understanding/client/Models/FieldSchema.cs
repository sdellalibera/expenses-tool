using System.Text.Json.Serialization;

namespace ContentUnderstanding.Client.Models;

/// <summary>
/// Represents the schema definition for fields to extract from content.
/// </summary>
public class FieldSchema
{
    [JsonPropertyName("fields")]
    public Dictionary<string, FieldDefinition> Fields { get; set; } = new();
}

/// <summary>
/// Defines a single field for extraction including its type and description.
/// </summary>
public class FieldDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
