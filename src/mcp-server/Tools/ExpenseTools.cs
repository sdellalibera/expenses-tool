using System.ComponentModel;
using ExpensesMcpServer.Data;
using ExpensesMcpServer.Models;
using ModelContextProtocol.Server;

namespace ExpensesMcpServer.Tools;

/// <summary>
/// MCP tools for the expense records themselves. Every expense must belong to a
/// trip, which is what groups a Munich trip apart from a Seattle one.
/// </summary>
[McpServerToolType]
public sealed class ExpenseTools(IExpensesRepository repository, ILogger<ExpenseTools> logger)
{
    [McpServerTool(Name = "create_expense", UseStructuredContent = true)]
    [Description("Record a new expense on an existing work trip. Call create_trip first if the trip does not exist yet. Returns the created expense including its generated id.")]
    public async Task<Expense> CreateExpenseAsync(
        [Description("Identifier of the user the expense belongs to.")] string userId,
        [Description("Identifier of the trip the expense is grouped under.")] string tripId,
        [Description("Business, hotel, restaurant or airline the expense was made at.")] string merchant,
        [Description("Total amount paid.")] decimal totalAmount,
        [Description("Date the expense was made, ISO 8601 (yyyy-MM-dd).")] string date,
        [Description("Expense category, for example food, hotel, flight or ground transport.")] string category = "other",
        [Description("ISO 4217 currency code, for example EUR.")] string currency = "EUR",
        [Description("Line items that make up the receipt.")] IReadOnlyList<ExpenseLineItem>? lineItems = null,
        [Description("Optional free form note about the expense.")] string? notes = null,
        [Description("File name of the receipt image the expense was extracted from.")] string? sourceImage = null,
        [Description("Identifier of the chat conversation the expense was created in.")] string? conversationId = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(tripId, nameof(tripId));
        ToolGuard.RequireValue(merchant, nameof(merchant));
        ToolGuard.RequireValue(date, nameof(date));
        ToolGuard.RequireNonNegative(totalAmount, nameof(totalAmount));

        var trip = await repository.GetTripAsync(userId, tripId, cancellationToken)
            ?? throw new ArgumentException($"Trip '{tripId}' does not exist for this user. Create it with create_trip first.", nameof(tripId));

        logger.LogInformation(
            "MCP tool create_expense invoked by user {UserId}: {Merchant} {TotalAmount} {Currency} on trip {TripName}",
            userId, merchant, totalAmount, currency, trip.Name);

        return await repository.CreateExpenseAsync(
            new Expense
            {
                UserId = userId,
                TripId = tripId,
                Merchant = merchant,
                Category = category,
                Date = date,
                TotalAmount = totalAmount,
                Currency = currency,
                LineItems = lineItems ?? [],
                Notes = notes,
                SourceImage = sourceImage,
                ConversationId = conversationId,
            },
            cancellationToken);
    }

    [McpServerTool(Name = "list_expenses", UseStructuredContent = true)]
    [Description("List the expenses of a user, optionally restricted to a single trip. Results are ordered by expense date, newest first.")]
    public async Task<IReadOnlyList<Expense>> ListExpensesAsync(
        [Description("Identifier of the user whose expenses should be listed.")] string userId,
        [Description("Optional trip identifier to only return the expenses of that trip.")] string? tripId = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);

        logger.LogInformation("MCP tool list_expenses invoked by user {UserId} (trip {TripId})", userId, tripId ?? "*");

        return await repository.ListExpensesAsync(userId, tripId, cancellationToken);
    }

    // No output schema: returns null when the expense does not exist, and an MCP
    // client that sees an output schema rejects a null structured result.
    [McpServerTool(Name = "get_expense")]
    [Description("Read a single expense by id. Returns null when the expense does not exist.")]
    public async Task<Expense?> GetExpenseAsync(
        [Description("Identifier of the user the expense belongs to.")] string userId,
        [Description("Identifier of the expense to read.")] string expenseId,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(expenseId, nameof(expenseId));

        logger.LogInformation("MCP tool get_expense invoked by user {UserId} for expense {ExpenseId}", userId, expenseId);

        return await repository.GetExpenseAsync(userId, expenseId, cancellationToken);
    }

    // No output schema: returns null when the expense does not exist (see get_expense).
    [McpServerTool(Name = "update_expense")]
    [Description("Update one or more fields of an existing expense, for example to fix a wrong total or move it to a different trip. Omitted fields are left unchanged.")]
    public async Task<Expense?> UpdateExpenseAsync(
        [Description("Identifier of the user the expense belongs to.")] string userId,
        [Description("Identifier of the expense to update.")] string expenseId,
        [Description("Move the expense to this trip.")] string? tripId = null,
        [Description("New merchant name.")] string? merchant = null,
        [Description("New category.")] string? category = null,
        [Description("New expense date, ISO 8601 (yyyy-MM-dd).")] string? date = null,
        [Description("New total amount.")] decimal? totalAmount = null,
        [Description("New ISO 4217 currency code.")] string? currency = null,
        [Description("Replacement list of line items.")] IReadOnlyList<ExpenseLineItem>? lineItems = null,
        [Description("New note.")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(expenseId, nameof(expenseId));

        if (totalAmount is not null)
        {
            ToolGuard.RequireNonNegative(totalAmount.Value, nameof(totalAmount));
        }

        if (tripId is not null && await repository.GetTripAsync(userId, tripId, cancellationToken) is null)
        {
            throw new ArgumentException($"Trip '{tripId}' does not exist for this user.", nameof(tripId));
        }

        logger.LogInformation("MCP tool update_expense invoked by user {UserId} for expense {ExpenseId}", userId, expenseId);

        return await repository.UpdateExpenseAsync(
            userId,
            expenseId,
            new ExpensePatch
            {
                TripId = tripId,
                Merchant = merchant,
                Category = category,
                Date = date,
                TotalAmount = totalAmount,
                Currency = currency,
                LineItems = lineItems,
                Notes = notes,
            },
            cancellationToken);
    }

    [McpServerTool(Name = "delete_expense", UseStructuredContent = true)]
    [Description("Delete an expense. Returns true when the expense existed and was deleted.")]
    public async Task<bool> DeleteExpenseAsync(
        [Description("Identifier of the user the expense belongs to.")] string userId,
        [Description("Identifier of the expense to delete.")] string expenseId,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(expenseId, nameof(expenseId));

        logger.LogInformation("MCP tool delete_expense invoked by user {UserId} for expense {ExpenseId}", userId, expenseId);

        return await repository.DeleteExpenseAsync(userId, expenseId, cancellationToken);
    }
}
