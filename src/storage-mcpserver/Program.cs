var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// "expenses-images" is the Aspire connection name for the blob service
// configured in the AppHost. It resolves to an Azurite emulator locally
// and to an Azure Storage account when deployed.
builder.AddAzureBlobServiceClient("expenses-images");

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<BlobStorageTools>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");

await app.RunAsync();
