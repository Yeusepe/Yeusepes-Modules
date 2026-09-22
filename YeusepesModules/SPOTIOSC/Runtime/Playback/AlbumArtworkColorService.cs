using System.ComponentModel;
using System.Drawing;
using System.IO;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class AlbumArtworkColorService : IAsyncDisposable
{
    private readonly SpotifyRequestContext _context;
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;
    private CancellationTokenSource? _pending;
    private Task _pendingTask = Task.CompletedTask;

    public AlbumArtworkColorService(
        SpotifyRequestContext context,
        SpotiOscOutput output,
        Action<string> logDebug)
    {
        _context = context;
        _output = output;
        _logDebug = logDebug;
        _context.PropertyChanged += OnContextChanged;
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(SpotifyRequestContext.AlbumArtworkUrl)) return;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();
        _pendingTask = UpdateAsync(_context.AlbumArtworkUrl, _pending.Token);
    }

    private async Task UpdateAsync(string? url, CancellationToken cancellationToken)
    {
        try
        {
            var color = System.Windows.Media.Colors.Transparent;
            if (!string.IsNullOrEmpty(url))
            {
                var bytes = await _context.HttpClient.GetByteArrayAsync(url, cancellationToken);
                await using var stream = new MemoryStream(bytes);
                using var bitmap = new Bitmap(stream);
                var pixel = bitmap.GetPixel(0, 0);
                color = System.Windows.Media.Color.FromArgb(pixel.A, pixel.R, pixel.G, pixel.B);
            }
            cancellationToken.ThrowIfCancellationRequested();
            _context.DominantColor = color;
            _output.Set(SpotiOSC.SpotiParameters.AlbumColorR, (float)color.R);
            _output.Set(SpotiOSC.SpotiParameters.AlbumColorG, (float)color.G);
            _output.Set(SpotiOSC.SpotiParameters.AlbumColorB, (float)color.B);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logDebug($"Album artwork color extraction failed: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _context.PropertyChanged -= OnContextChanged;
        _pending?.Cancel();
        await _pendingTask;
        _pending?.Dispose();
        _pending = null;
    }
}
