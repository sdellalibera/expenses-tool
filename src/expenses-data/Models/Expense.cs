using System.ComponentModel;

namespace Expenses.Data.Models;

/// <summary>
/// A single expense (one receipt) that always belongs to a <see cref="Trip"/>.
/// Stored in the <c>expenses</c> Cosmos container, partitioned by <c>/userId</c>.
/// </summary>
public sealed record Expense
{
    [Description("Unique expense identifier.")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Description("Identifier of the user the expense belongs to (Cosmos partition key).")]
    public string UserId { get; set; } = string.Empty;

    [Description("Identifier of the trip this expense is grouped under.")]
    public string TripId { get; set; } = string.Empty;

    [Description("Business, hotel, restaurant or airline the expense was made at.")]
    public string Merchant { get; set; } = string.Empty;

    [Description("Expense category, for example food, hotel, flight, ground transport.")]
    public string Category { get; set; } = "other";

    [Description("Date the expense was made, ISO 8601 (yyyy-MM-dd).")]
    public string Date { get; set; } = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

    [Description("Total amount paid.")]
    public decimal TotalAmount { get; set; }

    [Description("ISO 4217 currency code, for example EUR.")]
    public string Currency { get; set; } = "EUR";

    [Description("Individual line items that make up the receipt.")]
    public IReadOnlyList<ExpenseLineItem> LineItems { get; set; } = [];

    [Description("Optional free form note captured with the receipt.")]
    public string? Notes { get; set; }

    [Description("Name of the uploaded receipt image the expense was extracted from.")]
    public string? SourceImage { get; set; }

    [Description("Durable URL of the private receipt image in Blob Storage, without a SAS token.")]
    public string? PhotoUrl { get; set; }

    [Description("Identifier of the chat conversation the expense was created in.")]
    public string? ConversationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One line of a receipt.</summary>
public sealed record ExpenseLineItem
{
    [Description("Description of the purchased item.")]
    public string Description { get; set; } = string.Empty;

    [Description("Category of the purchased item, for example food or drink.")]
    public string Category { get; set; } = "other";

    [Description("Price paid for this line item.")]
    public decimal Price { get; set; }

    [Description("Quantity purchased. Defaults to 1.")]
    public decimal Quantity { get; set; } = 1;
}
