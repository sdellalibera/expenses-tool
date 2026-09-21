using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Expenses.Data;

public static class ServiceRegistration
{
    public static void AddExpensesData(this IHostApplicationBuilder builder, bool readOnly = false)
    {
        builder.Services.Configure<CosmosOptions>(builder.Configuration.GetSection(CosmosOptions.SectionName));
        var useInMemory = builder.Configuration.GetValue("Cosmos:UseInMemory", false);
        if (!useInMemory)
        {
            builder.AddAzureCosmosClient("cosmos-db", configureClientOptions: options =>
            {
                options.UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                };
            });
        }

        if (readOnly)
        {
            if (useInMemory)
            {
                builder.Services.AddSingleton<IExpensesReader, InMemoryExpensesRepository>();
            }
            else
            {
                builder.Services.AddSingleton<IExpensesReader, CosmosExpensesRepository>();
            }
        }
        else
        {
            if (useInMemory)
            {
                builder.Services.AddSingleton<IExpensesRepository, InMemoryExpensesRepository>();
            }
            else
            {
                builder.Services.AddSingleton<IExpensesRepository, CosmosExpensesRepository>();
            }
            builder.Services.AddSingleton<IExpensesReader>(services => services.GetRequiredService<IExpensesRepository>());
        }

        builder.AddAzureBlobContainerClient("receipt-images");
        builder.Services.AddSingleton<ReceiptStorage>();
        builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource(Telemetry.ActivitySourceName));
    }
}
