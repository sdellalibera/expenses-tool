#:package Aspire.Hosting.AppHost@13.4.2
#:package Aspire.Hosting.Azure.CosmosDB@13.4.2
#:package Aspire.Hosting.Azure.Storage@13.4.2
#:package Aspire.Hosting.Foundry@13.4.2-preview.1.26303.6
#:package Aspire.Hosting.JavaScript@13.4.2

#:sdk Aspire.AppHost.Sdk@13.4.2

#:project ../expenses-agent/expenses-agent.csproj
#:project ../content-understanding-mcpserver/content-understanding-mcpserver.csproj
#:project ../storage-mcpserver/storage-mcpserver.csproj

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure.CosmosDB;
using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

var existingFoundryName = builder.AddParameter("existingFoundryName");
var existingFoundryResourceGroup = builder.AddParameter("existingFoundryResourceGroup");
var foundry = builder.AddFoundry("foundry").RunAsExisting(existingFoundryName,existingFoundryResourceGroup);

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
var sessions = db.AddContainer("sessions","/id");
var conversations = db.AddContainer("conversations","/conversationsId");

// Azure Storage account for captured receipt images. Runs against the Azurite
// emulator locally (BlobPort 27000) and against a real Storage account when deployed.
var storage = builder.AddAzureStorage("expenses-storage")
    .RunAsEmulator(emulator =>
    {
        emulator.WithBlobPort(27000);
        emulator.WithLifetime(ContainerLifetime.Persistent);
    });

var expensesImages = storage.AddBlobs("expenses-images");

var mcpserver = builder.AddProject("mcpserver", "../content-understanding-mcpserver/content-understanding-mcpserver.csproj")
    .WithHttpEndpoint()
    .WithReference(foundry).WaitFor(foundry);

var storagemcp = builder.AddProject("storagemcp", "../storage-mcpserver/storage-mcpserver.csproj")
    .WithHttpEndpoint()
    .WithReference(expensesImages).WaitFor(expensesImages);

var expensesAgent = builder.AddProject("expenses-agent", "../expenses-agent/expenses-agent.csproj")
    .WithExternalHttpEndpoints()
    .WithReference(foundry).WaitFor(foundry)
    .WithReference(sessions).WaitFor(sessions)
    .WithReference(expensesImages).WaitFor(expensesImages)
    .WithReference(mcpserver).WaitFor(mcpserver)
    .WithReference(storagemcp).WaitFor(storagemcp)
    .WithEnvironment("MCPSERVER_HTTP", mcpserver.GetEndpoint("http"))
    .WithEnvironment("STORAGEMCP_HTTP", storagemcp.GetEndpoint("http"));

// React frontend (Vite) used to capture photos from a phone camera. The Vite
// dev server is launched via npm and Aspire automatically forwards the chosen
// HTTP endpoint to Vite so it's reachable from a mobile device on the same network.
builder.AddViteApp("frontend", "../expenses-frontend")
    .WithNpm()
    .WithReference(expensesAgent).WaitFor(expensesAgent)
    .WithExternalHttpEndpoints();

builder.Build().Run();
