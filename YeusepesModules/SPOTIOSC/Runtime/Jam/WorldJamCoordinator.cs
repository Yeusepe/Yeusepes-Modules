using YeusepesModules.SPOTIOSC.Utils.Requests;
using P = YeusepesModules.SPOTIOSC.SpotiOSC.SpotiParameters;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed class WorldJamCoordinator : IAsyncDisposable
{
    private readonly SpotifyRequestContext _context;
    private readonly SpotifyJamService _jam;
    private readonly JamPairingClient _client;
    private readonly SpotiOscOutput _output;
    private readonly Func<string, Func<bool>, CancellationToken, Task<bool>> _join;
    private readonly Func<string?> _location;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private readonly JamFragmentDecoder _decoder = new();
    private readonly HashSet<string> _dismissed = new();
    private readonly Dictionary<string, int> _guestBudgets = new();
    private readonly int[,] _received = new int[4, 3];
    private readonly long[] _receivedAt = new long[4];
    private readonly int[] _profiles = new int[4];
    private readonly List<Task> _retiring = new();
    private Task? _task, _serviceTask;
    private JamPairingHost? _host;
    private JamPairingGuest? _guest;
    private JamBootstrap? _candidate;
    private string? _room, _hostSession;
    private bool _hosting, _joining, _retry, _manualSharing;
    private readonly float[] _localControls = { float.NaN, float.NaN };
    private int _version, _visit, _guestVisit, _lastPrompt = -1, _bits = 2, _symbol, _fragment, _cycle, _sendLane;
    private int[]? _frame;
    private long _candidateSeen, _nextSymbol, _serviceAt, _joinedAt, _controlsUntil;
    private CancellationTokenSource? _joinCancellation;
    private TaskCompletionSource? _joinCompletion;
    private const int CandidateLifetime = 90_000;
    private static long Now => Environment.TickCount64;

    public WorldJamCoordinator(SpotifyRequestContext context, SpotifyJamService jam, JamPairingClient client,
        SpotiOscOutput output, Func<string, Func<bool>, CancellationToken, Task<bool>> join, Action<string> log, Func<string?>? location = null)
    { _context = context; _jam = jam; _client = client; _output = output; _join = join; _log = log; _location = location ?? new VrchatInstanceTracker().CurrentRoomKey; }
    public void Start() => _task = RunAsync();
    public bool WantsToHost { get { lock (_sync) return _hosting; } }
    public bool CanHelp { get { lock (_sync) return _guest is not null && _guestVisit == _visit; } }
    public void SetBandwidth(int bits) { lock (_sync) { int value = bits is 5 or 6 or 10 ? bits : 2; if (_bits != value) { _bits = value; StopSending(); } } }
    public void SetLocalControl(int index, float value)
    {
        if (index is < 0 or > 1 || !float.IsFinite(value)) return;
        lock (_sync) { if (_localControls[index] == value) return; _localControls[index] = value; PreemptControls(); }
    }
    public void PreemptControls() { lock (_sync) { _controlsUntil = Now + 1200; StopSending(); } }
    public void SetManualSharing(bool active) { lock (_sync) { _manualSharing = active; PreemptControls(); } }
    public void SetHosting(bool value) { lock (_sync) { if (_hosting != value) { _hosting = value; _version++; _serviceAt = 0; if (!value) { RetireHost(); StopSending(); } } } }
    private void RetireHost() { if (_host is not null) { _retiring.Add(_host.DisposeAsync().AsTask()); _host = null; _hostSession = null; } }
    public void Receive(float proximity) => Receive(0, 0, proximity);
    public void Receive(int lane, int part, float proximity)
    {
        if (lane is < 0 or > 3 || part is < 0 or > 2) return;
        lock (_sync) {
            float scaled = proximity * (part == 0 ? 128 : 32);
            int value = (int)MathF.Round(scaled);
            if (!float.IsFinite(proximity) || proximity <= 0) value = 0;
            else if (Math.Abs(scaled - value) > .2f || value < 1 || value > (part == 0 ? 68 : 16)) value = -1;
            if (_received[lane, part] != value) { _received[lane, part] = value; _receivedAt[lane] = Now; }
        }
    }
    public void Dismiss() { lock (_sync) { if (_candidate is not null) _dismissed.Add(_candidate.LocalId); _joinCancellation?.Cancel(); _retry = false; PublishPrompt(); } }
    public void RefreshAvatar() { lock (_sync) { _lastPrompt = -1; _bits = 2; ClearCandidate(); StopSending(); _output.Set(P.WorldJamHosting, _hosting); PublishPrompt(); } }
    private void ClearCandidate() { _candidate = null; _decoder.Reset(); Array.Clear(_received); Array.Clear(_profiles); _version++; _joinCancellation?.Cancel(); }
    private void StopSending() { _frame = null; _symbol = 0; _nextSymbol = 0; _output.Set(P.WorldJamTransmit, 0); }

    public async Task JoinAsync()
    {
        JamBootstrap candidate; int version, visit; bool helper; CancellationTokenSource cancellation;
        lock (_sync) {
            if (_joining || _context.IsInJam || _candidate is null || Now - _candidateSeen > CandidateLifetime) return;
            candidate = _candidate; version = _version; visit = _visit; helper = _hosting && _bits != 2;
            int used = _guestBudgets.GetValueOrDefault(candidate.LocalId);
            if (used >= 3) { _retry = true; PublishPrompt(); return; }
            if (_guestBudgets.Count >= 64 && !_guestBudgets.ContainsKey(candidate.LocalId)) return;
            _guestBudgets[candidate.LocalId] = used + 1; _dismissed.Remove(candidate.LocalId);
            cancellation = _joinCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            cancellation.CancelAfter(TimeSpan.FromSeconds(40)); _joinCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _joining = true; _retry = false; PublishPrompt();
        }
        bool Current() { lock (_sync) return _version == version && _candidate?.LocalId == candidate.LocalId && !_dismissed.Contains(candidate.LocalId) && Now - _candidateSeen <= CandidateLifetime; }
        JamPairingGuest? guest = null;
        try {
            // 候选缓存完全在本机；只有明确点击 Join 才联网进行 PAKE。
            guest = await _client.Join(candidate, helper, cancellation.Token);
            if (!Current()) return;
            bool joined = await _join(guest.Session, Current, cancellation.Token);
            lock (_sync) if (Current()) {
                _retry = !joined;
                if (joined) { _guest = guest; _guestVisit = visit; guest = null; _dismissed.Add(candidate.LocalId); _joinedAt = Now; _serviceAt = 0; }
            }
        } catch (OperationCanceledException) { lock (_sync) _retry = Current() && !_lifetime.IsCancellationRequested; }
        catch (Exception exception) { _log($"World jam pairing failed: {exception.GetType().Name}"); lock (_sync) _retry = Current(); }
        finally { guest?.Dispose(); lock (_sync) { _joining = false; _joinCancellation = null; cancellation.Dispose(); PublishPrompt(); _joinCompletion?.TrySetResult(); } }
    }
    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested) {
            try {
                string? room = _location();
                lock (_sync) {
                    if (_room != room) {
                        bool leaving = _room is not null; _room = room; _visit++;
                        ClearCandidate(); _dismissed.Clear(); _guestBudgets.Clear(); _retry = false; _joinedAt = 0; _manualSharing = false;
                        if (leaving) { _hosting = false; _output.Set(P.WorldJamHosting, false); }
                        RetireHost();
                        StopSending(); _serviceAt = 0;
                    }
                    if (_host is not null && (!_context.IsInJam || !_context.IsJamOwner || _hostSession != (_jam.State.JoinToken ?? _jam.State.SessionId))) { RetireHost(); StopSending(); _serviceAt = 0; }
                    ReadContact();
                    if ((_serviceTask is null || _serviceTask.IsCompleted) && Now >= _serviceAt) {
                        _serviceAt = Now + 10_000;
                        _serviceTask = ServiceAsync(_visit);
                    }
                    SendContact(); PublishPrompt(); _retiring.RemoveAll(task => task.IsCompleted);
                }
            } catch (Exception exception) { _log($"World jam update failed: {exception.GetType().Name}"); }
            try { await Task.Delay(20, _lifetime.Token); } catch (OperationCanceledException) { break; }
        }
    }
    private async Task ServiceAsync(int visit)
    {
        // 与 20ms 接收循环分开：长轮询、Spotify 或断网重试不能阻塞解码。
        await Task.Yield();
        JamPairingGuest? renewing = null;
        try {
            string? session; JamPairingHost? retire; JamPairingGuest? guest;
            lock (_sync) {
                session = _hosting && _context.IsInJam && _context.IsJamOwner && _room is not null ? _jam.State.JoinToken ?? _jam.State.SessionId : null;
                retire = _host is not null && (_hostSession != session || !_host.Live) ? _host : null;
                if (retire is not null) { _host = null; _hostSession = null; StopSending(); }
                guest = _guest;
                if (guest is not null && (!_context.IsInJam || _room is null || _guestVisit != _visit || _context.IsJamOwner)) { _guest = null; guest.Dispose(); guest = null; StopSending(); }
            }
            if (retire is not null) await retire.DisposeAsync();
            if (session is not null && _host is null) {
                var created = await _client.Host(session, _lifetime.Token);
                bool stale;
                lock (_sync) { stale = visit != _visit || !_hosting || !_context.IsJamOwner || !_context.IsInJam; if (!stale) { _host = created; _hostSession = session; _fragment = 0; _cycle = 0; } }
                if (stale) await created.DisposeAsync();
            }
            if (guest is not null) {
                bool helper; lock (_sync) helper = _hosting && _bits != 2;
                renewing = guest;
                await guest.Renew(helper, _lifetime.Token);
                lock (_sync) if (visit != _visit) { if (_guest == guest) _guest = null; guest.Dispose(); StopSending(); }
            }
        } catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (System.Net.Http.HttpRequestException exception) when (renewing is not null && exception.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) {
            lock (_sync) if (ReferenceEquals(_guest, renewing)) { _guest = null; renewing.Dispose(); _hosting = false; _output.Set(P.WorldJamHosting, false); StopSending(); }
        }
        catch (Exception exception) { _log($"World jam lease unavailable: {exception.GetType().Name}"); }
    }
    private void ReadContact()
    {
        for (int lane = 0; lane < 4; lane++) {
            if (Now - _receivedAt[lane] < 35) continue;
            int main = _received[lane, 0]; bool start = main >= 65;
            if (start) _profiles[lane] = main == 65 ? 5 : main == 66 ? 2 : main == 67 ? 6 : 10;
            int symbol = main, bits = _profiles[lane];
            if (!start && bits != 5 && main is >= 1 and <= 8) {
                int value = main - 1, payload = value & 3;
                if (bits >= 6) { if (_received[lane, 1] <= 0) continue; payload |= (_received[lane, 1] - 1) << 2; }
                if (bits == 10) { if (_received[lane, 2] <= 0) continue; payload |= (_received[lane, 2] - 1) << 6; }
                symbol = 1 + payload + ((value >> 2) << bits);
            }
            var candidate = _decoder.Accept(lane, symbol, Now, start);
            if (candidate is not null && _host?.Bootstrap.LocalId != candidate.LocalId) {
                if (_candidate?.LocalId != candidate.LocalId) { _candidate = candidate; _retry = false; _version++; _joinCancellation?.Cancel(); }
                _candidateSeen = Now;
            }
        }
        if (_candidate is not null && Now - _candidateSeen > CandidateLifetime) { _candidate = null; _joinCancellation?.Cancel(); _version++; }
    }
    private void SendContact()
    {
        if (_manualSharing || Now < _controlsUntil) { if (_frame is not null) StopSending(); return; }
        var bootstrap = _hosting && _context.IsInJam && _context.IsJamOwner && _host?.Live == true ? _host.Bootstrap :
            _hosting && _guestVisit == _visit && _context.IsInJam && _guest?.Lane > 0 && Now < _guest.LeaseUntil ? _guest.Bootstrap : null;
        int lane = _host?.Live == true ? 0 : _guest?.Lane ?? 0;
        if (bootstrap is null || _room is null) { if (_frame is not null) StopSending(); return; }
        if (_sendLane != lane) { _sendLane = lane; _fragment = lane; StopSending(); }
        if (Now < _nextSymbol) return;
        if (_frame is null) { _frame = JamFragmentProtocol.Encode(bootstrap, _fragment % 5, _bits); _symbol = 0; }
        if (_symbol == _frame.Length) {
            StopSending(); if (++_fragment % 5 == 0) _cycle++;
            // 非整秒窗口避免固定网络采样相位一直错过控制更新。
            _nextSymbol = Now + Random.Shared.Next(650, 950); return;
        }
        int value = _frame[_symbol], main, low = 0, high = 0;
        if (_symbol == 0) { main = _bits == 5 ? 65 + lane : 66; low = lane; }
        else if (_bits == 5) main = value;
        else { int packed = value - 1; main = 1 + (packed & 3) + ((packed >> _bits) << 2); low = (packed >> 2) & 15; high = (packed >> 6) & 15; }
        _output.Set(P.WorldJamTransmitLow, low); _output.Set(P.WorldJamTransmitHigh, high); _output.Set(P.WorldJamTransmit, main);
        _symbol++;
        // 快速首轮，随后中速与 1Hz 修复轮；抖动避免与网络采样锁相。
        int duration = _cycle % 4 == 3 ? JamFragmentProtocol.SlowMilliseconds : _cycle % 4 == 2 ? JamFragmentProtocol.MediumMilliseconds : JamFragmentProtocol.FastMilliseconds;
        _nextSymbol = Now + duration + Random.Shared.Next(0, 31);
    }
    private void PublishPrompt()
    {
        int state = _joining ? 2 : _joinedAt != 0 && Now - _joinedAt < 2000 ? 4 : _context.IsInJam || _candidate is null || _dismissed.Contains(_candidate.LocalId) ? 0 : _retry ? 3 : 1;
        if (state != _lastPrompt) { _lastPrompt = state; _output.Set(P.WorldJamPrompt, state); }
    }
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel(); if (_task is not null) await _task; if (_serviceTask is not null) await _serviceTask;
        if (_joinCompletion is not null) await _joinCompletion.Task;
        StopSending(); _output.Set(P.WorldJamPrompt, 0); _output.Set(P.WorldJamHosting, false);
        if (_host is not null) await _host.DisposeAsync(); _guest?.Dispose(); await Task.WhenAll(_retiring);
        _client.Dispose(); _lifetime.Dispose();
    }
}
