using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Configure all logs to go to stderr (stdout is used for the MCP protocol messages).
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddAzureClients(clients =>
{
   clients.AddContentUnderstandingClient(new Uri(builder.Configuration["ContentUnderstanding:Endpoint"]
    ?? throw new InvalidOperationException("Content Understanding endpoint not set."))); 
});

// Add the MCP services: the transport to use (stdio) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ContentUnderstandingTools>();

await builder.Build().RunAsync();
