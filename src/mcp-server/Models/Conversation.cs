using System.ComponentModel;

namespace ExpensesMcpServer.Models;

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

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A single turn in a conversation.</summary>
public sealed record ConversationMessage
{
    [Description("Who produced the message: user, assistant, system or tool.")]
    public string Role { get; set; } = "user";

    [Description("Text content of the message.")]
    public string Text { get; set; } = string.Empty;

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
