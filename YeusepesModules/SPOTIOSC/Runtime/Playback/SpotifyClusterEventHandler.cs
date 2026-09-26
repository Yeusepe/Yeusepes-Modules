using System.Text.Json;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class SpotifyClusterEventHandler
{
    private const string TrackPrefix = "spotify:track:";
    private const string ImagePrefix = "spotify:image:";
    private readonly SpotifyRequestContext _context;
    private readonly SpotiOscOutput _output;
    private readonly PlaybackClock _clock;
    private readonly PlaybackProjection _projection;
    private readonly TrackMetadataEnricher _metadata;
    private readonly Action<string> _logDebug;

    public SpotifyClusterEventHandler(
        SpotifyRequestContext context,
        SpotiOscOutput output,
        PlaybackClock clock,
        PlaybackProjection projection,
        TrackMetadataEnricher metadata,
        Action<string> logDebug)
    {
        _context = context;
        _output = output;
        _clock = clock;
        _projection = projection;
        _metadata = metadata;
        _logDebug = logDebug;
    }

    public void Handle(JsonElement message)
    {
        try
        {
            if (!message.TryGetProperty("payloads", out var payloads) ||
                payloads.ValueKind != JsonValueKind.Array)
                return;

            foreach (var payload in payloads.EnumerateArray())
            {
                if (payload.ValueKind != JsonValueKind.Object ||
                    !payload.TryGetProperty("cluster", out var cluster))
                    continue;
                if (cluster.TryGetProperty("player_state", out var state)) ApplyState(state);
                if (cluster.TryGetProperty("active_device_id", out var deviceId))
                    _context.DeviceId = deviceId.GetString();
            }
        }
        catch (Exception exception)
        {
            _logDebug($"Cluster update failed: {exception.Message}");
        }
    }

    private void ApplyState(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object) return;
        var isPlaying = state.TryGetProperty("is_playing", out var playing)
            ? playing.GetBoolean()
            : _context.IsPlaying;
        if (state.TryGetProperty("is_paused", out var paused)) isPlaying &= !paused.GetBoolean();
        _context.IsPlaying = isPlaying;
        _output.Set(SpotiOSC.SpotiParameters.IsPlaying, isPlaying);

        var position = _context.ProgressMs;
        if (TryReadInt32(state, "position_as_of_timestamp", out var positionValue))
            _context.ProgressMs = position = positionValue;

        var duration = _context.TrackDurationMs;
        if (TryReadInt32(state, "duration", out var durationValue))
        {
            _context.TrackDurationMs = duration = durationValue;
            _output.Set(SpotiOSC.SpotiParameters.TrackDurationMs, (float)duration);
        }
        if (TryReadInt64(state, "timestamp", out var timestamp))
        {
            _context.Timestamp = timestamp;
            _output.Set(SpotiOSC.SpotiParameters.Timestamp, (float)(timestamp % int.MaxValue));
        }
        if (state.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.Object)
            ApplyTrack(track);
        if (state.TryGetProperty("context_uri", out var contextUri))
            _context.ContextUri = contextUri.GetString();
        if (state.TryGetProperty("options", out var options)) ApplyOptions(options);

        _clock.Synchronize(position, isPlaying, duration);
        _output.Trigger(isPlaying ? "PlayEvent" : "PauseEvent");
        _projection.Update();
    }

    private void ApplyTrack(JsonElement track)
    {
        if (track.TryGetProperty("uri", out var uri) && _context.TrackUri != uri.GetString())
        {
            _context.TrackUri = uri.GetString();
            _context.Artists = [];
        }
        if (track.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            ApplyMetadata(metadata);
        if (string.IsNullOrWhiteSpace(_context.ArtistNames) &&
            _context.TrackUri?.StartsWith(TrackPrefix, StringComparison.Ordinal) == true)
            _metadata.Enqueue(_context.TrackUri[TrackPrefix.Length..]);
    }

    private void ApplyMetadata(JsonElement metadata)
    {
        if (metadata.TryGetProperty("title", out var title)) _context.TrackName = title.GetString();
        if (metadata.TryGetProperty("album_title", out var album)) _context.AlbumName = album.GetString();

        var artistName = metadata.TryGetProperty("artist_name", out var artist) ? artist.GetString() : null;
        var artistUri = metadata.TryGetProperty("artist_uri", out var artistId) ? artistId.GetString() : null;
        if (!string.IsNullOrWhiteSpace(artistName))
            _context.Artists = [(artistName, artistUri ?? string.Empty)];

        var image = metadata.TryGetProperty("image_url", out var normalImage)
            ? normalImage.GetString()
            : metadata.TryGetProperty("image_large_url", out var largeImage) ? largeImage.GetString() : null;
        if (!string.IsNullOrEmpty(image))
            _context.AlbumArtworkUrl = image.StartsWith(ImagePrefix, StringComparison.Ordinal)
                ? $"https://i.scdn.co/image/{image[ImagePrefix.Length..]}"
                : image;
    }

    private void ApplyOptions(JsonElement options)
    {
        if (options.TryGetProperty("shuffling_context", out var shuffle))
        {
            _context.ShuffleState = shuffle.GetBoolean();
            _output.Set(SpotiOSC.SpotiParameters.ShuffleMode, _context.ShuffleState ? 1 : 0);
        }
        var repeat = options.TryGetProperty("repeating_track", out var trackRepeat) && trackRepeat.GetBoolean()
            ? "track"
            : options.TryGetProperty("repeating_context", out var contextRepeat) && contextRepeat.GetBoolean()
                ? "context"
                : "off";
        _context.RepeatState = repeat;
        _output.Set(SpotiOSC.SpotiParameters.RepeatMode, repeat == "track" ? 1 : repeat == "context" ? 2 : 0);
    }

    private static bool TryReadInt32(JsonElement source, string property, out int result)
    {
        result = 0;
        if (!source.TryGetProperty(property, out var value)) return false;
        return value.ValueKind == JsonValueKind.String
            ? int.TryParse(value.GetString(), out result)
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result);
    }

    private static bool TryReadInt64(JsonElement source, string property, out long result)
    {
        result = 0;
        if (!source.TryGetProperty(property, out var value)) return false;
        return value.ValueKind == JsonValueKind.String
            ? long.TryParse(value.GetString(), out result)
            : value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out result);
    }
}
