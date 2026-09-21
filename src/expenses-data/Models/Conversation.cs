using System.ComponentModel;

namespace Expenses.Data.Models;

/// <summary>
/// The persisted transcript of one chat between a user and the expenses agent.
/// Stored in the <c>conversations</c> Cosmos container, partitioned by <c>/userId</c>.
/// </summary>
public sealed record Conversation
{
    [Description("Unique conversation identifier.")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Description("Identifier of the user the conversation belongs to (Cosmos partition key).")]
    public string UserId { get; set; } = string.Empty;

    [Description("Short title describing the conversation.")]
    public string Title { get; set; } = "New conversation";

    [Description("Trip the conversation is currently working on, when known.")]
    public string? TripId { get; set; }

    [Description("Ordered transcript of the conversation.")]
    public IReadOnlyList<ConversationMessage> Messages { get; set; } = [];

    public IReadOnlyDictionary<string, ReceiptCheckpoint> ReceiptCheckpoints { get; set; } = new Dictionary<string, ReceiptCheckpoint>();

    public void SetReceiptCheckpoint(string key, ReceiptCheckpoint checkpoint)
    {
        var checkpoints = ReceiptCheckpoints.ToDictionary(entry => entry.Key, entry => entry.Value);
        if (checkpoints.TryGetValue(key, out var previous) && previous.AnalysisJson is not null && checkpoint.AnalysisJson is null)
        {
            return;
        }
        checkpoints[key] = checkpoint with { UpdatedAt = DateTimeOffset.UtcNow };
        ReceiptCheckpoints = checkpoints.OrderByDescending(entry => entry.Value.UpdatedAt).Take(16)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ReceiptCheckpoint
{
    public required string DocumentKey { get; init; }
    public required string BlobName { get; init; }
    public required string FileName { get; init; }
    public required string PhotoUrl { get; init; }
    public required string ConversationId { get; init; }
    public string? AnalysisJson { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A single turn in a conversation.</summary>
public sealed record ConversationMessage
{
    [Description("Who produced the message: user, assistant, system or tool.")]
    public string Role { get; set; } = "user";

    [Description("Text content of the message.")]
    public string Text { get; set; } = string.Empty;

    [Description("Receipt references and extracted fields replayed to the agent, separate from the displayed message.")]
    public string? ReceiptContext { get; set; }

    [Description("Names of the tools invoked while producing this message.")]
    public IReadOnlyList<string> ToolCalls { get; set; } = [];

    [Description("Names of image attachments sent with this message.")]
    public IReadOnlyList<string> Attachments { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Lightweight conversation projection used by list operations.</summary>
public sealed record ConversationSummary
{
    public required string Id { get; init; }

    public required string UserId { get; init; }

    public required string Title { get; init; }

    public string? TripId { get; init; }

    public int MessageCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
