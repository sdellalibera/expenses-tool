using Expenses.Data.Models;

namespace Expenses.Data;

public interface IExpensesReader
{
    Task<IReadOnlyList<Trip>> ListTripsAsync(string userId, string? status = null, CancellationToken cancellationToken = default);
    Task<Trip?> GetTripAsync(string userId, string tripId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Expense>> ListExpensesAsync(string userId, string? tripId = null, CancellationToken cancellationToken = default);
    Task<Expense?> GetExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken = default);
    Task<Conversation?> GetConversationAsync(string userId, string conversationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default);
}
