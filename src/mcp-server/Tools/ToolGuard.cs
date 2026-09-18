namespace ExpensesMcpServer.Tools;

/// <summary>
/// Argument validation shared by the MCP tools. Throwing here surfaces a clean
/// tool error back to the agent instead of a Cosmos exception.
/// </summary>
internal static class ToolGuard
{
    public static void RequireUserId(string userId)
        => RequireValue(userId, "userId");

    public static void RequireValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"'{name}' is required and cannot be empty.", name);
        }
    }

    public static void RequireOneOf(string? value, IReadOnlyCollection<string> allowed, string name)
    {
        if (value is null)
        {
            return;
        }

        if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"'{name}' must be one of: {string.Join(", ", allowed)}.", name);
        }
    }

    public static void RequireNonNegative(decimal value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentException($"'{name}' cannot be negative.", name);
        }
    }
}
