using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SpotifyAPI.Web;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifyApiService
{
    private const string MelodyEndpoint = "https://gue1-spclient.spotify.com/melody/v1/msg/batch";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly HttpClient _httpClient;
    private SpotifyClient _client = null!;

    public SpotifyApiService(HttpClient httpClient) => _httpClient = httpClient;

    public async Task InitializeAsync()
    {
        var accessToken = CredentialManager.LoadApiAccessToken();
        var refreshToken = CredentialManager.LoadApiRefreshToken();
        var clientId = CredentialManager.ApiClientId;
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientId))
        {
            accessToken = (await new OAuthClient().RequestToken(
                new PKCETokenRefreshRequest(clientId, refreshToken))).AccessToken;
        }

        var authenticator = new PKCEAuthenticator(clientId, new PKCETokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken
        });
        authenticator.TokenRefreshed += (_, tokens) => SaveApiTokens(tokens.AccessToken, tokens.RefreshToken);
        _client = new SpotifyClient(SpotifyClientConfig.CreateDefault().WithAuthenticator(authenticator));
    }

    public async Task PlayAsync(string? deviceId = null)
    {
        try
        {
            await WithRefreshAsync(() => ResumeAsync(new PlayerResumePlaybackRequest { DeviceId = deviceId }));
        }
        catch (APIException exception) when (HasMessage(exception, "Device not found"))
        {
            await ResumeAsync(new PlayerResumePlaybackRequest { DeviceId = (await GetActiveDeviceAsync())?.Id });
        }
        catch (APIException exception) when (HasMessage(exception, "Resuming is not allowed"))
        {
            await SendMelodyCommandAsync("resume", deviceId);
        }
        catch (APIException exception)
        {
            throw SpotifyError("play", exception);
        }
    }

    public Task PlayUriAsync(string uri, string? deviceId = null) =>
        ResumeUriAsync(id => CreateUriRequest(uri, id), deviceId, "play URI");

    public Task PlayUriWithOffsetAsync(
        string contextUri,
        string? offsetTrackUri = null,
        int? offsetPosition = null,
        string? deviceId = null) =>
        ResumeUriAsync(
            id => CreateContextRequest(contextUri, offsetTrackUri, offsetPosition, id),
            deviceId,
            "play URI with offset");

    public async Task<int?> FindTrackPositionInPlaylistAsync(string playlistId, string trackUri)
    {
        try
        {
            return await WithRefreshAsync(() => FindTrackPositionCoreAsync(playlistId, trackUri));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Error finding track position in playlist: {exception.Message}",
                exception);
        }
    }

    public async Task PauseAsync(string? deviceId = null)
    {
        try
        {
            await WithRefreshAsync(() =>
                _client.Player.PausePlayback(new PlayerPausePlaybackRequest { DeviceId = deviceId }));
        }
        catch (APIException exception) when (
            HasMessage(exception, "Device not found") || HasMessage(exception, "Pausing is not allowed"))
        {
            await SendMelodyCommandAsync("pause", deviceId);
        }
        catch (APIException exception)
        {
            throw SpotifyError("pause", exception);
        }
    }

    public Task NextTrackAsync(string? deviceId = null) => SkipAsync(true, deviceId);
    public Task PreviousTrackAsync(string? deviceId = null) => SkipAsync(false, deviceId);

    public Task SetVolumeAsync(int volumePercent, string? deviceId = null) =>
        _client.Player.SetVolume(new PlayerVolumeRequest(volumePercent) { DeviceId = deviceId });

    public Task SeekAsync(int positionMs, string? deviceId = null) =>
        _client.Player.SeekTo(new PlayerSeekToRequest(positionMs) { DeviceId = deviceId });

    public async Task<TrackAudioFeatures?> GetTrackFeaturesAsync(string trackId)
    {
        var token = CredentialManager.LoadApiAccessToken();
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("No API access token available");

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.spotify.com/v1/audio-features/{trackId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Spotify API error: {response.StatusCode} - {body}");
        return JsonSerializer.Deserialize<TrackAudioFeatures>(body, JsonOptions);
    }

    private Task ResumeAsync(PlayerResumePlaybackRequest request) => _client.Player.ResumePlayback(request);

    private async Task ResumeUriAsync(
        Func<string?, PlayerResumePlaybackRequest> createRequest,
        string? deviceId,
        string operation)
    {
        try
        {
            await ResumeWithDeviceFallbackAsync(createRequest, deviceId);
        }
        catch (APIException exception)
        {
            throw SpotifyError(operation, exception);
        }
    }

    private async Task ResumeWithDeviceFallbackAsync(
        Func<string?, PlayerResumePlaybackRequest> createRequest,
        string? deviceId)
    {
        try
        {
            await WithRefreshAsync(() => ResumeAsync(createRequest(deviceId)));
        }
        catch (APIException exception) when (HasMessage(exception, "Device not found"))
        {
            await ResumeAsync(createRequest((await GetActiveDeviceAsync())?.Id));
        }
    }

    private static PlayerResumePlaybackRequest CreateUriRequest(string uri, string? deviceId)
    {
        var request = new PlayerResumePlaybackRequest { DeviceId = deviceId };
        if (uri.StartsWith("spotify:track:", StringComparison.Ordinal)) request.Uris = [uri];
        else request.ContextUri = uri;
        return request;
    }

    private static PlayerResumePlaybackRequest CreateContextRequest(
        string contextUri,
        string? offsetTrackUri,
        int? offsetPosition,
        string? deviceId)
    {
        var request = new PlayerResumePlaybackRequest { ContextUri = contextUri, DeviceId = deviceId };
        if (!string.IsNullOrEmpty(offsetTrackUri))
            request.OffsetParam = new PlayerResumePlaybackRequest.Offset { Uri = offsetTrackUri };
        else if (offsetPosition.HasValue)
            request.OffsetParam = new PlayerResumePlaybackRequest.Offset { Position = offsetPosition.Value };
        return request;
    }

    private async Task<int?> FindTrackPositionCoreAsync(string playlistId, string trackUri)
    {
        const int pageSize = 100;
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await _client.Playlists.GetItems(playlistId, new PlaylistGetItemsRequest
            {
                Limit = pageSize,
                Offset = offset
            });
            if (page?.Items is not { Count: > 0 } tracks) return null;

            for (var index = 0; index < tracks.Count; index++)
            {
                var uri = tracks[index].Track switch
                {
                    FullTrack track => track.Uri,
                    FullEpisode episode => episode.Uri,
                    _ => null
                };
                if (uri == trackUri) return offset + index;
            }
            if (tracks.Count < pageSize) return null;
        }
    }

    private async Task SkipAsync(bool next, string? deviceId)
    {
        var direction = next ? "next" : "previous";
        Task Send(string? id) => next
            ? _client.Player.SkipNext(new PlayerSkipNextRequest { DeviceId = id })
            : _client.Player.SkipPrevious(new PlayerSkipPreviousRequest { DeviceId = id });

        try
        {
            await WithRefreshAsync(() => Send(deviceId));
        }
        catch (APIException exception) when (exception.Response?.StatusCode == HttpStatusCode.NotFound)
        {
            var activeDevice = await GetActiveDeviceAsync();
            if (activeDevice is not null)
            {
                await Send(activeDevice.Id);
                return;
            }
            throw new InvalidOperationException($"{direction} track command failed: {await DescribeNoActiveDeviceAsync()}");
        }
        catch (APIException exception) when (HasMessage(exception, "Restriction violated"))
        {
            throw new InvalidOperationException(
                $"{direction} track command failed: Spotify API restriction violated. " +
                "This may be due to account limitations, device restrictions, or rate limiting.");
        }
        catch (APIException exception)
        {
            throw SpotifyError($"{direction} track", exception);
        }
    }

    private async Task<Device?> GetActiveDeviceAsync()
    {
        try
        {
            return (await WithRefreshAsync(() => _client.Player.GetAvailableDevices()))
                .Devices.FirstOrDefault(device => device.IsActive);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> DescribeNoActiveDeviceAsync()
    {
        try
        {
            var devices = (await _client.Player.GetAvailableDevices()).Devices;
            return devices.Count == 0
                ? "no Spotify devices available. Open Spotify on this PC or your phone, then try again."
                : $"no active Spotify device — start playback first. Spotify can see: " +
                  string.Join(", ", devices.Select(device => $"{device.Name} ({device.Type})"));
        }
        catch (Exception exception)
        {
            return $"no active Spotify device, and the device list could not be read: {exception.Message}";
        }
    }

    private async Task SendMelodyCommandAsync(string command, string? deviceId)
    {
        var accessToken = CredentialManager.LoadAccessToken();
        var clientToken = CredentialManager.LoadClientToken();
        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientToken))
            throw new InvalidOperationException("Missing access token or client token for Melody API");

        deviceId ??= (await _client.Player.GetAvailableDevices()).Devices
            .FirstOrDefault(device => device.Type == "Computer")?.Id;
        if (string.IsNullOrEmpty(deviceId))
            throw new InvalidOperationException("No suitable device found for Melody API");

        var payload = new
        {
            messages = new[]
            {
                new
                {
                    type = "jssdk_connect_command",
                    message = new
                    {
                        ms_ack_duration = 327,
                        ms_request_latency = 299,
                        command_id = Guid.NewGuid().ToString("N"),
                        command_type = command,
                        target_device_brand = "spotify",
                        target_device_model = "PC desktop",
                        target_device_client_id = "65b708073fc0480ea92a077233ca87bd",
                        target_device_id = deviceId,
                        interaction_ids = "",
                        play_origin = "",
                        result = "success",
                        http_response = "",
                        http_status_code = 200
                    }
                }
            },
            sdk_id = "harmony:4.58.0-a717498aa",
            platform = "web_player windows 10;microsoft edge 140.0.0.0;desktop",
            client_version = "0.0.0"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, MelodyEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "text/plain")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Client-Token", clientToken);
        request.Headers.Add("Accept", "*/*");
        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Melody API failed with status {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task RefreshAndReinitializeAsync()
    {
        var tokens = await new OAuthClient().RequestToken(new PKCETokenRefreshRequest(
            CredentialManager.ApiClientId,
            CredentialManager.LoadApiRefreshToken()));
        SaveApiTokens(tokens.AccessToken, tokens.RefreshToken);
        await InitializeAsync();
    }

    private async Task WithRefreshAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (APIUnauthorizedException)
        {
            await RefreshAndReinitializeAsync();
            await operation();
        }
    }

    private async Task<T> WithRefreshAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (APIUnauthorizedException)
        {
            await RefreshAndReinitializeAsync();
            return await operation();
        }
    }

    private static void SaveApiTokens(string accessToken, string? refreshToken)
    {
        CredentialManager.SaveApiAccessToken(accessToken);
        if (!string.IsNullOrEmpty(refreshToken)) CredentialManager.SaveApiRefreshToken(refreshToken);
    }

    private static bool HasMessage(Exception exception, string text) =>
        exception.Message.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException SpotifyError(string operation, APIException exception) =>
        new($"Spotify API error during {operation}: {exception.Message}", exception);
}
