using ExpensesMcpServer.Models;

namespace ExpensesMcpServer.Data;

/// <summary>Helpers shared by the Cosmos and in-memory repositories.</summary>
internal static class ConversationTitle
{
    public static string FromMessages(IReadOnlyList<ConversationMessage> messages)
    {
        var first = messages.FirstOrDefault(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Text));
        if (first is null)
        {
            return "New conversation";
        }

        var text = first.Text.Trim();
        return text.Length <= 60 ? text : text[..57] + "...";
    }

    public static ConversationSummary ToSummary(Conversation conversation) => new()
    {
        Id = conversation.Id,
        UserId = conversation.UserId,
        Title = conversation.Title,
        TripId = conversation.TripId,
        MessageCount = conversation.Messages.Count,
        CreatedAt = conversation.CreatedAt,
        UpdatedAt = conversation.UpdatedAt,
    };
}
