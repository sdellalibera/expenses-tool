using System.ComponentModel;
using ModelContextProtocol.Server;

/// <summary>
/// Sample MCP tools for demonstration purposes.
/// These tools can be invoked by MCP clients to perform various operations.
/// </summary>
internal class ContentUnderstandingTools
{
    [McpServerTool]
    [Description("Generates a random number between the specified minimum and maximum values.")]
    public string GetDocumentContentByPath(
        [Description("Path of the document that has to be analyzed")] string url)
    {
        var uri = new Uri(url);
        
        return null;
    }
}
