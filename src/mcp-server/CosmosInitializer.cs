using ExpensesMcpServer.Data;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace ExpensesMcpServer;

/// <summary>
/// Creates the database and the three containers on start-up so the demo works
/// against a freshly started Cosmos emulator without any manual step.
/// </summary>
public sealed class CosmosInitializer(
    CosmosClient client,
    IOptions<CosmosOptions> options,
    ILogger<CosmosInitializer> logger) : IHostedService
{
    private readonly CosmosOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Ensuring Cosmos database '{Database}' and containers exist", _options.DatabaseName);

        var database = await client.CreateDatabaseIfNotExistsAsync(_options.DatabaseName, cancellationToken: cancellationToken);

        foreach (var container in new[] { _options.TripsContainer, _options.ExpensesContainer, _options.ConversationsContainer })
        {
            await database.Database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(container, _options.PartitionKeyPath),
                cancellationToken: cancellationToken);

            logger.LogInformation("Cosmos container '{Container}' is ready", container);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
