#:package Aspire.Hosting.AppHost@13.4.2
#:package Aspire.Hosting.Azure.CosmosDB@13.4.6
#:package Aspire.Hosting.Azure.Storage@13.4.6
#:package Aspire.Hosting.SqlServer@13.4.6
#:package Aspire.Hosting.Azure.Sql@13.4.6
#:package Aspire.Hosting.Foundry@13.4.6-preview.1.26319.6
#:package Aspire.Hosting.JavaScript@13.4.6

#:sdk Aspire.AppHost.Sdk@13.4.6

#:project ../storage-mcpserver/storage-mcpserver.csproj
#:project ../expenses-agent/expenses-agent.csproj

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure.CosmosDB;
using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

//var existingFoundryName = builder.AddParameter("existingFoundryName");
//var existingFoundryResourceGroup = builder.AddParameter("existingFoundryResourceGroup");
var foundry = builder.AddFoundry("foundry");
var project = foundry.AddProject("expenses-poc");
var ChatModel = project.AddModelDeployment("gpt-5",FoundryModel.OpenAI.Gpt5);


//subject to removal or change in future, requires pragma
#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos-db")
    .RunAsPreviewEmulator(
        emulator =>
        {
            emulator.WithDataExplorer();
            emulator.WithLifetime(ContainerLifetime.Persistent);
        });

var db = cosmos.AddCosmosDatabase("db");
// The C# agent persists each conversation thread in the "sessions" container so
// history survives across requests and restarts.
var sessions = db.AddContainer("sessions", "/id");
db.AddContainer("conversations", "/conversationsId");

// Azure Storage account for captured receipt images. Runs against the Azurite
// emulator locally (BlobPort 27000) and against a real Storage account when deployed.
var storage = builder.AddAzureStorage("expenses-blob-storage")
    .RunAsEmulator(emulator =>
    {
        emulator.WithBlobPort(27000);
        emulator.WithLifetime(ContainerLifetime.Persistent);
    });

var expensesImages = storage.AddBlobs("expenses-images");

// SQL Server database that stores expense reports and individual expense line
// items. Following the "blob reference" model, receipt images live in blob
// storage and only a reference (ReceiptBlobKey) plus metadata is kept in SQL.
//
// Local development: runs as a SQL Server container (Podman) with a persistent
// data volume; the schema in ../expenses-database/schema.sql is applied on first
// start via the creation script.
// Deployment: provisioned as an Azure SQL Database.
var expensesSchema = File.ReadAllText(
    Path.Combine(builder.AppHostDirectory, "..", "expenses-database", "schema.sql"));

IResourceBuilder<IResourceWithConnectionString> expensesDatabase;

if (builder.ExecutionContext.IsPublishMode)
{
    expensesDatabase = builder.AddAzureSqlServer("expenses-sql-server")
        .AddDatabase("expenses-sql-database", "expensesdb");
}
else
{
    expensesDatabase = builder.AddSqlServer("expenses-sql-database")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .AddDatabase("expenses-sql-database", "expensesdb")
        .WithCreationScript(expensesSchema);
}

// SQL MCP Server powered by Data API builder. Exposes the SQL entities defined
// in ../expenses-database/dab-config.json as MCP tools (plus REST at /api and
// GraphQL at /graphql, MCP at /mcp). Runs locally as a container and deploys to
// Azure Container Apps.
var sqlMcpServer = builder.AddContainer("sql-mcp-server", "azure-databases/data-api-builder", "2.0.8")
    .WithImageRegistry("mcr.microsoft.com")
    .WithHttpEndpoint(targetPort: 5000, name: "http")
    .WithEnvironment("MSSQL_CONNECTION_STRING", expensesDatabase)
    .WithBindMount("../expenses-database/dab-config.json", "/App/dab-config.json", isReadOnly: true)
    .WaitFor(expensesDatabase);

// The Storage MCP server is exposed with external HTTP endpoints so that a
// Foundry-hosted agent can reach its /mcp endpoint. It validates Entra bearer
// tokens using the AZURE_AD_TENANT_ID / AZURE_AD_AUDIENCE environment variables.
var blobStorageMcpServer = builder.AddProject("blob-storage-mcp-server", "../storage-mcpserver/storage-mcpserver.csproj")
    .WithHttpEndpoint()
    .WithExternalHttpEndpoints()
    .WithReference(expensesImages).WaitFor(expensesImages);

// Expenses agent (C#, Microsoft Agent Framework) hosted as an ASP.NET app.
// It analyzes receipt images with Azure AI Content Understanding (via the SDK)
// and uses the Storage MCP server as a tool. The MCP server URL arrives via
// service discovery (WithReference / STORAGEMCP_HTTP); the Foundry connection and
// Content Understanding endpoint come from configuration.
var expensesAgent = builder.AddProject("expenses-agent", "../expenses-agent/expenses-agent.csproj")
    .WithExternalHttpEndpoints()
    .WithReference(foundry).WaitFor(foundry)
    .WithReference(project)
    .WithReference(sessions).WaitFor(sessions)
    .WithReference(expensesImages).WaitFor(expensesImages)
    .WithReference(blobStorageMcpServer).WaitFor(blobStorageMcpServer)
    .WithReference(sqlMcpServer)
    .AsHostedAgent(project);

// React frontend (Vite) used to capture photos from a phone camera. The Vite
// dev server is launched via npm and Aspire automatically forwards the chosen
// HTTP endpoint to Vite so it's reachable from a mobile device on the same network.
builder.AddViteApp("frontend", "../frontend")
    .WithNpm()
    .WithReference(expensesAgent).WaitFor(expensesAgent)
    .WithExternalHttpEndpoints();

builder.Build().Run();
