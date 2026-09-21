//Packages
#:package Aspire.Hosting.AppHost@13.5.2
#:package Aspire.Hosting.Azure.CosmosDB@13.5.2
#:package Aspire.Hosting.Azure.Storage@13.5.2
#:package Aspire.Hosting.Foundry@13.5.2-preview.1.26421.6
#:package Aspire.Hosting.JavaScript@13.5.2
#:package Aspire.Hosting.Python@13.5.2
#:package CommunityToolkit.Aspire.Hosting.PowerShell@13.5.0

//Sdks
#:sdk Aspire.AppHost.Sdk@13.5.2

// The file-based AppHost intentionally does not use the Aspire CLI bundle.
#:property NoWarn=ASPIRE010

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;
using Azure.Provisioning.Storage;
using CommunityToolkit.Aspire.Hosting.PowerShell;
using System.Management.Automation;

var builder = DistributedApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Microsoft Foundry: the chat model plus the deployments Content Understanding
// needs for its default analyzer models.
// ---------------------------------------------------------------------------
var foundry = builder.AddFoundry("foundry");
var foundryProject = foundry.AddProject("expenses");

var chatModel = foundryProject.AddModelDeployment("gpt5-4", FoundryModel.OpenAI.Gpt54);
var miniModel = foundryProject.AddModelDeployment("gpt5-4-mini", FoundryModel.OpenAI.Gpt54Mini);
var embeddingModel = foundryProject.AddModelDeployment("TextEmbedding3Large", FoundryModel.OpenAI.TextEmbedding3Large);

// Content Understanding lives on the same AI Services account; its endpoint is
// supplied as a parameter (`aspire secret set Parameters:contentUnderstandingEndpoint ...`).
var contentUnderstandingEndpoint = builder.AddParameter("contentUnderstandingEndpoint");
const string AnalyzerId = "ExpensesAnalyzer";

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
// Private receipt images, persisted locally by Azurite and in Azure Blob Storage
// when published. Both services receive the blob endpoint through WithReference.
// ---------------------------------------------------------------------------
var storage = builder.AddAzureStorage("receipt-storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume().WithLifetime(ContainerLifetime.Persistent))
    .ConfigureInfrastructure(infrastructure =>
    {
        var account = infrastructure.GetProvisionableResources().OfType<StorageAccount>().Single();
        account.AllowBlobPublicAccess = false;
    });
var receiptImages = storage.AddBlobContainer("receipt-images");

// ---------------------------------------------------------------------------
// MCP server (C#): record mutations and receipt management tools on /mcp.
// ---------------------------------------------------------------------------
var mcpServer = builder.AddProject("mcp-server", "../mcp-server/mcp-server.csproj")
    .WithReference(cosmos)
    .WaitFor(cosmos)
    .WithReference(receiptImages)
    .WaitFor(receiptImages)
    .WithEnvironment("Cosmos__DatabaseName", DatabaseName)
    .WithEnvironment("Cosmos__TripsContainer", TripsContainer)
    .WithEnvironment("Cosmos__ExpensesContainer", ExpensesContainer)
    .WithEnvironment("Cosmos__ConversationsContainer", ConversationsContainer)
    .WithHttpHealthCheck("/health");

// Direct, read-only database access for the frontend; no agent or model needed.
var recordsApi = builder.AddProject("expenses-api", "../expenses-api/expenses-api.csproj")
    .WithReference(cosmos)
    .WithReference(receiptImages)
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataReader)
    .WaitFor(cosmos)
    .WaitFor(receiptImages)
    .WaitFor(mcpServer) // The MCP startup initializers create the shared containers.
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
    .WithReference(miniModel).WaitFor(miniModel)
    .WithReference(embeddingModel).WaitFor(embeddingModel)
    .WithReference(mcpServer).WaitFor(mcpServer)
    // The durable-memory provider (Agent Memory Toolkit) reads and writes its own
    // Cosmos containers directly; trips and expenses still go through the MCP server.
    .WithEnvironment("ENABLE_COSMOS_MEMORY", builder.ExecutionContext.IsRunMode ? "false" : "true")
    .WithEnvironment("MEMORY_CHAT_MODEL", "gpt5-4-mini")
    .WithEnvironment("MEMORY_EMBEDDING_MODEL", "TextEmbedding3Large")
    .WithEnvironment("contentUnderstandingEndpoint", contentUnderstandingEndpoint)
    // `to_llm_input` keeps only the extracted receipt fields (no page markdown).
    .WithEnvironment("CONTENT_UNDERSTANDING_ANALYZER_ID", AnalyzerId)
    .WithEnvironment("CONTENT_UNDERSTANDING_OUTPUT_SECTIONS", "fields")
    // Let Aspire allocate both the proxy port and the port uvicorn binds (injected
    // as PORT). Pinning them by hand invites proxy/target port clashes; the frontend
    // reaches the agent through service discovery and the Vite proxy anyway.
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf);

if (builder.ExecutionContext.IsRunMode)
{
    var scripts = builder.AddPowerShell("scripts", languageMode: PSLanguageMode.FullLanguage);
    scripts.AddScript("analyzer-setup", File.ReadAllText(
            Path.GetFullPath("../../scripts/Initialize-ExpensesAnalyzer.ps1", builder.AppHostDirectory)))
        .WithArgs(
            contentUnderstandingEndpoint.Resource,
            Path.GetFullPath("../analyzers/Expenses.json", builder.AppHostDirectory),
            AnalyzerId,
            chatModel.Resource.Name,
            miniModel.Resource.Name,
            embeddingModel.Resource.Name)
        .WaitFor(chatModel)
        .WaitFor(miniModel)
        .WaitFor(embeddingModel);
}

if (builder.ExecutionContext.IsPublishMode)
{
    expensesAgent.WithReference(cosmos)
        .WaitFor(cosmos)
        .WithEnvironment("COSMOS_DATABASE", DatabaseName);
    expensesAgent.PublishAsDockerFile(container =>
        container.WithDockerfile("..", "python-agent/Dockerfile"));
    expensesAgent.AsHostedAgent(foundryProject);
}

// ---------------------------------------------------------------------------
// Vite routes /api to the read API and /chat, /health to the agent.
// ---------------------------------------------------------------------------
builder.AddViteApp("frontend", "../frontend")
    .WithNpm()
    .WithReference(expensesAgent)
    .WithReference(recordsApi)
    .WaitFor(recordsApi)
    .WithExternalHttpEndpoints();

builder.Build().Run();
