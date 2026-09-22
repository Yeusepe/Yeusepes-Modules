using System.Diagnostics;

namespace YeusepesModules.SPOTIOSC.Runtime.Playback;

internal sealed class PlaybackClock : IDisposable
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(16);
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;
    private readonly object _gate = new();
    private Timer? _timer;
    private long _lastUpdateTimestamp;
    private int _positionMs;
    private int _durationMs;
    private bool _isPlaying;
    private int _tickActive;

    public PlaybackClock(SpotiOscOutput output, Action<string> logDebug)
    {
        _output = output;
        _logDebug = logDebug;
    }

    public void Start()
    {
        _timer ??= new Timer(Tick, null, UpdateInterval, UpdateInterval);
        _logDebug("Continuous position tracking started");
    }

    public void Synchronize(int positionMs, bool isPlaying, int durationMs)
    {
        lock (_gate)
        {
            _positionMs = positionMs;
            _isPlaying = isPlaying;
            _durationMs = durationMs;
            _lastUpdateTimestamp = Stopwatch.GetTimestamp();
        }

        _output.Set(SpotiOSC.SpotiParameters.PlaybackPosition, (float)positionMs);
        _logDebug($"Position synchronized - Progress: {positionMs}ms, Playing: {isPlaying}, Duration: {durationMs}ms");
    }

    private void Tick(object? _)
    {
        if (Interlocked.Exchange(ref _tickActive, 1) != 0) return;
        try
        {
            int position;
            lock (_gate)
            {
                if (!_isPlaying || _durationMs <= 0 || _positionMs < 0) return;

                var now = Stopwatch.GetTimestamp();
                var elapsedMs = (int)Stopwatch.GetElapsedTime(_lastUpdateTimestamp, now).TotalMilliseconds;
                if (elapsedMs <= 0) return;

                _positionMs = Math.Min(_positionMs + elapsedMs, _durationMs);
                _lastUpdateTimestamp = now;
                position = _positionMs;
            }

            _output.Set(SpotiOSC.SpotiParameters.PlaybackPosition, (float)position);
        }
        catch (Exception exception)
        {
            _logDebug($"Position tracking error: {exception.Message}");
        }
        finally
        {
            Volatile.Write(ref _tickActive, 0);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _logDebug("Continuous position tracking stopped");
    }
}
