
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddAzureOpenAIClient(connectionName:"foundry").AddChatClient("gpt-5.4");

var mcpserverUrl = Environment.GetEnvironmentVariable("") ?? throw new InvalidOperationException ("Could not resolve MCP server URL");

var mcpEndpoint = new Uri(new Uri(mcpserverUrl),"/mcp");

var transport = new HttpClientTransport(new HttpClientTransportOptions{
    Endpoint = mcpEndpoint
});

var mcpClient = await McpClient.CreateAsync(transport);

var mcpTools = await mcpClient.ListToolsAsync();

builder.Services.AddSingleton(mcpClient);

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.UseHttpsRedirection();

app.Run();

