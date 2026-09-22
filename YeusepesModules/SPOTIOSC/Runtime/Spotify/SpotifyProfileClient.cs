using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifyProfileClient
{
    private const string ProfileUrl = "https://api.spotify.com/v1/me";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SpotifyAuthenticatedClient _client;
    private readonly string _webPlayerToken;

    public SpotifyProfileClient(HttpClient httpClient, string webPlayerToken)
    {
        _client = new SpotifyAuthenticatedClient(httpClient, webPlayerToken);
        _webPlayerToken = webPlayerToken;
    }

    public async Task<SpotifyProfile> GetAsync()
    {
        var token = CredentialManager.LoadApiAccessToken();
        if (string.IsNullOrEmpty(token)) token = _webPlayerToken;

        using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var body = await _client.SendAsync(request);
        return JsonSerializer.Deserialize<SpotifyProfile>(body, JsonOptions)
               ?? throw new JsonException("Spotify returned an empty profile");
    }
}

internal sealed record SpotifyProfile(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("product")] string? Product,
    [property: JsonPropertyName("images")] IReadOnlyList<SpotifyProfileImage>? Images);

internal sealed record SpotifyProfileImage(
    [property: JsonPropertyName("url")] string? Url);
