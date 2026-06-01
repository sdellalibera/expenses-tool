namespace agents.models;

/// <summary>
/// Represents an incoming chat request submitted to the expenses agent.
/// </summary>
/// <param name="Message">The user's message to send to the agent.</param>
public record AgentChatRequest(string Message);
