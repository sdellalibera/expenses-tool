using System.Text.Json;
using System.Text.Json.Serialization;
using ExpensesMcpServer;
using ExpensesMcpServer.Data;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<CosmosOptions>(builder.Configuration.GetSection(CosmosOptions.SectionName));

// `Cosmos:UseInMemory=true` keeps the MCP server runnable (and smoke-testable)
// without a database. The AppHost always runs against the real emulator.
var useInMemory = builder.Configuration.GetValue("Cosmos:UseInMemory", false);

if (useInMemory)
{
    builder.Services.AddSingleton<IExpensesRepository, InMemoryExpensesRepository>();
}
else
{
    builder.AddAzureCosmosClient(
        "cosmos-db",
        configureClientOptions: clientOptions =>
        {
            // camelCase so the documents match what the frontend and agent expect
            // (and so Cosmos gets the mandatory lowercase `id` property).
            clientOptions.UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        });

    builder.Services.AddSingleton<IExpensesRepository, CosmosExpensesRepository>();
    builder.Services.AddHostedService<CosmosInitializer>();
}

// Traces raised by the repository for every CRUD operation.
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource(Telemetry.ActivitySourceName));

builder.Services
    .AddMcpServer(options => options.ServerInfo = new() { Name = "expenses-mcp-server", Version = "1.0.0" })
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapDefaultEndpoints();

// The agent connects here with the streamable HTTP transport.
app.MapMcp("/mcp");

app.MapGet("/", () => Results.Ok(new
{
    service = "expenses-mcp-server",
    mcp = "/mcp",
    health = "/health",
}));

app.Run();

/// <summary>Exposed so the integration tests can boot the server with WebApplicationFactory.</summary>
public partial class Program;
