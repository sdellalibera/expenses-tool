using agents.models;
using Azure;
using Azure.AI.ContentUnderstanding;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Azure;
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

// Azure AI Content Understanding client. The endpoint comes from configuration
// (AZURE_CONTENTUNDERSTANDING_ENDPOINT, with FOUNDRY_URI as a fallback). When it
// is not configured the client is simply not registered and the /expenses/report
// endpoint returns a clear error; /chat keeps working.
var contentUnderstandingEndpoint = builder.Configuration["AZURE_CONTENTUNDERSTANDING_ENDPOINT"];

if (!string.IsNullOrWhiteSpace(contentUnderstandingEndpoint))
{
    builder.Services.AddAzureClients(clients =>
    {
        clients.AddContentUnderstandingClient(new Uri(contentUnderstandingEndpoint));
        clients.UseCredential(new DefaultAzureCredential());
    });
}

builder.AddAzureChatCompletionsClient(connectionName: "foundry",
    configureSettings: settings =>
    {
        settings.TokenCredential = new DefaultAzureCredential();
        settings.EnableSensitiveTelemetryData = true;
    })
    .AddChatClient("gpt-5");



// Aspire's service discovery injects "services__<resource>__<scheme>__0" env vars
// whenever the agent declares .WithReference(<resource>) in the AppHost. We collect
// tools from the Storage MCP server and pass them to the agent.
var mcpEndpoints = new[] { "storagemcp" };

var agentTools = new List<AITool>();
foreach (var name in mcpEndpoints)
{
    var url = Environment.GetEnvironmentVariable($"services__{name}__http__0")
              ?? throw new InvalidOperationException(
                  $"Could not resolve MCP server URL for '{name}'. " +
                  $"Expected 'services__{name}__http__0'.");

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
// analyzes it with Azure AI Content Understanding, and asks the agent to summarize
// the extracted expense.
app.MapPost("/expenses/report", async (
    HttpRequest httpRequest,
    BlobServiceClient blobService,
    [FromKeyedServices(AgentName)] AIAgent agent,
    CosmosAgentSessionStore sessionStore,
    ContentUnderstandingClient? contentUnderstanding,
    CancellationToken cancellationToken) =>
{
    if (contentUnderstanding is null)
    {
        return Results.Problem(
            "Content Understanding is not configured. Set AZURE_CONTENTUNDERSTANDING_ENDPOINT.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

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

    // Read the upload into memory once so it can be both stored and analyzed.
    byte[] imageBytes;
    await using (var uploadStream = file.OpenReadStream())
    {
        using var memory = new MemoryStream();
        await uploadStream.CopyToAsync(memory, cancellationToken);
        imageBytes = memory.ToArray();
    }

    var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
    await blobClient.UploadAsync(
        BinaryData.FromBytes(imageBytes),
        new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
        cancellationToken);

    // Analyze the image directly with the Content Understanding SDK.
    var analyzerId = app.Configuration["AZURE_CONTENTUNDERSTANDING_ANALYZER_ID"] ?? "prebuilt-documentSearch";
    Operation<AnalysisResult> operation = await contentUnderstanding.AnalyzeBinaryAsync(
        WaitUntil.Completed,
        analyzerId,
        BinaryData.FromBytes(imageBytes),
        cancellationToken: cancellationToken);

    var extractedContent = operation.Value.ToLlmInput(options: new LlmInputOptions { IncludeMarkdown = false });

    // Hand the extracted content to the agent and let it produce a friendly summary.
    var promptBuilder = new System.Text.StringBuilder();
    promptBuilder.AppendLine("A new receipt image was uploaded to blob storage and analyzed with Content Understanding.");
    promptBuilder.AppendLine($"Container: {ExpensesContainerName}");
    promptBuilder.AppendLine($"Blob name: {blobName}");
    promptBuilder.AppendLine();
    promptBuilder.AppendLine("Extracted content:");
    promptBuilder.AppendLine(extractedContent);
    if (!string.IsNullOrWhiteSpace(note))
    {
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"User note: {note}");
    }
    promptBuilder.AppendLine();
    promptBuilder.AppendLine("Summarize the extracted expense (vendor, date, total, currency, category, line item count).");

    var session = await sessionStore.GetSessionAsync(agent, conversationId, cancellationToken);
    var agentResponse = await agent.RunAsync(promptBuilder.ToString(), session, cancellationToken: cancellationToken);
    await sessionStore.SaveSessionAsync(agent, conversationId, session, cancellationToken);

    return Results.Ok(new ExpensesReportResponse(conversationId, blobName, agentResponse.Text));
})
.WithName("CreateExpensesReport")
.WithDescription("Uploads a captured receipt/invoice image, analyzes it with Content Understanding, and asks the agent to summarize it.")
.DisableAntiforgery();

app.Run();
