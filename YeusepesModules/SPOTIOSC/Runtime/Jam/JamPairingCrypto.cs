using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

// 固定 BoringSSL draft-02 变体；协议名明确区分 RFC 9382 和旧版邀请。
internal sealed class JamPairingCrypto : IDisposable
{
    internal const string Suite = "melody-pairing-v3/boringssl-spake2-5fbad228";
    private readonly PakeHandle _handle;
    private readonly byte[] _context;
    private readonly bool _host;
    private bool _finished;
    public byte[] Message { get; } = new byte[32];
    public static void EnsureAvailable() { if (Native.Abi() != 1) throw new CryptographicException("Unsupported pairing library"); }

    public JamPairingCrypto(bool host, byte[] password, string locator, string attempt)
    {
        if (password.Length != 6 || !Hex(locator, 10) || !Hex(attempt, 32)) throw new ArgumentException("Invalid pairing context");
        _host = host;
        _context = Encoding.ASCII.GetBytes(Suite + "/" + locator + "/" + attempt);
        var guestName = Encoding.ASCII.GetBytes("guest/" + Encoding.ASCII.GetString(_context));
        var hostName = Encoding.ASCII.GetBytes("host/" + Encoding.ASCII.GetString(_context));
        EnsureAvailable();
        _handle = Native.New(host ? 1 : 0, host ? hostName : guestName, (nuint)(host ? hostName.Length : guestName.Length),
            host ? guestName : hostName, (nuint)(host ? guestName.Length : hostName.Length));
        if (_handle.IsInvalid || Native.Start(_handle, password, (nuint)password.Length, Message) != 1) {
            _handle.Dispose(); throw new CryptographicException("Pairing initialization failed");
        }
    }
    internal static bool Hex(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public JamPairingKeys Finish(byte[] peer)
    {
        if (_finished || peer.Length != 32) throw new CryptographicException("Invalid pairing state");
        _finished = true;
        var key = new byte[64];
        try {
            if (Native.Finish(_handle, peer, (nuint)peer.Length, key) != 1) throw new CryptographicException("Invalid pairing message");
            var transcript = SHA256.HashData(_context.Concat(_host ? peer : Message).Concat(_host ? Message : peer).ToArray());
            return new JamPairingKeys(key, transcript);
        } finally { CryptographicOperations.ZeroMemory(key); _handle.Dispose(); }
    }
    public void Dispose() => _handle.Dispose();
    private sealed class PakeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public PakeHandle() : base(true) { }
        protected override bool ReleaseHandle() { Native.Free(handle); return true; }
    }
    private static class Native
    {
        private const string Library = "JamPake.dll";
        [DllImport(Library, EntryPoint = "jam_pake_abi", CallingConvention = CallingConvention.Cdecl)] internal static extern int Abi();
        [DllImport(Library, EntryPoint = "jam_pake_new", CallingConvention = CallingConvention.Cdecl)] internal static extern PakeHandle New(int host, byte[] mine, nuint mineLength, byte[] peer, nuint peerLength);
        [DllImport(Library, EntryPoint = "jam_pake_free", CallingConvention = CallingConvention.Cdecl)] internal static extern void Free(IntPtr context);
        [DllImport(Library, EntryPoint = "jam_pake_start", CallingConvention = CallingConvention.Cdecl)] internal static extern int Start(PakeHandle context, byte[] password, nuint length, byte[] message);
        [DllImport(Library, EntryPoint = "jam_pake_finish", CallingConvention = CallingConvention.Cdecl)] internal static extern int Finish(PakeHandle context, byte[] peer, nuint length, byte[] key);
    }
}

internal sealed class JamPairingKeys : IDisposable
{
    private readonly byte[] _transcript, _hostConfirm, _guestConfirm, _hostEncryption, _guestEncryption;
    public JamPairingKeys(byte[] key, byte[] transcript)
    {
        _transcript = transcript;
        byte[] Derive(string purpose) => HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, transcript, Encoding.ASCII.GetBytes(purpose));
        _hostConfirm = Derive("host-confirm"); _guestConfirm = Derive("guest-confirm");
        _hostEncryption = Derive("host-encryption"); _guestEncryption = Derive("guest-encryption");
    }
    public byte[] Confirmation(bool host) => HMACSHA256.HashData(host ? _hostConfirm : _guestConfirm, _transcript);
    public bool Verify(bool host, ReadOnlySpan<byte> confirmation) => confirmation.Length == 32 && CryptographicOperations.FixedTimeEquals(Confirmation(host), confirmation);
    private byte[] Aad(bool host, int round) => _transcript.Concat(Encoding.ASCII.GetBytes((host ? "/host/" : "/guest/") + round)).ToArray();
    public byte[] Seal(bool host, int round, byte[] plain)
    {
        if (round < 2 || plain.Length > 1000) throw new CryptographicException("Invalid pairing envelope");
        var result = new byte[plain.Length + 28]; RandomNumberGenerator.Fill(result.AsSpan(0, 12));
        using var aes = new AesGcm(host ? _hostEncryption : _guestEncryption, 16);
        aes.Encrypt(result.AsSpan(0, 12), plain, result.AsSpan(28), result.AsSpan(12, 16), Aad(host, round));
        return result;
    }
    public byte[] Open(bool host, int round, byte[] data)
    {
        if (round < 2 || data.Length is < 28 or > 1028) throw new CryptographicException("Invalid pairing envelope");
        var result = new byte[data.Length - 28];
        using var aes = new AesGcm(host ? _hostEncryption : _guestEncryption, 16);
        aes.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), result, Aad(host, round));
        return result;
    }
    public void Dispose() { foreach (var key in new[] { _hostConfirm, _guestConfirm, _hostEncryption, _guestEncryption }) CryptographicOperations.ZeroMemory(key); }
}
