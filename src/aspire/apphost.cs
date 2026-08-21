//Packages
#:package Aspire.Hosting.AppHost@13.4.2
#:package Aspire.Hosting.Azure.CosmosDB@13.4.6
#:package Aspire.Hosting.Foundry@13.4.6-preview.1.26319.6
#:package Aspire.Hosting.JavaScript@13.4.6
#:package Aspire.Hosting.Python@*

//Sdks
#:sdk Aspire.AppHost.Sdk@13.4.6

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure.CosmosDB;
using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

var existingFoundryName = builder.AddParameter("existingFoundryName");
var existingFoundryResourceGroup = builder.AddParameter("existingFoundryResourceGroup");

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
var conversations = db.AddContainer("conversations", "/conversationsId");
var records = db.AddContainer("records","/records");

// Expenses agent (Python, FastAPI + uvicorn). Receives invoice images from the
// frontend and runs Azure AI Content Understanding with the prebuilt-invoice
// analyzer. Renders the result via `to_llm_input` and returns the YAML payload
// that the downstream agent will consume via A2A (not wired up yet).
var expensesAgent = builder.AddPythonApp(
        name: "expenses-agent",
        appDirectory: "../expenses-agent-python",
        scriptPath: "agent.py")
    .WithHttpEndpoint(port: 8000, env: "PORT")
    .WithExternalHttpEndpoints()
    .WithEnvironment("AZURE_CONTENTUNDERSTANDING_ENDPOINT",
        builder.Configuration["AZURE_CONTENTUNDERSTANDING_ENDPOINT"]);

// React frontend (Vite) used to capture photos from a phone camera. The Vite
// dev server is launched via npm and Aspire automatically forwards the chosen
// HTTP endpoint to Vite so it's reachable from a mobile device on the same network.

/*
builder.AddViteApp("frontend", "../frontend")
    .WithNpm()
    .WithReference(expensesAgent).WaitFor(expensesAgent)
    .WithExternalHttpEndpoints();
*/

builder.Build().Run();
