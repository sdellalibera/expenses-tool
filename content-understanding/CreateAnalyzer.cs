#:package Azure.AI.ContentUnderstanding@1.0.0
#:package Azure.Identity@1.18.0-beta.3

using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Identity;

var endpoint = "https://aiservicesvpgpt5mhpmwls.cognitiveservices.azure.com/";

var credential = new DefaultAzureCredential();
var client = new ContentUnderstandingClient(new Uri(endpoint),credential);

// Generate a unique analyzer ID
string analyzerId = $"my_custom_analyzer_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

// Define field schema with custom fields
// This example demonstrates three extraction methods:
// - extract: Literal text extraction (requires estimateSourceAndConfidence)
// - generate: AI-generated values based on content interpretation
// - classify: Classification against predefined categories
var fieldSchema = new ContentFieldSchema(
    new Dictionary<string, ContentFieldDefinition>
    {
        ["company_name"] = new ContentFieldDefinition
        {
            Type = ContentFieldType.String,
            Method = GenerationMethod.Extract,
            Description = "Name of the company"
        },
        ["total_amount"] = new ContentFieldDefinition
        {
            Type = ContentFieldType.Number,
            Method = GenerationMethod.Extract,
            Description = "Total amount on the document"
        },
        ["document_summary"] = new ContentFieldDefinition
        {
            Type = ContentFieldType.String,
            Method = GenerationMethod.Generate,
            Description = "A brief summary of the document content"
        },
        ["document_type"] = new ContentFieldDefinition
        {
            Type = ContentFieldType.String,
            Method = GenerationMethod.Classify,
            Description = "Type of document"
        }
    })
{
    Name = "company_schema",
    Description = "Schema for extracting company information"
};

// Add enum values for the classify field
fieldSchema.Fields["document_type"].Enum.Add("invoice");
fieldSchema.Fields["document_type"].Enum.Add("receipt");
fieldSchema.Fields["document_type"].Enum.Add("contract");
fieldSchema.Fields["document_type"].Enum.Add("report");
fieldSchema.Fields["document_type"].Enum.Add("other");

// Create analyzer configuration
var config = new ContentAnalyzerConfig
{
    EnableFormula = true,
    EnableLayout = true,
    EnableOcr = true,
    EstimateFieldSourceAndConfidence = true,
    ShouldReturnDetails = true
};

// Create the custom analyzer
var customAnalyzer = new ContentAnalyzer
{
    BaseAnalyzerId = "prebuilt-document",
    Description = "Custom analyzer for extracting company information",
    Config = config,
    FieldSchema = fieldSchema
};

// Add model mappings for supported large language models (required for custom analyzers)
// Maps model roles (completion, embedding) to specific model names
customAnalyzer.Models["completion"] = "gpt-4.1";
customAnalyzer.Models["embedding"] = "text-embedding-3-large";

// Create the analyzer
var operation = await client.CreateAnalyzerAsync(
    WaitUntil.Completed,
    analyzerId,
    customAnalyzer);

ContentAnalyzer result = operation.Value;
Console.WriteLine($"Analyzer ID: '{analyzerId}'");