using Azure;
using Expenses.Data;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddExpensesData(readOnly: true);
builder.Services.AddProblemDetails();
var allowedOrigins = builder.Configuration["ALLOWED_ORIGINS"]?
    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? ["*"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Contains("*"))
    {
        policy.AllowAnyOrigin();
    }
    else
    {
        policy.WithOrigins(allowedOrigins);
    }
    policy.WithMethods("GET").AllowAnyHeader();
}));
var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
app.MapDefaultEndpoints();

var records = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
{
    var userId = context.HttpContext.Request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
    {
        return Results.BadRequest(new { detail = "userId is required." });
    }

    try
    {
        return await next(context);
    }
    catch (CosmosException ex)
    {
        app.Logger.LogError(ex, "Cosmos read failed");
        return Results.Problem(statusCode: 503, detail: "The records database is unavailable. Try again shortly.");
    }
    catch (RequestFailedException ex)
    {
        app.Logger.LogError(ex, "Receipt storage read failed");
        return Results.Problem(statusCode: 503, detail: "Receipt storage is unavailable. Try again shortly.");
    }
    catch (ArgumentException ex)
    {
        app.Logger.LogWarning(ex, "Invalid record request");
        return Results.BadRequest(new { detail = ex.Message });
    }
});

records.MapGet("/trips", async (string userId, string? status, IExpensesReader repository, CancellationToken ct) =>
    Results.Ok(await TripQueries.ListSummariesAsync(repository, userId, status, ct)));

records.MapGet("/trips/{tripId}", async (string userId, string tripId, IExpensesReader repository, CancellationToken ct) =>
    await TripQueries.GetSummaryAsync(repository, userId, tripId, ct) is { } trip
        ? Results.Ok(trip) : Results.NotFound(new { detail = $"Trip '{tripId}' was not found." }));

records.MapGet("/expenses", async (string userId, string? tripId, IExpensesReader repository, CancellationToken ct) =>
    Results.Ok(await repository.ListExpensesAsync(userId, tripId, ct)));

records.MapGet("/expenses/{expenseId}", async (string userId, string expenseId, IExpensesReader repository, CancellationToken ct) =>
    await repository.GetExpenseAsync(userId, expenseId, ct) is { } expense
        ? Results.Ok(expense) : Results.NotFound(new { detail = $"Expense '{expenseId}' was not found." }));

records.MapGet("/conversations", async (string userId, IExpensesReader repository, CancellationToken ct) =>
    Results.Ok(await repository.ListConversationsAsync(userId, ct)));

records.MapGet("/conversations/{conversationId}", async (string userId, string conversationId, IExpensesReader repository, CancellationToken ct) =>
    await repository.GetConversationAsync(userId, conversationId, ct) is { } conversation
        ? Results.Ok(conversation) : Results.NotFound(new { detail = $"Conversation '{conversationId}' was not found." }));

records.MapGet("/expenses/{expenseId}/photo", async (
    string userId, string expenseId, IExpensesReader repository, ReceiptStorage receipts, HttpContext context, CancellationToken ct) =>
{
    var expense = await repository.GetExpenseAsync(userId, expenseId, ct);
    if (expense?.PhotoUrl is null)
    {
        return Results.NotFound(new { detail = "No receipt photo is stored for this expense." });
    }
    var download = await receipts.DownloadAsync(userId, expense.PhotoUrl, ct);
    if (download is null)
    {
        return Results.NotFound(new { detail = "The receipt photo was not found." });
    }
    context.Response.Headers.CacheControl = "private, no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return Results.Stream(download.Content, download.Details.ContentType);
});

app.Run();
