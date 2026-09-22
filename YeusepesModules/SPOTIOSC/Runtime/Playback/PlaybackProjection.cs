using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class PlaybackProjection
{
    private readonly SpotifyRequestContext _context;
    private readonly SpotiOscOutput _output;
    private string _trackUri = string.Empty;
    private string _contextUri = string.Empty;
    private bool _trackActive;
    private bool _contextActive;
    private string? _state;

    public PlaybackProjection(SpotifyRequestContext context, SpotiOscOutput output)
    {
        _context = context;
        _output = output;
    }

    public void Update()
    {
        UpdateEndpoint("CurrentSong", ref _trackUri, ref _trackActive, _context.TrackUri);
        UpdateEndpoint("CurrentPlaylist", ref _contextUri, ref _contextActive, _context.ContextUri);
    }

    public void UpdateState()
    {
        var state = _context.IsPlaying
            ? $"Playing_{(_context.IsInJam ? "Jam_" : "")}{(_context.IsExplicit ? "Explicit" : "Clean")}_{(_context.ShuffleState ? "Shuffle" : "NoShuffle")}"
            : _context.IsInJam ? "Paused_Jam" : "Paused_Normal";
        if (_state == state) return;

        _state = state;
        _output.ChangeState(state);
    }

    public void Reset()
    {
        Deactivate("CurrentSong", _trackUri, ref _trackActive);
        Deactivate("CurrentPlaylist", _contextUri, ref _contextActive);
        _trackUri = string.Empty;
        _contextUri = string.Empty;
        _state = null;
    }

    private void UpdateEndpoint(string endpoint, ref string trackedUri, ref bool active, string? currentUri)
    {
        currentUri ??= string.Empty;
        if (!string.Equals(trackedUri, currentUri, StringComparison.Ordinal))
        {
            Deactivate(endpoint, trackedUri, ref active);
            trackedUri = currentUri;
        }

        var shouldBeActive = _context.IsPlaying && currentUri.Length > 0;
        if (shouldBeActive == active || currentUri.Length == 0) return;

        _output.Send($"SpotiOSC/{endpoint}/{currentUri}", shouldBeActive);
        active = shouldBeActive;
    }

    private void Deactivate(string endpoint, string uri, ref bool active)
    {
        if (active && uri.Length > 0)
            _output.Send($"SpotiOSC/{endpoint}/{uri}", false);
        active = false;
    }
}
