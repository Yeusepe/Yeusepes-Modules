using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Runtime.Spotify;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class TrackMetadataEnricher : IAsyncDisposable
{
    private const string Base62 = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly TimeSpan FetchDelay = TimeSpan.FromMilliseconds(500);
    private readonly SpotifyRequestContext _context;
    private readonly PlaybackProjection _projection;
    private readonly Action<string> _logDebug;
    private string? _pendingId;
    private readonly HashSet<Task> _activeRequests = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _disposed;

    public TrackMetadataEnricher(
        SpotifyRequestContext context,
        PlaybackProjection projection,
        Action<string> logDebug)
    {
        _context = context;
        _projection = projection;
        _logDebug = logDebug;
    }

    public void Enqueue(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId)) return;
        lock (_gate)
        {
            if (_disposed || _pendingId == trackId) return;
            _pendingId = trackId;
            (_timer ??= new Timer(FetchPending, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan))
                .Change(FetchDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void FetchPending(object? _)
    {
        var request = FetchPendingAsync(_lifetime.Token);
        lock (_gate) _activeRequests.Add(request);
        _ = RemoveWhenCompleteAsync(request);
    }

    private async Task RemoveWhenCompleteAsync(Task request)
    {
        await request;
        lock (_gate) _activeRequests.Remove(request);
    }

    private async Task FetchPendingAsync(CancellationToken cancellationToken)
    {
        string? trackId;
        lock (_gate)
        {
            trackId = _pendingId;
            _pendingId = null;
        }
        if (trackId is null) return;

        try
        {
            var client = new SpotifyAuthenticatedClient(_context.HttpClient, CredentialManager.LoadAccessToken());
            using var request = client.CreateRequest(
                HttpMethod.Get,
                $"https://spclient.wg.spotify.com/metadata/4/track/{ToGid(trackId)}?market=from_token");
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("client-token", CredentialManager.LoadClientToken());
            using var document = JsonDocument.Parse(await client.SendAsync(request, cancellationToken));
            Apply(trackId, document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SpotifyRateLimitException exception)
        {
            Requeue(trackId, exception.RetryAt - DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            _logDebug($"Track metadata request failed: {exception.Message}");
        }
    }

    private void Requeue(string trackId, TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed || _context.TrackUri != $"spotify:track:{trackId}") return;
            _pendingId = trackId;
            _timer?.Change(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        }
        _logDebug($"Track metadata rate limited; retrying in {delay.TotalMilliseconds}ms");
    }

    private static string ToGid(string trackId)
    {
        var value = BigInteger.Zero;
        foreach (var character in trackId)
            value = value * 62 + Base62.IndexOf(character);
        return value.ToString("x32")[^32..];
    }

    private void Apply(string trackId, JsonElement track)
    {
        if (_context.TrackUri != $"spotify:track:{trackId}") return;
        List<(string Name, string Uri)> artists = track.TryGetProperty("artist", out var source)
            ? source.EnumerateArray()
                .Select(artist => artist.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => (name!, string.Empty))
                .ToList()
            : [];
        if (artists.Count == 0)
        {
            var raw = track.GetRawText();
            _logDebug($"Track metadata had no artists: {raw[..Math.Min(300, raw.Length)]}");
            return;
        }

        _context.Artists = artists;
        _projection.Update();
        _logDebug($"Enriched track metadata: {string.Join(", ", artists.Select(artist => artist.Name))}");
    }

    public async ValueTask DisposeAsync()
    {
        Task[] activeRequests;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        if (_timer is not null) await _timer.DisposeAsync();
        _timer = null;
        _lifetime.Cancel();
        lock (_gate)
        {
            _pendingId = null;
            activeRequests = _activeRequests.ToArray();
        }
        await Task.WhenAll(activeRequests);
        _lifetime.Dispose();
    }
}
