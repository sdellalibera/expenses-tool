using System.ComponentModel;
using Expenses.Data;
using ModelContextProtocol.Server;

namespace ExpensesMcpServer.Tools;

[McpServerToolType]
public sealed class ReceiptTools(ReceiptStorage receipts, IExpensesReader repository)
{
    [McpServerTool(Name = "upload_receipt_image", UseStructuredContent = true)]
    [Description("Store an uploaded receipt image in private Blob Storage. Used by the upload pipeline before analysis. Returns its durable photoUrl; pass that exact URL to create_expense. Never invent or generate image bytes.")]
    public Task<ReceiptImage> UploadAsync(
        string userId, string conversationId, string fileName, string contentType, string base64Data,
        CancellationToken cancellationToken = default)
        => receipts.UploadAsync(userId, conversationId, fileName, contentType, base64Data, cancellationToken);

    [McpServerTool(Name = "get_receipt_image")]
    [Description("Get a user's receipt image metadata and durable photoUrl by blobName. Returns null if missing.")]
    public Task<ReceiptImage?> GetAsync(string userId, string blobName, CancellationToken cancellationToken = default)
        => receipts.GetAsync(userId, blobName, cancellationToken);

    [McpServerTool(Name = "list_receipt_images", UseStructuredContent = true)]
    [Description("List a user's stored receipt images, optionally from one conversation. Use to recover photoUrl values, including uploads awaiting trip clarification.")]
    public Task<IReadOnlyList<ReceiptImage>> ListAsync(
        string userId, string? conversationId = null, CancellationToken cancellationToken = default)
        => receipts.ListAsync(userId, conversationId, cancellationToken);

    [McpServerTool(Name = "delete_receipt_image", UseStructuredContent = true)]
    [Description("Delete an unlinked receipt image when the user explicitly requests it. Refuses to delete images still referenced by an expense. Deleting expenses or trips retains their original photos for auditing.")]
    public async Task<bool> DeleteAsync(string userId, string blobName, CancellationToken cancellationToken = default)
    {
        var image = await receipts.GetAsync(userId, blobName, cancellationToken);
        if (image is null)
        {
            return false;
        }
        var expenses = await repository.ListExpensesAsync(userId, cancellationToken: cancellationToken);
        if (expenses.Any(expense => expense.PhotoUrl == image.PhotoUrl))
        {
            throw new ArgumentException("This receipt is still linked to an expense. Remove or replace that link before deleting the photo.", nameof(blobName));
        }
        return await receipts.DeleteAsync(userId, blobName, cancellationToken);
    }
}
