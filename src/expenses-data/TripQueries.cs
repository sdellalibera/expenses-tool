using Expenses.Data.Models;

namespace Expenses.Data;

public static class TripQueries
{
    public static async Task<IReadOnlyList<TripSummary>> ListSummariesAsync(
        IExpensesReader repository, string userId, string? status = null, CancellationToken cancellationToken = default)
    {
        var trips = await repository.ListTripsAsync(userId, status, cancellationToken);
        var expenses = await repository.ListExpensesAsync(userId, cancellationToken: cancellationToken);
        var byTrip = expenses.ToLookup(expense => expense.TripId);
        return [.. trips.Select(trip => Summarize(trip, byTrip[trip.Id]))];
    }

    public static async Task<TripSummary?> GetSummaryAsync(
        IExpensesReader repository, string userId, string tripId, CancellationToken cancellationToken = default)
    {
        var trip = await repository.GetTripAsync(userId, tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var expenses = await repository.ListExpensesAsync(userId, tripId, cancellationToken);
        return Summarize(trip, expenses);
    }

    private static TripSummary Summarize(Trip trip, IEnumerable<Expense> expenses)
    {
        var items = expenses.ToList();
        return new TripSummary { Trip = trip, ExpenseCount = items.Count, TotalAmount = items.Sum(item => item.TotalAmount) };
    }
}
