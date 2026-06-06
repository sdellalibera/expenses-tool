using System.ComponentModel;
using System.Text.Json;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Core;
using ModelContextProtocol.Server;

/// <summary>
/// MCP tools backed by Azure AI Content Understanding.
/// </summary>
internal class ContentUnderstandingTools
{
    [McpServerTool]
    [Description("Analyzes a document with Azure AI Content Understanding and returns extracted fields, source input, and token usage.")]
    public async Task<string> AnalyzeDocumentBytes(
        ContentUnderstandingClient client,
        [Description("Absolute path to the document file (PDF, image, Office document, etc.) to analyze.")] string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        BinaryData binaryData = BinaryData.FromBytes(fileBytes);

        Operation<AnalysisResult> operation = await client.AnalyzeBinaryAsync(
            WaitUntil.Completed,
            "prebuilt-documentSearch",
            binaryData,
            cancellationToken: cancellationToken);

        AnalysisResult result = operation.Value;

        return result.ToLlmInput(
            options: new LlmInputOptions
            {
                IncludeMarkdown = false
            }
        );
    }

    [McpServerTool]
    [Description("Analyzes a document at a publicly accessible URL with Azure AI Content Understanding and returns extracted fields, source input, and token usage.")]
    public async Task<string> AnalyzeDocumentByUrl(
        ContentUnderstandingClient client,
        [Description("Publicly accessible URL of the document (PDF, image, Office document, etc.) to analyze.")] string url,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL must be provided.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uriSource))
        {
            throw new ArgumentException($"Invalid absolute URL: {url}", nameof(url));
        }

        Operation<AnalysisResult> operation = await client.AnalyzeAsync(
            WaitUntil.Completed,
            "prebuilt-documentSearch",
            inputs: new[]
            {
                new AnalysisInput
                {
                    Uri = uriSource
                }
            },
            cancellationToken: cancellationToken);

        AnalysisResult result = operation.Value;

        return result.ToLlmInput(
            options: new LlmInputOptions
            {
                IncludeMarkdown = false
            }
        );
    }

    [McpServerTool]
    [Description(
        "Lists every Content Understanding analyzer available on the connected resource. " +
        "Returns a JSON array of { analyzerId, description, baseAnalyzerId, status, tags } so the caller can " +
        "pick the most relevant analyzer (for example, a receipts/invoice analyzer) for the document at hand.")]
    public async Task<string> ListAnalyzers(
        ContentUnderstandingClient client,
        CancellationToken cancellationToken = default)
    {
        var analyzers = new List<object>();

        await foreach (var analyzer in client.GetAnalyzersAsync(cancellationToken))
        {
            analyzers.Add(new
            {
                analyzerId = analyzer.AnalyzerId,
                description = analyzer.Description,
                baseAnalyzerId = analyzer.BaseAnalyzerId,
                status = analyzer.Status.ToString(),
                tags = analyzer.Tags
            });
        }

        return JsonSerializer.Serialize(analyzers);
    }

    [McpServerTool]
    [Description(
        "Creates (or replaces) a Content Understanding analyzer. Supply a stable analyzerId, a human-readable " +
        "description, an optional baseAnalyzerId (defaults to 'prebuilt-documentAnalyzer'), and an optional JSON " +
        "field schema describing the structured fields to extract. Returns the analyzerId of the created analyzer.")]
    public async Task<string> CreateAnalyzer(
        ContentUnderstandingClient client,
        [Description("Stable identifier for the new analyzer, e.g. 'expenses-receipts'.")] string analyzerId,
        [Description("Human-readable description of what the analyzer extracts. Used by the agent when selecting an analyzer.")] string description,
        [Description("Optional base analyzer to extend. Defaults to 'prebuilt-documentAnalyzer'.")] string? baseAnalyzerId = null,
        [Description("Optional JSON object describing the field schema, for example {\"fields\":{\"vendor\":{\"type\":\"string\"},\"total\":{\"type\":\"number\"}}}.")] string? fieldSchemaJson = null,
        [Description("If true, replaces an existing analyzer with the same id.")] bool allowReplace = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(analyzerId))
        {
            throw new ArgumentException("analyzerId must be provided.", nameof(analyzerId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("description must be provided.", nameof(description));
        }

        var payload = new Dictionary<string, object?>
        {
            ["description"] = description,
            ["baseAnalyzerId"] = string.IsNullOrWhiteSpace(baseAnalyzerId) ? "prebuilt-documentAnalyzer" : baseAnalyzerId
        };

        if (!string.IsNullOrWhiteSpace(fieldSchemaJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(fieldSchemaJson);
                payload["fieldSchema"] = JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"fieldSchemaJson is not valid JSON: {ex.Message}", nameof(fieldSchemaJson), ex);
            }
        }

        var requestContent = RequestContent.Create(BinaryData.FromObjectAsJson(payload));

        var operation = await client.CreateAnalyzerAsync(
            WaitUntil.Completed,
            analyzerId,
            requestContent,
            allowReplace: allowReplace,
            context: new RequestContext { CancellationToken = cancellationToken });

        return analyzerId;
    }
}
