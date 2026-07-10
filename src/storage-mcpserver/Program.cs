using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// "expenses-images" is the Aspire connection name for the blob service
// configured in the AppHost. It resolves to an Azurite emulator locally
// and to an Azure Storage account when deployed.
builder.AddAzureBlobServiceClient("expenses-images");

// Microsoft Entra authentication for the MCP endpoint. It is only enforced when
// AzureAd:TenantId and AzureAd:Audience are configured (injected by the AppHost
// when Entra parameters are set); otherwise the server accepts anonymous calls
// for local development.
var azureAdTenantId = builder.Configuration["AzureAd:TenantId"];
var azureAdAudience = builder.Configuration["AzureAd:Audience"];
var requireEntraAuth = !string.IsNullOrEmpty(azureAdTenantId) && !string.IsNullOrEmpty(azureAdAudience);

if (requireEntraAuth)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{azureAdTenantId}/v2.0";
            options.Audience = azureAdAudience;
        });
    builder.Services.AddAuthorization();
}

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<BlobStorageTools>();

var app = builder.Build();

if (requireEntraAuth)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapDefaultEndpoints();

var mcp = app.MapMcp("/mcp");
if (requireEntraAuth)
{
    mcp.RequireAuthorization();
}

await app.RunAsync();
