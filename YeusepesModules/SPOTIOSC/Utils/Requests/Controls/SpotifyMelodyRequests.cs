using System;
using System.Threading.Tasks;
using SpotifyAPI.Web;
using YeusepesModules.SPOTIOSC.Credentials;

public class SpotifyApiService
{
    private SpotifyClient _client;

    /// <summary>
    /// Ensures we have a valid access token (scraped or loaded) and builds the SpotifyClient.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(CredentialManager.LoadApiAccessToken()))
        {
            // Headless‐only: scrape the API endpoint for access_token + refresh_token
            await CredentialManager.CaptureApiTokensAsync();
        }

        var apiToken = CredentialManager.LoadApiAccessToken();
        if (string.IsNullOrEmpty(apiToken))
            throw new InvalidOperationException("Failed to obtain API access token.");

        _client = new SpotifyClient(apiToken);
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
        => await _client.Player.ResumePlayback(new PlayerResumePlaybackRequest { DeviceId = deviceId });

    public async Task PlayUriAsync(string uri, string deviceId = null)
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


    /// <summary> Pause playback. </summary>
    public async Task PauseAsync(string deviceId = null)
        => await _client.Player.PausePlayback(new PlayerPausePlaybackRequest { DeviceId = deviceId });

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

    /// <summary> Add a track or episode to the end of the user’s queue. </summary>
    public async Task AddToQueueAsync(string uri, string deviceId = null)
        => await _client.Player.AddToQueue(new PlayerAddToQueueRequest(uri) { DeviceId = deviceId });

    /// <summary>
    /// Refreshes the access token using the saved refresh token, persists the new pair,
    /// and rebuilds the SpotifyClient.
    /// </summary>
    private async Task RefreshAndReinitializeAsync()
    {
        var savedRefresh = CredentialManager.LoadApiRefreshToken();
        if (string.IsNullOrEmpty(savedRefresh))
            throw new InvalidOperationException("No saved API refresh token.");

        var refreshRequest = new PKCETokenRefreshRequest(
            clientId: CredentialManager.ApiClientId,
            refreshToken: savedRefresh
        );
        var refreshResponse = await new OAuthClient().RequestToken(refreshRequest);

        CredentialManager.SaveApiAccessToken(refreshResponse.AccessToken);
        if (!string.IsNullOrEmpty(refreshResponse.RefreshToken))
            CredentialManager.SaveApiRefreshToken(refreshResponse.RefreshToken);

        _client = new SpotifyClient(refreshResponse.AccessToken);
    }
}
