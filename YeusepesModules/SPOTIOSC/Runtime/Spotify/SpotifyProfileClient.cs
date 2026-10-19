using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifyProfileClient
{
    private const string ProfileUrl = "https://api.spotify.com/v1/me";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public SpotifyProfileClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<SpotifyProfile> GetAsync()
    {
        await CredentialManager.EnsureSignedInAsync();
        var token = CredentialManager.LoadApiAccessToken();
        if (string.IsNullOrEmpty(token))
            throw new UnauthorizedAccessException("Spotify API authorization has not completed.");

        var client = new SpotifyAuthenticatedClient(_httpClient, token, useApiToken: true);
        using var request = client.CreateRequest(HttpMethod.Get, ProfileUrl);
        var body = await client.SendAsync(request);
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
