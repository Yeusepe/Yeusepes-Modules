using System.Diagnostics;
using System.Net.Http;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed class SyncopationCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan JoinCooldown = TimeSpan.FromSeconds(10);
    private readonly SpotifyRequestContext _context;
    private readonly SpotifyJamService _jam;
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;
    private readonly SyncopationClient _client;
    private readonly Dictionary<SpotiOSC.SpotiParameters, bool> _receiverValues = [];
    private readonly SemaphoreSlim _joinGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private (string Word1, string Word2)? _broadcast;
    private Task? _joinTask;
    private long _lastJoinAttempt;

    public SyncopationCoordinator(
        SpotifyRequestContext context,
        SpotifyJamService jam,
        SpotiOscOutput output,
        HttpClient httpClient,
        Action<string> logDebug,
        string serverUrl)
    {
        _context = context;
        _jam = jam;
        _output = output;
        _logDebug = logDebug;
        _client = new SyncopationClient(httpClient, serverUrl, logDebug);
        _client.JamJoined += ClearBroadcast;
        foreach (var binding in WordBindings) _receiverValues[binding.Receiver] = false;
    }

    public static IReadOnlyList<EphemeralWordBinding> WordBindings { get; } =
    [
        new("allegro", SpotiOSC.SpotiParameters.Allegro, SpotiOSC.SpotiParameters.AllegroReceiver),
        new("cadence", SpotiOSC.SpotiParameters.Cadence, SpotiOSC.SpotiParameters.CadenceReceiver),
        new("groove", SpotiOSC.SpotiParameters.Groove, SpotiOSC.SpotiParameters.GrooveReceiver),
        new("ritmo", SpotiOSC.SpotiParameters.Ritmo, SpotiOSC.SpotiParameters.RitmoReceiver),
        new("metronome", SpotiOSC.SpotiParameters.Metronome, SpotiOSC.SpotiParameters.MetronomeReceiver),
        new("encore", SpotiOSC.SpotiParameters.Encore, SpotiOSC.SpotiParameters.EncoreReceiver),
        new("chorus", SpotiOSC.SpotiParameters.Chorus, SpotiOSC.SpotiParameters.ChorusReceiver)
    ];

    public Task StartAsync() => _client.StartAsync();

    public bool TryHandleParameter(SpotiOSC.SpotiParameters parameter, bool value)
    {
        var binding = WordBindings.FirstOrDefault(item => item.Sender == parameter || item.Receiver == parameter);
        if (binding is null) return false;
        if (binding.Receiver != parameter) return true;

        _receiverValues[parameter] = value;
        if (_joinTask is null || _joinTask.IsCompleted) _joinTask = TryJoinFromReceiversAsync();
        return true;
    }

    public async Task SetTouchingAsync(bool touching)
    {
        if (!touching)
        {
            ClearBroadcast();
            return;
        }
        if (!_context.IsInJam || string.IsNullOrEmpty(_jam.State.JoinToken) || _broadcast.HasValue) return;

        var words = await _client.CreateCodeAsync(_jam.State.JoinToken);
        if (!words.HasValue) return;
        _broadcast = words;
        SetWord(words.Value.Word1, true);
        SetWord(words.Value.Word2, true);
    }

    public async Task SetWantJamAsync(bool wantJam)
    {
        if (_context.IsInJam == wantJam) return;
        if (wantJam)
        {
            await _jam.CreateAsync();
            return;
        }
        await _jam.LeaveAsync();
        ClearBroadcast();
    }

    private async Task TryJoinFromReceiversAsync()
    {
        if (_context.IsInJam || !_joinGate.Wait(0)) return;
        try
        {
            await Task.Delay(50, _lifetime.Token);
            var words = WordBindings
                .Where(binding => _receiverValues.GetValueOrDefault(binding.Receiver))
                .Select(binding => binding.Word)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (words.Length != 2 ||
                (_lastJoinAttempt != 0 && Stopwatch.GetElapsedTime(_lastJoinAttempt) < JoinCooldown))
                return;

            _lastJoinAttempt = Stopwatch.GetTimestamp();
            var sessionId = await _client.ResolveSessionAsync($"{words[0]}_{words[1]}");
            if (string.IsNullOrEmpty(sessionId) || !await _jam.JoinAsync(sessionId))
            {
                await _output.PulseErrorAsync();
                return;
            }
            ClearReceivers();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logDebug($"Syncopation receiver join failed: {exception.Message}");
        }
        finally
        {
            _joinGate.Release();
        }
    }

    private void ClearReceivers()
    {
        foreach (var binding in WordBindings)
        {
            _receiverValues[binding.Receiver] = false;
            _output.Set(binding.Receiver, false);
        }
    }

    private void SetWord(string word, bool value)
    {
        var binding = WordBindings.FirstOrDefault(item =>
            item.Word.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (binding is not null) _output.Set(binding.Sender, value);
    }

    private void ClearBroadcast()
    {
        if (_broadcast is not { } words) return;
        SetWord(words.Word1, false);
        SetWord(words.Word2, false);
        _broadcast = null;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_joinTask is not null)
        {
            try
            {
                await _joinTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _client.JamJoined -= ClearBroadcast;
        await _client.DisposeAsync();
        ClearBroadcast();
        _joinGate.Dispose();
        _lifetime.Dispose();
    }
}

internal sealed record EphemeralWordBinding(
    string Word,
    SpotiOSC.SpotiParameters Sender,
    SpotiOSC.SpotiParameters Receiver);
