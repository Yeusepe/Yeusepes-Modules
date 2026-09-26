using System.Security.Cryptography;

namespace YeusepesModules.SPOTIOSC.Runtime.Jam;

internal sealed record JamBootstrap(byte[] Data)
{
    public string Locator => Convert.ToHexString(Data.AsSpan(1, 5)).ToLowerInvariant();
    public byte[] Password => Data[6..12];
    public string LocalId => Convert.ToHexString(SHA256.HashData(Data));
    public static JamBootstrap Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(14); bytes[0] = 3;
        ushort crc = JamFragmentProtocol.Crc16(bytes.AsSpan(0, 12)); bytes[12] = (byte)(crc >> 8); bytes[13] = (byte)crc;
        return new(bytes);
    }
    public static JamBootstrap? Parse(byte[] bytes) => bytes.Length == 14 && bytes[0] == 3 &&
        JamFragmentProtocol.Crc16(bytes.AsSpan(0, 12)) == ((bytes[12] << 8) | bytes[13]) ? new(bytes) : null;
}

// 4 个数据片段 + 1 个 XOR 修复片段。每个片段自带代际标签、索引和 CRC，丢帧不清空其它片段。
internal static class JamFragmentProtocol
{
    public const int Lanes = 4;
    public const int FastMilliseconds = 240;
    public const int MediumMilliseconds = 480;
    public const int SlowMilliseconds = 2200;
    public const int FragmentBytes = 7;
    public static ushort Crc16(ReadOnlySpan<byte> input)
    {
        ushort crc = 0xffff;
        foreach (var b in input) { crc ^= (ushort)(b << 8); for (int i = 0; i < 8; i++) crc = (ushort)((crc << 1) ^ ((crc & 0x8000) != 0 ? 0x1021 : 0)); }
        return crc;
    }
    private static byte Crc8(ReadOnlySpan<byte> input)
    {
        byte crc = 0;
        foreach (var b in input) { crc ^= b; for (int i = 0; i < 8; i++) crc = (byte)((crc << 1) ^ ((crc & 128) != 0 ? 7 : 0)); }
        return crc;
    }
    public static byte[] Fragment(JamBootstrap bootstrap, int index)
    {
        if (index is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(index));
        var data = new byte[16]; bootstrap.Data.CopyTo(data, 0);
        var result = new byte[FragmentBytes]; result[0] = (byte)(0x30 | index); result[1] = SHA256.HashData(bootstrap.Data)[0];
        for (int j = 0; j < 4; j++) result[2 + j] = index < 4 ? data[index * 4 + j] : (byte)(data[j] ^ data[4 + j] ^ data[8 + j] ^ data[12 + j]);
        result[6] = Crc8(result.AsSpan(0, 6)); return result;
    }
    public static bool ValidFragment(byte[] fragment) => fragment.Length == FragmentBytes && fragment[0] is >= 0x30 and <= 0x34 && Crc8(fragment.AsSpan(0, 6)) == fragment[6];
    public static int[] Encode(JamBootstrap bootstrap, int fragment, int bits)
    {
        if (bits is not (2 or 5 or 6 or 10)) throw new ArgumentOutOfRangeException(nameof(bits));
        var bytes = Fragment(bootstrap, fragment); var count = (56 + bits - 1) / bits;
        var result = new int[count + 1]; result[0] = bits switch { 5 => 65, 2 => 66, 6 => 67, _ => 68 };
        for (int i = 0; i < count; i++) {
            int value = 0;
            for (int bit = 0; bit < bits; bit++) { int offset = i * bits + bit; value = (value << 1) | (offset < 56 ? ((bytes[offset / 8] >> (7 - offset % 8)) & 1) : 0); }
            result[i + 1] = 1 + value + (i % 2) * (1 << bits);
        }
        return result;
    }
}

