using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed class JamPairingClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _base;
    public JamPairingClient(string url, HttpClient? client = null)
    {
        _base = new Uri(url.TrimEnd('/') + "/syncopation/v3/pairing/");
        if (_base.Scheme != "https" && !(_base.Scheme == "http" && _base.IsLoopback)) throw new ArgumentException("Pairing requires HTTPS");
        _ownsHttp = client is null;
        _http = client ?? new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    }
    internal async Task<JsonDocument> Post(string route, object body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, route)) { Content = JsonContent.Create(body) };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(route == "listen" ? 20_000 : 4096);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }
    internal async Task<byte[]> Exchange(string locator, string attempt, int round, byte[] payload, CancellationToken cancellation)
    {
        // 网络重试复用同一状态、同一报文；绝不悄悄再进行一次密码猜测。
        var body = new { locator, attempt, round, payload = Convert.ToBase64String(payload) };
        for (int retry = 0; retry < 2; retry++) {
            using var response = await Post("exchange", body, cancellation);
            if (response.RootElement.TryGetProperty("payload", out var value)) return Convert.FromBase64String(value.GetString()!);
        }
        throw new TimeoutException("Pairing response expired");
    }
    public async Task<JamPairingGuest> Join(JamBootstrap bootstrap, bool helper, CancellationToken cancellation)
    {
        string attempt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var password = bootstrap.Password;
        JamPairingKeys? keys = null;
        try {
            using var pake = new JamPairingCrypto(false, password, bootstrap.Locator, attempt);
            var first = await Exchange(bootstrap.Locator, attempt, 1, pake.Message, cancellation);
            if (first.Length != 64) throw new CryptographicException("Invalid pairing answer");
            keys = pake.Finish(first[..32]);
            if (!keys.Verify(true, first.AsSpan(32))) throw new CryptographicException("Host confirmation failed");
            var request = keys.Confirmation(false).Concat(keys.Seal(false, 2, JsonSerializer.SerializeToUtf8Bytes(new { helper }))).ToArray();
            long sentAt = Environment.TickCount64;
            var encrypted = await Exchange(bootstrap.Locator, attempt, 2, request, cancellation);
            var guest = new JamPairingGuest(this, bootstrap, attempt, keys);
            guest.Accept(encrypted, 2, true, sentAt); keys = null; return guest;
        } finally { CryptographicOperations.ZeroMemory(password); keys?.Dispose(); }
    }
    public async Task<JamPairingHost> Host(string session, CancellationToken cancellation)
    {
        for (int retry = 0; retry < 3; retry++) {
            var host = new JamPairingHost(this, session);
            try { await host.Start(cancellation); return host; }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict && retry < 2) { await host.DisposeAsync(); }
            catch { await host.DisposeAsync(); throw; }
        }
        throw new InvalidOperationException("Unable to reserve pairing mailbox");
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}

