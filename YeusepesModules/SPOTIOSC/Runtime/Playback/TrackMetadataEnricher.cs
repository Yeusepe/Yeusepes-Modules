using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class TrackMetadataEnricher : IAsyncDisposable
{
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
            using var request = CreateRequest(trackId);
            using var response = await _context.HttpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsByteArrayAsync(cancellationToken));
                Apply(document.RootElement);
                return;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Requeue(trackId, response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1));
                return;
            }

            _logDebug($"Track metadata request failed: {response.StatusCode} - " +
                      await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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
            _timer?.Change(delay, Timeout.InfiniteTimeSpan);
        }
        _logDebug($"Track metadata rate limited; retrying in {delay.TotalMilliseconds}ms");
    }

    private HttpRequestMessage CreateRequest(string trackId)
    {
        var token = CredentialManager.LoadApiAccessToken();
        if (string.IsNullOrEmpty(token)) token = _context.AccessToken;

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.spotify.com/v1/tracks/{Uri.EscapeDataString(trackId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
        request.Headers.Add("Priority", "u=1, i");
        request.Headers.Add("Sec-Fetch-Dest", "empty");
        request.Headers.Add("Sec-Fetch-Mode", "cors");
        request.Headers.Add("Sec-Fetch-Site", "same-site");
        request.Headers.Add("Referer", "https://developer.spotify.com/");
        return request;
    }

    private void Apply(JsonElement track)
    {
        if (!TryReadArtists(track, out var trackUri, out var artists) ||
            !string.Equals(_context.TrackUri, trackUri, StringComparison.Ordinal))
            return;

        _context.Artists = artists;
        _projection.Update();
        _logDebug($"Enriched track metadata: {string.Join(", ", artists.Select(artist => artist.Name))}");
    }

    private static bool TryReadArtists(
        JsonElement track,
        out string? trackUri,
        out List<(string Name, string Uri)> artists)
    {
        trackUri = null;
        artists = [];
        if (track.ValueKind != JsonValueKind.Object ||
            !track.TryGetProperty("artists", out var source) ||
            source.ValueKind != JsonValueKind.Array)
            return false;

        trackUri = track.TryGetProperty("uri", out var uri) ? uri.GetString() : null;
        artists = source.EnumerateArray()
            .Select(artist => (
                Name: artist.TryGetProperty("name", out var name) ? name.GetString() : null,
                Uri: artist.TryGetProperty("uri", out var artistUri) ? artistUri.GetString() : null))
            .Where(artist => !string.IsNullOrEmpty(artist.Name))
            .Select(artist => (artist.Name!, artist.Uri ?? string.Empty))
            .ToList();
        return artists.Count > 0;
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
