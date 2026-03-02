using System.Net.Http.Headers;
using Azure.Core;

namespace ContentUnderstanding.Client;

/// <summary>
/// A <see cref="DelegatingHandler"/> that acquires a bearer token from an
/// <see cref="Azure.Core.TokenCredential"/> and attaches it to every outgoing request.
/// Tokens are cached and refreshed automatically when they are about to expire.
/// </summary>
internal sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly TokenCredential _credential;
    private readonly string[] _scopes;

    private AccessToken _cachedToken;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="BearerTokenHandler"/> class.
    /// </summary>
    /// <param name="credential">The <see cref="TokenCredential"/> used to obtain tokens.</param>
    /// <param name="scopes">The scopes required for the token request.</param>
    public BearerTokenHandler(TokenCredential credential, string[] scopes)
        : base(new HttpClientHandler())
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        // Return the cached token if it is still valid (with a 5-minute buffer).
        if (_cachedToken.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return _cachedToken.Token;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring the lock.
            if (_cachedToken.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _cachedToken.Token;
            }

            _cachedToken = await _credential.GetTokenAsync(
                new TokenRequestContext(_scopes), cancellationToken);

            return _cachedToken.Token;
        }
        finally
        {
            _lock.Release();
        }
    }
}