internal sealed class JamFragmentDecoder
{
    private sealed class Lane { public int Previous, Bits, Count; public long Started; public readonly byte[] Data = new byte[7]; public readonly List<int> Unknown = new(); }
    private sealed class Assembly { public readonly byte[][] Fragments = new byte[5][]; public long Seen; }
    private readonly Lane[] _lanes = Enumerable.Range(0, 4).Select(_ => new Lane()).ToArray();
    private readonly Dictionary<int, Assembly> _assemblies = new();
    public void Reset() { _assemblies.Clear(); foreach (var lane in _lanes) { lane.Bits = 0; lane.Previous = 0; } }
    public JamBootstrap? Accept(int laneIndex, int symbol, long now, bool start = false)
    {
        if (laneIndex is < 0 or >= 4 || symbol <= 0) return null;
        var lane = _lanes[laneIndex];
        // Start 单独传入，避免宽信道数据值与起始码重合。
        int identity = start ? -symbol : symbol;
        if (lane.Previous == identity) return null;
        lane.Previous = identity;
        if (start) {
            JamBootstrap? recovered = null;
            if (lane.Bits != 0 && now - lane.Started <= 90_000 && lane.Count > 0) {
                int remaining = 56 - lane.Count * lane.Bits;
                if (remaining > 0 && remaining + lane.Unknown.Count <= 8) {
                    for (int offset = lane.Count * lane.Bits; offset < 56; offset++) lane.Unknown.Add(offset);
                    var repaired = Repair(lane); if (repaired is not null) recovered = AcceptFragment(repaired, now);
                }
            }
            lane.Bits = symbol switch { 65 => 5, 66 => 2, 67 => 6, 68 => 10, _ => 0 };
            lane.Started = now; lane.Count = 0; lane.Unknown.Clear(); Array.Clear(lane.Data); return recovered;
        }
        if (lane.Bits == 0) return null;
        int value = symbol - 1;
        if (now - lane.Started > 90_000 || value >= (2 << lane.Bits)) { lane.Bits = 0; return null; }
        if ((value >> lane.Bits) != lane.Count % 2) {
            // 奇偶相位指出一个擦除位置。最多枚举 8 个未知位，只有 CRC 唯一确定的片段才保留。
            int end = Math.Min(56, (lane.Count + 1) * lane.Bits);
            if (lane.Unknown.Count + end - lane.Count * lane.Bits > 8 || end == 56) { lane.Bits = 0; return null; }
            for (int offset = lane.Count * lane.Bits; offset < end; offset++) lane.Unknown.Add(offset);
            lane.Count++;
        }
        for (int bit = 0; bit < lane.Bits; bit++) {
            int offset = lane.Count * lane.Bits + bit;
            if (offset < 56) lane.Data[offset / 8] |= (byte)(((value >> (lane.Bits - 1 - bit)) & 1) << (7 - offset % 8));
            else if (((value >> (lane.Bits - 1 - bit)) & 1) != 0) { lane.Bits = 0; return null; }
        }
        if (++lane.Count < (56 + lane.Bits - 1) / lane.Bits) return null;
        lane.Bits = 0;
        var packet = Repair(lane);
        return packet is null ? null : AcceptFragment(packet, now);
    }
    private static byte[]? Repair(Lane lane)
    {
        if (lane.Unknown.Count == 0) return JamFragmentProtocol.ValidFragment(lane.Data) ? lane.Data.ToArray() : null;
        byte[]? found = null;
        for (int value = 0; value < (1 << lane.Unknown.Count); value++) {
            var candidate = lane.Data.ToArray();
            for (int bit = 0; bit < lane.Unknown.Count; bit++) if ((value & (1 << bit)) != 0) {
                int offset = lane.Unknown[bit]; candidate[offset / 8] |= (byte)(1 << (7 - offset % 8));
            }
            if (!JamFragmentProtocol.ValidFragment(candidate)) continue;
            if (found is not null) return null;
            found = candidate;
        }
        return found;
    }
    internal JamBootstrap? AcceptFragment(byte[] packet, long now)
    {
        if (!JamFragmentProtocol.ValidFragment(packet)) return null;
        foreach (var id in _assemblies.Where(pair => now - pair.Value.Seen > 180_000).Select(pair => pair.Key).ToArray()) _assemblies.Remove(id);
        int generation = packet[1], index = packet[0] & 15;
        if (!_assemblies.TryGetValue(generation, out var assembly)) {
            if (_assemblies.Count >= 16) _assemblies.Remove(_assemblies.MinBy(pair => pair.Value.Seen).Key);
            assembly = new Assembly(); _assemblies.Add(generation, assembly);
        }
        assembly.Seen = now; assembly.Fragments[index] = packet[2..6];
        var blocks = assembly.Fragments;
        var missing = Enumerable.Range(0, 4).Where(i => blocks[i] is null).ToArray();
        if (missing.Length > 1 || (missing.Length == 1 && blocks[4] is null)) return null;
        var data = new byte[16];
        for (int i = 0; i < 4; i++) {
            if (blocks[i] is not null) blocks[i].CopyTo(data, i * 4);
            else for (int j = 0; j < 4; j++) { data[i * 4 + j] = blocks[4][j]; for (int k = 0; k < 4; k++) if (k != i) data[i * 4 + j] ^= blocks[k][j]; }
        }
        var bootstrap = data[14] == 0 && data[15] == 0 ? JamBootstrap.Parse(data[..14]) : null;
        return bootstrap is not null && SHA256.HashData(bootstrap.Data)[0] == generation ? bootstrap : null;
    }
}
