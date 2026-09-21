namespace Expenses.Data;

/// <summary>
/// Names of the Cosmos database and containers. The AppHost injects these as
/// <c>Cosmos__*</c> environment variables so a single place drives both the
/// Aspire resource graph and this server.
/// </summary>
public sealed class CosmosOptions
{
    public const string SectionName = "Cosmos";

    public string DatabaseName { get; set; } = "db";

    public string TripsContainer { get; set; } = "trips";

    public string ExpensesContainer { get; set; } = "expenses";

    public string ConversationsContainer { get; set; } = "conversations";
}
