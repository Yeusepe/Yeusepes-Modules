using System.Text.Json;
using YeusepesModules.SPOTIOSC.Runtime.Jam;
using YeusepesModules.SPOTIOSC.Runtime.Playback;
using YeusepesModules.SPOTIOSC.Runtime.Spotify;

namespace YeusepesModules.SPOTIOSC.Runtime.Events;

internal sealed class DealerEventRouter
{
    private const string ContentSettingsUri = "playback-settings/content-settings-update";
    private const string VolumeUri = "hm://connect-state/v1/connect/volume";
    private const string ClusterUri = "hm://connect-state/v1/cluster";
    private readonly SpotifyPlaybackEventHandler _playback;
    private readonly SpotifyClusterEventHandler _cluster;
    private readonly JamEventHandler _jam;
    private readonly SpotifyConnectEventHandler _connect;
    private readonly PlaybackProjection _projection;
    private readonly EventDeduplicator _deduplicator = new();
    private readonly Action<string> _logDebug;

    public DealerEventRouter(
        SpotifyPlaybackEventHandler playback,
        SpotifyClusterEventHandler cluster,
        JamEventHandler jam,
        SpotifyConnectEventHandler connect,
        PlaybackProjection projection,
        Action<string> logDebug)
    {
        _playback = playback;
        _cluster = cluster;
        _jam = jam;
        _connect = connect;
        _projection = projection;
        _logDebug = logDebug;
    }

    public void Handle(JsonElement message)
    {
        try
        {
            if (message.ValueKind != JsonValueKind.Object) return;
            var uri = message.TryGetProperty("uri", out var uriElement) ? uriElement.GetString() : null;
            if (uri == ContentSettingsUri)
            {
                _connect.HandleContentSettings(message);
                return;
            }
            if (uri?.StartsWith(VolumeUri, StringComparison.OrdinalIgnoreCase) == true)
            {
                _connect.HandleVolume(message);
                return;
            }
            if (uri?.StartsWith(ClusterUri, StringComparison.OrdinalIgnoreCase) == true)
            {
                _cluster.Handle(message);
                _projection.UpdateState();
                return;
            }

            if (message.TryGetProperty("payloads", out var payloads) &&
                payloads.ValueKind == JsonValueKind.Array)
            {
                foreach (var payload in payloads.EnumerateArray())
                    HandlePayload(payload);
            }
            _projection.UpdateState();
        }
        catch (Exception exception)
        {
            _logDebug($"Player event failed: {exception.Message}");
        }
    }

    public void Clear() => _deduplicator.Clear();

    private void HandlePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return;
        if (payload.TryGetProperty("session", out var session) && _deduplicator.IsDuplicate(session))
        {
            _logDebug("Duplicate session event ignored");
            return;
        }

        _jam.Handle(payload);
        _playback.HandlePayload(payload);
    }
}
