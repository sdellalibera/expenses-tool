using System.Collections.Concurrent;
using Expenses.Data.Models;

namespace Expenses.Data;

/// <summary>
/// Process-local repository for explicitly opted-in development smoke runs.
/// Aspire always uses Cosmos so the MCP server and read API share data.
/// </summary>
public sealed class InMemoryExpensesRepository : IExpensesRepository
{
    private readonly ConcurrentDictionary<string, Trip> _trips = new();
    private readonly ConcurrentDictionary<string, Expense> _expenses = new();
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();

    private static string Key(string userId, string id) => $"{userId}::{id}";

    public Task<Trip> CreateTripAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("create", "trip", trip.UserId, trip.Id);

        trip.CreatedAt = DateTimeOffset.UtcNow;
        trip.UpdatedAt = trip.CreatedAt;
        _trips[Key(trip.UserId, trip.Id)] = trip;

        return Task.FromResult(trip);
    }

    public Task<IReadOnlyList<Trip>> ListTripsAsync(string userId, string? status = null, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("list", "trip", userId);

        IReadOnlyList<Trip> result =
        [
            .. _trips.Values
                .Where(t => t.UserId == userId && (status is null || t.Status == status))
                .OrderByDescending(t => t.CreatedAt)
        ];

        return Task.FromResult(result);
    }

    public Task<Trip?> GetTripAsync(string userId, string tripId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("read", "trip", userId, tripId);
        return Task.FromResult(_trips.TryGetValue(Key(userId, tripId), out var trip) ? trip : null);
    }

    public Task<Trip?> UpdateTripAsync(string userId, string tripId, TripPatch patch, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("update", "trip", userId, tripId);

        if (!_trips.TryGetValue(Key(userId, tripId), out var trip))
        {
            return Task.FromResult<Trip?>(null);
        }

        trip.Name = patch.Name ?? trip.Name;
        trip.Destination = patch.Destination ?? trip.Destination;
        trip.StartDate = patch.StartDate ?? trip.StartDate;
        trip.EndDate = patch.EndDate ?? trip.EndDate;
        trip.Purpose = patch.Purpose ?? trip.Purpose;
        trip.Currency = patch.Currency ?? trip.Currency;
        trip.Status = patch.Status ?? trip.Status;
        trip.UpdatedAt = DateTimeOffset.UtcNow;

        return Task.FromResult<Trip?>(trip);
    }

    public async Task<bool> DeleteTripAsync(string userId, string tripId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("delete", "trip", userId, tripId);

        foreach (var expense in await ListExpensesAsync(userId, tripId, cancellationToken))
        {
            _expenses.TryRemove(Key(userId, expense.Id), out _);
        }

        return _trips.TryRemove(Key(userId, tripId), out _);
    }

    public Task<Expense> CreateExpenseAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("create", "expense", expense.UserId, expense.Id);

        expense.CreatedAt = DateTimeOffset.UtcNow;
        expense.UpdatedAt = expense.CreatedAt;
        _expenses[Key(expense.UserId, expense.Id)] = expense;

        return Task.FromResult(expense);
    }

    public Task<IReadOnlyList<Expense>> ListExpensesAsync(string userId, string? tripId = null, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("list", "expense", userId, tripId);

        IReadOnlyList<Expense> result =
        [
            .. _expenses.Values
                .Where(e => e.UserId == userId && (tripId is null || e.TripId == tripId))
                .OrderByDescending(e => e.Date)
        ];

        return Task.FromResult(result);
    }

    public Task<Expense?> GetExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("read", "expense", userId, expenseId);
        return Task.FromResult(_expenses.TryGetValue(Key(userId, expenseId), out var expense) ? expense : null);
    }

    public Task<Expense?> UpdateExpenseAsync(string userId, string expenseId, ExpensePatch patch, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("update", "expense", userId, expenseId);

        if (!_expenses.TryGetValue(Key(userId, expenseId), out var expense))
        {
            return Task.FromResult<Expense?>(null);
        }

        expense.TripId = patch.TripId ?? expense.TripId;
        expense.Merchant = patch.Merchant ?? expense.Merchant;
        expense.Category = patch.Category ?? expense.Category;
        expense.Date = patch.Date ?? expense.Date;
        expense.TotalAmount = patch.TotalAmount ?? expense.TotalAmount;
        expense.Currency = patch.Currency ?? expense.Currency;
        expense.LineItems = patch.LineItems ?? expense.LineItems;
        expense.Notes = patch.Notes ?? expense.Notes;
        expense.SourceImage = patch.SourceImage ?? expense.SourceImage;
        expense.PhotoUrl = patch.PhotoUrl ?? expense.PhotoUrl;
        expense.UpdatedAt = DateTimeOffset.UtcNow;

        return Task.FromResult<Expense?>(expense);
    }

    public Task<bool> DeleteExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("delete", "expense", userId, expenseId);
        return Task.FromResult(_expenses.TryRemove(Key(userId, expenseId), out _));
    }

    public Task<Conversation> AppendConversationMessagesAsync(
        string userId,
        string conversationId,
        IReadOnlyList<ConversationMessage> messages,
        string? title = null,
        string? tripId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("append", "conversation", userId, conversationId);

        var conversation = _conversations.GetValueOrDefault(Key(userId, conversationId))
            ?? new Conversation
            {
                Id = conversationId,
                UserId = userId,
                Title = title ?? ConversationTitle.FromMessages(messages),
            };

        conversation.Messages = [.. conversation.Messages, .. messages];
        conversation.Title = title ?? (conversation.Title == "New conversation" ? ConversationTitle.FromMessages(messages) : conversation.Title);
        conversation.TripId = tripId ?? conversation.TripId;
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        _conversations[Key(userId, conversationId)] = conversation;

        return Task.FromResult(conversation);
    }

    public Task<Conversation?> GetConversationAsync(string userId, string conversationId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("read", "conversation", userId, conversationId);
        return Task.FromResult(_conversations.GetValueOrDefault(Key(userId, conversationId)));
    }

    public Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("list", "conversation", userId);

        IReadOnlyList<ConversationSummary> result =
        [
            .. _conversations.Values
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.UpdatedAt)
                .Select(ConversationTitle.ToSummary)
        ];

        return Task.FromResult(result);
    }

    public Task<bool> DeleteConversationAsync(string userId, string conversationId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("delete", "conversation", userId, conversationId);
        return Task.FromResult(_conversations.TryRemove(Key(userId, conversationId), out _));
    }
}
