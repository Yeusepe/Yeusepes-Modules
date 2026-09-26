using System.Text.Json;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class SpotifyPlaybackEventHandler
{
    private readonly SpotifyRequestContext _context;
    private readonly SpotiOscOutput _output;
    private readonly PlaybackClock _clock;
    private readonly PlaybackProjection _projection;

    public SpotifyPlaybackEventHandler(
        SpotifyRequestContext context,
        SpotiOscOutput output,
        PlaybackClock clock,
        PlaybackProjection projection)
    {
        _context = context;
        _output = output;
        _clock = clock;
        _projection = projection;
    }

    public void HandlePayload(JsonElement payload)
    {
        if (!payload.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in events.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("event", out var details) ||
                !details.TryGetProperty("state", out var state))
                continue;

            ApplyState(state);
            ApplyTrack(state);
            _projection.Update();
        }
    }

    private void ApplyState(JsonElement state)
    {
        ApplyDevice(state);
        ApplyOptions(state);
        ApplyTiming(state);
        ApplyContext(state);

        _context.IsPlaying = state.GetProperty("is_playing").GetBoolean();
        _output.Set(SpotiOSC.SpotiParameters.IsPlaying, _context.IsPlaying);
        _output.Trigger(_context.IsPlaying ? "PlayEvent" : "PauseEvent");
        ApplyRestrictions(state);
        _output.Trigger("ShuffleEvent");
        _output.Trigger("RepeatEvent");
        _output.Trigger("VolumeEvent");
    }

    private void ApplyDevice(JsonElement state)
    {
        if (!state.TryGetProperty("device", out var device) || device.ValueKind != JsonValueKind.Object) return;
        _context.DeviceId = device.GetProperty("id").GetString();
        _context.DeviceName = device.GetProperty("name").GetString();
        _context.IsActiveDevice = device.GetProperty("is_active").GetBoolean();
        if (device.TryGetProperty("volume_percent", out var volume) && volume.ValueKind == JsonValueKind.Number)
            _context.VolumePercent = volume.GetInt32();
        _output.Set(SpotiOSC.SpotiParameters.DeviceIsActive, _context.IsActiveDevice);
        _output.Set(SpotiOSC.SpotiParameters.DeviceIsPrivate, device.GetProperty("is_private_session").GetBoolean());
        _output.Set(SpotiOSC.SpotiParameters.DeviceIsRestricted, device.GetProperty("is_restricted").GetBoolean());
        _output.Set(SpotiOSC.SpotiParameters.DeviceSupportsVolume, device.GetProperty("supports_volume").GetBoolean());
        _output.Set(SpotiOSC.SpotiParameters.DeviceVolumePercent, _context.VolumePercent);
    }

    private void ApplyOptions(JsonElement state)
    {
        var shuffle = state.TryGetProperty("shuffle_state", out var shuffleElement)
            ? shuffleElement.GetBoolean()
            : _context.ShuffleState;
        var smartShuffle = state.TryGetProperty("smart_shuffle", out var smartElement)
            ? smartElement.GetBoolean()
            : _context.SmartShuffle;
        _context.ShuffleState = shuffle;
        _context.SmartShuffle = smartShuffle;
        _output.Set(SpotiOSC.SpotiParameters.ShuffleMode, !shuffle ? 0 : smartShuffle ? 2 : 1);

        if (!state.TryGetProperty("repeat_state", out var repeat)) return;
        _context.RepeatState = repeat.GetString();
        _output.Set(SpotiOSC.SpotiParameters.RepeatMode, MapRepeat(_context.RepeatState));
    }

    private void ApplyTiming(JsonElement state)
    {
        if (state.TryGetProperty("timestamp", out var timestamp))
        {
            _context.Timestamp = timestamp.GetInt64();
            _output.Set(SpotiOSC.SpotiParameters.Timestamp, (float)(_context.Timestamp % int.MaxValue));
        }
        if (!state.TryGetProperty("progress_ms", out var progress)) return;

        _context.ProgressMs = progress.GetInt32();
        var isPlaying = state.TryGetProperty("is_playing", out var playing) && playing.GetBoolean();
        var duration = state.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object &&
                       item.TryGetProperty("duration_ms", out var value)
            ? value.GetInt32()
            : _context.TrackDurationMs;
        _clock.Synchronize(_context.ProgressMs, isPlaying, duration);
    }

    private void ApplyContext(JsonElement state)
    {
        if (!state.TryGetProperty("context", out var playbackContext) || playbackContext.ValueKind != JsonValueKind.Object) return;
        if (playbackContext.TryGetProperty("external_urls", out var urls) && urls.TryGetProperty("spotify", out var spotify))
            _context.ContextExternalUrl = spotify.GetString();
        if (playbackContext.TryGetProperty("href", out var href)) _context.ContextHref = href.GetString();
        if (playbackContext.TryGetProperty("type", out var type))
        {
            _context.ContextType = type.GetString();
            _output.Set(SpotiOSC.SpotiParameters.ContextType, _context.ContextType == "playlist" ? 0 : -1);
        }
        if (playbackContext.TryGetProperty("uri", out var uri)) _context.ContextUri = uri.GetString();
    }

    private void ApplyRestrictions(JsonElement state)
    {
        if (!state.TryGetProperty("actions", out var actions) ||
            !actions.TryGetProperty("disallows", out var disallows))
            return;

        if (disallows.TryGetProperty("resuming", out var resume))
        {
            _context.DisallowResuming = resume.GetBoolean();
            _output.Set(SpotiOSC.SpotiParameters.DisallowResuming, _context.DisallowResuming);
        }
        if (disallows.TryGetProperty("pausing", out var pause))
        {
            _context.DisallowPausing = pause.GetBoolean();
            _output.Set(SpotiOSC.SpotiParameters.DisallowPausing, _context.DisallowPausing);
        }
        if (disallows.TryGetProperty("skipping_prev", out var previous))
        {
            _context.DisallowSkippingPrev = previous.GetBoolean();
            _output.Set(SpotiOSC.SpotiParameters.DisallowSkippingPrev, _context.DisallowSkippingPrev);
        }
    }

    private void ApplyTrack(JsonElement state)
    {
        if (!state.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) return;
        _context.TrackName = item.GetProperty("name").GetString();
        if (item.TryGetProperty("artists", out var artists))
            _context.Artists = artists.EnumerateArray()
                .Select(artist => (artist.GetProperty("name").GetString()!, artist.GetProperty("uri").GetString()!))
                .ToList();
        SetInt(item, "duration_ms", value => _context.TrackDurationMs = value, SpotiOSC.SpotiParameters.TrackDurationMs, true);
        SetInt(item, "disc_number", value => _context.DiscNumber = value, SpotiOSC.SpotiParameters.DiscNumber);
        SetBool(item, "explicit", value => _context.IsExplicit = value, SpotiOSC.SpotiParameters.IsExplicit);
        SetBool(item, "is_local", value => _context.IsLocal = value, SpotiOSC.SpotiParameters.IsLocal);
        SetInt(item, "popularity", value => _context.Popularity = value, SpotiOSC.SpotiParameters.SongPopularity);
        SetInt(item, "track_number", value => _context.TrackNumber = value, SpotiOSC.SpotiParameters.TrackNumber);
        if (item.TryGetProperty("preview_url", out var preview)) _context.PreviewUrl = preview.GetString();
        if (item.TryGetProperty("uri", out var uri)) _context.TrackUri = uri.GetString();
        if (item.TryGetProperty("currently_playing_type", out var type)) _context.CurrentlyPlayingType = type.GetString();
        if (item.TryGetProperty("album", out var album)) ApplyAlbum(album);
    }

    private void ApplyAlbum(JsonElement album)
    {
        _context.AlbumName = album.GetProperty("name").GetString();
        if (album.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
            _context.AlbumArtworkUrl = images[0].GetProperty("url").GetString();
        if (album.TryGetProperty("album_type", out var type))
        {
            _context.AlbumType = type.GetString();
            _output.Set(SpotiOSC.SpotiParameters.AlbumType, _context.AlbumType == "single" ? 0 : _context.AlbumType == "album" ? 1 : 2);
        }
        if (album.TryGetProperty("release_date", out var release)) _context.AlbumReleaseDate = release.GetString();
        SetInt(album, "total_tracks", value => _context.AlbumTotalTracks = value, SpotiOSC.SpotiParameters.AlbumTotalTracks);
    }

    private void SetInt(
        JsonElement source,
        string property,
        Action<int> assign,
        SpotiOSC.SpotiParameters parameter,
        bool sendAsFloat = false)
    {
        if (!source.TryGetProperty(property, out var element)) return;
        var value = element.GetInt32();
        assign(value);
        _output.Set(parameter, sendAsFloat ? (object)(float)value : value);
    }

    private void SetBool(
        JsonElement source,
        string property,
        Action<bool> assign,
        SpotiOSC.SpotiParameters parameter)
    {
        if (!source.TryGetProperty(property, out var element)) return;
        var value = element.GetBoolean();
        assign(value);
        _output.Set(parameter, value);
    }

    private static int MapRepeat(string? repeat) => repeat switch
    {
        "track" => 1,
        "context" => 2,
        _ => 0
    };

}
