using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using Xunit;

namespace ExpensesMcpServer.Tests;

/// <summary>
/// Boots the real MCP server (against the in-memory repository) and drives it
/// over the MCP streamable HTTP transport, exactly like the Python agent does.
/// </summary>
public sealed class McpServerEndpointTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _httpClient = null!;
    private McpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(webHost =>
        {
            webHost.UseSetting("Cosmos:UseInMemory", "true");
            webHost.UseSetting("environment", "Development");
        });

        _httpClient = _factory.CreateClient();

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://localhost/mcp") },
            _httpClient);

        _client = await McpClient.CreateAsync(transport);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _httpClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Health_endpoint_is_available()
    {
        var response = await _httpClient.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Every_crud_tool_is_advertised()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var names = tools.Select(t => t.Name).ToHashSet();

        string[] expected =
        [
            "create_trip", "list_trips", "get_trip", "find_trip_by_name", "update_trip", "delete_trip",
            "create_expense", "list_expenses", "get_expense", "update_expense", "delete_expense",
            "append_conversation_messages", "get_conversation", "list_conversations", "delete_conversation",
        ];

        Assert.All(expected, name => Assert.Contains(name, names));
    }

    [Fact]
    public async Task Tools_expose_descriptions_so_the_model_can_pick_them()
    {
        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }

    [Fact]
    public async Task A_receipt_can_be_filed_end_to_end_over_mcp()
    {
        var userId = $"user-{Guid.NewGuid():N}";

        var trip = await CallAsync("create_trip", new()
        {
            ["userId"] = userId,
            ["name"] = "Munich kickoff",
            ["destination"] = "Munich",
        });

        var tripId = trip.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(tripId));

        var expense = await CallAsync("create_expense", new()
        {
            ["userId"] = userId,
            ["tripId"] = tripId!,
            ["merchant"] = "Hofbrauhaus",
            ["totalAmount"] = 42.50m,
            ["date"] = "2026-03-02",
            ["category"] = "food",
            ["currency"] = "EUR",
            ["lineItems"] = new object[]
            {
                new { description = "Schnitzel", category = "food", price = 24.50m, quantity = 1 },
            },
        });

        Assert.Equal("Hofbrauhaus", expense.GetProperty("merchant").GetString());
        Assert.Equal(42.50m, expense.GetProperty("totalAmount").GetDecimal());

        var trips = await CallAsync("list_trips", new() { ["userId"] = userId });
        var summary = trips.EnumerateArray().Single();

        Assert.Equal(1, summary.GetProperty("expenseCount").GetInt32());
        Assert.Equal(42.50m, summary.GetProperty("totalAmount").GetDecimal());
        Assert.Equal("Munich kickoff", summary.GetProperty("trip").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Delete_tools_return_a_plain_boolean()
    {
        var userId = $"user-{Guid.NewGuid():N}";

        var trip = await CallAsync("create_trip", new() { ["userId"] = userId, ["name"] = "Seattle summit" });
        var tripId = trip.GetProperty("id").GetString()!;

        var deleted = await CallAsync("delete_trip", new() { ["userId"] = userId, ["tripId"] = tripId });
        var missing = await CallAsync("delete_trip", new() { ["userId"] = userId, ["tripId"] = tripId });

        Assert.True(AsBoolean(deleted));
        Assert.False(AsBoolean(missing));
    }

    /// <summary>Scalar results arrive either bare or wrapped in <c>{"result": ...}</c>.</summary>
    private static bool AsBoolean(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => element.GetProperty("result").GetBoolean(),
            _ => throw new InvalidOperationException($"Unexpected result kind {element.ValueKind}"),
        };

    [Fact]
    public async Task Creating_an_expense_on_an_unknown_trip_is_reported_as_a_tool_error()
    {
        var result = await _client.CallToolAsync(
            "create_expense",
            new Dictionary<string, object?>
            {
                ["userId"] = "user-x",
                ["tripId"] = "missing",
                ["merchant"] = "Hofbrauhaus",
                ["totalAmount"] = 10m,
                ["date"] = "2026-03-02",
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError is true);
    }

    [Fact]
    public async Task Conversation_transcripts_round_trip()
    {
        var userId = $"user-{Guid.NewGuid():N}";

        await CallAsync("append_conversation_messages", new()
        {
            ["userId"] = userId,
            ["conversationId"] = "conv-1",
            ["messages"] = new object[]
            {
                new { role = "user", text = "Add my hotel receipt", toolCalls = Array.Empty<string>(), attachments = Array.Empty<string>() },
                new { role = "assistant", text = "Stored 210 EUR.", toolCalls = new[] { "create_expense" }, attachments = Array.Empty<string>() },
            },
        });

        var conversation = await CallAsync("get_conversation", new()
        {
            ["userId"] = userId,
            ["conversationId"] = "conv-1",
        });

        var messages = conversation.GetProperty("messages").EnumerateArray().ToList();

        Assert.Equal(2, messages.Count);
        Assert.Equal("Add my hotel receipt", messages[0].GetProperty("text").GetString());
        Assert.Equal("Add my hotel receipt", conversation.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Tools_that_can_return_null_do_not_declare_an_output_schema()
    {
        // An MCP client rejects a null structured result when the tool advertises an
        // output schema, which would break the very first turn of every new chat.
        var tools = await _client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        string[] nullable = ["get_trip", "update_trip", "get_expense", "update_expense", "get_conversation"];

        Assert.All(nullable, name =>
        {
            var tool = tools.Single(t => t.Name == name);
            Assert.Null(tool.ProtocolTool.OutputSchema);
        });
    }

    [Fact]
    public async Task Reading_a_missing_record_returns_a_successful_null_result()
    {
        var userId = $"user-{Guid.NewGuid():N}";

        foreach (var (tool, args) in new (string, Dictionary<string, object?>)[]
        {
            ("get_trip", new() { ["userId"] = userId, ["tripId"] = "missing" }),
            ("get_expense", new() { ["userId"] = userId, ["expenseId"] = "missing" }),
            ("get_conversation", new() { ["userId"] = userId, ["conversationId"] = "missing" }),
        })
        {
            var result = await _client.CallToolAsync(tool, args, cancellationToken: TestContext.Current.CancellationToken);

            Assert.False(result.IsError is true, $"'{tool}' should succeed for a missing record");
        }
    }

    private async Task<JsonElement> CallAsync(string name, Dictionary<string, object?> arguments)
    {
        var result = await _client.CallToolAsync(name, arguments, cancellationToken: TestContext.Current.CancellationToken);

        var text = string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));

        Assert.False(
            result.IsError is true,
            $"Tool '{name}' failed: text={text}; structured={result.StructuredContent?.GetRawText() ?? "<null>"}");

        // Tools that can return null carry no output schema, so their payload only
        // arrives as a text block. Parse it the same way the Python client does.
        if (result.StructuredContent is { } structured)
        {
            return structured;
        }

        Assert.False(string.IsNullOrWhiteSpace(text), $"Tool '{name}' returned no content at all");

        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
