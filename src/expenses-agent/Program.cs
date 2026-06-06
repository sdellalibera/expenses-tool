using agents.models;
using Azure.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using SharedServices;

const string AgentName = "expenses-agent";

var builder = WebApplication.CreateBuilder(args);

var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "SystemPrompt.md");
var agentInstructions = await File.ReadAllTextAsync(promptPath);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

// Register the Cosmos "sessions" container (provisioned by the AppHost) using a
// System.Text.Json serializer, then expose it as an agent session store so each
// conversation thread is created on first use and its messages persisted per turn.
builder.AddKeyedAzureCosmosContainer("sessions",
    configureClientOptions: options =>
    {
        options.Serializer = new CosmosSystemTextJsonSerializer();
    });

builder.Services.AddCosmosAgentSessionStore("sessions");

builder.AddAzureChatCompletionsClient(connectionName: "foundry",
    configureSettings: settings =>
    {
        settings.TokenCredential = new DefaultAzureCredential();
        settings.EnableSensitiveTelemetryData = true;
    })
    .AddChatClient("gpt-5.4");

// Resolve the Content Understanding MCP server endpoint. Aspire's service
// discovery injects "services__<resource>__<scheme>__0" env vars whenever the
// agent declares .WithReference(mcpserver) in the AppHost.
var contentUnderstandingMcpUrl =
    Environment.GetEnvironmentVariable("services__mcpserver__https__0")
    ?? Environment.GetEnvironmentVariable("services__mcpserver__http__0")
    ?? Environment.GetEnvironmentVariable("MCPSERVER_HTTP")
    ?? throw new InvalidOperationException(
        "Could not resolve Content Understanding MCP server URL. " +
        "Expected one of 'services__mcpserver__https__0', " +
        "'services__mcpserver__http__0', or 'MCPSERVER_HTTP'.");

var mcpClient = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(new Uri(contentUnderstandingMcpUrl), "/mcp")
}));

var mcpTools = await mcpClient.ListToolsAsync();
List<AITool> agentTools = mcpTools.Cast<AITool>().ToList();

builder.Services.AddSingleton(mcpClient);

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

    // Persist the updated thread (including the new user and assistant messages).
    await sessionStore.SaveSessionAsync(agent, conversationId, session, cancellationToken);

    return Results.Ok(new AgentChatResponse(agentResponse.Text, conversationId));
})
.WithName("Chat")
.WithDescription("Sends a message to the expenses agent and returns its reply.");

app.Run();


