using ExpensesMcpServer.Data;
using ExpensesMcpServer.Models;
using ExpensesMcpServer.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExpensesMcpServer.Tests;

/// <summary>
/// Exercises the CRUD tools the agent calls over MCP against the in-memory
/// repository, which implements exactly the same contract as the Cosmos one.
/// </summary>
public class ExpenseToolsTests
{
    private const string UserId = "user-1";

    private static (TripTools Trips, ExpenseTools Expenses) CreateTools(out IExpensesRepository repository)
    {
        repository = new InMemoryExpensesRepository();
        return (
            new TripTools(repository, NullLogger<TripTools>.Instance),
            new ExpenseTools(repository, NullLogger<ExpenseTools>.Instance));
    }

    [Fact]
    public async Task CreateTrip_persists_and_is_listed()
    {
        var (trips, _) = CreateTools(out _);

        var created = await trips.CreateTripAsync(UserId, "Munich kickoff", destination: "Munich", startDate: "2026-03-01");

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.Equal("Munich kickoff", created.Name);
        Assert.Equal(TripStatus.Open, created.Status);

        var listed = await trips.ListTripsAsync(UserId);

        var only = Assert.Single(listed);
        Assert.Equal(created.Id, only.Trip.Id);
        Assert.Equal(0, only.ExpenseCount);
        Assert.Equal(0m, only.TotalAmount);
    }

    [Fact]
    public async Task CreateTrip_rejects_missing_user()
    {
        var (trips, _) = CreateTools(out _);

        await Assert.ThrowsAsync<ArgumentException>(() => trips.CreateTripAsync("  ", "Munich"));
    }

    [Fact]
    public async Task Expenses_are_grouped_by_trip()
    {
        var (trips, expenses) = CreateTools(out _);

        var munich = await trips.CreateTripAsync(UserId, "Munich kickoff", destination: "Munich");
        var seattle = await trips.CreateTripAsync(UserId, "Seattle summit", destination: "Seattle");

        await expenses.CreateExpenseAsync(UserId, munich.Id, "Hofbrauhaus", 42.50m, "2026-03-02", "food");
        await expenses.CreateExpenseAsync(UserId, munich.Id, "Hotel Bayern", 210m, "2026-03-01", "hotel");
        await expenses.CreateExpenseAsync(UserId, seattle.Id, "Pike Place Chowder", 18m, "2026-04-10", "food");

        var munichExpenses = await expenses.ListExpensesAsync(UserId, munich.Id);
        var seattleExpenses = await expenses.ListExpensesAsync(UserId, seattle.Id);
        var all = await expenses.ListExpensesAsync(UserId);

        Assert.Equal(2, munichExpenses.Count);
        Assert.Single(seattleExpenses);
        Assert.Equal(3, all.Count);

        var summaries = await trips.ListTripsAsync(UserId);
        var munichSummary = summaries.Single(s => s.Trip.Id == munich.Id);

        Assert.Equal(2, munichSummary.ExpenseCount);
        Assert.Equal(252.50m, munichSummary.TotalAmount);
    }

    [Fact]
    public async Task CreateExpense_requires_an_existing_trip()
    {
        var (_, expenses) = CreateTools(out _);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => expenses.CreateExpenseAsync(UserId, "does-not-exist", "Hofbrauhaus", 10m, "2026-03-02"));

