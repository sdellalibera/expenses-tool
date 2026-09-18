using System.ComponentModel;
using ExpensesMcpServer.Data;
using ExpensesMcpServer.Models;
using ModelContextProtocol.Server;

namespace ExpensesMcpServer.Tools;

/// <summary>
/// MCP tools that persist the agent conversation history in Cosmos DB so a chat
/// survives page reloads and can be replayed.
/// </summary>
[McpServerToolType]
public sealed class ConversationTools(IExpensesRepository repository, ILogger<ConversationTools> logger)
{
    [McpServerTool(Name = "append_conversation_messages", UseStructuredContent = true)]
    [Description("Append one or more messages to a conversation transcript, creating the conversation when it does not exist yet.")]
    public async Task<Conversation> AppendAsync(
        [Description("Identifier of the user the conversation belongs to.")] string userId,
        [Description("Identifier of the conversation.")] string conversationId,
        [Description("Messages to append, in order.")] IReadOnlyList<ConversationMessage> messages,
        [Description("Optional conversation title. Derived from the first user message when omitted.")] string? title = null,
        [Description("Trip the conversation is currently working on.")] string? tripId = null,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(conversationId, nameof(conversationId));

        logger.LogInformation(
            "MCP tool append_conversation_messages invoked by user {UserId} for conversation {ConversationId} ({Count} message(s))",
            userId, conversationId, messages.Count);

        return await repository.AppendConversationMessagesAsync(userId, conversationId, messages, title, tripId, cancellationToken);
    }

    // No output schema: returns null for a conversation that does not exist yet,
    // which is the normal case on the first turn of a new chat.
    [McpServerTool(Name = "get_conversation")]
    [Description("Read the full transcript of a conversation. Returns null when the conversation does not exist.")]
    public async Task<Conversation?> GetAsync(
        [Description("Identifier of the user the conversation belongs to.")] string userId,
        [Description("Identifier of the conversation to read.")] string conversationId,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(conversationId, nameof(conversationId));

        logger.LogInformation("MCP tool get_conversation invoked by user {UserId} for conversation {ConversationId}", userId, conversationId);

        return await repository.GetConversationAsync(userId, conversationId, cancellationToken);
    }

    [McpServerTool(Name = "list_conversations", UseStructuredContent = true)]
    [Description("List the conversations of a user, most recently updated first, without their message bodies.")]
    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(
        [Description("Identifier of the user whose conversations should be listed.")] string userId,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);

        logger.LogInformation("MCP tool list_conversations invoked by user {UserId}", userId);

        return await repository.ListConversationsAsync(userId, cancellationToken);
    }

    [McpServerTool(Name = "delete_conversation", UseStructuredContent = true)]
    [Description("Delete a conversation transcript. Returns true when the conversation existed and was deleted.")]
    public async Task<bool> DeleteAsync(
        [Description("Identifier of the user the conversation belongs to.")] string userId,
        [Description("Identifier of the conversation to delete.")] string conversationId,
        CancellationToken cancellationToken = default)
    {
        ToolGuard.RequireUserId(userId);
        ToolGuard.RequireValue(conversationId, nameof(conversationId));

        logger.LogInformation("MCP tool delete_conversation invoked by user {UserId} for conversation {ConversationId}", userId, conversationId);

        return await repository.DeleteConversationAsync(userId, conversationId, cancellationToken);
    }
}
