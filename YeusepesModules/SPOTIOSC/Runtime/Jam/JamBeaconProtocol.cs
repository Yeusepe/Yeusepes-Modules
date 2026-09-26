using System.Security.Cryptography;
using System.Text;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

// The proximity receiver is local only. Its quantized value carries a framed stream, not an identity.
internal static class JamBeaconProtocol
{
    public const int Start = 65;
    public const int SlowStart = 66;
    public const int SymbolCount = 32;
    public const int SymbolMilliseconds = 850;
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(16);
    public static string Lookup(byte[] secret) => Convert.ToHexString(Derive("lookup", secret)).ToLowerInvariant();
    private static byte[] Derive(string domain, byte[] secret) => SHA256.HashData(Encoding.UTF8.GetBytes("jam-beacon:" + domain + ":v1\0").Concat(secret).ToArray());
    public static string Encrypt(byte[] secret, string session)
    {
        var plain = Encoding.UTF8.GetBytes(session);
        if (plain.Length is 0 or > 512) throw new ArgumentException("Invalid invitation size");
        var data = new byte[28 + plain.Length];
        RandomNumberGenerator.Fill(data.AsSpan(0, 12));
        using var aes = new AesGcm(Derive("key", secret), 16);
        aes.Encrypt(data.AsSpan(0, 12), plain, data.AsSpan(28), data.AsSpan(12, 16), "jam-beacon:v1"u8);
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(data);
    }
    public static string Decrypt(byte[] secret, string envelope)
    {
        if (envelope.Length > 1400) throw new CryptographicException("Invalid invitation");
        var data = Convert.FromBase64String(envelope);
        if (data.Length is <= 28 or > 540) throw new CryptographicException("Invalid invitation");
        var plain = new byte[data.Length - 28];
        using var aes = new AesGcm(Derive("key", secret), 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain, "jam-beacon:v1"u8);
        try { return new UTF8Encoding(false, true).GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static int[] Encode(byte[] secret, int bits = 5)
    {
        if (secret.Length != 16) throw new ArgumentException("Expected 128-bit capability");
        if (bits != 2 && bits != 5) throw new ArgumentOutOfRangeException(nameof(bits));
        var frame = secret.Concat(SHA256.HashData(secret).Take(4)).ToArray();
        var symbols = new int[160 / bits + 1]; symbols[0] = bits == 5 ? Start : SlowStart;
        for (int i = 0; i < 160 / bits; i++) {
            int value = 0;
            for (int bit = 0; bit < bits; bit++) { int offset = i * bits + bit; value = (value << 1) | ((frame[offset / 8] >> (7 - offset % 8)) & 1); }
            symbols[i + 1] = 1 + value + (i % 2) * (1 << bits);
        }
        return symbols;
    }
    public static int Quantize(float proximity)
    {
        if (!float.IsFinite(proximity) || proximity <= 0) return 0;
        float scaled = proximity * 128;
        int symbol = (int)MathF.Round(scaled);
        return symbol is >= 1 and <= SlowStart && Math.Abs(scaled - symbol) <= .2f ? symbol : -1;
    }
}

internal sealed class JamBeaconDecoder
{
    private readonly List<int> _symbols = [];
    private bool _frame;
    private int _previous;
    private long _started;
    private int _bits = 5;
    public void Reset() { _symbols.Clear(); _frame = false; _previous = 0; }
    public byte[]? Accept(int symbol, long milliseconds)
    {
        // Repeated samples are harmless; gaps or a missing phase cannot form another valid capability.
        if (symbol == 0) return null;
        if (symbol == _previous) return null;
        _previous = symbol;
        if (symbol == JamBeaconProtocol.Start || symbol == JamBeaconProtocol.SlowStart) { _symbols.Clear(); _frame = true; _bits = symbol == JamBeaconProtocol.Start ? 5 : 2; _started = milliseconds; return null; }
        if (!_frame) return null;
        if (milliseconds - _started > 120_000 || symbol < 0) { Reset(); return null; }
        int encoded = symbol - 1;
        if (encoded < 0 || encoded >= (2 << _bits) || (encoded >> _bits) != (_symbols.Count % 2)) { Reset(); return null; }
        _symbols.Add(encoded & ((1 << _bits) - 1));
        if (_symbols.Count == 160 / _bits) {
            _frame = false;
            var data = new byte[20];
            for (int i = 0; i < _symbols.Count; i++) for (int bit = 0; bit < _bits; bit++) {
                int offset = i * _bits + bit;
                data[offset / 8] |= (byte)(((_symbols[i] >> (_bits - 1 - bit)) & 1) << (7 - offset % 8));
            }
            var secret = data[..16];
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(secret).AsSpan(0, 4), data.AsSpan(16)) ? secret : null;
        }
        return null;
    }
}
