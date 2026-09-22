using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YeusepesModules.SPOTIOSC.Runtime.Playback;
using YeusepesModules.SPOTIOSC.Runtime.Spotify;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed class SpotifyJamService
{
    private const string CurrentOrNewSessionUrl =
        "https://gue1-spclient.spotify.com/social-connect/v2/sessions/current_or_new?activate=true";
    private const string UrlDispenserUrl =
        "https://gue1-spclient.spotify.com/url-dispenser/v1/generate-url";
    private readonly SpotifyRequestContext _context;
    private readonly SpotifyUtilities _utilities;
    private readonly SpotiOscOutput _output;
    private readonly SpotifyAuthenticatedClient _client;

    public SpotifyJamService(
        SpotifyRequestContext context,
        SpotifyUtilities utilities,
        SpotiOscOutput output)
    {
        _context = context;
        _utilities = utilities;
        _output = output;
        _client = new SpotifyAuthenticatedClient(context.HttpClient, context.AccessToken);
    }

    public JamSessionState State { get; } = new();

    public async Task<bool> CreateAsync()
    {
        try
        {
            using var request = _client.CreateRequest(HttpMethod.Get, CurrentOrNewSessionUrl);
            var body = await _client.SendAsync(request);
            ApplyCreatedSession(JsonSerializer.Deserialize<JsonElement>(body));
            _utilities.LogDebug("Spotify Jam session created successfully");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _utilities.LogDebug("Token refresh failed. Please sign in again.");
        }
        catch (Exception exception)
        {
            _utilities.LogDebug($"Spotify Jam creation failed: {exception.Message}");
            _output.Set(SpotiOSC.SpotiParameters.Error, true);
        }
        return false;
    }

    public async Task<bool> JoinAsync(string sessionId)
    {
        try
        {
            await SpotifyPlaybackStateLoader.LoadAsync(_context, _utilities);
            if (string.IsNullOrEmpty(_context.DeviceId))
            {
                _utilities.LogDebug("Failed to find an active device. Cannot join Spotify Jam.");
                return false;
            }

            var url = $"https://gue1-spclient.spotify.com/social-connect/v2/sessions/join/{sessionId}" +
                      $"?playback_control=listen_and_control&join_type=deeplinking&local_device_id={_context.DeviceId}";
            using var request = _client.CreateRequest(
                HttpMethod.Post,
                url,
                new StringContent("{}", Encoding.UTF8, "application/json"));
            ConfigureSocialHeaders(request, "1.2.57.463", "131");
            await _client.SendAsync(request);

            State.SessionId = sessionId;
            SetActive(true);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _utilities.LogDebug("Token refresh failed. Please sign in again.");
        }
        catch (Exception exception)
        {
            _utilities.LogDebug($"Spotify Jam join failed: {exception.Message}");
            _output.Set(SpotiOSC.SpotiParameters.Error, true);
        }
        return false;
    }

    public async Task<bool> LeaveAsync()
    {
        if (string.IsNullOrEmpty(State.SessionId))
        {
            ApplyLeave();
            return true;
        }

        var url = $"https://gue1-spclient.spotify.com/social-connect/v3/sessions/{State.SessionId}/leave";
        try
        {
            using (var options = CreateLeaveOptionsRequest(url))
            using (var response = await _context.HttpClient.SendAsync(options))
            {
                if (!response.IsSuccessStatusCode)
                {
                    _utilities.LogDebug($"Jam leave preflight failed: {response.StatusCode}");
                    return false;
                }
            }

            using var request = _client.CreateRequest(
                HttpMethod.Post,
                url,
                new StringContent("{}", Encoding.UTF8, "application/json"));
            ConfigureSocialHeaders(request, "1.2.52.442", "130");
            await _client.SendAsync(request);
            ApplyLeave();
            _utilities.LogDebug("Successfully left the Spotify Jam session.");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _utilities.LogDebug("Token refresh failed. Please sign in again.");
        }
        catch (Exception exception)
        {
            _utilities.LogDebug($"Spotify Jam leave failed: {exception.Message}");
            _output.Set(SpotiOSC.SpotiParameters.Error, true);
        }
        return false;
    }

    public void ApplyLeave()
    {
        State.Reset();
        _context.IsInJam = false;
        _context.IsJamOwner = false;
        _context.JamShortCode = null;
        _context.JamOwnerName = null;
        _output.Set(SpotiOSC.SpotiParameters.InAJam, false);
        _output.Set(SpotiOSC.SpotiParameters.IsJamOwner, false);
        _output.Set(SpotiOSC.SpotiParameters.WantJam, false);
    }

    public void SetActive(bool active)
    {
        State.IsActive = active;
        _context.IsInJam = active;
        _output.Set(SpotiOSC.SpotiParameters.InAJam, active);
    }

    public async Task<string?> GenerateShareableCodeAsync(string spotifyUri)
    {
        var sessionId = spotifyUri.Split(':').Last();
        var payload = new
        {
            spotify_uri = spotifyUri,
            custom_data = new[]
            {
                new { key = "ssp", value = "1" },
                new { key = "app_destination", value = "socialsession" }
            },
            link_preview = new
            {
                title = "Join my Jam on Spotify",
                image_url = $"https://shareables.scdn.co/publish/socialsession/{sessionId}"
            },
            utm_parameters = new
            {
                utm_medium = "share-link",
                utm_source = "share-options-sheet",
                utm_campaign = (string?)null,
                utm_term = (string?)null,
                utm_content = (string?)null
            }
        };
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, UrlDispenserUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, options), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
        request.Headers.Add("app-platform", "Win32_x86_64");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _context.AccessToken);
        request.Headers.Add("client-token", _context.ClientToken);
        ConfigureBrowserHeaders(request, "131");

        using var response = await _context.HttpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        var url = JsonSerializer.Deserialize<JsonElement>(body).GetProperty("shareable_url").GetString();
        return url?.Split('/').Last();
    }

    private void ApplyCreatedSession(JsonElement session)
    {
        if (session.TryGetProperty("session_id", out var sessionId))
            State.SessionId = sessionId.GetString();
        if (session.TryGetProperty("join_session_token", out var joinToken))
            State.JoinToken = joinToken.GetString();
        if (session.TryGetProperty("shareable_url", out var shareableUrl))
        {
            State.ShareableUrl = shareableUrl.GetString();
            _context.JamShortCode = State.ShareableUrl?.Split('/').Last();
        }
        if (session.TryGetProperty("active", out var active))
        {
            SetActive(active.GetBoolean());
            _context.IsJamOwner = true;
            _output.Set(SpotiOSC.SpotiParameters.IsJamOwner, true);
        }
    }

    private static HttpRequestMessage CreateLeaveOptionsRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, url);
        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("Accept-Language", "en-Latn-US,en-US;q=0.9,en-Latn;q=0.8,en;q=0.7");
        request.Headers.Add("Access-Control-Request-Headers", "app-platform,authorization,client-token,content-type,spotify-app-version");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Origin", "https://xpui.app.spotify.com");
        request.Headers.Add("Referer", "https://xpui.app.spotify.com/");
        request.Headers.Add("Sec-Fetch-Dest", "empty");
        request.Headers.Add("Sec-Fetch-Mode", "cors");
        request.Headers.Add("Sec-Fetch-Site", "same-site");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.6723.117 Spotify/1.2.52.442 Safari/537.36");
        return request;
    }

    private static void ConfigureSocialHeaders(HttpRequestMessage request, string appVersion, string chromiumVersion)
    {
        request.Headers.Remove("User-Agent");
        request.Headers.Remove("Accept");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Accept-Language", "en");
        ConfigureBrowserHeaders(request, chromiumVersion);
        request.Headers.Add("spotify-app-version", appVersion);
        request.Headers.UserAgent.ParseAdd($"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{chromiumVersion}.0.0.0 Spotify/{appVersion} Safari/537.36");
    }

    private static void ConfigureBrowserHeaders(HttpRequestMessage request, string chromiumVersion)
    {
        request.Headers.TryAddWithoutValidation("origin", "https://xpui.app.spotify.com");
        request.Headers.TryAddWithoutValidation("priority", "u=1, i");
        request.Headers.TryAddWithoutValidation("referer", "https://xpui.app.spotify.com/");
        request.Headers.TryAddWithoutValidation("sec-ch-ua", $"\"Chromium\";v=\"{chromiumVersion}\", \"Not_A Brand\";v=\"24\"");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
        request.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
        request.Headers.TryAddWithoutValidation("sec-fetch-site", "same-site");
    }
}
