using Aspire.Hosting.Azure;
using Azure.Provisioning.Authorization;

var builder = DistributedApplication.CreateBuilder(args);

//Create Blob storage resource and containers
//Videos container for uploading videos to be analyzed by content understanding model
//Content-understanding container for the output of the analyzed videos and cu-models
var blob = builder.AddAzureStorage("storage");
var container_input = blob.AddBlobContainer("videos");
var container_output = blob.AddBlobContainer("content-understanding");

//Deploy foundry resource with necessary deployments for content-understanding
//Models required : gpt4.1,gpt4.1-mini,text-embedding
var foundry = builder.AddAzureAIFoundry("foundry")
    .WithRoleAssignments(blob,Azure.Provisioning.Storage.StorageBuiltInRole.StorageBlobDataReader);

var chat41 = foundry.AddDeployment("gpt-4-1",AIFoundryModel.OpenAI.Gpt41);

var chat41mini = foundry.AddDeployment("gpt-4-1-mini",AIFoundryModel.OpenAI.Gpt41Mini);

var embedding = foundry.AddDeployment("text-embedding-3-large",AIFoundryModel.OpenAI.TextEmbedding3Large);

//Deploy Search resource for indexing json output from content understanding
var search = builder.AddAzureSearch("search");

var server = builder.AddProject<Projects.VideoEnhancedAgent_Server>("server")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

//Function for triggering analysis of videos uploaded into input container
var function = builder.AddAzureFunctionsProject<Projects.azure_function>("functions")
    .WithReference(container_input)
    .WithReference(search)
    .WithRoleAssignments(blob,Azure.Provisioning.Storage.StorageBuiltInRole.StorageBlobDataReader)
    .WithRoleAssignments(blob,Azure.Provisioning.Storage.StorageBuiltInRole.StorageBlobDataContributor)
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(function)
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
