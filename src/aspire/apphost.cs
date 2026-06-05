#:package Aspire.Hosting.AppHost@13.4.2
#:package Aspire.Hosting.Azure.CosmosDB@13.4.2
#:package Aspire.Hosting.Foundry@13.4.2-preview.1.26303.6

#:sdk Aspire.AppHost.Sdk@13.4.2

#:project ../expenses-agent/expenses-agent.csproj
#:project ../content-understanding-mcpserver/content-understanding-mcpserver.csproj

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
var sessions = db.AddContainer("sessions","/sessionsId");
var conversations = db.AddContainer("conversations","/conversationsId");

var mcpserver = builder.AddProject("mcpserver", "../content-understanding-mcpserver/content-understanding-mcpserver.csproj")
    .WithHttpEndpoint()
    .WithReference(foundry).WaitFor(foundry);


var expensesAgent = builder.AddProject("expenses-agent", "../expenses-agent/expenses-agent.csproj")
    .WithReference(foundry).WaitFor(foundry)
    .WithReference(conversations).WaitFor(conversations)
    .WithReference(mcpserver).WaitFor(mcpserver)
    .WithEnvironment("MCPSERVER_HTTP", mcpserver.GetEndpoint("http"));

builder.Build().Run();
