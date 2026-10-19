using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifyAuthenticatedClient
{
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly bool _useApiToken;

    public SpotifyAuthenticatedClient(HttpClient httpClient, string accessToken, bool useApiToken = false)
    {
        _httpClient = httpClient;
        _accessToken = accessToken;
        _useApiToken = useApiToken;
    }

    public HttpRequestMessage CreateRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CurrentToken());
        request.Options.Set(SpotifyHttp.TokenKind, _useApiToken ? "WebAPI" : "WebPlayer");
        return request;
    }

    public async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var copy = await CloneAsync(request);
            if (attempt > 0)
                copy.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    CurrentToken());

            using var response = await _httpClient.SendAsync(copy, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return await SpotifyHttp.ReadResponseAsync(response, cancellationToken);
            }
            if (attempt > 0)
            {
                var details = await SpotifyHttp.ReadErrorAsync(response, cancellationToken);
                throw new UnauthorizedAccessException(details);
            }
            if (copy.Headers.Authorization?.Parameter == CurrentToken() && !await RefreshAsync()) break;
        }
        throw new UnauthorizedAccessException("Token refresh failed. Please sign in again.");
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source)
    {
        var copy = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in source.Options)
            copy.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        if (source.Content is null) return copy;

        copy.Content = new ByteArrayContent(await source.Content.ReadAsByteArrayAsync());
        foreach (var header in source.Content.Headers)
            copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }

    private string CurrentToken()
    {
        var token = _useApiToken ? CredentialManager.LoadApiAccessToken() : CredentialManager.LoadAccessToken();
        return string.IsNullOrEmpty(token) ? _accessToken : token;
    }

    private async Task<bool> RefreshAsync()
    {
        await CredentialManager.LoginAndCaptureCookiesAsync();
        return !string.IsNullOrEmpty(_useApiToken
            ? CredentialManager.LoadApiAccessToken()
            : CredentialManager.LoadAccessToken());
    }
}
