using System.Diagnostics;
using VRCOSC.App.SDK.Parameters;
using YeusepesModules.SPOTIOSC.Runtime.Spotify;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class PlaybackCommandController
{
    private readonly SpotifyRequestContext _context;
    private readonly SpotifyApiService _api;
    private readonly SpotifyConnectCommandClient _connect;
    private readonly AudioFeatureController _features;
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;

    public PlaybackCommandController(
        SpotifyRequestContext context,
        SpotifyApiService api,
        SpotifyConnectCommandClient connect,
        AudioFeatureController features,
        SpotiOscOutput output,
        Action<string> logDebug)
    {
        _context = context;
        _api = api;
        _connect = connect;
        _features = features;
        _output = output;
        _logDebug = logDebug;
    }

    public bool TryHandle(RegisteredParameter parameter)
    {
        if (parameter.Lookup is not SpotiOSC.SpotiParameters lookup) return false;
        switch (lookup)
        {
            case SpotiOSC.SpotiParameters.Play:
                HandlePlay(parameter);
                return true;
            case SpotiOSC.SpotiParameters.PlayUri:
                HandleLocalUri(parameter);
                return true;
            case SpotiOSC.SpotiParameters.Pause when parameter.GetValue<bool>():
                Run(() => _api.PauseAsync(_context.DeviceId), "Pause failed");
                return true;
            case SpotiOSC.SpotiParameters.NextTrack when parameter.GetValue<bool>():
                Run(() => _api.NextTrackAsync(_context.DeviceId), "Next track failed");
                return true;
            case SpotiOSC.SpotiParameters.PreviousTrack when parameter.GetValue<bool>():
                Run(() => _api.PreviousTrackAsync(_context.DeviceId), "Previous track failed");
                return true;
            case SpotiOSC.SpotiParameters.RepeatMode:
                Run(() => _connect.SetRepeatAsync(parameter.GetValue<int>()), "Repeat update failed");
                return true;
            case SpotiOSC.SpotiParameters.ShuffleMode:
                Run(() => _connect.SetShuffleAsync(parameter.GetValue<int>()), "Shuffle update failed");
                return true;
            case SpotiOSC.SpotiParameters.DeviceVolumePercent:
                var volume = parameter.GetValue<int>();
                if (volume is >= 0 and <= 100)
                    Run(() => _api.SetVolumeAsync(volume, _context.DeviceId), "Volume update failed");
                return true;
            case SpotiOSC.SpotiParameters.PlaybackPosition:
                var position = parameter.GetValue<float>();
                if (float.IsFinite(position) && position >= 0 && (double)position <= int.MaxValue)
                    Run(() => _api.SeekAsync((int)position, _context.DeviceId), "Seek failed");
                return true;
            case SpotiOSC.SpotiParameters.GetTrackFeatures:
                _features.SetEnabled(parameter.GetValue<bool>());
                return true;
            default:
                return false;
        }
    }

    private void HandlePlay(RegisteredParameter parameter)
    {
        if (!parameter.GetValue<bool>()) return;
        var uri = parameter.IsWildcardType<string>(0) ? parameter.GetWildcard<string>(0) : null;
        if (string.IsNullOrEmpty(uri))
        {
            Run(() => _api.PlayAsync(_context.DeviceId), "Resume failed");
            return;
        }
        if (!uri.Contains('|'))
        {
            Run(() => _api.PlayUriAsync(uri, _context.DeviceId), $"Could not play {uri}");
            return;
        }
        if (uri.Count(character => character == '|') != 1)
        {
            _logDebug("Invalid combined URI. Expected context|track or context|position:N");
            _ = _output.PulseErrorAsync();
            return;
        }
        Run(() => PlayWithOffsetAsync(uri), $"Could not play {uri}");
    }

    private async Task PlayWithOffsetAsync(string combinedUri)
    {
        var parts = combinedUri.Split('|', 2);
        var contextUri = parts[0].Trim();
        var offset = parts[1].Trim();
        if (offset.StartsWith("spotify:track:", StringComparison.Ordinal) ||
            offset.StartsWith("track:", StringComparison.Ordinal))
        {
            var trackUri = offset.StartsWith("track:", StringComparison.Ordinal) ? $"spotify:{offset}" : offset;
            var playlistId = ExtractPlaylistId(contextUri);
            var position = playlistId is null
                ? null
                : await _api.FindTrackPositionInPlaylistAsync(playlistId, trackUri);
            if (position.HasValue)
                await _api.PlayUriWithOffsetAsync(contextUri, offsetPosition: position, deviceId: _context.DeviceId);
            else
                await _api.PlayUriWithOffsetAsync(contextUri, offsetTrackUri: trackUri, deviceId: _context.DeviceId);
            return;
        }
        if (offset.StartsWith("position:", StringComparison.Ordinal) &&
            int.TryParse(offset["position:".Length..], out var positionOffset))
        {
            await _api.PlayUriWithOffsetAsync(contextUri, offsetPosition: positionOffset, deviceId: _context.DeviceId);
            return;
        }
        throw new ArgumentException("Invalid offset. Use spotify:track:ID, track:ID, or position:N.");
    }

    private void HandleLocalUri(RegisteredParameter parameter)
    {
        if (!parameter.GetValue<bool>()) return;
        var uri = parameter.IsWildcardType<string>(0) ? parameter.GetWildcard<string>(0) : null;
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            _logDebug("Local playback requires a spotify: URI");
            _ = _output.PulseErrorAsync();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _logDebug($"Could not launch Spotify URI: {exception.Message}");
            _ = _output.PulseErrorAsync();
        }
    }

    private void Run(Func<Task> operation, string errorContext) => _ = ExecuteAsync(operation, errorContext);

    private async Task ExecuteAsync(Func<Task> operation, string errorContext)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            _logDebug($"{errorContext}: {exception.Message}");
            await _output.PulseErrorAsync();
        }
    }

    private static string? ExtractPlaylistId(string uri)
    {
        const string uriPrefix = "spotify:playlist:";
        if (uri.StartsWith(uriPrefix, StringComparison.Ordinal)) return uri[uriPrefix.Length..];
        const string urlMarker = "/playlist/";
        var start = uri.IndexOf(urlMarker, StringComparison.Ordinal);
        if (start < 0) return null;
        var id = uri[(start + urlMarker.Length)..];
        var suffix = id.IndexOfAny(['?', '&']);
        return suffix < 0 ? id : id[..suffix];
    }
}
