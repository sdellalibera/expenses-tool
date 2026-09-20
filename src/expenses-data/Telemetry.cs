using System.Diagnostics;

namespace Expenses.Data;

/// <summary>
/// Single <see cref="ActivitySource"/> for record operations in both services.
/// The AppHost wires the OTLP exporter, so these spans show up in the
/// Aspire dashboard traces view next to the agent and frontend spans.
/// </summary>
public static class Telemetry
{
    public const string ActivitySourceName = "Expenses.Data";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>Starts a span that describes a record operation.</summary>
    public static Activity? StartRecordActivity(string operation, string entity, string userId, string? entityId = null)
    {
        var activity = ActivitySource.StartActivity($"records.{entity}.{operation}", ActivityKind.Internal);
        activity?.SetTag("expenses.operation", operation);
        activity?.SetTag("expenses.entity", entity);
        activity?.SetTag("expenses.user_id", userId);
        if (entityId is not null)
        {
            activity?.SetTag("expenses.entity_id", entityId);
        }

        return activity;
    }
}
