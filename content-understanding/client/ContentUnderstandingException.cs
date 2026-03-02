using System.Net;

namespace ContentUnderstanding.Client;

/// <summary>
/// Represents an error returned by the Azure AI Content Understanding API.
/// </summary>
public class ContentUnderstandingException : Exception
{
    /// <summary>
    /// The HTTP status code returned by the API.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The raw response body from the API.
    /// </summary>
    public string? ResponseBody { get; }

    public ContentUnderstandingException(string message, HttpStatusCode statusCode, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
