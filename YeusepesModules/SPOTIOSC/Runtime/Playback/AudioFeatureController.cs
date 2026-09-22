using SpotifyAPI.Web;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Runtime.Spotify;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class AudioFeatureController
{
    private const string TrackPrefix = "spotify:track:";
    private readonly SpotifyRequestContext _context;
    private readonly SpotifyApiService _api;
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;
    private string? _requestedTrackId;

    public AudioFeatureController(
        SpotifyRequestContext context,
        SpotifyApiService api,
        SpotiOscOutput output,
        Action<string> logDebug)
    {
        _context = context;
        _api = api;
        _output = output;
        _logDebug = logDebug;
    }

    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) _requestedTrackId = null;
        _logDebug($"GetTrackFeatures set to: {enabled}");
        if (enabled && _context.TrackUri?.StartsWith(TrackPrefix, StringComparison.Ordinal) == true)
            Request(_context.TrackUri[TrackPrefix.Length..]);
    }

    public void FetchIfEnabled(JsonElement item)
    {
        if (!Enabled) return;
        if (item.TryGetProperty("id", out var id) && !string.IsNullOrEmpty(id.GetString()))
            Request(id.GetString()!);
        else
            _logDebug("No track ID found; audio features were not fetched");
    }

    private void Request(string trackId)
    {
        if (_requestedTrackId == trackId) return;
        _requestedTrackId = trackId;
        _ = FetchAsync(trackId);
    }

    private async Task FetchAsync(string trackId)
    {
        try
        {
            var features = await _api.GetTrackFeaturesAsync(trackId);
            if (features is null)
            {
                if (_requestedTrackId == trackId) _requestedTrackId = null;
                return;
            }
            if (!Enabled || _requestedTrackId != trackId) return;
            Apply(features);
            _logDebug($"Audio features updated for track: {_context.TrackName}");
        }
        catch (Exception exception)
        {
            if (_requestedTrackId == trackId) _requestedTrackId = null;
            _logDebug($"Audio feature request failed: {exception.Message}");
        }
    }

    private void Apply(TrackAudioFeatures value)
    {
        _context.Danceability = value.Danceability;
        _context.Energy = value.Energy;
        _context.Key = value.Key;
        _context.Loudness = value.Loudness;
        _context.Mode = value.Mode;
        _context.Speechiness = value.Speechiness;
        _context.Acousticness = value.Acousticness;
        _context.Instrumentalness = value.Instrumentalness;
        _context.Liveness = value.Liveness;
        _context.Valence = value.Valence;
        _context.Tempo = value.Tempo;
        _context.TimeSignature = value.TimeSignature;

        _output.Set(SpotiOSC.SpotiParameters.Danceability, value.Danceability);
        _output.Set(SpotiOSC.SpotiParameters.Energy, value.Energy);
        _output.Set(SpotiOSC.SpotiParameters.Key, value.Key);
        _output.Set(SpotiOSC.SpotiParameters.Loudness, value.Loudness);
        _output.Set(SpotiOSC.SpotiParameters.Mode, value.Mode);
        _output.Set(SpotiOSC.SpotiParameters.Speechiness, value.Speechiness);
        _output.Set(SpotiOSC.SpotiParameters.Acousticness, value.Acousticness);
        _output.Set(SpotiOSC.SpotiParameters.Instrumentalness, value.Instrumentalness);
        _output.Set(SpotiOSC.SpotiParameters.Liveness, value.Liveness);
        _output.Set(SpotiOSC.SpotiParameters.Valence, value.Valence);
        _output.Set(SpotiOSC.SpotiParameters.Tempo, value.Tempo);
        _output.Set(SpotiOSC.SpotiParameters.TimeSignature, value.TimeSignature);
    }
}
