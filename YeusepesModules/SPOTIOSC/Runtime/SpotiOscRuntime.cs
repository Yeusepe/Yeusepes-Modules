using System.Net.Http;
using VRCOSC.App.SDK.Parameters;
using YeusepesModules.SPOTIOSC.Runtime.Events;
using YeusepesModules.SPOTIOSC.Runtime.Jam;
using YeusepesModules.SPOTIOSC.Runtime.Playback;
using YeusepesModules.SPOTIOSC.Runtime.Spotify;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime;

internal sealed class SpotiOscRuntime : IAsyncDisposable
{
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;
    private readonly SpotifyDealerClient _dealer;
    private readonly DealerEventRouter _events;
    private readonly PlaybackClock _clock;
    private readonly PlaybackProjection _projection;
    private readonly TrackMetadataEnricher _metadata;
    private readonly AlbumArtworkColorService _artworkColors;
    private readonly PlaybackCommandController _commands;
    private readonly SpotifyJamService _jam;
    private readonly JamEventHandler _jamEvents;
    private readonly SyncopationCoordinator _syncopation;
    private bool _disposed;

    private SpotiOscRuntime(
        HttpClient httpClient,
        SpotifyRequestContext context,
        SpotifyUtilities utilities,
        SpotifyApiService api,
        SpotiOscOutput output,
        Action<string> logDebug,
        string melodyServerUrl)
    {
        _output = output;
        _logDebug = logDebug;
        var features = new AudioFeatureController(context, api, output, logDebug);
        _projection = new PlaybackProjection(context, output, features);
        _clock = new PlaybackClock(output, logDebug);
        _metadata = new TrackMetadataEnricher(context, _projection, logDebug);
        _artworkColors = new AlbumArtworkColorService(context, output, logDebug);
        _jam = new SpotifyJamService(context, utilities, output);
        _jamEvents = new JamEventHandler(context, _jam, output, logDebug);
        _syncopation = new SyncopationCoordinator(
            context,
            _jam,
            output,
            httpClient,
            logDebug,
            melodyServerUrl);
        _commands = new PlaybackCommandController(
            context,
            api,
            new SpotifyConnectCommandClient(context, logDebug),
            features,
            output,
            logDebug);
        _events = new DealerEventRouter(
            new SpotifyPlaybackEventHandler(
                context,
                output,
                _clock,
                _projection),
            new SpotifyClusterEventHandler(
                context,
                output,
                _clock,
                _projection,
                _metadata,
                logDebug),
            _jamEvents,
            new SpotifyConnectEventHandler(context, output, logDebug),
            _projection,
            logDebug);
        _dealer = new SpotifyDealerClient(context, logDebug);
        _dealer.MessageReceived += _events.Handle;
    }

    public JamSessionState JamState => _jam.State;

    public static async Task<SpotiOscRuntime> CreateAsync(
        HttpClient httpClient,
        SpotifyRequestContext context,
        SpotifyUtilities utilities,
        SpotiOscOutput output,
        Action<string> logDebug,
        string melodyServerUrl)
    {
        var api = new SpotifyApiService(httpClient);
        await api.InitializeAsync();
        return new SpotiOscRuntime(
            httpClient,
            context,
            utilities,
            api,
            output,
            logDebug,
            melodyServerUrl);
    }

    public async Task StartAsync()
    {
        await _jamEvents.LoadAsync();
        await _dealer.StartAsync();
        _output.Set(SpotiOSC.SpotiParameters.Enabled, true);
        _output.Set(SpotiOSC.SpotiParameters.Error, false);
        _output.Set(SpotiOSC.SpotiParameters.GetTrackFeatures, false);
        await _syncopation.StartAsync();
        _clock.Start();
    }

    public bool IsEphemeral(SpotiOSC.SpotiParameters parameter) =>
        SyncopationCoordinator.WordBindings.Any(binding =>
            binding.Sender == parameter || binding.Receiver == parameter);

    public void HandleEphemeral(SpotiOSC.SpotiParameters parameter, bool value) =>
        _syncopation.TryHandleParameter(parameter, value);

    public Task SetWantJamAsync(bool value) => _syncopation.SetWantJamAsync(value);
    public Task SetTouchingAsync(bool value) => _syncopation.SetTouchingAsync(value);
    public Task SetWorldHostingAsync(bool value) => _syncopation.SetWorldHostingAsync(value);
    public Task JoinWorldJamAsync() => _syncopation.World.JoinAsync();
    public void ReceiveWorldJam(float proximity) => _syncopation.World.Receive(proximity);
    public void ReceiveWorldJam(int lane, int part, float proximity) => _syncopation.World.Receive(lane, part, proximity);
    public void SetWorldJamBandwidth(int bits) => _syncopation.World.SetBandwidth(bits);
    public void SetJamLocalControl(int index, float value) => _syncopation.World.SetLocalControl(index, value);
    public void DismissWorldJam() => _syncopation.World.Dismiss();
    public void RefreshWorldPrompt() => _syncopation.World.RefreshAvatar();
    public bool TryHandleCommand(RegisteredParameter parameter) => _commands.TryHandle(parameter);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _dealer.MessageReceived -= _events.Handle;
        try
        {
            await _dealer.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logDebug($"Dealer shutdown failed: {exception.Message}");
        }
        try
        {
            await _syncopation.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logDebug($"Syncopation shutdown failed: {exception.Message}");
        }

        _clock.Dispose();
        await _metadata.DisposeAsync();
        await _artworkColors.DisposeAsync();
        _projection.Reset();
        _events.Clear();
        _output.Set(SpotiOSC.SpotiParameters.Enabled, false);
    }
}
