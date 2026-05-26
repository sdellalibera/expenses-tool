using System.ComponentModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using ModelContextProtocol.Server;

/// <summary>
/// MCP tools backed by the Azure AI Content Understanding Person Directory
/// REST API. See:
/// https://learn.microsoft.com/azure/ai-services/content-understanding/tutorial/build-person-directory
///
/// The Person Directory APIs are currently only exposed via REST (api-version
/// 2025-05-01-preview), so this class issues HTTP requests against the Foundry
/// endpoint that is configured for the ContentUnderstandingClient.
/// </summary>
internal sealed class PersonDirectoryTools
{
    private const string ApiVersion = "2025-05-01-preview";
    private static readonly string[] CognitiveServicesScopes =
        new[] { "https://cognitiveservices.azure.com/.default" };

    private readonly HttpClient _httpClient;
    private readonly BlobContainerClient _facesContainer;
    private readonly TokenCredential _credential;
    private readonly Uri _endpoint;

    public PersonDirectoryTools(
        HttpClient httpClient,
        BlobContainerClient facesContainer,
        IConfiguration configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _facesContainer = facesContainer ?? throw new ArgumentNullException(nameof(facesContainer));

        var endpoint = configuration["FOUNDRY_URI"]
            ?? throw new InvalidOperationException("Content Understanding endpoint (FOUNDRY_URI) not set.");
        _endpoint = new Uri(endpoint);

        // Mirrors the default credential used by AddAzureClients(). Used to
        // obtain bearer tokens for the Content Understanding REST API.
        _credential = new DefaultAzureCredential();
    }

    [McpServerTool]
    [Description("Creates an empty Person Directory in Azure AI Content Understanding. The directory acts as a container for persons and their associated face entries.")]
    public async Task<string> CreatePersonDirectory(
        [Description("Unique, user-defined identifier for the directory (alphanumeric, dashes, underscores).")] string personDirectoryId,
        [Description("Optional short description of the directory's purpose.")] string? description = null,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(personDirectoryId, nameof(personDirectoryId));

        var payload = new JsonObject();
        if (!string.IsNullOrWhiteSpace(description))
        {
            payload["description"] = description;
        }

        var request = new HttpRequestMessage(
            HttpMethod.Put,
            BuildUri($"/contentunderstanding/personDirectories/{Uri.EscapeDataString(personDirectoryId)}"))
        {
            Content = JsonContent.Create(payload),
        };

        return await SendAndReadAsync(request, cancellationToken);
    }

    [McpServerTool]
    [Description("Deletes a Person Directory and all its persons and faces from Azure AI Content Understanding.")]
    public async Task<string> DeletePersonDirectory(
        [Description("Identifier of the directory to delete.")] string personDirectoryId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(personDirectoryId, nameof(personDirectoryId));

        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            BuildUri($"/contentunderstanding/personDirectories/{Uri.EscapeDataString(personDirectoryId)}"));

