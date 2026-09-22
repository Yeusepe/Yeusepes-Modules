using System.Text.Json;
using Google.Protobuf;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC.Runtime.Spotify;

internal sealed class SpotifyConnectEventHandler
{
    private readonly SpotifyRequestContext _context;
    private readonly SpotiOscOutput _output;
    private readonly Action<string> _logDebug;

    public SpotifyConnectEventHandler(
        SpotifyRequestContext context,
        SpotiOscOutput output,
        Action<string> logDebug)
    {
        _context = context;
        _output = output;
        _logDebug = logDebug;
    }

    public void HandleContentSettings(JsonElement message)
    {
        foreach (var payload in DecodePayloads(message, "content settings"))
        {
            try
            {
                ApplyShuffleModes(payload);
            }
            catch (Exception exception)
            {
                _logDebug($"Content settings payload failed: {exception.Message}");
            }
        }
    }

    public void HandleVolume(JsonElement message)
    {
        foreach (var payload in DecodePayloads(message, "volume"))
        {
            try
            {
                using var input = new CodedInputStream(payload);
                while (!input.IsAtEnd)
                {
                    var tag = input.ReadTag();
                    if (WireFormat.GetTagFieldNumber(tag) == 1 &&
                        WireFormat.GetTagWireType(tag) == WireFormat.WireType.Varint)
                    {
                        ApplyVolume(input.ReadUInt32());
                        break;
                    }
                    input.SkipLastField();
                }
            }
            catch (Exception exception)
            {
                _logDebug($"Volume payload failed: {exception.Message}");
            }
        }
    }

    private void ApplyShuffleModes(byte[] payload)
    {
        bool? shuffle = null;
        bool? smartShuffle = null;
        using var input = new CodedInputStream(payload);
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            if (WireFormat.GetTagFieldNumber(tag) != 3 ||
                WireFormat.GetTagWireType(tag) != WireFormat.WireType.LengthDelimited)
            {
                input.SkipLastField();
                continue;
            }

            if (!TryReadMode(input.ReadBytes(), out var mode, out var enabled)) continue;
            if (mode == 4) shuffle = enabled;
            else if (mode == 5) smartShuffle = enabled;
        }

        if (!shuffle.HasValue && !smartShuffle.HasValue) return;
        if (shuffle.HasValue) _context.ShuffleState = shuffle.Value;
        if (smartShuffle.HasValue) _context.SmartShuffle = smartShuffle.Value;
        _output.Set(
            SpotiOSC.SpotiParameters.ShuffleMode,
            !_context.ShuffleState ? 0 : _context.SmartShuffle ? 2 : 1);
    }

    private void ApplyVolume(uint rawVolume)
    {
        var volume = rawVolume <= 100
            ? (int)rawVolume
            : (int)Math.Round(rawVolume * 100d / ushort.MaxValue);
        volume = Math.Clamp(volume, 0, 100);
        if (_context.VolumePercent == volume) return;

        _context.VolumePercent = volume;
        _output.Set(SpotiOSC.SpotiParameters.DeviceVolumePercent, volume);
        _output.Trigger("VolumeEvent");
    }

    private IEnumerable<byte[]> DecodePayloads(JsonElement message, string label)
    {
        if (!message.TryGetProperty("payloads", out var payloads) ||
            payloads.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var payload in payloads.EnumerateArray())
        {
            if (payload.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(payload.GetString()))
                continue;

            byte[] decoded;
            try
            {
                decoded = Convert.FromBase64String(payload.GetString()!);
            }
            catch (FormatException exception)
            {
                _logDebug($"Invalid {label} payload: {exception.Message}");
                continue;
            }
            yield return decoded;
        }
    }

    private static bool TryReadMode(ByteString bytes, out int mode, out bool enabled)
    {
        mode = 0;
        bool? value = null;
        using var input = new CodedInputStream(bytes.ToByteArray());
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            var field = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);
            if (field == 1 && wireType == WireFormat.WireType.Varint)
            {
                mode = input.ReadInt32();
            }
            else if (field == 2 && wireType == WireFormat.WireType.LengthDelimited)
            {
                value = ReadModeValue(input.ReadBytes());
            }
            else
            {
                input.SkipLastField();
            }
        }
        enabled = value ?? false;
        return mode != 0 && value.HasValue;
    }

    private static bool? ReadModeValue(ByteString bytes)
    {
        using var input = new CodedInputStream(bytes.ToByteArray());
        while (!input.IsAtEnd)
        {
            var tag = input.ReadTag();
            var field = WireFormat.GetTagFieldNumber(tag);
            if ((field == 2 || field == 3) &&
                WireFormat.GetTagWireType(tag) == WireFormat.WireType.Varint)
                return input.ReadInt32() != 0;
            input.SkipLastField();
        }
        return null;
    }
}
