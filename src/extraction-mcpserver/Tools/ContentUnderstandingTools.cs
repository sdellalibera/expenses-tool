using System.ComponentModel;
using Azure;
using Azure.AI.ContentUnderstanding;
using ModelContextProtocol.Server;

/// <summary>
/// MCP tools backed by Azure AI Content Understanding.
/// </summary>
internal class ContentUnderstandingTools
{
    [McpServerTool]
    [Description("Analyzes a document with Azure AI Content Understanding and returns LLM-ready markdown text.")]
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

        // A document produces a single AnalysisContent entry with markdown that is
        // optimized for RAG / LLM consumption.
        AnalysisContent content = result.Contents!.First();
        return content.Markdown ?? string.Empty;
    }
}
