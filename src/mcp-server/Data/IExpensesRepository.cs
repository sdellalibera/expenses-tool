using ExpensesMcpServer.Models;

namespace ExpensesMcpServer.Data;

/// <summary>
/// Every read and write the MCP server performs against Cosmos DB.
/// The interface exists so the MCP tools can be unit tested against
/// <see cref="InMemoryExpensesRepository"/> without a live database.
/// </summary>
public interface IExpensesRepository
{
    // ---- Trips -------------------------------------------------------
    Task<Trip> CreateTripAsync(Trip trip, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Trip>> ListTripsAsync(string userId, string? status = null, CancellationToken cancellationToken = default);

    Task<Trip?> GetTripAsync(string userId, string tripId, CancellationToken cancellationToken = default);

    Task<Trip?> UpdateTripAsync(string userId, string tripId, TripPatch patch, CancellationToken cancellationToken = default);

    Task<bool> DeleteTripAsync(string userId, string tripId, CancellationToken cancellationToken = default);

    // ---- Expenses ----------------------------------------------------
    Task<Expense> CreateExpenseAsync(Expense expense, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Expense>> ListExpensesAsync(string userId, string? tripId = null, CancellationToken cancellationToken = default);

    Task<Expense?> GetExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken = default);

    Task<Expense?> UpdateExpenseAsync(string userId, string expenseId, ExpensePatch patch, CancellationToken cancellationToken = default);

    Task<bool> DeleteExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken = default);

    // ---- Conversations ----------------------------------------------
    Task<Conversation> AppendConversationMessagesAsync(
        string userId,
        string conversationId,
        IReadOnlyList<ConversationMessage> messages,
        string? title = null,
        string? tripId = null,
        CancellationToken cancellationToken = default);

    Task<Conversation?> GetConversationAsync(string userId, string conversationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> DeleteConversationAsync(string userId, string conversationId, CancellationToken cancellationToken = default);
}

/// <summary>Partial update for a trip. <c>null</c> means "leave unchanged".</summary>
public sealed record TripPatch
{
    public string? Name { get; init; }
    public string? Destination { get; init; }
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public string? Purpose { get; init; }
    public string? Currency { get; init; }
    public string? Status { get; init; }
}

/// <summary>Partial update for an expense. <c>null</c> means "leave unchanged".</summary>
public sealed record ExpensePatch
{
    public string? TripId { get; init; }
    public string? Merchant { get; init; }
    public string? Category { get; init; }
    public string? Date { get; init; }
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
    public IReadOnlyList<ExpenseLineItem>? LineItems { get; init; }
    public string? Notes { get; init; }
}
