using A2A;
using A2A.AspNetCore;
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

// AgentCard advertised over A2A so other agents can discover this agent's
// identity, capabilities, and skills.
var agentCard = new AgentCard
{
    Name = AgentName,
    Description = "Ingests receipts/invoices via Content Understanding and persists them in SQL.",
    Version = "1.0.0",
    DefaultInputModes = new List<string> { "text" },
    DefaultOutputModes = new List<string> { "text" },
    Capabilities = new AgentCapabilities
    {
        Streaming = false,
        PushNotifications = false
    },
    Skills = new List<A2A.AgentSkill>
    {
        new()
        {
            Id = "ingest-invoice",
            Name = "Ingest invoice or receipt",
            Description = "Analyzes a receipt or invoice with Content Understanding and stores the structured result (documents, invoices, line items) in the expenses SQL database.",
            Tags = new List<string> { "invoice", "receipt", "expenses", "content-understanding", "sql" },
            Examples = new List<string>
            {
                "Process this receipt and add it to the expenses database.",
                "Ingest the attached invoice PDF.",
                "Analyze https://example.com/receipt.png and persist the line items."
            },
            InputModes = new List<string> { "text" },
            OutputModes = new List<string> { "text" }
        },
        new()
        {
            Id = "query-expenses",
            Name = "Query expenses",
            Description = "Answers natural-language questions over the expenses SQL database (totals, vendors, dates, line items).",
            Tags = new List<string> { "expenses", "sql", "query", "reporting" },
            Examples = new List<string>
            {
                "What did I spend at Contoso last month?",
                "List all invoices over $500.",
                "Show line items for invoice #42."
            },
            InputModes = new List<string> { "text" },
            OutputModes = new List<string> { "text" }
        }
    }
};

// A2A protocol surface for cross-agent communication. Uses the explicit
// AgentCard so the well-known discovery endpoint advertises real metadata
// rather than the framework's default placeholder card.
var a2aServer = app.Services.GetRequiredKeyedService<A2AServer>(AgentName);
app.MapHttpA2A((IA2ARequestHandler)a2aServer, agentCard, $"/a2a/{AgentName}");
app.MapWellKnownAgentCard(agentCard);

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
