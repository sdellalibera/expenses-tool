using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Expenses.Data;

public sealed record ReceiptImage(
    string BlobName, string PhotoUrl, string FileName, string ContentType, long SizeBytes, string ConversationId);

public sealed class ReceiptStorage(
    BlobContainerClient container, ILogger<ReceiptStorage> logger)
{
    public const int MaxUploadBytes = 12 * 1024 * 1024;
    private BlobContainerClient Container => container;

    public async Task<ReceiptImage> UploadAsync(
        string userId, string conversationId, string fileName, string contentType, string base64Data,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Data);
        if (base64Data.Length > ((MaxUploadBytes + 2) / 3) * 4)
        {
            throw new ArgumentException("Receipt images must be no larger than 12 MB.", nameof(base64Data));
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(base64Data);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Receipt image data must be valid base64.", nameof(base64Data), ex);
        }

        contentType = contentType.ToLowerInvariant();
        if (data.Length is 0 or > MaxUploadBytes || !IsImage(data, contentType))
        {
            throw new ArgumentException("Provide a nonempty JPEG, PNG, GIF, WebP, BMP or TIFF image matching its content type, up to 12 MB.");
        }

        var name = fileName.Replace('\\', '/').Split('/')[^1];
        if (string.IsNullOrWhiteSpace(name) || Encoding.UTF8.GetByteCount(name) > 512 ||
            Encoding.UTF8.GetByteCount(conversationId) > 512)
        {
            throw new ArgumentException("The receipt filename and conversation identifier must be nonempty and at most 512 UTF-8 bytes.");
        }

        var blobName = $"{UserPrefix(userId)}{Hash(conversationId)}/{Guid.NewGuid():N}";
        var blob = Container.GetBlobClient(blobName);
        using var stream = new MemoryStream(data, writable: false);
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata = new Dictionary<string, string>
            {
                ["filename"] = Encode(name),
                ["conversationid"] = Encode(conversationId),
            },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        }, cancellationToken);
        logger.LogInformation("Stored receipt {BlobName} for user {UserId} ({SizeBytes} bytes)", blobName, userId, data.Length);
        return new ReceiptImage(blobName, blob.Uri.AbsoluteUri, name, contentType, data.Length, conversationId);
    }

    public async Task<ReceiptImage?> GetAsync(string userId, string blobName, CancellationToken cancellationToken)
    {
        var blob = UserBlob(userId, blobName);
        try
        {
            var properties = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;
            return Describe(blobName, properties.ContentType, properties.ContentLength, properties.Metadata);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task<ReceiptImage?> GetByUrlAsync(string userId, string photoUrl, CancellationToken cancellationToken)
        => GetAsync(userId, BlobNameFromUrl(userId, photoUrl), cancellationToken);

    public async Task<IReadOnlyList<ReceiptImage>> ListAsync(
        string userId, string? conversationId, CancellationToken cancellationToken)
    {
        var prefix = UserPrefix(userId);
        if (conversationId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
            prefix += $"{Hash(conversationId)}/";
        }

        var result = new List<ReceiptImage>();
        await foreach (var blob in Container.GetBlobsAsync(
            BlobTraits.Metadata, BlobStates.None, prefix: prefix, cancellationToken: cancellationToken))
        {
            result.Add(Describe(blob.Name, blob.Properties.ContentType ?? "application/octet-stream",
                blob.Properties.ContentLength ?? 0, blob.Metadata));
        }
        return result;
    }

    public async Task<bool> DeleteAsync(string userId, string blobName, CancellationToken cancellationToken)
    {
        var response = await UserBlob(userId, blobName).DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
        logger.LogInformation("Receipt deletion for {BlobName}, user {UserId}: {Deleted}", blobName, userId, response.Value);
        return response.Value;
    }

    public async Task<BlobDownloadStreamingResult?> DownloadAsync(
        string userId, string photoUrl, CancellationToken cancellationToken)
    {
        var blob = UserBlob(userId, BlobNameFromUrl(userId, photoUrl));
        try
        {
            return (await blob.DownloadStreamingAsync(cancellationToken: cancellationToken)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private BlobClient UserBlob(string userId, string blobName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        if (!blobName.StartsWith(UserPrefix(userId), StringComparison.Ordinal) ||
            blobName.Split('/').Length != 3 || blobName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("The receipt does not belong to this user.", nameof(blobName));
        }
        return Container.GetBlobClient(blobName);
    }

    private string BlobNameFromUrl(string userId, string photoUrl)
    {
        var prefix = Container.Uri.AbsoluteUri.TrimEnd('/') + "/";
        if (!Uri.TryCreate(photoUrl, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("photoUrl must be a permanent URL in the configured private receipt container.", nameof(photoUrl));
        }
        var blobName = Uri.UnescapeDataString(uri.AbsoluteUri[prefix.Length..]);
        _ = UserBlob(userId, blobName);
        return blobName;
    }

    private ReceiptImage Describe(string blobName, string contentType, long length, IDictionary<string, string> metadata)
        => new(blobName, Container.GetBlobClient(blobName).Uri.AbsoluteUri,
            Decode(metadata["filename"]), contentType, length, Decode(metadata["conversationid"]));

    private static string UserPrefix(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return $"{Hash(userId)}/";
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static bool IsImage(ReadOnlySpan<byte> data, string contentType) => contentType switch
    {
        "image/jpeg" => data.StartsWith<byte>([0xFF, 0xD8, 0xFF]),
        "image/png" => data.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/gif" => data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8),
        "image/webp" => data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..].StartsWith("WEBP"u8),
        "image/bmp" => data.StartsWith("BM"u8),
        "image/tiff" => data.StartsWith<byte>([0x49, 0x49, 0x2A, 0x00]) || data.StartsWith<byte>([0x4D, 0x4D, 0x00, 0x2A]),
        _ => false,
    };
}
