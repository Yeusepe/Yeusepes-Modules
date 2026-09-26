using System.Net;
using System.Net.Http;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal static class SpotifyPlaybackStateLoader
{
    public static async Task LoadAsync(SpotifyRequestContext context, SpotifyUtilities utilities, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateRequest(context);
            using var response = await context.HttpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                utilities.Log("No active playback detected. Please start playing something in Spotify.");
                return;
            }
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
                Apply(document.RootElement, context);
                return;
            }
            utilities.Log("Unable to fetch playback state. Please start playing something in Spotify.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException)
        {
            utilities.LogDebug("Playback state response did not contain JSON");
        }
        catch (Exception exception)
        {
            utilities.LogDebug($"Playback state request failed: {exception.Message}");
        }
    }

    private static HttpRequestMessage CreateRequest(SpotifyRequestContext context)
    {
        var token = CredentialManager.LoadApiAccessToken();
        if (string.IsNullOrEmpty(token)) token = context.AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player");
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
        request.Headers.Add("Referer", "https://developer.spotify.com/");
        return request;
    }

    private static void Apply(JsonElement state, SpotifyRequestContext context)
    {
        if (state.TryGetProperty("device", out var device) && device.ValueKind == JsonValueKind.Object)
        {
            context.DeviceId = device.GetProperty("id").GetString();
            context.DeviceName = device.GetProperty("name").GetString();
            context.IsActiveDevice = device.GetProperty("is_active").GetBoolean();
            if (device.TryGetProperty("volume_percent", out var volume) && volume.ValueKind == JsonValueKind.Number)
                context.VolumePercent = volume.GetInt32();
        }
        if (state.TryGetProperty("shuffle_state", out var shuffle)) context.ShuffleState = shuffle.GetBoolean();
        if (state.TryGetProperty("smart_shuffle", out var smart)) context.SmartShuffle = smart.GetBoolean();
        if (state.TryGetProperty("repeat_state", out var repeat)) context.RepeatState = repeat.GetString();
        if (state.TryGetProperty("is_playing", out var playing)) context.IsPlaying = playing.GetBoolean();
        if (state.TryGetProperty("timestamp", out var timestamp)) context.Timestamp = timestamp.GetInt64();
        if (state.TryGetProperty("progress_ms", out var progress)) context.ProgressMs = progress.GetInt32();
        if (state.TryGetProperty("context", out var playbackContext)) ApplyContext(playbackContext, context);
        if (state.TryGetProperty("item", out var item)) ApplyItem(item, context);
    }

    private static void ApplyContext(JsonElement source, SpotifyRequestContext context)
    {
        if (source.ValueKind != JsonValueKind.Object) return;
        if (source.TryGetProperty("external_urls", out var urls) && urls.TryGetProperty("spotify", out var url))
            context.ContextExternalUrl = url.GetString();
        if (source.TryGetProperty("href", out var href)) context.ContextHref = href.GetString();
        if (source.TryGetProperty("type", out var type)) context.ContextType = type.GetString();
        if (source.TryGetProperty("uri", out var uri)) context.ContextUri = uri.GetString();
    }

    private static void ApplyItem(JsonElement item, SpotifyRequestContext context)
    {
        if (item.ValueKind != JsonValueKind.Object) return;
        context.TrackName = item.GetProperty("name").GetString();
        if (item.TryGetProperty("duration_ms", out var duration)) context.TrackDurationMs = duration.GetInt32();
        if (item.TryGetProperty("disc_number", out var disc)) context.DiscNumber = disc.GetInt32();
        if (item.TryGetProperty("explicit", out var explicitValue)) context.IsExplicit = explicitValue.GetBoolean();
        if (item.TryGetProperty("popularity", out var popularity)) context.Popularity = popularity.GetInt32();
        if (item.TryGetProperty("preview_url", out var preview)) context.PreviewUrl = preview.GetString();
        if (item.TryGetProperty("track_number", out var trackNumber)) context.TrackNumber = trackNumber.GetInt32();
        if (item.TryGetProperty("uri", out var trackUri)) context.TrackUri = trackUri.GetString();
        if (item.TryGetProperty("currently_playing_type", out var type)) context.CurrentlyPlayingType = type.GetString();
        if (item.TryGetProperty("artists", out var artists))
            context.Artists = artists.EnumerateArray()
                .Select(artist => (artist.GetProperty("name").GetString()!, artist.GetProperty("uri").GetString()!))
                .ToList();
        if (!item.TryGetProperty("album", out var album)) return;
        context.AlbumName = album.GetProperty("name").GetString();
        if (album.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
            context.AlbumArtworkUrl = images[0].GetProperty("url").GetString();
        if (album.TryGetProperty("album_type", out var albumType)) context.AlbumType = albumType.GetString();
        if (album.TryGetProperty("release_date", out var release)) context.AlbumReleaseDate = release.GetString();
        if (album.TryGetProperty("total_tracks", out var total)) context.AlbumTotalTracks = total.GetInt32();
    }
}