        Assert.Contains("create_trip", ex.Message);
    }

    [Fact]
    public async Task CreateExpense_stores_line_items_and_totals()
    {
        var (trips, expenses) = CreateTools(out _);
        var trip = await trips.CreateTripAsync(UserId, "Munich kickoff");

        var created = await expenses.CreateExpenseAsync(
            UserId,
            trip.Id,
            "Hofbrauhaus",
            42.50m,
            "2026-03-02",
            category: "food",
            currency: "EUR",
            lineItems:
            [
                new ExpenseLineItem { Description = "Schnitzel", Category = "food", Price = 24.50m },
                new ExpenseLineItem { Description = "Weissbier", Category = "drink", Price = 18m, Quantity = 2 },
            ],
            notes: "Team dinner",
            sourceImage: "receipt.jpg");

        var read = await expenses.GetExpenseAsync(UserId, created.Id);

        Assert.NotNull(read);
        Assert.Equal("Hofbrauhaus", read.Merchant);
        Assert.Equal(42.50m, read.TotalAmount);
        Assert.Equal("EUR", read.Currency);
        Assert.Equal(2, read.LineItems.Count);
        Assert.Equal("Team dinner", read.Notes);
        Assert.Equal("receipt.jpg", read.SourceImage);
    }

    [Fact]
    public async Task UpdateExpense_only_changes_supplied_fields()
    {
        var (trips, expenses) = CreateTools(out _);
        var trip = await trips.CreateTripAsync(UserId, "Munich kickoff");
        var created = await expenses.CreateExpenseAsync(UserId, trip.Id, "Hofbrauhaus", 42.50m, "2026-03-02", "food");

        var updated = await expenses.UpdateExpenseAsync(UserId, created.Id, totalAmount: 45m);

        Assert.NotNull(updated);
        Assert.Equal(45m, updated.TotalAmount);
        Assert.Equal("Hofbrauhaus", updated.Merchant);
        Assert.Equal("food", updated.Category);
    }

    [Fact]
    public async Task UpdateExpense_can_move_an_expense_to_another_trip()
    {
        var (trips, expenses) = CreateTools(out _);
        var munich = await trips.CreateTripAsync(UserId, "Munich kickoff");
        var seattle = await trips.CreateTripAsync(UserId, "Seattle summit");
        var created = await expenses.CreateExpenseAsync(UserId, munich.Id, "Hofbrauhaus", 42.50m, "2026-03-02");

        var moved = await expenses.UpdateExpenseAsync(UserId, created.Id, tripId: seattle.Id);

        Assert.Equal(seattle.Id, moved!.TripId);
        Assert.Empty(await expenses.ListExpensesAsync(UserId, munich.Id));
        Assert.Single(await expenses.ListExpensesAsync(UserId, seattle.Id));
    }

    [Fact]
    public async Task UpdateExpense_rejects_negative_totals()
    {
        var (trips, expenses) = CreateTools(out _);
        var trip = await trips.CreateTripAsync(UserId, "Munich kickoff");
        var created = await expenses.CreateExpenseAsync(UserId, trip.Id, "Hofbrauhaus", 42.50m, "2026-03-02");

        await Assert.ThrowsAsync<ArgumentException>(() => expenses.UpdateExpenseAsync(UserId, created.Id, totalAmount: -1m));
    }

    [Fact]
    public async Task DeleteTrip_cascades_to_its_expenses()
    {
        var (trips, expenses) = CreateTools(out _);
        var trip = await trips.CreateTripAsync(UserId, "Munich kickoff");
        await expenses.CreateExpenseAsync(UserId, trip.Id, "Hofbrauhaus", 42.50m, "2026-03-02");

        Assert.True(await trips.DeleteTripAsync(UserId, trip.Id));
        Assert.Empty(await expenses.ListExpensesAsync(UserId));
        Assert.Null(await trips.GetTripAsync(UserId, trip.Id));
    }

    [Fact]
    public async Task Users_cannot_see_each_others_records()
    {
        var (trips, expenses) = CreateTools(out _);
        var trip = await trips.CreateTripAsync(UserId, "Munich kickoff");
        await expenses.CreateExpenseAsync(UserId, trip.Id, "Hofbrauhaus", 42.50m, "2026-03-02");

        Assert.Empty(await trips.ListTripsAsync("someone-else"));
        Assert.Empty(await expenses.ListExpensesAsync("someone-else"));
        Assert.Null(await trips.GetTripAsync("someone-else", trip.Id));
    }

    [Fact]
    public async Task FindTripByName_matches_name_or_destination_case_insensitively()
    {
        var (trips, _) = CreateTools(out _);
        await trips.CreateTripAsync(UserId, "Q1 kickoff", destination: "Munich");
        await trips.CreateTripAsync(UserId, "Seattle summit", destination: "Seattle");

        var byDestination = await trips.FindTripByNameAsync(UserId, "munich");
        var byName = await trips.FindTripByNameAsync(UserId, "SUMMIT");

        Assert.Equal("Q1 kickoff", Assert.Single(byDestination).Name);
        Assert.Equal("Seattle summit", Assert.Single(byName).Name);
        Assert.Empty(await trips.FindTripByNameAsync(UserId, "paris"));
    }

    [Fact]
    public async Task UpdateTrip_validates_status()
    {
        var (trips, _) = CreateTools(out _);
        var trip = await trips.CreateTripAsync(UserId, "Munich kickoff");

        await Assert.ThrowsAsync<ArgumentException>(() => trips.UpdateTripAsync(UserId, trip.Id, status: "banana"));

        var updated = await trips.UpdateTripAsync(UserId, trip.Id, status: TripStatus.Submitted);
        Assert.Equal(TripStatus.Submitted, updated!.Status);
    }
}
