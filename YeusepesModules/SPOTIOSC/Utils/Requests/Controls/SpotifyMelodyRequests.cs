using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualBasic.Logging;
using SpotifyAPI.Web;
using YeusepesModules.SPOTIOSC.Credentials;
using System.Net.Http;
using System.Text.Json;

public class SpotifyApiService
{
    private SpotifyClient _client;

    /// <summary>
    /// Ensures we have a valid access token (scraped or loaded) and builds the SpotifyClient.
    /// </summary>
    public async Task InitializeAsync()
    {
        var accessToken = CredentialManager.LoadApiAccessToken();
        var refreshToken = CredentialManager.LoadApiRefreshToken();
        var clientId = CredentialManager.ApiClientId;

        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientId)) { 
            var refreshRequest = new PKCETokenRefreshRequest(clientId,refreshToken);
            var refreshResponse = await new OAuthClient().RequestToken(refreshRequest);
            accessToken = refreshResponse.AccessToken;
        }


        var initialResponse = new PKCETokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken
        };

        var authenticator = new PKCEAuthenticator(clientId, initialResponse);
        authenticator.TokenRefreshed += (s, tokens) => {            
            CredentialManager.SaveApiAccessToken(tokens.AccessToken);
            if (!string.IsNullOrEmpty(tokens.RefreshToken))
                CredentialManager.SaveApiRefreshToken(tokens.RefreshToken);
        };

        var config = SpotifyClientConfig
                        .CreateDefault()
                        .WithAuthenticator(authenticator);

        _client = new SpotifyClient(config);        
    }



    /// <summary>
    /// Gets the user's current playback state. On 401 it will refresh once & retry.
    /// </summary>
    public async Task<CurrentlyPlaying> GetCurrentPlaybackAsync()
    {
        try
        {
            return await _client.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
        }
        catch (APIUnauthorizedException)
        {
            await RefreshAndReinitializeAsync();
            return await _client.Player.GetCurrentlyPlaying(new PlayerCurrentlyPlayingRequest());
        }
    }

    /// <summary> Start or resume playback. </summary>
    public async Task PlayAsync(string deviceId = null)
    {
        try
        {
            await _client.Player.ResumePlayback(new PlayerResumePlaybackRequest { DeviceId = deviceId });
        }
        catch (APIUnauthorizedException)
        {
            await RefreshAndReinitializeAsync();
            await _client.Player.ResumePlayback(new PlayerResumePlaybackRequest { DeviceId = deviceId });
        }
        catch (APIException ex) when (ex.Message.Contains("Device not found"))
        {
            // Device not found - try to get current active device
            var activeDevice = await GetActiveDeviceAsync();
            if (activeDevice != null)
            {
                await _client.Player.ResumePlayback(new PlayerResumePlaybackRequest { DeviceId = activeDevice.Id });
            }
            else
            {
                // No active device - try without device ID (Spotify will use default)
                await _client.Player.ResumePlayback(new PlayerResumePlaybackRequest());
            }
        }
        catch (APIException ex) when (ex.Message.Contains("Device not found") || ex.Message.Contains("Resuming is not allowed"))
        {
            // Try using the melody API as a fallback
            await TryMelodyResumeAsync(deviceId);
        }
        catch (APIException ex)
        {
            throw new Exception($"Spotify API error during play: {ex.Message}");
        }
    }

    public async Task PlayUriAsync(string uri, string deviceId = null)
    {
        try
        {
            // Spotify only accepts single‐track URIs in `uris`, but albums/artists/playlists
            // as `context_uri`.
            var req = new PlayerResumePlaybackRequest { DeviceId = deviceId };
            if (uri.StartsWith("spotify:track:"))
                req.Uris = new List<string> { uri };
            else
                req.ContextUri = uri;

            await _client.Player.ResumePlayback(req);
        }
        catch (APIUnauthorizedException)
        {
            await RefreshAndReinitializeAsync();
            var req = new PlayerResumePlaybackRequest { DeviceId = deviceId };
            if (uri.StartsWith("spotify:track:"))
                req.Uris = new List<string> { uri };
            else
                req.ContextUri = uri;
            await _client.Player.ResumePlayback(req);
        }
        catch (APIException ex) when (ex.Message.Contains("Device not found"))
        {
            // Device not found - try to get current active device
            var activeDevice = await GetActiveDeviceAsync();
            var req = new PlayerResumePlaybackRequest { DeviceId = activeDevice?.Id };
            if (uri.StartsWith("spotify:track:"))
                req.Uris = new List<string> { uri };
            else
                req.ContextUri = uri;
            await _client.Player.ResumePlayback(req);
        }
        catch (APIException ex)
        {
            throw new Exception($"Spotify API error during play URI: {ex.Message}");
        }
    }


    /// <summary> Pause playback. </summary>
    public async Task PauseAsync(string deviceId = null)
    {
        try
        {
            await _client.Player.PausePlayback(new PlayerPausePlaybackRequest { DeviceId = deviceId });
        }
        catch (APIUnauthorizedException)
        {
            await RefreshAndReinitializeAsync();
            await _client.Player.PausePlayback(new PlayerPausePlaybackRequest { DeviceId = deviceId });
        }
        catch (APIException ex) when (ex.Message.Contains("Device not found") || ex.Message.Contains("Pausing is not allowed"))
        {
            // Try using the melody API as a fallback
            await TryMelodyPauseAsync(deviceId);
        }
        catch (APIException ex)
        {
            throw new Exception($"Spotify API error during pause: {ex.Message}");
        }
    }

    /// <summary> Skip to next track. </summary>
    public async Task NextTrackAsync()
        => await _client.Player.SkipNext();

    /// <summary> Skip to previous track. </summary>
    public async Task PreviousTrackAsync()
        => await _client.Player.SkipPrevious();

    /// <summary> Seek to a position (ms) in the currently playing track. </summary>
    public async Task SeekAsync(int positionMs, string deviceId = null)
        => await _client.Player.SeekTo(new PlayerSeekToRequest(positionMs) { DeviceId = deviceId });

    /// <summary> Set volume (0–100%). </summary>
    public async Task SetVolumeAsync(int volumePercent, string deviceId = null)
        => await _client.Player.SetVolume(new PlayerVolumeRequest(volumePercent) { DeviceId = deviceId });

    /// <summary> Toggle shuffle on/off. </summary>
    public async Task SetShuffleAsync(bool state, string deviceId = null)
        => await _client.Player.SetShuffle(new PlayerShuffleRequest(state) { DeviceId = deviceId });

    /// <summary> Set repeat mode: off, track, or context. </summary>
    public async Task SetRepeatAsync(PlayerSetRepeatRequest.State state, string deviceId = null)
        => await _client.Player.SetRepeat(new PlayerSetRepeatRequest(state) { DeviceId = deviceId });

    /// <summary>
    /// Transfer playback to one or more devices. 
    /// If play = true, playback will start on the new device immediately.
    /// </summary>
    public async Task TransferPlaybackAsync(string[] deviceIds, bool play = false)
        => await _client.Player.TransferPlayback(new PlayerTransferPlaybackRequest(deviceIds)
        {
            Play = play
        });

    /// <summary> Add a track or episode to the end of the user's queue. </summary>
    public async Task AddToQueueAsync(string uri, string deviceId = null)
        => await _client.Player.AddToQueue(new PlayerAddToQueueRequest(uri) { DeviceId = deviceId });

    /// <summary> Get the currently active device. </summary>
    public async Task<Device> GetActiveDeviceAsync()
    {
        try
        {
            var devices = await _client.Player.GetAvailableDevices();
            return devices.Devices.FirstOrDefault(d => d.IsActive);
        }
        catch (APIUnauthorizedException)
        {
            await RefreshAndReinitializeAsync();
            var devices = await _client.Player.GetAvailableDevices();
            return devices.Devices.FirstOrDefault(d => d.IsActive);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary> Try to resume playback using Spotify's melody API as a fallback. </summary>
    private async Task TryMelodyResumeAsync(string deviceId = null)
    {
        try
        {
            var accessToken = CredentialManager.LoadAccessToken();
            var clientToken = CredentialManager.LoadClientToken();
            
            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientToken))
            {
                throw new Exception("Missing access token or client token for melody API");
            }

            // If no device ID provided, try to get the desktop device (most reliable)
            if (string.IsNullOrEmpty(deviceId))
            {
                var devices = await _client.Player.GetAvailableDevices();
                var desktopDevice = devices.Devices.FirstOrDefault(d => d.Type == "Computer");
                deviceId = desktopDevice?.Id;
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                throw new Exception("No suitable device found for melody API");
            }

            // Create the melody API request
            var melodyRequest = new
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
                            command_type = "resume",
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

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(melodyRequest);
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.DefaultRequestHeaders.Add("Client-Token", clientToken);
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            httpClient.DefaultRequestHeaders.Add("Content-Type", "text/plain;charset=UTF-8");

            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "text/plain");
            var response = await httpClient.PostAsync("https://gue1-spclient.spotify.com/melody/v1/msg/batch", content);
            
            if (response.IsSuccessStatusCode)
            {
                // Melody API call succeeded
                return;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Melody API failed with status {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Melody API error: {ex.Message}");
        }
    }

    /// <summary> Try to pause playback using Spotify's melody API as a fallback. </summary>
    private async Task TryMelodyPauseAsync(string deviceId = null)
    {
        try
        {
            var accessToken = CredentialManager.LoadAccessToken();
            var clientToken = CredentialManager.LoadClientToken();
            
            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientToken))
            {
                throw new Exception("Missing access token or client token for melody API");
            }

            // If no device ID provided, try to get the desktop device (most reliable)
            if (string.IsNullOrEmpty(deviceId))
            {
                var devices = await _client.Player.GetAvailableDevices();
                var desktopDevice = devices.Devices.FirstOrDefault(d => d.Type == "Computer");
                deviceId = desktopDevice?.Id;
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                throw new Exception("No suitable device found for melody API");
            }

            // Create the melody API request for pause
            var melodyRequest = new
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
                            command_type = "pause",
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

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(melodyRequest);
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            httpClient.DefaultRequestHeaders.Add("Client-Token", clientToken);
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            httpClient.DefaultRequestHeaders.Add("Content-Type", "text/plain;charset=UTF-8");

            var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "text/plain");
            var response = await httpClient.PostAsync("https://gue1-spclient.spotify.com/melody/v1/msg/batch", content);
            
            if (response.IsSuccessStatusCode)
            {
                // Melody API call succeeded
                return;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Melody API failed with status {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Melody API error: {ex.Message}");
        }
    }

    /// <summary> Get audio features for a single track using direct HTTP request. </summary>
    public async Task<TrackAudioFeatures> GetTrackFeaturesAsync(string trackId)
    {
        try
        {
            // Use direct HTTP request with the same token that works for the rest of the module
            var accessToken = CredentialManager.LoadAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new Exception("No access token available");
            }

            var url = $"https://api.spotify.com/v1/audio-features/{trackId}";
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var audioFeatures = System.Text.Json.JsonSerializer.Deserialize<TrackAudioFeatures>(responseContent, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });
                return audioFeatures;
            }
            else
            {
                throw new Exception($"Spotify API Error: {response.StatusCode} - {responseContent}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching audio features: {ex.Message}");
        }
    }

    /// <summary> Get audio features for multiple tracks using direct HTTP request. </summary>
    public async Task<List<TrackAudioFeatures>> GetMultipleTrackFeaturesAsync(List<string> trackIds)
    {
        try
        {
            // Use direct HTTP request with the same token that works for the rest of the module
            var accessToken = CredentialManager.LoadAccessToken();
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new Exception("No access token available");
            }

            var idsParam = string.Join(",", trackIds);
            var url = $"https://api.spotify.com/v1/audio-features?ids={idsParam}";
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.GetAsync(url);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var result = System.Text.Json.JsonSerializer.Deserialize<AudioFeaturesResponse>(responseContent, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true
                });

                return result.AudioFeatures ?? new List<TrackAudioFeatures>();
            }
            else
            {
                throw new Exception($"Spotify API Error: {response.StatusCode} - {responseContent}");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching audio features: {ex.Message}");
        }
    }

    public class AudioFeaturesResponse
    {
        public List<TrackAudioFeatures> AudioFeatures { get; set; }
    }

    /// <summary>
    /// Refreshes the access token using the saved refresh token, persists the new pair,
    /// and rebuilds the SpotifyClient.
    /// </summary>
    public async Task RefreshAndReinitializeAsync()
    {
        var savedRefresh = CredentialManager.LoadApiRefreshToken();
        if (string.IsNullOrEmpty(savedRefresh))
        {
            var clientToken = CredentialManager.LoadClientToken();
            var accessToken = CredentialManager.LoadAccessToken();
            var clientId = CredentialManager.ClientId;

            savedRefresh = CredentialManager.LoadApiRefreshToken();
        }
        
        var refreshRequest = new PKCETokenRefreshRequest(
            clientId: CredentialManager.ApiClientId,
            refreshToken: savedRefresh
        );
        var refreshResponse = await new OAuthClient().RequestToken(refreshRequest);
        
        _client = new SpotifyClient(refreshResponse.AccessToken);
    }

}
