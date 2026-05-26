#:package Aspire.Hosting.Azure.CosmosDB@13.3.5
#:package Aspire.Hosting.Azure.Storage@13.3.5
#:package Aspire.Hosting.Foundry@13.3.0-preview.1.26256.5

#:sdk Aspire.AppHost.Sdk@13.3.0

#:project ../extraction-agent/extraction-agent.csproj
#:project ../extraction-mcpserver/extraction-mcpserver.csproj

using Projects;
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
var sessions = db.AddContainer("sessions","/sessionsId");
var conversations = db.AddContainer("conversations","/conversationsId");

// Local Azurite-based Azure Storage emulator. Runs as a container using the
// configured container runtime (Docker or Podman). The "faces" blob container
// stores face images that are referenced from the Content Understanding
// Person Directory APIs (see PersonDirectoryTools in the mcpserver project).
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator =>
    {
        emulator.WithLifetime(ContainerLifetime.Persistent);
    });

var facesContainer = storage.AddBlobContainer("faces");

var mcpserver = builder.AddProject<Projects.extraction_mcpserver>("mcpserver")
    .WithHttpEndpoint()
    .WithReference(foundry).WaitFor(foundry)
    .WithReference(facesContainer).WaitFor(facesContainer);

var extraction_agent = builder.AddProject<Projects.extraction_agent>("extraction-agent")
    .WithReference(foundry).WaitFor(foundry)
    .WithReference(conversations).WaitFor(conversations)
    .WithReference(mcpserver).WaitFor(mcpserver);

builder.Build().Run();
