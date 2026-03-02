using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using ContentUnderstanding.Client.Models;

namespace ContentUnderstanding.Client;

/// <summary>
/// A custom client for interacting with the Azure AI Content Understanding REST API.
/// Supports analyzing content from URLs and local files.
/// </summary>
public class ContentUnderstandingClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _apiVersion;
    private readonly bool _ownsHttpClient;

    private const string DefaultScope = "https://cognitiveservices.azure.com/.default";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of <see cref="ContentUnderstandingClient"/>
    /// using the specified endpoint and API key.
    /// </summary>
    /// <param name="endpoint">The Azure AI Content Understanding service endpoint URL.</param>
    /// <param name="apiKey">The subscription key for authentication.</param>
    /// <param name="apiVersion">The API version to use (default: 2024-12-01-preview).</param>
    public ContentUnderstandingClient(string endpoint, string apiKey, string apiVersion = "2024-12-01-preview")
        : this(endpoint, apiKey, apiVersion, httpClient: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ContentUnderstandingClient"/>
    /// using a <see cref="TokenCredential"/> (e.g. <c>DefaultAzureCredential</c>) for
    /// Microsoft Entra ID (Azure AD) authentication instead of an API key.
    /// </summary>
    /// <param name="endpoint">The Azure AI Content Understanding service endpoint URL.</param>
    /// <param name="credential">The <see cref="TokenCredential"/> used to authenticate requests.</param>
    /// <param name="apiVersion">The API version to use (default: 2024-12-01-preview).</param>
    public ContentUnderstandingClient(string endpoint, TokenCredential credential, string apiVersion = "2024-12-01-preview")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);

        _endpoint = endpoint.TrimEnd('/');
        _apiVersion = apiVersion;

        var handler = new BearerTokenHandler(credential, [DefaultScope]);
        _httpClient = new HttpClient(handler);
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ContentUnderstandingClient"/>
    /// allowing injection of a custom <see cref="HttpClient"/> for testing.
    /// </summary>
    internal ContentUnderstandingClient(string endpoint, string apiKey, string apiVersion, HttpClient? httpClient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);

        _endpoint = endpoint.TrimEnd('/');
        _apiVersion = apiVersion;

        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }

        _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", apiKey);
    }

    /// <summary>
    /// Creates or replaces an analyzer with the given ID and definition.
    /// </summary>
    public async Task<AnalyzerResponse> CreateOrReplaceAnalyzerAsync(
        string analyzerId,
        AnalyzerDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);
        ArgumentNullException.ThrowIfNull(definition);

        var url = $"{_endpoint}/contentunderstanding/analyzers/{Uri.EscapeDataString(analyzerId)}?api-version={_apiVersion}";
        var json = JsonSerializer.Serialize(definition, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PutAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<AnalyzerResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize analyzer response.");
    }

    /// <summary>
    /// Retrieves the configuration of an existing analyzer.
    /// </summary>
    public async Task<AnalyzerResponse> GetAnalyzerAsync(
        string analyzerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);

        var url = $"{_endpoint}/contentunderstanding/analyzers/{Uri.EscapeDataString(analyzerId)}?api-version={_apiVersion}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<AnalyzerResponse>(responseJson, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize analyzer response.");
    }

    /// <summary>
    /// Deletes an existing analyzer.
    /// </summary>
    public async Task DeleteAnalyzerAsync(
        string analyzerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);

        var url = $"{_endpoint}/contentunderstanding/analyzers/{Uri.EscapeDataString(analyzerId)}?api-version={_apiVersion}";

        using var response = await _httpClient.DeleteAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>
    /// Submits content at the given URL for analysis and returns the result
    /// after polling until the operation completes.
    /// </summary>
    public async Task<AnalyzeResult> AnalyzeContentFromUrlAsync(
        string analyzerId,
        string contentUrl,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentUrl);

        var request = new AnalyzeRequest { Url = contentUrl };
        return await AnalyzeContentAsync(analyzerId, request, pollingInterval, cancellationToken);
    }

    /// <summary>
    /// Reads a local file and submits it for analysis, returning the result
    /// after polling until the operation completes.
    /// </summary>
    public async Task<AnalyzeResult> AnalyzeContentFromFileAsync(
        string analyzerId,
        string filePath,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"The file '{filePath}' was not found.", filePath);
        }

        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var base64Content = Convert.ToBase64String(fileBytes);
        var mimeType = MimeTypeHelper.GetMimeType(filePath);

        var request = new AnalyzeRequest
        {
            Data = base64Content,
            MimeType = mimeType
        };

        return await AnalyzeContentAsync(analyzerId, request, pollingInterval, cancellationToken);
    }

    /// <summary>
    /// Processes all supported files in a local directory, analyzing each one
    /// with the specified analyzer.
    /// </summary>
    /// <param name="analyzerId">The analyzer to use for processing.</param>
    /// <param name="directoryPath">Path to the local directory containing files.</param>
    /// <param name="searchPattern">File search pattern (default: "*.*").</param>
    /// <param name="includeSubdirectories">Whether to search subdirectories.</param>
    /// <param name="pollingInterval">Interval between result polling attempts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary mapping file paths to their analysis results.</returns>
    public async Task<Dictionary<string, AnalyzeResult>> AnalyzeFilesInDirectoryAsync(
        string analyzerId,
        string directoryPath,
        string searchPattern = "*.*",
        bool includeSubdirectories = false,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analyzerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"The directory '{directoryPath}' was not found.");
        }

        var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, searchPattern, searchOption);

        var results = new Dictionary<string, AnalyzeResult>();

        foreach (var file in files)
        {
            var result = await AnalyzeContentFromFileAsync(analyzerId, file, pollingInterval, cancellationToken);
            results[file] = result;
        }

        return results;
    }

    private async Task<AnalyzeResult> AnalyzeContentAsync(
        string analyzerId,
        AnalyzeRequest request,
        TimeSpan? pollingInterval,
        CancellationToken cancellationToken)
    {
        var interval = pollingInterval ?? TimeSpan.FromSeconds(2);

        var url = $"{_endpoint}/contentunderstanding/analyzers/{Uri.EscapeDataString(analyzerId)}:analyze?api-version={_apiVersion}";
        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        // The API returns 202 Accepted with an Operation-Location header for polling.
        if (!response.Headers.TryGetValues("Operation-Location", out var locationValues))
        {
            // If no polling header, try to read the result directly from the response body.
            var directJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<AnalyzeResult>(directJson, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize analysis result.");
        }

        var operationUrl = locationValues.First();

        // Poll until the operation completes.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(interval, cancellationToken);

            using var pollResponse = await _httpClient.GetAsync(operationUrl, cancellationToken);
            await EnsureSuccessAsync(pollResponse, cancellationToken);

            var resultJson = await pollResponse.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<AnalyzeResult>(resultJson, JsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize polling result.");

            if (result.Status is "Succeeded" or "Failed" or "Canceled")
            {
                return result;
            }
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new ContentUnderstandingException(
                $"API request failed with status {(int)response.StatusCode} ({response.StatusCode}): {body}",
                response.StatusCode,
                body);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
