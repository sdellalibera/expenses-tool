#:package Aspire.Hosting.Azure.CosmosDB@13.3.5
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
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(
        emulator =>
        {
            emulator.WithDataExplorer();
            emulator.WithLifetime(ContainerLifetime.Persistent);
        });

var mcpserver = builder.AddProject<Projects.extraction_mcpserver>("mcpserver")
    .WithReference(foundry);

var extraction_agent = builder.AddProject<Projects.extraction_agent>("extraction-agent")
    .WithReference(cosmos)
    .WithReference(mcpserver);

builder.Build().Run();
