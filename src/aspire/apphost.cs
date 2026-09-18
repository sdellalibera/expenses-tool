//Packages
#:package Aspire.Hosting.AppHost@13.5.2
#:package Aspire.Hosting.Azure.CosmosDB@13.5.2
#:package Aspire.Hosting.Foundry@13.5.2-preview.1.26421.6
#:package Aspire.Hosting.JavaScript@13.5.2
#:package Aspire.Hosting.Python@13.5.2

//Sdks
#:sdk Aspire.AppHost.Sdk@13.5.2

// The file-based AppHost intentionally does not use the Aspire CLI bundle.
#:property NoWarn=ASPIRE010

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Microsoft Foundry: the chat model plus the deployments Content Understanding
// needs for its default analyzer models.
// ---------------------------------------------------------------------------
var foundry = builder.AddFoundry("foundry");
var foundryProject = foundry.AddProject("expenses");

var chatModel = foundryProject.AddModelDeployment("gpt5", FoundryModel.OpenAI.Gpt5);
var miniModel = foundryProject.AddModelDeployment("gpt5mini", FoundryModel.OpenAI.Gpt5Mini);
var embeddingModel = foundryProject.AddModelDeployment("TextEmbedding3Large", FoundryModel.OpenAI.TextEmbedding3Large);

// Content Understanding lives on the same AI Services account; its endpoint is
// supplied as a parameter (`aspire secret set Parameters:contentUnderstandingEndpoint ...`).
var contentUnderstandingEndpoint = builder.AddParameter("contentUnderstandingEndpoint");

// ---------------------------------------------------------------------------
// Cosmos DB: conversation transcripts + every trip and expense record.
// Runs locally as the preview emulator container (Podman, see aspire.config.json).
// ---------------------------------------------------------------------------
#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos-db")
    .RunAsPreviewEmulator(emulator =>
    {
        emulator.WithDataExplorer();
        emulator.WithLifetime(ContainerLifetime.Persistent);
    });
#pragma warning restore ASPIRECOSMOSDB001

const string DatabaseName = "db";
const string TripsContainer = "trips";
const string ExpensesContainer = "expenses";
const string ConversationsContainer = "conversations";

var database = cosmos.AddCosmosDatabase(DatabaseName);
database.AddContainer(TripsContainer, "/userId");
// Aspire resource names are unique across the whole app model and the Foundry
// project above is already called "expenses", so this resource gets a distinct
// name while the Cosmos container itself stays "expenses".
database.AddContainer("expense-records", "/userId", ExpensesContainer);
database.AddContainer(ConversationsContainer, "/userId");

// ---------------------------------------------------------------------------
// MCP server (C#): the only component that talks to Cosmos DB. It exposes the
// record CRUD as MCP tools on /mcp.
// ---------------------------------------------------------------------------
var mcpServer = builder.AddProject("mcp-server", "../mcp-server/mcp-server.csproj")
    .WithReference(cosmos)
    .WaitFor(cosmos)
    .WithEnvironment("Cosmos__DatabaseName", DatabaseName)
    .WithEnvironment("Cosmos__TripsContainer", TripsContainer)
    .WithEnvironment("Cosmos__ExpensesContainer", ExpensesContainer)
    .WithEnvironment("Cosmos__ConversationsContainer", ConversationsContainer)
    .WithHttpHealthCheck("/health");

// ---------------------------------------------------------------------------
// Expenses agent (Python, Microsoft Agent Framework), published to Foundry as a
// hosted agent. It reaches Cosmos only through the MCP server above.
// ---------------------------------------------------------------------------
var expensesAgent = builder.AddPythonApp(
        name: "expenses-agent",
        appDirectory: "../python-agent",
        scriptPath: "expenses_agent/main.py")
    .WithReference(chatModel).WaitFor(chatModel)
    .WaitFor(miniModel)
    .WaitFor(embeddingModel)
    .WithReference(mcpServer).WaitFor(mcpServer)
    // The durable-memory provider (Agent Memory Toolkit) reads and writes its own
    // Cosmos containers directly; trips and expenses still go through the MCP server.
    .WithReference(cosmos).WaitFor(cosmos)
    .WithEnvironment("COSMOS_DATABASE", DatabaseName)
    .WithEnvironment("MEMORY_CHAT_MODEL", "gpt5mini")
    .WithEnvironment("MEMORY_EMBEDDING_MODEL", "TextEmbedding3Large")
    .WithEnvironment("contentUnderstandingEndpoint", contentUnderstandingEndpoint)
    // `to_llm_input` keeps only the extracted receipt fields (no page markdown).
    .WithEnvironment("CONTENT_UNDERSTANDING_ANALYZER_ID", "ExpensesAnalyzer")
    .WithEnvironment("CONTENT_UNDERSTANDING_OUTPUT_SECTIONS", "fields")
    // Let Aspire allocate both the proxy port and the port uvicorn binds (injected
    // as PORT). Pinning them by hand invites proxy/target port clashes; the frontend
    // reaches the agent through service discovery and the Vite proxy anyway.
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .AsHostedAgent(foundryProject);

// ---------------------------------------------------------------------------
// React frontend. The Vite dev server proxies /chat, /api and /health to the
// agent, so the app is same-origin and works from a phone on the same network.
// ---------------------------------------------------------------------------
builder.AddViteApp("frontend", "../frontend")
    .WithNpm()
    .WithReference(expensesAgent)
    .WaitFor(expensesAgent)
    .WithExternalHttpEndpoints();

builder.Build().Run();