        return await SendAndReadAsync(request, cancellationToken);
    }

    [McpServerTool]
    [Description("Adds a person profile to an existing Person Directory and returns its personId. Tags can be used to store metadata such as the person's name.")]
    public async Task<string> AddPerson(
        [Description("Identifier of the directory to add the person to.")] string personDirectoryId,
        [Description("Display name of the person; stored under the 'name' tag.")] string name,
        [Description("Optional additional tags as a JSON object, e.g. {\"employeeId\":\"E12345\"}.")] string? additionalTagsJson = null,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(personDirectoryId, nameof(personDirectoryId));
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name must be provided.", nameof(name));
        }

        var tags = new JsonObject { ["name"] = name };
        MergeTags(tags, additionalTagsJson);

        var payload = new JsonObject { ["tags"] = tags };

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri($"/contentunderstanding/personDirectories/{Uri.EscapeDataString(personDirectoryId)}/persons"))
        {
            Content = JsonContent.Create(payload),
        };

        return await SendAndReadAsync(request, cancellationToken);
    }

    [McpServerTool]
    [Description("Lists the persons enrolled in a Person Directory.")]
    public async Task<string> ListPersons(
        [Description("Identifier of the directory whose persons should be listed.")] string personDirectoryId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(personDirectoryId, nameof(personDirectoryId));

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri($"/contentunderstanding/personDirectories/{Uri.EscapeDataString(personDirectoryId)}/persons"));

        return await SendAndReadAsync(request, cancellationToken);
    }

    [McpServerTool]
    [Description("Uploads a face image from a local file to the 'faces' blob container and returns the resulting blob URL, suitable for use as a faceSource URL in other Person Directory operations.")]
    public async Task<string> UploadFaceImage(
        [Description("Absolute path to the local image file (JPEG, PNG, etc.) to upload.")] string path,
        [Description("Optional blob name to store the image under. Defaults to a GUID-based name preserving the file extension.")] string? blobName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must be provided.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"File not found: {path}", path);
        }

        await _facesContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        blobName = string.IsNullOrWhiteSpace(blobName)
            ? $"{Guid.NewGuid():N}{Path.GetExtension(path)}"
            : blobName;

        var blob = _facesContainer.GetBlobClient(blobName);

        await using (var stream = File.OpenRead(path))
        {
            await blob.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
        }

        return blob.Uri.ToString();
    }

    [McpServerTool]
    [Description("Adds a face image to a Person Directory and optionally associates it with an existing person. Accepts either a blob/HTTP URL or a local file path. Local files are uploaded to the 'faces' blob container first.")]
    public async Task<string> AddFace(
        [Description("Identifier of the Person Directory.")] string personDirectoryId,
        [Description("Either a publicly accessible URL of the face image or an absolute local file path. Local files are uploaded to blob storage first.")] string imageUrlOrPath,
        [Description("Optional personId of an existing person to associate the face with.")] string? personId = null,
        [Description("Optional face quality threshold: 'low', 'medium' (default) or 'high'.")] string? qualityThreshold = null,
        [Description("Optional user-defined identifier for the source image.")] string? imageReferenceId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(personDirectoryId, nameof(personDirectoryId));
        if (string.IsNullOrWhiteSpace(imageUrlOrPath))
        {
            throw new ArgumentException("imageUrlOrPath must be provided.", nameof(imageUrlOrPath));
        }

        var faceSource = await BuildFaceSourceAsync(imageUrlOrPath, imageReferenceId, cancellationToken);

        var payload = new JsonObject
        {
            ["faceSource"] = faceSource,
        };

        if (!string.IsNullOrWhiteSpace(personId))
        {
            payload["personId"] = personId;
        }

        if (!string.IsNullOrWhiteSpace(qualityThreshold))
        {
            payload["qualityThreshold"] = qualityThreshold;
        }

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri($"/contentunderstanding/personDirectories/{Uri.EscapeDataString(personDirectoryId)}/faces"))
        {
            Content = JsonContent.Create(payload),
        };

        return await SendAndReadAsync(request, cancellationToken);
    }

    [McpServerTool]
    [Description("Identifies the most likely person matches for a face image by comparing it against the persons enrolled in a Person Directory. Accepts either a URL or a local file path.")]
    public async Task<string> IdentifyPerson(
        [Description("Identifier of the Person Directory to search.")] string personDirectoryId,
        [Description("Either a publicly accessible URL of the face image or an absolute local file path. Local files are uploaded to blob storage first.")] string imageUrlOrPath,
        [Description("Maximum number of person candidates to return. Defaults to 1.")] int maxPersonCandidates = 1,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(personDirectoryId, nameof(personDirectoryId));
        if (string.IsNullOrWhiteSpace(imageUrlOrPath))
        {
            throw new ArgumentException("imageUrlOrPath must be provided.", nameof(imageUrlOrPath));
        }

        var payload = new JsonObject
        {
            ["faceSource"] = await BuildFaceSourceAsync(imageUrlOrPath, null, cancellationToken),
            ["maxPersonCandidates"] = maxPersonCandidates,
        };

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri($"/contentunderstanding/personDirectory/{Uri.EscapeDataString(personDirectoryId)}/persons:identify"))
        {
            Content = JsonContent.Create(payload),
        };

        return await SendAndReadAsync(request, cancellationToken);
    }

    [McpServerTool]
    [Description("Finds visually similar faces from all stored face entries in a Person Directory. Accepts either a URL or a local file path.")]
    public async Task<string> FindSimilarFaces(
        [Description("Identifier of the Person Directory to search.")] string personDirectoryId,
        [Description("Either a publicly accessible URL of the face image or an absolute local file path. Local files are uploaded to blob storage first.")] string imageUrlOrPath,
        [Description("Maximum number of similar faces to return. Defaults to 10, capped at 1000.")] int maxSimilarFaces = 10,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentifier(personDirectoryId, nameof(personDirectoryId));
        if (string.IsNullOrWhiteSpace(imageUrlOrPath))
        {
            throw new ArgumentException("imageUrlOrPath must be provided.", nameof(imageUrlOrPath));
        }

        var payload = new JsonObject
        {
            ["faceSource"] = await BuildFaceSourceAsync(imageUrlOrPath, null, cancellationToken),
            ["maxSimilarFaces"] = maxSimilarFaces,
        };

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri($"/personDirectory/{Uri.EscapeDataString(personDirectoryId)}/faces:find"))
        {
            Content = JsonContent.Create(payload),
        };

        return await SendAndReadAsync(request, cancellationToken);
    }

    private async Task<JsonObject> BuildFaceSourceAsync(
        string imageUrlOrPath,
        string? imageReferenceId,
        CancellationToken cancellationToken)
    {
        var faceSource = new JsonObject();

        if (Uri.TryCreate(imageUrlOrPath, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            faceSource["url"] = uri.ToString();
            if (!string.IsNullOrWhiteSpace(imageReferenceId))
            {
                faceSource["imageReferenceId"] = imageReferenceId;
            }

            return faceSource;
        }

        if (!File.Exists(imageUrlOrPath))
        {
            throw new FileNotFoundException(
                $"File not found and value is not an http(s) URL: {imageUrlOrPath}",
                imageUrlOrPath);
        }

        // Embed local files as base64 data so callers don't need a publicly
        // reachable URL. The tutorial explicitly supports this as an
        // alternative to a blob URL.
        var bytes = await File.ReadAllBytesAsync(imageUrlOrPath, cancellationToken);
        faceSource["data"] = Convert.ToBase64String(bytes);

        var referenceId = imageReferenceId ?? Path.GetFileName(imageUrlOrPath);
        if (!string.IsNullOrWhiteSpace(referenceId))
        {
            faceSource["imageReferenceId"] = referenceId;
        }

        return faceSource;
    }

    private Uri BuildUri(string path)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return new Uri(_endpoint, $"{path}{separator}api-version={ApiVersion}");
    }

    private async Task<string> SendAndReadAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(CognitiveServicesScopes),
            cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Content Understanding Person Directory request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
        }

        return body;
    }

    private static void MergeTags(JsonObject tags, string? additionalTagsJson)
    {
        if (string.IsNullOrWhiteSpace(additionalTagsJson))
        {
            return;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(additionalTagsJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                $"additionalTagsJson is not valid JSON: {ex.Message}",
                nameof(additionalTagsJson),
                ex);
        }

        if (parsed is not JsonObject extra)
        {
            throw new ArgumentException(
                "additionalTagsJson must be a JSON object.",
                nameof(additionalTagsJson));
        }

        foreach (var kvp in extra)
        {
            tags[kvp.Key] = kvp.Value?.DeepClone();
        }
    }

    private static void EnsureIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} must be provided.", name);
        }
    }
}
