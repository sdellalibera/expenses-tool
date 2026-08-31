//Packages
#:package Aspire.Hosting.AppHost@13.5.2
#:package Aspire.Hosting.Azure.CosmosDB@13.5.2
#:package Aspire.Hosting.DevTunnels@13.5.2
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

var foundry = builder.AddFoundry("foundry");
var project = foundry.AddProject("expenses");

var deployment = project.AddModelDeployment("gpt5", FoundryModel.OpenAI.Gpt5);
// Required by Content Understanding default models.
var deploymentMini = project.AddModelDeployment("gpt5mini", FoundryModel.OpenAI.Gpt5Mini);
var embeddingDeployment = project.AddModelDeployment("TextEmbedding3Large", FoundryModel.OpenAI.TextEmbedding3Large);

var contentUnderstandingEndpoint = builder.AddParameter("contentUnderstandingEndpoint");

#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos-db")
    .RunAsPreviewEmulator(emulator =>
    {
        emulator.WithDataExplorer();
        emulator.WithLifetime(ContainerLifetime.Persistent);
    });

var db = cosmos.AddCosmosDatabase("db");
var records = db.AddContainer("records", "/userId");

var expensesAgent = builder.AddPythonApp(
        name: "expenses-agent",
        appDirectory: "../expenses-agent-python",
        scriptPath: "expenses_agent_python/main.py")
    .WithEnvironment("contentUnderstandingEndpoint", contentUnderstandingEndpoint)
    .WithReference(deployment).WaitFor(deployment)
    .WithReference(cosmos).WaitFor(cosmos)
    .WithEnvironment("COSMOS_DATABASE", "db")
    .WithEnvironment("COSMOS_RECORDS_CONTAINER", "records")
    .WithHttpEndpoint(port: 8000, env: "PORT")
    .WithExternalHttpEndpoints()
    .AsHostedAgent(project);

var frontend = builder.AddViteApp("frontend", "../frontend")
    .WithNpm()
    .WithReference(expensesAgent).WaitFor(expensesAgent)
    .WithExternalHttpEndpoints();



builder.Build().Run();
