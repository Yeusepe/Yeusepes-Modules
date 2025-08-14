using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using System.Net;

namespace YeusepesModules.ShazamOSC.ShazamAPI
{
    public interface IShazamUtilities
    {
        Action<string> Log { get; set; }
        Action<string> LogDebug { get; set; }
        Action<Enum, object> SendParameter { get; set; }
    }

    public class ShazamUtilities : IShazamUtilities
    {
        public Action<string> Log { get; set; }
        public Action<string> LogDebug { get; set; }
        public Action<Enum, object> SendParameter { get; set; }
    }

    /// <summary>
    /// C struct mapping for the 48‐byte signature header.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RawSignatureHeader
    {
        public uint magic1;
        public uint crc32;
        public uint size_minus_header;
        public uint magic2;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] void1;
        public uint shifted_sample_rate_id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] void2;
        public uint number_samples_plus_divided_sample_rate;
        public uint magic3;
    }

    /// <summary>
    /// Frequency bands used by Shazam.
    /// </summary>
    public enum FrequencyBand
    {
        band_0_250 = -1,
        band_250_520 = 0,
        band_520_1450 = 1,
        band_1450_3500 = 2,
        band_3500_5500 = 3
    }


    public class RingBuffer<T>
    {
        private readonly T[] _buffer;
        public int Position { get; set; }
        public int NumWritten { get; set; }
        public int BufferSize => _buffer.Length;

        public RingBuffer(int bufferSize, T defaultValue = default)
        {
            _buffer = Enumerable.Repeat(defaultValue, bufferSize).ToArray();
            Position = 0;
            NumWritten = 0;
        }

        public T this[int idx]
        {
            get => _buffer[idx];
            set => _buffer[idx] = value;
        }

        public void Append(T value)
        {
            _buffer[Position] = value;
            Position = (Position + 1) % BufferSize;
            NumWritten++;
        }
    }

    /// <summary>
    /// Holds one frequency‐peak detection.
    /// </summary>
    public class FrequencyPeak
    {
        public int FftPassNumber { get; }
        public int PeakMagnitude { get; }
        public int CorrectedPeakFrequencyBin { get; }
        public int SampleRateHz { get; }

        public FrequencyPeak(int fftPassNumber, int peakMagnitude, int correctedPeakFrequencyBin, int sampleRateHz)
        {
            FftPassNumber = fftPassNumber;
            PeakMagnitude = peakMagnitude;
            CorrectedPeakFrequencyBin = correctedPeakFrequencyBin;
            SampleRateHz = sampleRateHz;
        }

        public double GetFrequencyHz() =>
            CorrectedPeakFrequencyBin * (SampleRateHz / 2.0 / 1024.0 / 64.0);

        public double GetAmplitudePcm() =>
            Math.Sqrt(
                Math.Exp((PeakMagnitude - 6144.0) / 1477.3) * (1 << 17) / 2.0
            ) / 1024.0;

        public double GetSeconds() =>
            (FftPassNumber * 128.0) / SampleRateHz;
    }

    /// <summary>
    /// The Shazam signature (decoded or to be encoded).
    /// </summary>
    public class DecodedMessage
    {
        private const string DataUriPrefix = "data:audio/vnd.shazam.sig;base64,";
        private const int HeaderSize = 48;
        private const uint HeaderMagic1 = 0xCAFE2580;
        private const uint HeaderMagic2 = 0x94119C00;
        private const uint HeaderMagic3 = ((15u << 19) + 0x40000);

        // sample‑rate ↔ id mapping:
        private static readonly IReadOnlyDictionary<uint, int> ShiftedSampleRateFromId = new Dictionary<uint, int>
        {
            {1u << 27,  8000},
            {2u << 27, 11025},
            {3u << 27, 16000},
            {4u << 27, 32000},
            {5u << 27, 44100},
            {6u << 27, 48000},
        };
        private static readonly IReadOnlyDictionary<int, uint> ShiftedSampleRateToId =
            ShiftedSampleRateFromId.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

        public int SampleRateHz { get; set; }
        public int NumberSamples { get; set; }
        public Dictionary<FrequencyBand, List<FrequencyPeak>> FrequencyBandToSoundPeaks { get; set; }
            = new Dictionary<FrequencyBand, List<FrequencyPeak>>();

        public static DecodedMessage DecodeFromBinary(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            // Read header
            var header = reader.ReadBytes(HeaderSize);
            var hdrHandle = GCHandle.Alloc(header, GCHandleType.Pinned);
            RawSignatureHeader rawHeader;
            try
            {
                rawHeader = Marshal.PtrToStructure<RawSignatureHeader>(hdrHandle.AddrOfPinnedObject());
            }
            finally
            {
                hdrHandle.Free();
            }


            if (rawHeader.magic1 != HeaderMagic1 || rawHeader.magic2 != HeaderMagic2)
                throw new InvalidDataException("Invalid header magic.");

            if (rawHeader.size_minus_header != data.Length - HeaderSize)
                throw new InvalidDataException("Invalid size in header.");

            // CRC check
            ms.Position = 8;
            var rest = reader.ReadBytes(data.Length - 8);
            var crc = Crc32Algorithm.Compute(rest);
            if (crc != rawHeader.crc32)
                throw new InvalidDataException("CRC mismatch.");

            var msg = new DecodedMessage
            {
                SampleRateHz = ShiftedSampleRateFromId[rawHeader.shifted_sample_rate_id],
                NumberSamples = (int)(rawHeader.number_samples_plus_divided_sample_rate - rawHeader.shifted_sample_rate_id switch
                {
                    uint id when ShiftedSampleRateFromId.ContainsKey(id) => ShiftedSampleRateFromId[id] * 0.24,
                    _ => 0
                })
            };

            // Skip the initial TLV
            ms.Position = HeaderSize + 8;

            // Read TLVs
            while (ms.Position < ms.Length)
            {
                var bandId = reader.ReadUInt32();
                var size = reader.ReadInt32();
                var payload = reader.ReadBytes(size);
                var padding = (4 - (size % 4)) % 4;
                ms.Position += padding;

                var band = (FrequencyBand)(bandId - 0x60030040);
                var peaks = new List<FrequencyPeak>();
                using var pms = new MemoryStream(payload);
                using var pr = new BinaryReader(pms);

                int fftPass = 0;
                while (pms.Position < pms.Length)
                {
                    byte offset = pr.ReadByte();
                    if (offset == 0xFF)
                    {
                        fftPass = pr.ReadInt32();
                        continue;
                    }
                    fftPass += offset;
                    int magnitude = pr.ReadUInt16();
                    int freqBin = pr.ReadUInt16();
                    peaks.Add(new FrequencyPeak(fftPass, magnitude, freqBin, msg.SampleRateHz));
                }

                msg.FrequencyBandToSoundPeaks[band] = peaks;
            }

            return msg;
        }

        public static DecodedMessage DecodeFromUri(string uri)
        {
            if (!uri.StartsWith(DataUriPrefix))
                throw new ArgumentException("Not a valid Shazam data URI.");
            var bin = Convert.FromBase64String(uri.Substring(DataUriPrefix.Length));
            return DecodeFromBinary(bin);
        }


        public byte[] EncodeToBinary()
        {
            const int HeaderSize = 48;
            const uint HeaderMagic1 = 0xCAFE2580;
            const uint HeaderMagic2 = 0x94119C00;
            const uint HeaderMagic3 = (15u << 19) + 0x40000;

            // 1) Build the TLV “content” payload
            byte[] contents;
            using (var contentMs = new MemoryStream())
            using (var contentWriter = new BinaryWriter(contentMs, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var kv in FrequencyBandToSoundPeaks.OrderBy(k => k.Key))
                {
                    using var peaksMs = new MemoryStream();
                    using var pw = new BinaryWriter(peaksMs, Encoding.UTF8, leaveOpen: true);
                    int fftPass = 0;

                    foreach (var peak in kv.Value)
                    {
                        if (peak.FftPassNumber - fftPass >= 255)
                        {
                            pw.Write((byte)0xFF);
                            pw.Write(peak.FftPassNumber);
                            fftPass = peak.FftPassNumber;
                        }
                        pw.Write((byte)(peak.FftPassNumber - fftPass));
                        pw.Write((ushort)peak.PeakMagnitude);
                        pw.Write((ushort)peak.CorrectedPeakFrequencyBin);
                        fftPass = peak.FftPassNumber;
                    }

                    var payload = peaksMs.ToArray();
                    contentWriter.Write((uint)(0x60030040 + (int)kv.Key));
                    contentWriter.Write(payload.Length);
                    contentWriter.Write(payload);
                    int pad = (4 - (payload.Length % 4)) % 4;
                    for (int i = 0; i < pad; i++) contentWriter.Write((byte)0);
                }

                contents = contentMs.ToArray();
            }

            uint sizeMinusHeader = (uint)(contents.Length + 8);

            // 2) Prepare the header struct with crc32 = 0
            var header = new RawSignatureHeader
            {
                magic1 = HeaderMagic1,
                crc32 = 0, // placeholder
                size_minus_header = sizeMinusHeader,
                magic2 = HeaderMagic2,
                void1 = new uint[3],          // ensure these are zeroed
                shifted_sample_rate_id = ShiftedSampleRateToId[SampleRateHz],
                void2 = new uint[2],          // ensure these are zeroed
                number_samples_plus_divided_sample_rate = (uint)(NumberSamples + SampleRateHz * 0.24),
                magic3 = HeaderMagic3
            };

            // Marshal header (with crc32=0) into bytes
            int hdrSize = Marshal.SizeOf<RawSignatureHeader>();
            var headerBytes = new byte[hdrSize];
            var ptr = Marshal.AllocHGlobal(hdrSize);
            try
            {
                Marshal.StructureToPtr(header, ptr, false);
                Marshal.Copy(ptr, headerBytes, 0, hdrSize);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            // 3) Write header + TLV + content
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(headerBytes);                // real header (crc32 still zero)
                bw.Write(0x40000000);                // TLV magic
                bw.Write(sizeMinusHeader);           // TLV length
                bw.Write(contents);                  // the fingerprint data
            }

            // 4) Compute CRC‑32 over everything after the first 8 bytes
            ms.Position = 8;
            var rest = new byte[ms.Length - 8];
            ms.Read(rest, 0, rest.Length);
            uint crc = Crc32Algorithm.Compute(rest);

            // 5) Patch only the CRC field (bytes 4–7) in the existing header
            var crcBytes = BitConverter.GetBytes(crc);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(crcBytes);
            ms.Position = 4;
            ms.Write(crcBytes, 0, 4);

            return ms.ToArray();
        }


        public string EncodeToUri() =>
            DataUriPrefix + Convert.ToBase64String(EncodeToBinary());

        public object ToJson() =>
            new
            {
                sample_rate_hz = SampleRateHz,
                number_samples = NumberSamples,
                _seconds = (double)NumberSamples / SampleRateHz,
                frequency_band_to_peaks = FrequencyBandToSoundPeaks.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => kv.Value.Select(fp => new
                    {
                        fft_pass_number = fp.FftPassNumber,
                        peak_magnitude = fp.PeakMagnitude,
                        corrected_peak_frequency_bin = fp.CorrectedPeakFrequencyBin,
                        _frequency_hz = fp.GetFrequencyHz(),
                        _amplitude_pcm = fp.GetAmplitudePcm(),
                        _seconds = fp.GetSeconds()
                    }).ToArray()
                )
            };

        public override string ToString() =>
            JsonConvert.SerializeObject(ToJson(), Formatting.Indented);
    }

    /// <summary>
    /// Core FFT+peak‐finding signature generator.
    /// </summary>
    public class SignatureGenerator
    {
        private readonly IShazamUtilities _utils;
        private const int WindowSize = 2048;
        private const int StepSize = 128;
        private static readonly double[] HanningWindow =
            Window.Hann(2050).Skip(1).Take(WindowSize).ToArray();

        private readonly List<short> _inputBuffer = new();
        private int _samplesProcessed;
        private RingBuffer<short> _ringBufferSamples = new(WindowSize, 0);
        private RingBuffer<double[]> _fftOutputs = new(256, new double[1025]);
        private RingBuffer<double[]> _spreadFfts = new(256, new double[1025]);
        private DecodedMessage _nextSignature = new() { SampleRateHz = 16000, NumberSamples = 0 };

        public int SamplesProcessed => _samplesProcessed;
        public int MaxPeaks { get; set; } = 255;
        public double MaxTimeSeconds { get; set; } = 3.1;

        public SignatureGenerator(IShazamUtilities utils)
        {
            _utils = utils ?? throw new ArgumentNullException(nameof(utils));
        }

        public void FeedInput(IEnumerable<short> samples)
        {
            try
            {
                _utils.LogDebug("SignatureGenerator.FeedInput: start");
                _inputBuffer.AddRange(samples);
                _utils.LogDebug($"SignatureGenerator.FeedInput: buffer size {_inputBuffer.Count}");
            }
            catch (Exception ex)
            {
                _utils.Log($"SignatureGenerator.FeedInput: exception {ex}");
                throw;
            }
        }

        public DecodedMessage GetNextSignature()
        {
            try
            {
                _utils.LogDebug("SignatureGenerator.GetNextSignature: start");

                if (_inputBuffer.Count - _samplesProcessed < StepSize)
                {
                    _utils.LogDebug("SignatureGenerator.GetNextSignature: insufficient data, returning null");
                    return null;
                }

                while (_inputBuffer.Count - _samplesProcessed >= StepSize
                    && (_nextSignature.NumberSamples / (double)_nextSignature.SampleRateHz < MaxTimeSeconds
                        || _nextSignature.FrequencyBandToSoundPeaks.Values.Sum(l => l.Count) < MaxPeaks))
                {
                    var chunk = _inputBuffer
                        .Skip(_samplesProcessed)
                        .Take(StepSize)
                        .ToArray();

                    ProcessInput(chunk);
                    _samplesProcessed += StepSize;
                }

                var result = _nextSignature;
                Reset();
                _utils.LogDebug("SignatureGenerator.GetNextSignature: end (signature ready)");
                return result;
            }
            catch (Exception ex)
            {
                _utils.Log($"SignatureGenerator.GetNextSignature: exception {ex}");
                throw;
            }
        }

        private void ProcessInput(short[] samples)
        {
            try
            {
                // _utils.LogDebug("SignatureGenerator.ProcessInput: start");
                _nextSignature.NumberSamples += samples.Length;
                DoFft(samples);
                DoPeakSpreading();
                if (_spreadFfts.NumWritten >= 46)
                    DoPeakRecognition();
                // _utils.LogDebug("SignatureGenerator.ProcessInput: end");
            }
            catch (Exception ex)
            {
                _utils.Log($"SignatureGenerator.ProcessInput: exception {ex}");
                throw;
            }
        }

        private void DoFft(short[] batch)
        {
            try
            {
                // _utils.LogDebug("SignatureGenerator.DoFft: start");
                foreach (var s in batch)
                {
                    _ringBufferSamples.Append(s);
                }

                // extract + window + FFT
                var wrap = _ringBufferSamples.Position;
                var excerpt = new double[WindowSize];
                for (int i = 0; i < WindowSize; i++)
                    excerpt[i] = _ringBufferSamples[(wrap + i) % WindowSize] * HanningWindow[i];

                var complex = excerpt.Select(d => new Complex(d, 0)).ToArray();
                Fourier.Forward(complex, FourierOptions.Matlab);

                // power spectrum
                var power = new double[1025];
                for (int i = 0; i < power.Length; i++)
                {
                    power[i] = Math.Max((complex[i].Magnitude * complex[i].Magnitude) / (1 << 17), 1e-10);
                }

                _fftOutputs.Append(power);
                // _utils.LogDebug("SignatureGenerator.DoFft: end");
            }
            catch (Exception ex)
            {
                _utils.Log($"SignatureGenerator.DoFft: exception {ex}");
                throw;
            }
        }

        private void DoPeakSpreading()
        {
            try
            {
                // _utils.LogDebug("SignatureGenerator.DoPeakSpreading: start");

                // 1) Grab the last FFT output, with safe wrap
                int fftBufSize = _fftOutputs.BufferSize;
                int lastPos = (_fftOutputs.Position - 1 + fftBufSize) % fftBufSize;
                var last = _fftOutputs[lastPos];
                // _utils.LogDebug($"SignatureGenerator.DoPeakSpreading: using fftOutputs[{lastPos}] as 'last'");

                // 2) Clone for frequency‐domain spreading
                var spread = (double[])last.Clone();

                // freq‐domain spreading
                for (int i = 0; i < spread.Length; i++)
                {
                    if (i <= spread.Length - 3)
                        spread[i] = spread.Skip(i).Take(3).Max();
                }
                // _utils.LogDebug("SignatureGenerator.DoPeakSpreading: freq‐domain spreading done");

                // 3) Time‐domain spreading with safe index wrap
                int spreadBufSize = _spreadFfts.BufferSize;
                for (int i = 0; i < spread.Length; i++)
                {
                    double m = spread[i];
                    foreach (int offset in new[] { -1, -3, -6 })
                    {
                        int rawIndex = _spreadFfts.Position + offset;
                        int wrapIndex = ((rawIndex % spreadBufSize) + spreadBufSize) % spreadBufSize;
                        /*_utils.LogDebug(
                            $"SignatureGenerator.DoPeakSpreading: accessing spreadFfts raw={rawIndex}, wrapped={wrapIndex}, element={i}"
                        );*/
                        var prev = _spreadFfts[wrapIndex];
                        m = Math.Max(m, prev[i]);
                        prev[i] = m;
                    }
                }
                _spreadFfts.Append(spread);
                // _utils.LogDebug("SignatureGenerator.DoPeakSpreading: end");
            }
            catch (Exception ex)
            {
                _utils.Log($"SignatureGenerator.DoPeakSpreading: exception\n{ex}");
                throw;
            }
        }


        private void DoPeakRecognition()
        {
            try
            {
                // _utils.LogDebug("SignatureGenerator.DoPeakRecognition: start");

                int fftBufSize = _fftOutputs.BufferSize;
                int idx46 = (_fftOutputs.Position - 46 + fftBufSize) % fftBufSize;
                var fft46 = _fftOutputs[idx46];

                int spreadBufSize = _spreadFfts.BufferSize;
                int idx49 = (_spreadFfts.Position - 49 + spreadBufSize) % spreadBufSize;
                var sp49 = _spreadFfts[idx49];

                /*_utils.LogDebug(
                    $"DoPeakRecognition: fftPos={_fftOutputs.Position}, idx46={idx46}; " +
                    $"spreadPos={_spreadFfts.Position}, idx49={idx49}"
                );*/

                int fftPassNumber = _spreadFfts.NumWritten - 46;

                for (int bin = 10; bin < 1015; bin++)
                {
                    if (fft46[bin] < 1.0 / 64 || fft46[bin] < sp49[bin - 1]) continue;

                    double maxNeighborIn49 = new[] { -10, -7, -4, -3, 1, 4, 7 }
                        .Select(off => sp49[bin + off]).Max();
                    if (fft46[bin] <= maxNeighborIn49) continue;

                    double maxTimeNeighbor = maxNeighborIn49;
                    foreach (int off in new[] { -53, -45 }
                        .Concat(Enumerable.Range(165, 36).Where((_, i) => i % 7 == 0))
                        .Concat(Enumerable.Range(214, 36).Where((_, i) => i % 7 == 0)))
                    {
                        int j = (_spreadFfts.Position + off + spreadBufSize) % spreadBufSize;
                        maxTimeNeighbor = Math.Max(maxTimeNeighbor, _spreadFfts[j][bin - 1]);
                    }
                    if (fft46[bin] <= maxTimeNeighbor) continue;

                    double peakMag = Math.Log(Math.Max(1.0 / 64, fft46[bin])) * 1477.3 + 6144;
                    double magBefore = Math.Log(Math.Max(1.0 / 64, fft46[bin - 1])) * 1477.3 + 6144;
                    double magAfter = Math.Log(Math.Max(1.0 / 64, fft46[bin + 1])) * 1477.3 + 6144;
                    double var1 = peakMag * 2 - magBefore - magAfter;
                    if (var1 <= 0) throw new InvalidOperationException("peak_variation_1 <= 0");

                    double var2 = (magAfter - magBefore) * 32 / var1;
                    int correctedBin = (int)(bin * 64 + var2);
                    double freqHz = correctedBin * (16000.0 / 2.0 / 1024.0 / 64.0);

                    FrequencyBand band;
                    if (freqHz < 250) continue;
                    else if (freqHz < 520) band = FrequencyBand.band_250_520;
                    else if (freqHz < 1450) band = FrequencyBand.band_520_1450;
                    else if (freqHz < 3500) band = FrequencyBand.band_1450_3500;
                    else if (freqHz <= 5500) band = FrequencyBand.band_3500_5500;
                    else continue;

                    if (!_nextSignature.FrequencyBandToSoundPeaks.ContainsKey(band))
                        _nextSignature.FrequencyBandToSoundPeaks[band] = new List<FrequencyPeak>();

                    _nextSignature.FrequencyBandToSoundPeaks[band].Add(
                        new FrequencyPeak(fftPassNumber, (int)peakMag, correctedBin, 16000)
                    );
                }

                // _utils.LogDebug("SignatureGenerator.DoPeakRecognition: end");
            }
            catch (Exception ex)
            {
                _utils.Log($"SignatureGenerator.DoPeakRecognition: exception {ex}");
                throw;
            }
        }

        private void Reset()
        {            
            _nextSignature = new DecodedMessage
            {
                SampleRateHz = 16000,
                NumberSamples = 0,
                FrequencyBandToSoundPeaks = new Dictionary<FrequencyBand, List<FrequencyPeak>>()
            };

            
            _ringBufferSamples = new RingBuffer<short>(WindowSize, 0);
            _fftOutputs = new RingBuffer<double[]>(256, new double[1025]);
            _spreadFfts = new RingBuffer<double[]>(256, new double[1025]);
            
        }

    }

    /// <summary>
    /// High‐level Shazam API wrapper.
    /// </summary>
    public class Shazam
    {
        private readonly IShazamUtilities _utils;
        private static readonly HttpClient _http = new();

        private const string ApiTemplate = "https://amp.shazam.com/discovery/v5/{0}/{1}/iphone/-/tag/{2}/{3}";
        private static readonly Dictionary<string, string> BaseHeaders = new()
        {
            ["X-Shazam-Platform"] = "IPHONE",
            ["X-Shazam-AppVersion"] = "14.1.0",
            ["Accept"] = "*/*",
            ["Accept-Encoding"] = "gzip, deflate",
            ["User-Agent"] = "Shazam/3685 CFNetwork/1197 Darwin/20.0.0"
        };
        private static readonly Dictionary<string, string> DefaultParams = new()
        {
            ["sync"] = "true",
            ["webv3"] = "true",
            ["sampling"] = "true",
            ["connected"] = "",
            ["shazamapiversion"] = "v3",
            ["sharehub"] = "true",
            ["hubv5minorversion"] = "v5.1",
            ["hidelb"] = "true",
            ["video"] = "v3"
        };

        private readonly string _lang, _region, _timezone;
        public int MaxTimeSeconds { get; set; } = 8;

        static Shazam()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler);
        }

        public Shazam(IShazamUtilities utils, string lang = "ru", string region = "RU", string timezone = "Europe/Moscow")
        {
            _utils = utils ?? throw new ArgumentNullException(nameof(utils));
            _lang = lang;
            _region = region;
            _timezone = timezone;
        }

        public async IAsyncEnumerable<(int offsetSec, string json)> RecognizeSongAsync(string audioFilePath)
        {
            _utils.LogDebug($"Shazam.RecognizeSongAsync: start for {audioFilePath}");
            var samples = await LoadAndNormalizeAsync(audioFilePath);
            var sigGen = new SignatureGenerator(_utils) { MaxTimeSeconds = MaxTimeSeconds };
            sigGen.FeedInput(samples);

            while (true)
            {
                var sig = sigGen.GetNextSignature();
                if (sig == null) yield break;

                var offset = sigGen.SamplesProcessed / 16000;
                string json;

                try
                {
                    json = await SendRecognizeRequestAsync(sig);
                }
                catch (Exception ex)
                {
                    _utils.Log($"Shazam.SendRecognizeRequestAsync: exception {ex}");
                    throw;
                }

                yield return (offset, json);
            }
        }


        public async Task<List<short>> LoadAndNormalizeAsync(string path)
        {
            _utils.LogDebug($"Shazam.LoadAndNormalizeAsync: start {path}");
            try
            {
                using var reader = new MediaFoundationReader(path);
                var outFormat = new WaveFormat(16000, 16, 1);
                using var resampler = new MediaFoundationResampler(reader, outFormat)
                {
                    ResamplerQuality = 60
                };

                var buffer = new byte[outFormat.AverageBytesPerSecond];
                var samples = new List<short>();
                int bytesRead;

                await Task.Run(() =>
                {
                    while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < bytesRead; i += 2)
                            samples.Add(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2)));
                    }
                });

                _utils.LogDebug($"Shazam.LoadAndNormalizeAsync: end, samples {samples.Count}");
                return samples;
            }
            catch (Exception ex)
            {
                _utils.Log($"Shazam.LoadAndNormalizeAsync: exception {ex}");
                throw;
            }
        }

        private async Task<string> SendRecognizeRequestAsync(DecodedMessage sig)
        {
            _utils.LogDebug("Shazam.SendRecognizeRequestAsync: start");

            var uuidA = Guid.NewGuid().ToString().ToUpper();
            var uuidB = Guid.NewGuid().ToString().ToUpper();
            var uri = string.Format(ApiTemplate, _lang, _region, uuidA, uuidB);

            var payload = new
            {
                timezone = _timezone,
                signature = new
                {
                    uri = sig.EncodeToUri(),
                    samplems = (int)(sig.NumberSamples / (double)sig.SampleRateHz * 1000)
                },
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                context = new { },
                geolocation = new { }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, uri);
            foreach (var kv in BaseHeaders)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

            req.Headers.AcceptLanguage.ParseAdd(_lang);
            req.Content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json"
            );

            // Append default params to the query string
            var query = string.Join("&",
                DefaultParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            req.RequestUri = new Uri(req.RequestUri + "?" + query);

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            // The handler already decompresses GZIP/Deflate for you.
            var body = await resp.Content.ReadAsStringAsync();
            _utils.LogDebug("Shazam.SendRecognizeRequestAsync: success");
            return body;
        }
    }

    /// <summary>
    /// CRC32 calculator (e.g. from Force.Crc32 or custom).
    /// </summary>
    public static class Crc32Algorithm
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256)
            .Select(i =>
            {
                uint c = (uint)i;
                for (int j = 0; j < 8; j++)
                    c = ((c & 1) != 0) ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                return c;
            }).ToArray();

        public static uint Compute(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }
    }
}
