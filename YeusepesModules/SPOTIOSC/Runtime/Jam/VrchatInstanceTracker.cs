using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

/// <summary>Incremental log reader for SDK versions which expose only the world ID.</summary>
internal sealed class VrchatInstanceTracker
{
    private static readonly Regex Location = new(@"(?:Destination set:|\[RoomManager\] Joining)\s+(wrld_[0-9a-fA-F-]{36}:[^\s""<>]+)", RegexOptions.Compiled);
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", "VRChat", "VRChat");
    private string? _file;
    private long _offset;
    private string _partial = string.Empty;
    private string? _destination;
    private string? _active;
    private long _lastRead;
    private bool _caughtUp;

    public string? CurrentRoomKey()
    {
        if (_lastRead != 0 && Stopwatch.GetElapsedTime(_lastRead) < TimeSpan.FromSeconds(2)) return _caughtUp ? _active : null;
        _lastRead = Stopwatch.GetTimestamp();
        try
        {
            using var process = Process.GetProcessesByName("VRChat").FirstOrDefault();
            if (process is null || !Directory.Exists(_directory)) { Reset(); return null; }
            var latest = new DirectoryInfo(_directory).EnumerateFiles("output_log_*.txt")
                .Where(file => file.CreationTimeUtc >= process.StartTime.ToUniversalTime().AddMinutes(-2))
                .OrderByDescending(file => file.CreationTimeUtc).FirstOrDefault();
            if (latest is null) { Reset(); return null; }
            if (_file != latest.FullName || latest.Length < _offset) { Reset(); _file = latest.FullName; }
            using var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Position = _offset;
            var bytes = new byte[(int)Math.Min(1024 * 1024, stream.Length - _offset)];
            int count = stream.Read(bytes, 0, bytes.Length);
            _offset += count;
            var text = _partial + Encoding.UTF8.GetString(bytes, 0, count);
            int end = text.LastIndexOf('\n');
            if (end >= 0)
            {
                foreach (var line in text[..end].Split('\n')) ApplyLine(line);
                _partial = text[(end + 1)..];
            }
            else _partial = text;
            if (_partial.Length > 16_384) _partial = string.Empty;
            _caughtUp = _offset >= stream.Length;
            return _caughtUp ? _active : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Reset();
            return null;
        }
    }

    internal void ApplyLine(string line)
    {
        var match = Location.Match(line);
        if (match.Success) { _active = null; _destination = HashLocation(match.Groups[1].Value); }
        if (line.Contains("OnLeftRoom", StringComparison.Ordinal)) _active = null;
        if (line.Contains("Finished entering world.", StringComparison.Ordinal)) _active = _destination;
    }

    internal static string? HashLocation(string location)
    {
        var parts = location.Trim().TrimEnd('"', '\r').Split(':', 2);
        if (parts.Length != 2 || !parts[0].StartsWith("wrld_", StringComparison.Ordinal) ||
            !Guid.TryParse(parts[0][5..], out var world) || string.IsNullOrWhiteSpace(parts[1])) return null;
        var tags = parts[1].Split('~');
        if (!ulong.TryParse(tags[0], out _)) return null;
        var canonical = "world-jam:v1:" + world.ToString("D") + ":" + tags[0] +
            string.Concat(tags.Skip(1).OrderBy(tag => tag, StringComparer.Ordinal).Select(tag => "~" + tag));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private void Reset()
    {
        _file = null; _offset = 0; _partial = string.Empty; _destination = null; _active = null; _caughtUp = false;
    }
}