internal sealed class JamPairingGuest : IDisposable
{
    private readonly JamPairingClient _client;
    private readonly string _attempt;
    private readonly JamPairingKeys _keys;
    private int _round = 2;
    private byte[]? _pendingRequest;
    private long _pendingSentAt;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public JamBootstrap Bootstrap { get; }
    public string Session { get; private set; } = "";
    public int Lane { get; private set; } = -1;
    public long LeaseUntil { get; private set; }
    public JamPairingGuest(JamPairingClient client, JamBootstrap bootstrap, string attempt, JamPairingKeys keys)
    { _client = client; Bootstrap = bootstrap; _attempt = attempt; _keys = keys; }
    internal void Accept(byte[] envelope, int round, bool initial, long sentAt)
    {
        var plain = _keys.Open(true, round, envelope);
        try {
            using var json = JsonDocument.Parse(plain);
            int duration = json.RootElement.GetProperty("lease_ms").GetInt32();
            if (duration is < 1 or > 25_000 || sentAt + duration <= Environment.TickCount64) throw new CryptographicException("Pairing lease expired");
            int lane = json.RootElement.GetProperty("lane").GetInt32();
            if (lane is < -1 or > 3 || lane == 0) throw new CryptographicException("Invalid helper lane");
            if (initial) {
                Session = json.RootElement.GetProperty("session").GetString() ?? "";
                if (Session.Length is 0 or > 512) throw new CryptographicException("Invalid invitation");
            }
            Lane = lane;
            // 从请求发出时开始计时；网络延迟只缩短授权，不依赖两台电脑的墙上时钟一致。
            LeaseUntil = sentAt + duration;
        } finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public async Task Renew(bool helper, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try {
            int round = _round + 1;
            if (_pendingRequest is null) _pendingSentAt = Environment.TickCount64;
            byte[] request = _pendingRequest ??= _keys.Seal(false, round, JsonSerializer.SerializeToUtf8Bytes(new { helper }));
            var response = await _client.Exchange(Bootstrap.Locator, _attempt, round, request, cancellation);
            Accept(response, round, false, _pendingSentAt); _round = round; _pendingRequest = null;
        } finally { _gate.Release(); }
    }
    public void Dispose() { Lane = -1; LeaseUntil = 0; _keys.Dispose(); }
}

internal sealed class JamPairingHost : IAsyncDisposable
{
    private sealed class Attempt : IDisposable
    {
        public JamPairingKeys? Keys;
        public byte[] Request = [];
        public byte[] Response = [];
        public int Round;
        public int Lane = -1;
        public long Until;
        public long Lease;
        public void Dispose() { Keys?.Dispose(); Keys = null; }
    }
    private readonly JamPairingClient _client;
    private readonly string _owner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private readonly string _session;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, Attempt> _attempts = new();
    private Task? _pump, _renew;
    private long _liveUntil, _lastPump;
    private bool _published;
    public JamBootstrap Bootstrap { get; } = JamBootstrap.Create();
    public bool Live => !_stop.IsCancellationRequested && Environment.TickCount64 < Interlocked.Read(ref _liveUntil) && Environment.TickCount64 - Interlocked.Read(ref _lastPump) < 35_000;
    public JamPairingHost(JamPairingClient client, string session) { _client = client; _session = session; }
    internal async Task Start(CancellationToken cancellation)
    {
        JamPairingCrypto.EnsureAvailable();
        using (await _client.Post("publish", new { locator = Bootstrap.Locator, owner = _owner }, cancellation)) { }
        _published = true; Interlocked.Exchange(ref _liveUntil, Environment.TickCount64 + 55_000); Interlocked.Exchange(ref _lastPump, Environment.TickCount64);
        _pump = Pump(); _renew = Renew();
    }
    private async Task Renew()
    {
        while (!_stop.IsCancellationRequested) {
            try {
                await Task.Delay(25_000, _stop.Token);
                using (await _client.Post("publish", new { locator = Bootstrap.Locator, owner = _owner }, _stop.Token)) { }
                Interlocked.Exchange(ref _liveUntil, Environment.TickCount64 + 55_000);
            } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch { if (!Live) { _stop.Cancel(); break; } }
        }
    }
    private async Task Pump()
    {
        while (!_stop.IsCancellationRequested) {
            try {
                using var incoming = await _client.Post("listen", new { locator = Bootstrap.Locator, owner = _owner }, _stop.Token);
                Interlocked.Exchange(ref _lastPump, Environment.TickCount64);
                foreach (var item in incoming.RootElement.GetProperty("messages").EnumerateArray().Take(8)) {
                    if (!Live) break;
                    string attempt = item.GetProperty("attempt").GetString()!;
                    int round = item.GetProperty("round").GetInt32();
                    byte[] payload = Convert.FromBase64String(item.GetProperty("payload").GetString()!);
                    byte[] response;
                    try { response = Handle(attempt, round, payload); }
                    catch (Exception ex) when (ex is CryptographicException or ArgumentException or JsonException or InvalidOperationException) { response = [0]; }
                    using (await _client.Post("answer", new { locator = Bootstrap.Locator, owner = _owner, attempt, round, payload = Convert.ToBase64String(response) }, _stop.Token)) { }
                }
            } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
            catch { try { await Task.Delay(500, _stop.Token); } catch (OperationCanceledException) { break; } }
        }
    }
    internal byte[] Handle(string id, int round, byte[] payload)
    {
        long now = Environment.TickCount64;
        if (!JamPairingCrypto.Hex(id, 32) || round is < 1 or > 65535 || payload.Length > 1200) throw new CryptographicException("Invalid request");
        foreach (var old in _attempts.Values.Where(value => value.Until <= now)) old.Dispose();
        if (_attempts.TryGetValue(id, out var state)) {
            if (round == state.Round && payload.SequenceEqual(state.Request)) return state.Response;
            if (state.Keys is null || state.Until <= now || round != state.Round + 1) throw new CryptographicException("Stale attempt");
        } else {
            if (round != 1 || payload.Length != 32 || _attempts.Count >= 256) throw new CryptographicException("Pairing admission exhausted");
            // 在任何密码相关响应之前计费。失败、超时和重新连线都不能重置预算。
            state = new Attempt { Until = now + 20_000 };
            _attempts.Add(id, state);
        }
        byte[] response;
        if (round == 1) {
            var password = Bootstrap.Password;
            try {
                using var pake = new JamPairingCrypto(true, password, Bootstrap.Locator, id);
                state.Keys = pake.Finish(payload);
                response = pake.Message.Concat(state.Keys.Confirmation(true)).ToArray();
            } finally { CryptographicOperations.ZeroMemory(password); }
        } else {
            if (round == 2 && (payload.Length < 60 || !state.Keys!.Verify(false, payload.AsSpan(0, 32)))) {
                state.Dispose(); throw new CryptographicException("Guest confirmation failed");
            }
            byte[] plain = state.Keys!.Open(false, round, round == 2 ? payload[32..] : payload);
            bool helper;
            try { using var request = JsonDocument.Parse(plain); helper = request.RootElement.GetProperty("helper").GetBoolean(); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            if (!helper) { state.Lane = -1; state.Lease = 0; }
            else {
                if (state.Lease <= now) state.Lane = Enumerable.Range(1, 3)
                    .Where(lane => !_attempts.Values.Any(other => other != state && other.Lane == lane && other.Lease > now)).DefaultIfEmpty(-1).First();
                state.Lease = state.Lane > 0 ? now + 30_000 : 0;
            }
            state.Until = now + 55_000;
            var answer = JsonSerializer.SerializeToUtf8Bytes(new { session = round == 2 ? _session : "", lease_ms = 25_000, lane = state.Lane });
            try { response = state.Keys.Seal(true, round, answer); }
            finally { CryptographicOperations.ZeroMemory(answer); }
        }
        state.Round = round; state.Request = payload.ToArray(); state.Response = response; return response;
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); if (_pump is not null) await _pump; if (_renew is not null) await _renew;
        foreach (var state in _attempts.Values) state.Dispose(); _attempts.Clear();
        if (_published) {
            using var cancellation = new CancellationTokenSource(3000);
            try { using (await _client.Post("revoke", new { locator = Bootstrap.Locator, owner = _owner }, cancellation.Token)) { } } catch { }
        }
        _stop.Dispose();
    }
}
