using Microsoft.Extensions.Azure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddAzureClients(clients =>
{
   clients.AddContentUnderstandingClient(new Uri(builder.Configuration["FOUNDRY_URI"]
    ?? throw new InvalidOperationException("Content Understanding endpoint not set.")));
});


builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<ContentUnderstandingTools>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapMcp("/mcp");

await app.RunAsync();
