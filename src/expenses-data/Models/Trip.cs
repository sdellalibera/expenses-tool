using System.ComponentModel;

namespace Expenses.Data.Models;

/// <summary>
/// A work trip that groups together every expense made while travelling.
/// Stored in the <c>trips</c> Cosmos container, partitioned by <c>/userId</c>.
/// </summary>
public sealed record Trip
{
    [Description("Unique trip identifier.")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Description("Identifier of the user the trip belongs to (Cosmos partition key).")]
    public string UserId { get; set; } = string.Empty;

    [Description("Human friendly trip name, for example 'Munich kickoff'.")]
    public string Name { get; set; } = string.Empty;

    [Description("City or country the trip took place in.")]
    public string? Destination { get; set; }

    [Description("Trip start date in ISO 8601 format (yyyy-MM-dd).")]
    public string? StartDate { get; set; }

    [Description("Trip end date in ISO 8601 format (yyyy-MM-dd).")]
    public string? EndDate { get; set; }

    [Description("Free form purpose of the trip.")]
    public string? Purpose { get; set; }

    [Description("ISO 4217 currency the trip is reported in, for example EUR.")]
    public string? Currency { get; set; }

    [Description("Lifecycle status: open, submitted or closed.")]
    public string Status { get; set; } = TripStatus.Open;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class TripStatus
{
    public const string Open = "open";
    public const string Submitted = "submitted";
    public const string Closed = "closed";

    public static readonly string[] All = [Open, Submitted, Closed];
}

/// <summary>A trip plus the roll-up of the expenses attached to it.</summary>
public sealed record TripSummary
{
    public required Trip Trip { get; init; }

    public int ExpenseCount { get; init; }

    public decimal TotalAmount { get; init; }
}
