using agents.models;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using SharedServices;

const string AgentName = "expenses-agent";
const string ExpensesContainerName = "expenses";
const string FrontendCorsPolicy = "frontend";

var builder = WebApplication.CreateBuilder(args);

var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "SystemPrompt.md");
var agentInstructions = await File.ReadAllTextAsync(promptPath);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

// Allow the React frontend (which may run on a different origin / phone IP)
// to call the /expenses/report and /chat endpoints from the browser.
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Register the Cosmos "sessions" container (provisioned by the AppHost) using a
// System.Text.Json serializer, then expose it as an agent session store so each
// conversation thread is created on first use and its messages persisted per turn.
builder.AddKeyedAzureCosmosContainer("sessions",
    configureClientOptions: options =>
    {
        options.Serializer = new CosmosSystemTextJsonSerializer();
    });

builder.Services.AddCosmosAgentSessionStore("sessions");

// Blob storage for uploaded receipt images. The "expenses-images" connection
// is provided by the AppHost (Azurite locally, Azure Storage when deployed).
builder.AddAzureBlobServiceClient("expenses-images");

builder.AddAzureChatCompletionsClient(connectionName: "foundry",
    configureSettings: settings =>
    {
        settings.TokenCredential = new DefaultAzureCredential();
        settings.EnableSensitiveTelemetryData = true;
    })
    .AddChatClient("gpt-5.4");

// Aspire's service discovery injects "services__<resource>__<scheme>__0" env vars
// whenever the agent declares .WithReference(<resource>) in the AppHost. We collect
// tools from the Content Understanding and Storage MCP servers and pass them to the agent.
var mcpEndpoints = new (string name, string envFallback)[]
{
    ("mcpserver", "MCPSERVER_HTTP"),
    ("storagemcp", "STORAGEMCP_HTTP")
};

var agentTools = new List<AITool>();
foreach (var (name, envFallback) in mcpEndpoints)
{
    var url = Environment.GetEnvironmentVariable($"services__{name}__https__0")
              ?? Environment.GetEnvironmentVariable($"services__{name}__http__0")
              ?? Environment.GetEnvironmentVariable(envFallback)
              ?? throw new InvalidOperationException(
                  $"Could not resolve MCP server URL for '{name}'. " +
                  $"Expected one of 'services__{name}__https__0', 'services__{name}__http__0', or '{envFallback}'.");

    var mcpClient = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = new Uri(new Uri(url), "/mcp")
    }));

    var tools = await mcpClient.ListToolsAsync();
    agentTools.AddRange(tools.Cast<AITool>());
}

builder.AddAIAgent(AgentName, (sp, key) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    var agentOptions = new ChatClientAgentOptions
    {
        Name = key,
        Description = "Analyzes receipts/invoices via Content Understanding and returns structured data.",
        ChatOptions = new ChatOptions
        {
            Instructions = agentInstructions,
            Tools = agentTools
        }
    };

    return chatClient.AsAIAgent(agentOptions, services: sp);
});

var app = builder.Build();

app.UseCors(FrontendCorsPolicy);

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/chat", async (
    AgentChatRequest request,
    [FromKeyedServices(AgentName)] AIAgent agent,
    CosmosAgentSessionStore sessionStore,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Message is required." });
    }

    // A missing conversation id starts a new thread; otherwise the existing
    // thread is loaded from Cosmos so the agent has the full conversation history.
    var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
        ? Guid.NewGuid().ToString("N")
        : request.ConversationId.Trim();

    var session = await sessionStore.GetSessionAsync(agent, conversationId, cancellationToken);

    var agentResponse = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

    await sessionStore.SaveSessionAsync(agent, conversationId, session, cancellationToken);

    return Results.Ok(new AgentChatResponse(agentResponse.Text, conversationId));
})
.WithName("Chat")
.WithDescription("Sends a message to the expenses agent and returns its reply.");

// Receives an image captured by the React frontend, persists it to blob storage,
// and asks the agent to analyze it via the Content Understanding MCP tools.
app.MapPost("/expenses/report", async (
    HttpRequest httpRequest,
    BlobServiceClient blobService,
    [FromKeyedServices(AgentName)] AIAgent agent,
    CosmosAgentSessionStore sessionStore,
    CancellationToken cancellationToken) =>
{
    if (!httpRequest.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Request must be multipart/form-data with an 'image' field." });
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);
    var file = form.Files["image"] ?? form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Missing 'image' file in form data." });
    }

    var conversationId = form["conversationId"].ToString();
    if (string.IsNullOrWhiteSpace(conversationId))
    {
        conversationId = Guid.NewGuid().ToString("N");
    }

    var note = form["note"].ToString();

    var container = blobService.GetBlobContainerClient(ExpensesContainerName);
    await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

    var extension = Path.GetExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(extension))
    {
        extension = file.ContentType switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            _ => ".bin"
        };
    }

    var blobName = $"{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}{extension}";
    var blobClient = container.GetBlobClient(blobName);

    await using (var uploadStream = file.OpenReadStream())
    {
        await blobClient.UploadAsync(
            uploadStream,
            new BlobHttpHeaders { ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType },
            cancellationToken: cancellationToken);
    }

    // Ask the agent to analyze the freshly uploaded blob. The agent has both
    // storage tools (to fetch a SAS URL) and Content Understanding tools.
    var promptBuilder = new System.Text.StringBuilder();
    promptBuilder.AppendLine("A new receipt image was uploaded to blob storage and is ready for analysis.");
    promptBuilder.AppendLine($"Container: {ExpensesContainerName}");
    promptBuilder.AppendLine($"Blob name: {blobName}");
    promptBuilder.AppendLine("Steps to take:");
    promptBuilder.AppendLine("  1. Call ListAnalyzers and select the analyzer best suited for receipts/invoices.");
    promptBuilder.AppendLine("  2. Call GetBlobReadUrl to obtain a SAS URL for the blob.");
    promptBuilder.AppendLine("  3. Call AnalyzeDocumentByUrl with that URL using the chosen analyzer.");
    promptBuilder.AppendLine("  4. Summarize the extracted expense (vendor, date, total, line item count).");
    if (!string.IsNullOrWhiteSpace(note))
    {
        promptBuilder.AppendLine($"User note: {note}");
    }

    var session = await sessionStore.GetSessionAsync(agent, conversationId, cancellationToken);
    var agentResponse = await agent.RunAsync(promptBuilder.ToString(), session, cancellationToken: cancellationToken);
    await sessionStore.SaveSessionAsync(agent, conversationId, session, cancellationToken);

    return Results.Ok(new ExpensesReportResponse(conversationId, blobName, agentResponse.Text));
})
.WithName("CreateExpensesReport")
.WithDescription("Uploads a captured receipt/invoice image to blob storage and asks the agent to analyze it.")
.DisableAntiforgery();

app.Run();
