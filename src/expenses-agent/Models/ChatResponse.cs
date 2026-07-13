namespace agents.models;

/// <summary>
/// Represents the response returned by the expenses agent.
/// </summary>
/// <param name="Reply">The agent's reply text.</param>
/// <param name="ConversationId">The conversation identifier associated with this reply.</param>
public record AgentChatResponse(string Reply, string ConversationId);
