using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAzureClients(clients =>
{
   clients.AddContentUnderstandingClient(new Uri(builder.Configuration["FOUNDRY_URI"]
    ?? throw new InvalidOperationException("Content Understanding endpoint not set.")));
});

// BlobContainerClient for the "faces" blob container, referenced from the
// Aspire AppHost via mcpserver.WithReference(facesContainer). Used by the
// Person Directory tools to store face images and produce blob URLs that the
// Content Understanding service can read.
builder.AddAzureBlobContainerClient("faces");

// Typed HttpClient used by PersonDirectoryTools to call the Content
// Understanding Person Directory REST API. The base address and bearer token
// are configured by PersonDirectoryTools at request time using the Foundry
// endpoint and the registered TokenCredential.
builder.Services.AddHttpClient<PersonDirectoryTools>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<ContentUnderstandingTools>()
    .WithTools<PersonDirectoryTools>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");

await app.RunAsync();
