using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ModelContextProtocol.Server;

/// <summary>
/// MCP tools for managing receipt / invoice images stored in Azure Blob Storage.
/// </summary>
internal class BlobStorageTools
{
    private const string DefaultContainer = "expenses";

    private static async Task<BlobContainerClient> GetContainerAsync(
        BlobServiceClient service,
        string? container,
        CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(container) ? DefaultContainer : container.Trim();
        var client = service.GetBlobContainerClient(name);
        await client.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        return client;
    }

    /// <summary>
    /// Builds a time-limited read-only URL for a blob. Uses an account-key SAS when
    /// available (e.g. the local Azurite emulator) and falls back to a
    /// user-delegation SAS when the client authenticates with a managed identity /
    /// token credential (the case in Azure).
    /// </summary>
    private static async Task<string> BuildReadUrlAsync(
        BlobServiceClient service,
        BlobClient blob,
        int minutesValid,
        CancellationToken cancellationToken)
    {
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, minutesValid));

        var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b"
        };

        // Account-key auth (Azurite / connection string with a key).
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sasBuilder).ToString();
        }

        // Managed identity / token credential: sign the SAS with a user delegation key.
        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
        var delegationKey = await service.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken);
        var sasToken = sasBuilder.ToSasQueryParameters(delegationKey.Value, service.AccountName).ToString();

        var uri = new UriBuilder(blob.Uri) { Query = sasToken };
        return uri.Uri.ToString();
    }

    [McpServerTool]
    [Description("Lists images stored in the expenses blob container. Returns a JSON array with name, size, contentType, and lastModified for each blob.")]
    public async Task<string> ListBlobs(
        BlobServiceClient blobService,
        [Description("Optional blob container name. Defaults to the expenses container.")] string? container = null,
        [Description("Optional name prefix used to filter the listing.")] string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var containerClient = await GetContainerAsync(blobService, container, cancellationToken);

        var items = new List<object>();
        await foreach (var blob in containerClient.GetBlobsAsync(traits: BlobTraits.None, states: BlobStates.None, prefix: prefix, cancellationToken: cancellationToken))
        {
            items.Add(new
            {
                name = blob.Name,
                size = blob.Properties.ContentLength,
                contentType = blob.Properties.ContentType,
                lastModified = blob.Properties.LastModified
            });
        }

        return JsonSerializer.Serialize(items);
    }

    [McpServerTool]
    [Description("Returns a time-limited read-only URL (SAS) that a downstream service (for example Content Understanding) can use to download the blob. Works with both account-key and managed-identity authentication.")]
    public async Task<string> GetBlobReadUrl(
        BlobServiceClient blobService,
        [Description("Name of the blob to retrieve a read URL for.")] string blobName,
        [Description("How many minutes the URL should remain valid. Defaults to 30.")] int minutesValid = 30,
        [Description("Optional blob container name. Defaults to the expenses container.")] string? container = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("blobName must be provided.", nameof(blobName));
        }

        var containerClient = await GetContainerAsync(blobService, container, cancellationToken);
        var blob = containerClient.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException($"Blob '{blobName}' not found in container '{containerClient.Name}'.");
        }

        return await BuildReadUrlAsync(blobService, blob, minutesValid, cancellationToken);
    }

    [McpServerTool]
    [Description("Uploads a receipt/invoice image to the expenses blob container so it can be analyzed and referenced from SQL. The content must be Base64-encoded. Returns JSON with the stored blob name (use it as ReceiptBlobKey), size, contentType, sha256, and a short-lived read URL.")]
    public async Task<string> SaveBlob(
        BlobServiceClient blobService,
        [Description("Base64-encoded bytes of the image to store.")] string contentBase64,
        [Description("MIME type of the image, e.g. image/jpeg or image/png.")] string contentType,
        [Description("Optional blob name/key. If omitted, a unique name is generated. Use forward slashes to create a virtual folder path, e.g. receipts/user1/abc.jpg.")] string? blobName = null,
        [Description("How many minutes the returned read URL should remain valid. Defaults to 30.")] int readUrlMinutesValid = 30,
        [Description("Optional blob container name. Defaults to the expenses container.")] string? container = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentBase64))
        {
            throw new ArgumentException("contentBase64 must be provided.", nameof(contentBase64));
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(contentBase64);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("contentBase64 is not valid Base64.", nameof(contentBase64), ex);
        }

        if (bytes.Length == 0)
        {
            throw new ArgumentException("contentBase64 decodes to an empty payload.", nameof(contentBase64));
        }

        var extension = contentType?.Trim().ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            "application/pdf" => ".pdf",
            _ => string.Empty
        };

        var name = string.IsNullOrWhiteSpace(blobName)
            ? $"receipts/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{extension}"
            : blobName.Trim().TrimStart('/');

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var containerClient = await GetContainerAsync(blobService, container, cancellationToken);
        var blob = containerClient.GetBlobClient(name);

        using var stream = new MemoryStream(bytes, writable: false);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string> { ["sha256"] = sha256 }
            },
            cancellationToken);

        var readUrl = await BuildReadUrlAsync(blobService, blob, readUrlMinutesValid, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            name,
            size = bytes.Length,
            contentType,
            sha256,
            readUrl
        });
    }

    [McpServerTool]
    [Description("Deletes a blob from the expenses container. Returns 'deleted' on success or 'not_found' if the blob did not exist.")]
    public async Task<string> DeleteBlob(
        BlobServiceClient blobService,
        [Description("Name of the blob to delete.")] string blobName,
        [Description("Optional blob container name. Defaults to the expenses container.")] string? container = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("blobName must be provided.", nameof(blobName));
        }

        var containerClient = await GetContainerAsync(blobService, container, cancellationToken);
        var response = await containerClient.DeleteBlobIfExistsAsync(blobName, cancellationToken: cancellationToken);
        return response.Value ? "deleted" : "not_found";
    }

    [McpServerTool]
    [Description("Returns metadata (size, content type, last modified, custom metadata) for a single blob.")]
    public async Task<string> GetBlobMetadata(
        BlobServiceClient blobService,
        [Description("Name of the blob to inspect.")] string blobName,
        [Description("Optional blob container name. Defaults to the expenses container.")] string? container = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("blobName must be provided.", nameof(blobName));
        }

        var containerClient = await GetContainerAsync(blobService, container, cancellationToken);
        var blob = containerClient.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException($"Blob '{blobName}' not found in container '{containerClient.Name}'.");
        }

        var props = (await blob.GetPropertiesAsync(cancellationToken: cancellationToken)).Value;

        return JsonSerializer.Serialize(new
        {
            name = blob.Name,
            size = props.ContentLength,
            contentType = props.ContentType,
            lastModified = props.LastModified,
            metadata = props.Metadata
        });
    }
}
