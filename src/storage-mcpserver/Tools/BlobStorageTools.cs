using System.ComponentModel;
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
    [Description("Returns a time-limited read-only URL (SAS or emulator URL) that the Content Understanding service can use to download the blob.")]
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

        if (blob.CanGenerateSasUri)
        {
            var sasBuilder = new BlobSasBuilder(
                BlobSasPermissions.Read,
                DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, minutesValid)))
            {
                BlobContainerName = containerClient.Name,
                BlobName = blob.Name
            };

            return blob.GenerateSasUri(sasBuilder).ToString();
        }

        // Fallback when SAS generation isn't available (e.g. managed identity auth).
        return blob.Uri.ToString();
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
