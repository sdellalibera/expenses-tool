#:package Aspire.Hosting.Foundry@13.3.0-preview.1.26256.5

#:sdk Aspire.AppHost.Sdk@13.3.0

#:project ../extraction-agent/extraction-agent.csproj

using Projects;
using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

var existingFoundryName = builder.AddParameter("existingFoundryName");
var existingFoundryResourceGroup = builder.AddParameter("existingFoundryResourceGroup");
var foundry = builder.AddFoundry("foundry").RunAsExisting(existingFoundryName,existingFoundryResourceGroup);

var extraction_agent = builder.AddProject<Projects.extraction_agent>("extraction-agent");

builder.Build().Run();
