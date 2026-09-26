using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifyAuthenticatedClient
{
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;

    public SpotifyAuthenticatedClient(HttpClient httpClient, string accessToken)
    {
        _httpClient = httpClient;
        _accessToken = accessToken;
    }

    public HttpRequestMessage CreateRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
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
                    CredentialManager.LoadAccessToken());

            using var response = await _httpClient.SendAsync(copy, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            if (attempt > 0 || !await RefreshAsync()) break;
        }
        throw new UnauthorizedAccessException("Token refresh failed. Please sign in again.");
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source)
    {
        var copy = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (source.Content is null) return copy;

        copy.Content = new ByteArrayContent(await source.Content.ReadAsByteArrayAsync());
        foreach (var header in source.Content.Headers)
            copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }

    private static async Task<bool> RefreshAsync()
    {
        try
        {
            await CredentialManager.LoginAndCaptureCookiesAsync();
            return !string.IsNullOrEmpty(CredentialManager.LoadAccessToken());
        }
        catch
        {
            return false;
        }
    }
}
