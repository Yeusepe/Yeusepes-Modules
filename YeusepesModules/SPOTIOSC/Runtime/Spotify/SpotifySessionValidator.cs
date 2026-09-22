using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifySessionValidator
{
    private const string Endpoint = "https://api-partner.spotify.com/pathfinder/v2/query";
    private const string QueryHash = "53bcb064f6cd18c23f752bc324a791194d20df612d8e1239c735144ab0399ced";
    private readonly HttpClient _httpClient;
    private readonly Action<string> _log;
    private readonly Action<string> _logDebug;

    public SpotifySessionValidator(HttpClient httpClient, Action<string> log, Action<string> logDebug)
    {
        _httpClient = httpClient;
        _log = log;
        _logDebug = logDebug;
    }

    public async Task<bool> ValidateAsync(string accessToken, string clientToken)
    {
        try
        {
            using var request = CreateRequest(accessToken, clientToken);
            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                ThrowRateLimit();
            using var document = JsonDocument.Parse(body);
            if (ContainsRateLimit(document.RootElement)) ThrowRateLimit();

            response.EnsureSuccessStatusCode();
            if (!TryGetIdentity(document.RootElement, out var displayName, out var userId))
            {
                _log("Spotify profile response was missing the display name or user ID.");
                return false;
            }

            _log($"Fetched user successfully! Display Name: {displayName}, ID: {userId}");
            return true;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log($"Error fetching profile attributes: {exception.Message}");
            _logDebug("Spotify session validation failed");
            return false;
        }
    }

    private static HttpRequestMessage CreateRequest(string accessToken, string clientToken)
    {
        var body = JsonSerializer.Serialize(new
        {
            variables = new { },
            operationName = "profileAttributes",
            extensions = new { persistedQuery = new { version = 1, sha256Hash = QueryHash } }
        });
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Content.Headers.ContentType!.CharSet = "UTF-8";
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("client-token", clientToken);
        request.Headers.Add("app-platform", "WebPlayer");
        request.Headers.Add("spotify-app-version", "1.2.80.289.gd6b01cc3");
        request.Headers.Add("accept", "application/json");
        request.Headers.Add("accept-language", "en");
        request.Headers.Add("priority", "u=1, i");
        request.Headers.Add("sec-fetch-dest", "empty");
        request.Headers.Add("sec-fetch-mode", "cors");
        request.Headers.Add("sec-fetch-site", "same-site");
        return request;
    }

    private static bool TryGetIdentity(JsonElement root, out string? displayName, out string? userId)
    {
        displayName = null;
        userId = null;
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("me", out var me) ||
            !me.TryGetProperty("profile", out var profile))
            return false;

        displayName = profile.TryGetProperty("name", out var name) ? name.GetString() : null;
        userId = profile.TryGetProperty("uri", out var uri)
            ? uri.GetString()
            : profile.TryGetProperty("username", out var username) ? username.GetString() : null;
        const string prefix = "spotify:user:";
        if (userId?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
            userId = userId[prefix.Length..];
        return !string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(userId);
    }

    private static bool ContainsRateLimit(JsonElement root) =>
        root.TryGetProperty("errors", out var errors) &&
        errors.ValueKind == JsonValueKind.Array &&
        errors.EnumerateArray().Any(error =>
            error.TryGetProperty("extensions", out var extensions) &&
            extensions.TryGetProperty("statusCode", out var status) &&
            status.TryGetInt32(out var code) && code == 429);

    private void ThrowRateLimit()
    {
        _log("API rate limit exceeded (429). Please wait before trying again.");
        throw new HttpRequestException(
            "API rate limit exceeded (429)",
            null,
            HttpStatusCode.TooManyRequests);
    }
}
