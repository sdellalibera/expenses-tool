#:package Azure.AI.ContentUnderstanding@1.0.0
#:package Azure.Identity@1.18.0-beta.3

using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Identity;

var endpoint = "https://aiservicesvpgpt5mhpmwls.cognitiveservices.azure.com/";

var credential = new DefaultAzureCredential();
var client = new ContentUnderstandingClient(new Uri(endpoint),credential);

Uri source = new Uri("https://stvpgpt5mhpmwls.blob.core.windows.net/files/invoice_example.pdf");

Operation<AnalysisResult> operation = await client.AnalyzeAsync(
    WaitUntil.Completed,
    "prebuilt-documentSearch",
    inputs: new[] { new AnalysisInput { Uri = source } }
);

AnalysisResult result = operation.Value;
AnalysisContent content = result.Contents!.First();
Console.WriteLine("Markdown:");
Console.WriteLine(content.Markdown);

// Cast AnalysisContent to DocumentContent to access document-specific properties
// DocumentContent derives from AnalysisContent and provides additional properties
// to access full information about document, including Pages, Tables and many others
DocumentContent documentContent = (DocumentContent)content;
Console.WriteLine($"Pages: {documentContent.StartPageNumber} - {documentContent.EndPageNumber}");

// Check for pages
if (documentContent.Pages != null && documentContent.Pages.Count > 0)
{
    Console.WriteLine($"Number of pages: {documentContent.Pages.Count}");
    foreach (var page in documentContent.Pages)
    {
        var unit = documentContent.Unit?.ToString() ?? "units";
        Console.WriteLine($"  Page {page.PageNumber}: {page.Width} x {page.Height} {unit}");
    }
}