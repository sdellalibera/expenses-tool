using Expenses.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddExpensesData();

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
