using content_understanding.models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

const string AgentName = "expenses-agent";

var builder = WebApplication.CreateBuilder(args);

var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "SystemPrompt.md");
var agentInstructions = await File.ReadAllTextAsync(promptPath);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

builder.AddAzureOpenAIClient(connectionName: "foundry").AddChatClient("gpt-5.4");

// Resolve both MCP server endpoints from the AppHost-injected environment variables.
var contentUnderstandingMcpUrl = Environment.GetEnvironmentVariable("MCPSERVER_HTTP")
    ?? throw new InvalidOperationException("MCPSERVER_HTTP env var (Content Understanding MCP) is not set.");
var sqlMcpUrl = Environment.GetEnvironmentVariable("SQL_MCP_HTTP")
    ?? throw new InvalidOperationException("SQL_MCP_HTTP env var (DAB SQL MCP) is not set.");

var contentUnderstandingMcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(new Uri(contentUnderstandingMcpUrl), "/mcp")
}));

var sqlMcp = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(new Uri(sqlMcpUrl), "/mcp")
}));

// Merge tools from both MCP servers into a single tool list the agent can call.
var tools = new List<AITool>();
tools.AddRange(await contentUnderstandingMcp.ListToolsAsync());
tools.AddRange(await sqlMcp.ListToolsAsync());

builder.Services.AddKeyedSingleton("content-understanding-mcp", contentUnderstandingMcp);
builder.Services.AddKeyedSingleton("sql-mcp", sqlMcp);

// Register the agent with the hosting infrastructure so A2A can resolve it by name.
builder.AddAIAgent(AgentName, (sp, _) =>
{
    var chatClient = sp.GetRequiredService<IChatClient>();
    return chatClient.AsAIAgent(
        instructions: agentInstructions,
        name: AgentName,
        description: "Ingests receipts/invoices via Content Understanding and persists them in SQL.",
        tools: tools);
});

builder.AddA2AServer(AgentName);

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// A2A protocol surface for cross-agent communication.
app.MapA2AHttpJson(AgentName, $"/a2a/{AgentName}");

// Convenience single-turn chat endpoint for direct user testing.
app.MapPost("/chat", async (
    [FromBody] ChatRequest request,
    [FromKeyedServices(AgentName)] AIAgent agent,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest("Message must not be empty.");
    }

    var session = await agent.CreateSessionAsync(cancellationToken);
    var response = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);
    return Results.Ok(new content_understanding.models.ChatResponse(response.Text));
});

app.Run();
