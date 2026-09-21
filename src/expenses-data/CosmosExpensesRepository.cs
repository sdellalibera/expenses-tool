using System.Net;
using Expenses.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Expenses.Data;

/// <summary>Cosmos DB backed implementation of <see cref="IExpensesRepository"/>.</summary>
public sealed class CosmosExpensesRepository : IExpensesRepository
{
    private readonly CosmosClient _client;
    private readonly CosmosOptions _options;
    private readonly ILogger<CosmosExpensesRepository> _logger;

    public CosmosExpensesRepository(
        CosmosClient client,
        IOptions<CosmosOptions> options,
        ILogger<CosmosExpensesRepository> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    private Container Trips => _client.GetContainer(_options.DatabaseName, _options.TripsContainer);

    private Container Expenses => _client.GetContainer(_options.DatabaseName, _options.ExpensesContainer);

    private Container Conversations => _client.GetContainer(_options.DatabaseName, _options.ConversationsContainer);

    // ------------------------------------------------------------------
    // Trips
    // ------------------------------------------------------------------

    public async Task<Trip> CreateTripAsync(Trip trip, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("create", "trip", trip.UserId, trip.Id);

        trip.CreatedAt = DateTimeOffset.UtcNow;
        trip.UpdatedAt = trip.CreatedAt;

        var response = await Trips.CreateItemAsync(trip, new PartitionKey(trip.UserId), cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Created trip {TripId} ({TripName}) for user {UserId} [{RequestCharge} RU]",
            trip.Id, trip.Name, trip.UserId, response.RequestCharge);

        return response.Resource;
    }

    public async Task<IReadOnlyList<Trip>> ListTripsAsync(string userId, string? status = null, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("list", "trip", userId);

        var sql = status is null
            ? "SELECT * FROM c WHERE c.userId = @userId ORDER BY c.createdAt DESC"
            : "SELECT * FROM c WHERE c.userId = @userId AND c.status = @status ORDER BY c.createdAt DESC";

        var query = new QueryDefinition(sql).WithParameter("@userId", userId);
        if (status is not null)
        {
            query = query.WithParameter("@status", status);
        }

        var trips = await QueryAsync<Trip>(Trips, query, userId, cancellationToken);

        _logger.LogInformation("Listed {Count} trips for user {UserId}", trips.Count, userId);
        activity?.SetTag("expenses.result_count", trips.Count);

        return trips;
    }

    public async Task<Trip?> GetTripAsync(string userId, string tripId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("read", "trip", userId, tripId);

        var trip = await ReadOrDefaultAsync<Trip>(Trips, tripId, userId, cancellationToken);
        _logger.LogInformation("Read trip {TripId} for user {UserId}: {Found}", tripId, userId, trip is not null);

        return trip;
    }

    public async Task<Trip?> UpdateTripAsync(string userId, string tripId, TripPatch patch, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("update", "trip", userId, tripId);

        var existing = await ReadOrDefaultAsync<Trip>(Trips, tripId, userId, cancellationToken);
        if (existing is null)
        {
            _logger.LogWarning("Trip {TripId} not found for user {UserId}; update skipped", tripId, userId);
            return null;
        }

        existing.Name = patch.Name ?? existing.Name;
        existing.Destination = patch.Destination ?? existing.Destination;
        existing.StartDate = patch.StartDate ?? existing.StartDate;
        existing.EndDate = patch.EndDate ?? existing.EndDate;
        existing.Purpose = patch.Purpose ?? existing.Purpose;
        existing.Currency = patch.Currency ?? existing.Currency;
        existing.Status = patch.Status ?? existing.Status;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        var response = await Trips.ReplaceItemAsync(existing, tripId, new PartitionKey(userId), cancellationToken: cancellationToken);
        _logger.LogInformation("Updated trip {TripId} for user {UserId} [{RequestCharge} RU]", tripId, userId, response.RequestCharge);

        return response.Resource;
    }

    public async Task<bool> DeleteTripAsync(string userId, string tripId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("delete", "trip", userId, tripId);

        // Cascade: a trip cannot leave orphaned expenses behind.
        var expenses = await ListExpensesAsync(userId, tripId, cancellationToken);
        foreach (var expense in expenses)
        {
            await DeleteExpenseAsync(userId, expense.Id, cancellationToken);
        }

        try
        {
            await Trips.DeleteItemAsync<Trip>(tripId, new PartitionKey(userId), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted trip {TripId} and {Count} expenses for user {UserId}", tripId, expenses.Count, userId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Trip {TripId} not found for user {UserId}; delete skipped", tripId, userId);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Expenses
    // ------------------------------------------------------------------

    public async Task<Expense> CreateExpenseAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("create", "expense", expense.UserId, expense.Id);

        expense.CreatedAt = DateTimeOffset.UtcNow;
        expense.UpdatedAt = expense.CreatedAt;

        var response = await Expenses.CreateItemAsync(expense, new PartitionKey(expense.UserId), cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Created expense {ExpenseId} ({Merchant}, {TotalAmount} {Currency}) on trip {TripId} for user {UserId} [{RequestCharge} RU]",
            expense.Id, expense.Merchant, expense.TotalAmount, expense.Currency, expense.TripId, expense.UserId, response.RequestCharge);

        return response.Resource;
    }

    public async Task<IReadOnlyList<Expense>> ListExpensesAsync(string userId, string? tripId = null, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("list", "expense", userId, tripId);

        var sql = tripId is null
            ? "SELECT * FROM c WHERE c.userId = @userId ORDER BY c.date DESC"
            : "SELECT * FROM c WHERE c.userId = @userId AND c.tripId = @tripId ORDER BY c.date DESC";

        var query = new QueryDefinition(sql).WithParameter("@userId", userId);
        if (tripId is not null)
        {
            query = query.WithParameter("@tripId", tripId);
        }

        var expenses = await QueryAsync<Expense>(Expenses, query, userId, cancellationToken);

        _logger.LogInformation("Listed {Count} expenses for user {UserId} (trip {TripId})", expenses.Count, userId, tripId ?? "*");
        activity?.SetTag("expenses.result_count", expenses.Count);

        return expenses;
    }

    public async Task<Expense?> GetExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("read", "expense", userId, expenseId);

        var expense = await ReadOrDefaultAsync<Expense>(Expenses, expenseId, userId, cancellationToken);
        _logger.LogInformation("Read expense {ExpenseId} for user {UserId}: {Found}", expenseId, userId, expense is not null);

        return expense;
    }

    public async Task<Expense?> UpdateExpenseAsync(string userId, string expenseId, ExpensePatch patch, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("update", "expense", userId, expenseId);

        var existing = await ReadOrDefaultAsync<Expense>(Expenses, expenseId, userId, cancellationToken);
        if (existing is null)
        {
            _logger.LogWarning("Expense {ExpenseId} not found for user {UserId}; update skipped", expenseId, userId);
            return null;
        }

        existing.TripId = patch.TripId ?? existing.TripId;
        existing.Merchant = patch.Merchant ?? existing.Merchant;
        existing.Category = patch.Category ?? existing.Category;
        existing.Date = patch.Date ?? existing.Date;
        existing.TotalAmount = patch.TotalAmount ?? existing.TotalAmount;
        existing.Currency = patch.Currency ?? existing.Currency;
        existing.LineItems = patch.LineItems ?? existing.LineItems;
        existing.Notes = patch.Notes ?? existing.Notes;
        existing.SourceImage = patch.SourceImage ?? existing.SourceImage;
        existing.PhotoUrl = patch.PhotoUrl ?? existing.PhotoUrl;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        var response = await Expenses.ReplaceItemAsync(existing, expenseId, new PartitionKey(userId), cancellationToken: cancellationToken);
        _logger.LogInformation("Updated expense {ExpenseId} for user {UserId} [{RequestCharge} RU]", expenseId, userId, response.RequestCharge);

        return response.Resource;
    }

    public async Task<bool> DeleteExpenseAsync(string userId, string expenseId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("delete", "expense", userId, expenseId);

        try
        {
            await Expenses.DeleteItemAsync<Expense>(expenseId, new PartitionKey(userId), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted expense {ExpenseId} for user {UserId}", expenseId, userId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Expense {ExpenseId} not found for user {UserId}; delete skipped", expenseId, userId);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Conversations
    // ------------------------------------------------------------------

    public async Task<Conversation> AppendConversationMessagesAsync(
        string userId,
        string conversationId,
        IReadOnlyList<ConversationMessage> messages,
        string? title = null,
        string? tripId = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("append", "conversation", userId, conversationId);

        return await MutateConversationAsync(userId, conversationId, existing =>
        {
            existing.Messages = [.. existing.Messages, .. messages];
            existing.Title = title ?? (existing.Title == "New conversation" ? ConversationTitle.FromMessages(messages) : existing.Title);
            existing.TripId = tripId ?? existing.TripId;
        }, cancellationToken);
    }

    public async Task SaveReceiptCheckpointAsync(string userId, string conversationId, string key,
        ReceiptCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("checkpoint", "conversation", userId, conversationId);
        await MutateConversationAsync(userId, conversationId, conversation => conversation.SetReceiptCheckpoint(key, checkpoint), cancellationToken);
    }

    private async Task<Conversation> MutateConversationAsync(string userId, string conversationId,
        Action<Conversation> mutate, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            Conversation conversation;
            string? etag = null;
            try
            {
                var read = await Conversations.ReadItemAsync<Conversation>(conversationId, new PartitionKey(userId), cancellationToken: cancellationToken);
                conversation = read.Resource;
                etag = read.ETag;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                conversation = new Conversation { Id = conversationId, UserId = userId };
            }
            mutate(conversation);
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            try
            {
                var response = etag is null
                    ? await Conversations.CreateItemAsync(conversation, new PartitionKey(userId), cancellationToken: cancellationToken)
                    : await Conversations.ReplaceItemAsync(conversation, conversationId, new PartitionKey(userId),
                        new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken);
                return response.Resource;
            }
            catch (CosmosException ex) when (attempt < 4 && ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
            }
        }
    }

    public async Task<Conversation?> GetConversationAsync(string userId, string conversationId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("read", "conversation", userId, conversationId);
        return await ReadOrDefaultAsync<Conversation>(Conversations, conversationId, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("list", "conversation", userId);

        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId ORDER BY c.updatedAt DESC")
            .WithParameter("@userId", userId);

        var conversations = await QueryAsync<Conversation>(Conversations, query, userId, cancellationToken);

        return [.. conversations.Select(ConversationTitle.ToSummary)];
    }

    public async Task<bool> DeleteConversationAsync(string userId, string conversationId, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartRecordActivity("delete", "conversation", userId, conversationId);

        try
        {
            await Conversations.DeleteItemAsync<Conversation>(conversationId, new PartitionKey(userId), cancellationToken: cancellationToken);
            _logger.LogInformation("Deleted conversation {ConversationId} for user {UserId}", conversationId, userId);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static async Task<T?> ReadOrDefaultAsync<T>(Container container, string id, string userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.ReadItemAsync<T>(id, new PartitionKey(userId), cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    private static async Task<List<T>> QueryAsync<T>(Container container, QueryDefinition query, string userId, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        using var iterator = container.GetItemQueryIterator<T>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        return results;
    }
}
