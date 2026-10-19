using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal static class SpotifyHttp
{
    internal static readonly HttpRequestOptionsKey<string> TokenKind = new("SpotifyTokenKind");
    private static readonly ConcurrentDictionary<string, HostRequests> Hosts = new();

    public static HttpClient CreateClient(Action<string> log, HttpMessageHandler? handler = null) =>
        new(new DiagnosticHandler(log, handler ?? new HttpClientHandler()));

    public static async Task<string> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            var details = await ReadErrorAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new SpotifyRateLimitException(details, DateTimeOffset.UtcNow + RetryDelay(response));
            throw new HttpRequestException(details, null, response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        var body = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        var request = response.RequestMessage;
        if (request?.Headers.Authorization?.Parameter is { Length: > 0 } token)
            body = body.Replace(token, "[redacted]", StringComparison.Ordinal);
        if (request?.Headers.TryGetValues("client-token", out var clientTokens) == true)
            foreach (var clientToken in clientTokens.Where(value => value.Length > 0))
                body = body.Replace(clientToken, "[redacted]", StringComparison.Ordinal);
        if (request?.Headers.TryGetValues("Cookie", out var cookies) == true)
            foreach (var cookie in cookies.SelectMany(value => value.Split(';')))
            {
                var value = cookie.Split('=', 2).ElementAtOrDefault(1)?.Trim();
                if (!string.IsNullOrEmpty(value)) body = body.Replace(value, "[redacted]", StringComparison.Ordinal);
            }
        body = Regex.Replace(body,
            "(?i)(\\\"?(?:access_?token|refresh_?token|client[-_]?token|token|authorization|cookie|sp_dc|sp_t|code_verifier|code|state|email)\\\"?\\s*[:=]\\s*)(\\\"[^\\\"]*\\\"|[^\\s,;&}]+)",
            "$1\"[redacted]\"");
        body = Regex.Replace(body, @"(?i)Bearer\s+[^\s\""<>,}]+", "Bearer [redacted]");
        body = body.Replace('\r', ' ').Replace('\n', ' ');
        if (body.Length > 2048) body = body[..2048] + "…";
        var retry = response.Headers.RetryAfter is { } retryAfter ? $"; Retry-After={retryAfter}" : string.Empty;
        return $"{request?.Method} {Endpoint(request?.RequestUri)} returned {(int)response.StatusCode} ({response.StatusCode}){retry}; response: {body}";
    }

    internal static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        var delay = retry?.Delta ?? (retry?.Date is { } date
            ? date - (response.Headers.Date ?? DateTimeOffset.UtcNow)
            : TimeSpan.FromSeconds(30));
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
    }

    private static string Endpoint(Uri? uri) => uri is null ? "Spotify" : uri.GetLeftPart(UriPartial.Path);

    private sealed class HostRequests
    {
        public readonly Queue<DateTimeOffset> Sent = new();
        public DateTimeOffset RetryAt;
    }

    private sealed class DiagnosticHandler(Action<string> log, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri!.Host;
            if (host != "spotify.com" && !host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase))
                return await base.SendAsync(request, cancellationToken);

            var state = Hosts.GetOrAdd($"{host} {request.Headers.Authorization?.Parameter}", _ => new HostRequests());
            int count;
            lock (state)
            {
                var now = DateTimeOffset.UtcNow;
                if (state.RetryAt > now)
                    throw new SpotifyRateLimitException($"Spotify requests to {host} with this token are paused until {state.RetryAt:O}.", state.RetryAt);
                while (state.Sent.TryPeek(out var sent) && now - sent >= TimeSpan.FromSeconds(30)) state.Sent.Dequeue();
                state.Sent.Enqueue(now);
                count = state.Sent.Count;
            }
            request.Options.TryGetValue(TokenKind, out var tokenKind);
            log($"[HTTP] {request.Method} {Endpoint(request.RequestUri)}; auth={tokenKind ?? "unspecified"}; requests with this token in last 30s: {count}");
            var response = await base.SendAsync(request, cancellationToken);
            response.RequestMessage ??= request;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lock (state)
                {
                    var retryAt = DateTimeOffset.UtcNow + RetryDelay(response);
                    if (retryAt > state.RetryAt) state.RetryAt = retryAt;
                }
            }
            if (!response.IsSuccessStatusCode)
            {
                try { log($"[HTTP] {await ReadErrorAsync(response, cancellationToken)}"); }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }
            return response;
        }
    }
}

internal sealed class SpotifyRateLimitException(string message, DateTimeOffset retryAt)
    : HttpRequestException(message, null, HttpStatusCode.TooManyRequests)
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}
