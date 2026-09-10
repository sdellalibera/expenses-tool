using System.ComponentModel;
using ExpensesMcpServer.Data;
using ExpensesMcpServer.Models;
using ModelContextProtocol.Server;

namespace ExpensesMcpServer.Tools;

/// <summary>
/// MCP tools that let the agent manage the work trips expenses are grouped under.
/// </summary>
[McpServerToolType]
public sealed class TripTools(IExpensesRepository repository, ILogger<TripTools> logger)
{
    [McpServerTool(Name = "create_trip", UseStructuredContent = true)]
    [Description("Create a new work trip that expenses can be grouped under. Returns the created trip including its generated id.")]
    public async Task<Trip> CreateTripAsync(
        [Description("Identifier of the user the trip belongs to.")] string userId,
        [Description("Human friendly trip name, for example 'Munich kickoff'.")] string name,
        [Description("City or country the trip takes place in.")] string? destination = null,
        [Description("Trip start date, ISO 8601 (yyyy-MM-dd).")] string? startDate = null,
        [Description("Trip end date, ISO 8601 (yyyy-MM-dd).")] string? endDate = null,
        [Description("Free form purpose of the trip.")] string? purpose = null,
        [Description("ISO 4217 currency code the trip is reported in, for example EUR.")] string? currency = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(name, nameof(name));

        logger.LogInformation("MCP tool create_trip invoked by user {UserId} for '{Name}'", userId, name);

        return await repository.CreateTripAsync(
            new Trip
            {
                UserId = userId,
                Name = name,
                Destination = destination,
                StartDate = startDate,
                EndDate = endDate,
                Purpose = purpose,
                Currency = currency,
            },
            cancellationToken);
    }

    [McpServerTool(Name = "list_trips", UseStructuredContent = true)]
    [Description("List every work trip for a user, newest first, together with the number of expenses and the total amount booked on each trip.")]
    public async Task<IReadOnlyList<TripSummary>> ListTripsAsync(
        [Description("Identifier of the user whose trips should be listed.")] string userId,
        [Description("Optional status filter: open, submitted or closed.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);

        logger.LogInformation("MCP tool list_trips invoked by user {UserId}", userId);

        var trips = await repository.ListTripsAsync(userId, status, cancellationToken);
        var expenses = await repository.ListExpensesAsync(userId, cancellationToken: cancellationToken);

        return
        [
            .. trips.Select(trip =>
            {
                var tripExpenses = expenses.Where(e => e.TripId == trip.Id).ToList();
                return new TripSummary
                {
                    Trip = trip,
                    ExpenseCount = tripExpenses.Count,
                    TotalAmount = tripExpenses.Sum(e => e.TotalAmount),
                };
            })
        ];
    }

    // No output schema: this tool returns null when the trip does not exist, and an
    // MCP client that sees an output schema rejects a null structured result.
    [McpServerTool(Name = "get_trip")]
    [Description("Read a single work trip together with its expense roll-up. Returns null when the trip does not exist.")]
    public async Task<TripSummary?> GetTripAsync(
        [Description("Identifier of the user the trip belongs to.")] string userId,
        [Description("Identifier of the trip to read.")] string tripId,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(tripId, nameof(tripId));

        logger.LogInformation("MCP tool get_trip invoked by user {UserId} for trip {TripId}", userId, tripId);

        var trip = await repository.GetTripAsync(userId, tripId, cancellationToken);
        if (trip is null)
        {
            return null;
        }

        var expenses = await repository.ListExpensesAsync(userId, tripId, cancellationToken);

        return new TripSummary
        {
            Trip = trip,
            ExpenseCount = expenses.Count,
            TotalAmount = expenses.Sum(e => e.TotalAmount),
        };
    }

    [McpServerTool(Name = "find_trip_by_name", UseStructuredContent = true)]
    [Description("Find the trips of a user whose name or destination contains the given text. Use this to resolve a trip the user refers to by name, for example 'the Munich trip'.")]
    public async Task<IReadOnlyList<Trip>> FindTripByNameAsync(
        [Description("Identifier of the user whose trips should be searched.")] string userId,
        [Description("Text to look for in the trip name or destination (case insensitive).")] string search,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(search, nameof(search));

        logger.LogInformation("MCP tool find_trip_by_name invoked by user {UserId} with '{Search}'", userId, search);

        var trips = await repository.ListTripsAsync(userId, cancellationToken: cancellationToken);

        return
        [
            .. trips.Where(t =>
                t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (t.Destination?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
        ];
    }

    // No output schema: returns null when the trip does not exist (see get_trip).
    [McpServerTool(Name = "update_trip")]
    [Description("Update one or more fields of an existing work trip. Omitted fields are left unchanged. Returns null when the trip does not exist.")]
    public async Task<Trip?> UpdateTripAsync(
        [Description("Identifier of the user the trip belongs to.")] string userId,
        [Description("Identifier of the trip to update.")] string tripId,
        [Description("New trip name.")] string? name = null,
        [Description("New destination.")] string? destination = null,
        [Description("New start date, ISO 8601 (yyyy-MM-dd).")] string? startDate = null,
        [Description("New end date, ISO 8601 (yyyy-MM-dd).")] string? endDate = null,
        [Description("New purpose.")] string? purpose = null,
        [Description("New ISO 4217 currency code.")] string? currency = null,
        [Description("New status: open, submitted or closed.")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(tripId, nameof(tripId));
        ToolGuard.RequireOneOf(status, TripStatus.All, nameof(status));

        logger.LogInformation("MCP tool update_trip invoked by user {UserId} for trip {TripId}", userId, tripId);

        return await repository.UpdateTripAsync(
            userId,
            tripId,
            new TripPatch
            {
                Name = name,
                Destination = destination,
                StartDate = startDate,
                EndDate = endDate,
                Purpose = purpose,
                Currency = currency,
                Status = status,
            },
            cancellationToken);
    }

    [McpServerTool(Name = "delete_trip", UseStructuredContent = true)]
    [Description("Delete a work trip and every expense booked on it. Returns true when the trip existed and was deleted.")]
    public async Task<bool> DeleteTripAsync(
        [Description("Identifier of the user the trip belongs to.")] string userId,
        [Description("Identifier of the trip to delete.")] string tripId,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(tripId, nameof(tripId));

        logger.LogInformation("MCP tool delete_trip invoked by user {UserId} for trip {TripId}", userId, tripId);

        return await repository.DeleteTripAsync(userId, tripId, cancellationToken);
    }
}
