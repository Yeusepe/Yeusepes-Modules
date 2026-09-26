using System.Diagnostics;
using System.Net.Http;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed class SyncopationCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ContactSettleTime = TimeSpan.FromMilliseconds(200);
    private readonly SpotifyRequestContext _context;
    private readonly SpotifyJamService _jam;
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;
    private readonly SyncopationClient _client;
    private readonly Dictionary<SpotiOSC.SpotiParameters, bool> _receiverValues = [];
    private readonly Dictionary<SpotiOSC.SpotiParameters, bool> _senderValues = [];
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _jamGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private (string Word1, string Word2)? _broadcast;
    private Task? _updateTask;
    private bool _touching;
    private int _broadcastVersion;
    private long _receiverChangedAt;
    private long _lastCodeAttempt;
    private string? _resolvedCode;
    private string? _resolvedSession;
    private long _lastJoinAttempt;
    internal WorldJamCoordinator World { get; }

    public SyncopationCoordinator(
        SpotifyRequestContext context,
        SpotifyJamService jam,
        SpotiOscOutput output,
        HttpClient httpClient,
        Action<string> logDebug,
        string serverUrl,
        Func<string?>? worldLocation = null,
        HttpClient? invitationTestClient = null)
    {
        _context = context;
        _jam = jam;
        _output = output;
        _logDebug = logDebug;
        _client = new SyncopationClient(httpClient, serverUrl, logDebug);
        _client.JamJoined += ClearBroadcast;
        World = new WorldJamCoordinator(context, jam, new JamPairingClient(serverUrl, invitationTestClient), output, JoinWorldSessionAsync, logDebug, worldLocation);
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

    public async Task StartAsync()
    {
        await _client.StartAsync();
        World.Start();
        _updateTask = RunInteractionsAsync();
    }

    public bool TryHandleParameter(SpotiOSC.SpotiParameters parameter, bool value)
    {
        var binding = WordBindings.FirstOrDefault(item => item.Sender == parameter || item.Receiver == parameter);
        if (binding is null) return false;
        if (binding.Receiver != parameter) {
            bool changed;
            lock (_stateLock) { changed = _senderValues.GetValueOrDefault(parameter) != value; _senderValues[parameter] = value; }
            if (changed) World.PreemptControls();
            return true;
        }

        lock (_stateLock)
        {
            if (_receiverValues[parameter] != value)
            {
                _receiverValues[parameter] = value;
                _receiverChangedAt = Stopwatch.GetTimestamp();
                _resolvedCode = null;
                _resolvedSession = null;
            }
        }
        return true;
    }

    public Task SetTouchingAsync(bool touching)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = _touching != touching;
            if (_touching != touching)
            {
                _touching = touching;
                _broadcastVersion++;
            }
            if (!touching) ClearBroadcast();
        }
        if (changed) World.SetManualSharing(touching);
        return Task.CompletedTask;
    }

    public async Task SetWantJamAsync(bool wantJam)
    {
        await _jamGate.WaitAsync(_lifetime.Token);
        try
        {
            if (_context.IsInJam == wantJam) return;
            _output.Set(SpotiOSC.SpotiParameters.Error, false);
            if (wantJam)
            {
                if (!await _jam.CreateAsync()) await _output.PulseErrorAsync();
            }
            else if (await _jam.LeaveAsync())
            {
                await SetTouchingAsync(false);
                ClearReceivers();
            }
        }
        finally { _jamGate.Release(); }
    }

    public async Task SetWorldHostingAsync(bool hosting)
    {
        World.SetHosting(hosting);
        if (hosting && !_context.IsInJam) await SetWantJamAsync(true);
        if (hosting && World.WantsToHost && (!_context.IsInJam || (!_context.IsJamOwner && !World.CanHelp)))
        {
            World.SetHosting(false);
            _output.Set(SpotiOSC.SpotiParameters.WorldJamHosting, false);
            _logDebug("Sharing requires the jam host or a current authenticated helper session.");
            await _output.PulseErrorAsync();
        }
    }

    private async Task<bool> JoinWorldSessionAsync(string session, Func<bool> stillCurrent, CancellationToken cancellationToken)
    {
        await _jamGate.WaitAsync(cancellationToken);
        try
        {
            if (_context.IsInJam || !stillCurrent()) return false;
            bool joined = await _jam.JoinAsync(session, cancellationToken);
            if (joined) { ClearReceivers(); _output.Set(SpotiOSC.SpotiParameters.Error, false); }
            return joined;
        }
        finally { _jamGate.Release(); }
    }

    private async Task RunInteractionsAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, _lifetime.Token);
                if (_context.IsInJam) await UpdateBroadcastAsync();
                else
                {
                    ClearBroadcast();
                    await TryJoinFromReceiversAsync();
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                _logDebug($"Syncopation interaction failed: {exception.GetType().Name}");
                await _output.PulseErrorAsync();
            }
        }
    }

    private async Task UpdateBroadcastAsync()
    {
        int version;
        string? session;
        lock (_stateLock)
        {
            session = _jam.State.JoinToken;
            if (!_touching || _broadcast.HasValue || string.IsNullOrEmpty(session) ||
                (_lastCodeAttempt != 0 && Stopwatch.GetElapsedTime(_lastCodeAttempt) < RetryDelay)) return;
            version = _broadcastVersion;
            _lastCodeAttempt = Stopwatch.GetTimestamp();
        }

        var words = await _client.CreateCodeAsync(session);
        lock (_stateLock)
        {
            // A released contact or a different session must never publish a late response.
            if (!words.HasValue || !_touching || !_context.IsInJam ||
                version != _broadcastVersion || session != _jam.State.JoinToken) return;
            _broadcast = words;
            SetWord(words.Value.Word1, true);
            SetWord(words.Value.Word2, true);
            _lastCodeAttempt = 0;
        }
    }

    private async Task TryJoinFromReceiversAsync()
    {
        string code;
        string? session;
        long changedAt;
        lock (_stateLock)
        {
            var words = WordBindings
                .Where(binding => _receiverValues.GetValueOrDefault(binding.Receiver))
                .Select(binding => binding.Word)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (words.Length != 2 || Stopwatch.GetElapsedTime(_receiverChangedAt) < ContactSettleTime ||
                (_lastJoinAttempt != 0 && Stopwatch.GetElapsedTime(_lastJoinAttempt) < RetryDelay)) return;
            code = $"{words[0]}_{words[1]}";
            changedAt = _receiverChangedAt;
            session = _resolvedCode == code ? _resolvedSession : null;
            _lastJoinAttempt = Stopwatch.GetTimestamp();
        }

        session ??= await _client.ResolveSessionAsync(code);
        lock (_stateLock)
        {
            if (_receiverChangedAt != changedAt) return;
            // The relay consumes a code on lookup. Retrying Spotify must reuse that result.
            _resolvedCode = code;
            _resolvedSession = session;
        }
        await _jamGate.WaitAsync(_lifetime.Token);
        try
        {
            lock (_stateLock)
                if (_context.IsInJam || _receiverChangedAt != changedAt) return;
            if (string.IsNullOrEmpty(session) || !await _jam.JoinAsync(session))
            {
                await _output.PulseErrorAsync();
                return;
            }
            _output.Set(SpotiOSC.SpotiParameters.Error, false);
            ClearReceivers();
        }
        finally { _jamGate.Release(); }
    }

    private void ClearReceivers()
    {
        lock (_stateLock)
        {
            _resolvedCode = null;
            _resolvedSession = null;
            _receiverChangedAt = Stopwatch.GetTimestamp();
            foreach (var binding in WordBindings)
            {
                _receiverValues[binding.Receiver] = false;
                _output.Set(binding.Receiver, false);
            }
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
        lock (_stateLock)
        {
            if (_broadcast is not { } words) return;
            SetWord(words.Word1, false);
            SetWord(words.Word2, false);
            _broadcast = null;
            _broadcastVersion++;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (_updateTask is not null)
        {
            try
            {
                await _updateTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _client.JamJoined -= ClearBroadcast;
        await World.DisposeAsync();
        await _client.DisposeAsync();
        ClearBroadcast();
        _jamGate.Dispose();
        _lifetime.Dispose();
    }
}

internal sealed record EphemeralWordBinding(
    string Word,
    SpotiOSC.SpotiParameters Sender,
    SpotiOSC.SpotiParameters Receiver);
