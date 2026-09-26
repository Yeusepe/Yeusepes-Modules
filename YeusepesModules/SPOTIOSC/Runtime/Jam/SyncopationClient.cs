using System.IO;
using System.Net.Http;
using System.Net;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed class SyncopationClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Action<string> _logDebug;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _baseUrl;
    private Task? _eventsTask;
    private Task? _heartbeatTask;
    private string? _instanceId;
    private string? _instanceToken;
    private int _lastEventSequence;
    private readonly SemaphoreSlim _registrationGate = new(1, 1);
    private readonly object _eventLock = new();
    private long _lastResponseAt;
    public bool IsHealthy => IsRegistered && _lastResponseAt != 0 && Stopwatch.GetElapsedTime(_lastResponseAt) < TimeSpan.FromSeconds(90);

    public SyncopationClient(HttpClient httpClient, string serverUrl, Action<string> logDebug)
    {
        _httpClient = httpClient;
        _logDebug = logDebug;
        _baseUrl = NormalizeServerUrl(serverUrl);
    }

    public event Action? JamJoined;
    public event Action? RegistrationChanged;

    public Task<bool> StartAsync()
    {
        // 被动接收世界信标时不注册旧的触碰协议，也不建立身份或心跳。
        return Task.FromResult(!string.IsNullOrEmpty(_baseUrl) && !_baseUrl.Contains("your-melody-server"));
    }

    public async Task<(string Word1, string Word2)?> CreateCodeAsync(string sessionId)
    {
        if (!await EnsureRegisteredAsync()) return null;
        try
        {
            using var response = await SendAsync(
                HttpMethod.Post,
                "/syncopation/v1/jams",
                new { instance_id = _instanceId, session_id = sessionId },
                _lifetime.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logDebug($"Syncopation code creation failed: {response.StatusCode}");
                return null;
            }
            using var document = await ReadJsonAsync(response, _lifetime.Token);
            var root = document.RootElement;
            return root.TryGetProperty("word1", out var word1) && root.TryGetProperty("word2", out var word2)
                ? (word1.GetString()!, word2.GetString()!)
                : null;
        }
        catch (Exception exception) when (LogFailure("code creation", exception))
        {
            return null;
        }
    }

    public async Task<string?> ResolveSessionAsync(string code)
    {
        if (!await EnsureRegisteredAsync()) return null;
        try
        {
            using var response = await SendAsync(
                HttpMethod.Post,
                "/syncopation/v1/jams/join",
                new { code },
                _lifetime.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logDebug($"Syncopation join lookup failed: {response.StatusCode}");
                return null;
            }
            using var document = await ReadJsonAsync(response, _lifetime.Token);
            return document.RootElement.TryGetProperty("session_id", out var id) ? id.GetString() : null;
        }
        catch (Exception exception) when (LogFailure("join lookup", exception))
        {
            return null;
        }
    }

    private async Task<bool> EnsureRegisteredAsync()
    {
        await _registrationGate.WaitAsync(_lifetime.Token);
        try {
            bool ready = IsRegistered || await RegisterAsync();
            if (ready) {
                _eventsTask ??= RunEventLoopAsync(_lifetime.Token);
                _heartbeatTask ??= RunHeartbeatLoopAsync(_lifetime.Token);
            }
            return ready;
        }
        finally { _registrationGate.Release(); }
    }

    private async Task<bool> RegisterAsync()
    {
        try
        {
            using var response = await SendAsync(
                HttpMethod.Post,
                "/syncopation/v1/instances",
                new { client_version = "spoti-osc" },
                _lifetime.Token,
                authenticated: false);
            if (!response.IsSuccessStatusCode)
            {
                _logDebug($"Syncopation registration failed: {response.StatusCode}");
                return false;
            }

            using var document = await ReadJsonAsync(response, _lifetime.Token);
            var root = document.RootElement;
            _instanceId = root.TryGetProperty("instance_id", out var id) ? id.GetString() : null;
            _instanceToken = root.TryGetProperty("instance_token", out var token) ? token.GetString() : null;
            if (!IsRegistered) return false;
            lock (_eventLock) _lastEventSequence = 0;
            RegistrationChanged?.Invoke();
            _logDebug("Syncopation registered");
            return true;
        }
        catch (Exception exception) when (LogFailure("registration", exception))
        {
            return false;
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            bool healthy = false;
            try
            {
                if (!await EnsureRegisteredAsync())
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    continue;
                }
                using var response = await SendAsync(
                    HttpMethod.Post,
                    $"/syncopation/v1/instances/{_instanceId}/heartbeat",
                    new { },
                    cancellationToken);
                healthy = response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logDebug($"Syncopation heartbeat failed: {exception.GetType().Name}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(healthy ? Random.Shared.Next(40, 51) : Random.Shared.Next(3, 8)), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunEventLoopAsync(CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await EnsureRegisteredAsync())
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    continue;
                }
                await ListenAsync(cancellationToken);
                failures = 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logDebug($"Syncopation event stream failed: {exception.GetType().Name}");
                failures++;
                await PollAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            var seconds = Math.Min(30, Math.Pow(2, Math.Min(failures, 4))) + Random.Shared.NextDouble();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        var token = _instanceToken;
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/syncopation/v1/events/stream?instance_id={_instanceId}&since={_lastEventSequence}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await CheckAuthenticationAsync(response, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        await foreach (var item in ReadEventsAsync(reader, cancellationToken))
            if (token == _instanceToken) ApplyEvent(item.Id, item.Data);
    }

    private async Task PollAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var stopAt = DateTime.UtcNow + duration;
        bool snapshot = true;
        while (DateTime.UtcNow < stopAt && !cancellationToken.IsCancellationRequested)
        {
            if (!IsRegistered) return;
            try
            {
                using var response = await SendAsync(
                    HttpMethod.Get,
                    $"/syncopation/v1/events?instance_id={_instanceId}&since={_lastEventSequence}&snapshot={(snapshot ? 1 : 0)}",
                    cancellationToken: cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    snapshot = false;
                    using var document = await ReadJsonAsync(response, cancellationToken);
                    if (document.RootElement.TryGetProperty("events", out var events))
                        foreach (var item in events.EnumerateArray()) ApplyEvent(null, item.GetRawText());
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logDebug($"Syncopation polling failed: {exception.GetType().Name}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ApplyEvent(string? id, string data)
    {
        lock (_eventLock)
        {
        using var document = JsonDocument.Parse(data);
        var item = document.RootElement;
        int.TryParse(id, out var eventSequence);
        if (item.TryGetProperty("seq", out var sequence) && sequence.TryGetInt32(out var value))
            eventSequence = Math.Max(eventSequence, value);
        if (eventSequence > 0 && eventSequence <= _lastEventSequence) return;
        AdvanceSequence(eventSequence);
        if (item.TryGetProperty("type", out var type) && type.GetString() == "jam_joined") JamJoined?.Invoke();

        }
    }

    private void AdvanceSequence(int sequence) =>
        _lastEventSequence = Math.Max(_lastEventSequence, sequence);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default,
        bool authenticated = true)
    {
        using var request = CreateRequest(method, path, body, authenticated);
        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) Interlocked.Exchange(ref _lastResponseAt, Stopwatch.GetTimestamp());
        if (authenticated) await CheckAuthenticationAsync(response, request, cancellationToken);
        return response;
    }

    private async Task CheckAuthenticationAsync(HttpResponseMessage response, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await _registrationGate.WaitAsync(cancellationToken);
            try
            {
                if (_instanceToken == request.Headers.Authorization?.Parameter)
                {
                    _instanceToken = null; _instanceId = null;
                }
            }
            finally { _registrationGate.Release(); }
        }
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        object? body = null,
        bool authenticated = true)
    {
        var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (authenticated && !string.IsNullOrEmpty(_instanceToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _instanceToken);
            request.Headers.Add("X-Syncopation-Nonce", Guid.NewGuid().ToString());
            request.Headers.Add("X-Syncopation-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        }
        return request;
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));

    private async IAsyncEnumerable<ServerEvent> ReadEventsAsync(
        StreamReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? id = null;
        var data = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) yield break;
            Interlocked.Exchange(ref _lastResponseAt, Stopwatch.GetTimestamp());
            if (line.StartsWith(':')) continue;
            if (line.StartsWith("id:", StringComparison.Ordinal)) id = line[3..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Append(line[5..].Trim());
            else if (line.Length == 0 && data.Length > 0)
            {
                yield return new ServerEvent(id, data.ToString());
                id = null;
                data.Clear();
            }
        }
    }

    private async Task DeregisterAsync()
    {
        if (!IsRegistered) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await SendAsync(
                HttpMethod.Delete,
                $"/syncopation/v1/instances/{_instanceId}",
                cancellationToken: timeout.Token);
        }
        catch (Exception exception)
        {
            _logDebug($"Syncopation deregistration failed: {exception.GetType().Name}");
        }
    }

    private bool LogFailure(string operation, Exception exception)
    {
        _logDebug($"Syncopation {operation} failed: {exception.GetType().Name}");
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        try
        {
            await Task.WhenAll(new[] { _eventsTask, _heartbeatTask }
                .Where(task => task is not null)
                .Cast<Task>());
        }
        catch (OperationCanceledException)
        {
        }
        await DeregisterAsync();
        _registrationGate.Dispose();
        _lifetime.Dispose();
    }

    private bool IsRegistered =>
        !string.IsNullOrEmpty(_instanceId) && !string.IsNullOrEmpty(_instanceToken);

    private static string NormalizeServerUrl(string url)
    {
        var normalized = (url ?? string.Empty).TrimEnd('/');
        return normalized.EndsWith("/syncopation", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^"/syncopation".Length]
            : normalized;
    }

    private sealed record ServerEvent(string? Id, string Data);
}
