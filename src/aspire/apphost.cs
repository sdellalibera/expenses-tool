#:package Aspire.Hosting.AppHost@13.4.2
#:package Aspire.Hosting.Azure.CosmosDB@13.4.6
#:package Aspire.Hosting.Azure.Storage@13.4.6
#:package Aspire.Hosting.SqlServer@13.4.6
#:package Aspire.Hosting.Azure.Sql@13.4.6
#:package Aspire.Hosting.Foundry@13.4.6-preview.1.26319.6
#:package Aspire.Hosting.JavaScript@13.4.6
#:package Aspire.Hosting.Python@13.4.6

#:sdk Aspire.AppHost.Sdk@13.4.6

#:project ../storage-mcpserver/storage-mcpserver.csproj

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
// NOTE: Cosmos is retained for future session storage but is currently unused by
// the Python agent (which manages sessions in-memory / via Foundry threads).
db.AddContainer("sessions", "/id");
db.AddContainer("conversations", "/conversationsId");

// Azure Storage account for captured receipt images. Runs against the Azurite
// emulator locally (BlobPort 27000) and against a real Storage account when deployed.
var storage = builder.AddAzureStorage("expenses-storage")
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
    expensesDatabase = builder.AddAzureSqlServer("expenses-sql")
        .AddDatabase("expenses-database", "expensesdb");
}
else
{
    expensesDatabase = builder.AddSqlServer("expenses-sql")
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent)
        .AddDatabase("expenses-database", "expensesdb")
        .WithCreationScript(expensesSchema);
}

// SQL MCP Server powered by Data API builder. Exposes the SQL entities defined
// in ../expenses-database/dab-config.json as MCP tools (plus REST at /api and
// GraphQL at /graphql, MCP at /mcp). Runs locally as a container and deploys to
// Azure Container Apps.
//
// The DAB config uses the AzureAD auth provider; the audience/issuer come from the
// AZURE_AD_AUDIENCE / AZURE_AD_ISSUER environment variables.
var sqlMcpServer = builder.AddContainer("sql-mcp-server", "azure-databases/data-api-builder", "2.0.8")
    .WithImageRegistry("mcr.microsoft.com")
    .WithHttpEndpoint(targetPort: 5000, name: "http")
    .WithEnvironment("MSSQL_CONNECTION_STRING", expensesDatabase)
    .WithEnvironment("AZURE_AD_AUDIENCE", builder.Configuration["AZURE_AD_AUDIENCE"] ?? "")
    .WithEnvironment("AZURE_AD_ISSUER", builder.Configuration["AZURE_AD_ISSUER"] ?? "")
    .WithBindMount("../expenses-database/dab-config.json", "/App/dab-config.json", isReadOnly: true)
    .WaitFor(expensesDatabase);

// The Storage MCP server is exposed with external HTTP endpoints so that a
// Foundry-hosted agent can reach its /mcp endpoint. It validates Entra bearer
// tokens using the AZURE_AD_TENANT_ID / AZURE_AD_AUDIENCE environment variables.
var storageMcpServer = builder.AddProject("storagemcp", "../storage-mcpserver/storage-mcpserver.csproj")
    .WithHttpEndpoint()
    .WithExternalHttpEndpoints()
    .WithReference(expensesImages).WaitFor(expensesImages)
    .WithEnvironment("AzureAd__TenantId", builder.Configuration["AZURE_AD_TENANT_ID"] ?? "")
    .WithEnvironment("AzureAd__Audience", builder.Configuration["AZURE_AD_AUDIENCE"] ?? "");

// Expenses agent (Python, Microsoft Agent Framework) hosted as an ASGI app.
// It uses Content Understanding plus the SQL and Storage MCP servers as tools.
// The MCP server URLs arrive via service discovery (WithReference); the Entra
// scopes, Foundry and Content Understanding endpoints come from environment
// variables.
var expensesAgent = builder.AddUvicornApp("expenses-agent", "../expenses-agent", "expenses_agent.app:app")
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithReference(foundry).WaitFor(foundry)
    .WithReference(project)
    .WithReference(expensesImages).WaitFor(expensesImages)
    .WithReference(sqlMcpServer.GetEndpoint("http")).WaitFor(sqlMcpServer)
    .WithReference(storageMcpServer).WaitFor(storageMcpServer)
    .WithEnvironment("FOUNDRY_MODEL", builder.Configuration["FOUNDRY_MODEL"] ?? "gpt-5")
    .WithEnvironment("SQL_MCP_SCOPE", builder.Configuration["SQL_MCP_SCOPE"] ?? "")
    .WithEnvironment("STORAGE_MCP_SCOPE", builder.Configuration["STORAGE_MCP_SCOPE"] ?? "")
    .WithEnvironment("AZURE_CONTENTUNDERSTANDING_ENDPOINT", builder.Configuration["AZURE_CONTENTUNDERSTANDING_ENDPOINT"] ?? "")
    .WithEnvironment("FOUNDRY_PROJECT_ENDPOINT", builder.Configuration["FOUNDRY_PROJECT_ENDPOINT"] ?? "");

// React frontend (Vite) used to capture photos from a phone camera. The Vite
// dev server is launched via npm and Aspire automatically forwards the chosen
// HTTP endpoint to Vite so it's reachable from a mobile device on the same network.
builder.AddViteApp("frontend", "../frontend")
    .WithNpm()
    .WithReference(expensesAgent).WaitFor(expensesAgent)
    .WithExternalHttpEndpoints();

builder.Build().Run();
