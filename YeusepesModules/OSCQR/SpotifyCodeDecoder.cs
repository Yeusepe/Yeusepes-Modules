using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.OSCQR
{
    /// <summary>
    /// Decodes Spotify Codes (logo + 23 bars). Format: bar heights -> Gray code -> 60 bits ->
    /// de-permute (step 43) -> tail-biting conv code (133/171 octal, punctured 110) -> 45 bits
    /// = 37-bit media ref (LSB first) + CRC-8 (poly 0x07, xorout 0xFF, over the ref as 5 LE bytes).
    /// Verified against sample codes and the worked example at boonepeter.github.io (ref 67775490487).
    /// </summary>
    public class SpotifyCodeDecoder
    {
        private const string DefaultSpotifyClientId = "58bd3c95768941ea9eb4350aaa033eb3";
        private const string MediaRefLutUrl = "https://spclient.wg.spotify.com:443/scannable-id/id";

        private static readonly int[] Gen1 = { 1, 0, 1, 1, 0, 1, 1 };
        private static readonly int[] Gen2 = { 1, 1, 1, 1, 0, 0, 1 };

        // Precomputed inverse of the 45->60 linear code: each data bit / parity check is a mask over the 60 code bits.
        private static readonly ulong[] DataMasks = new ulong[45];
        private static readonly ulong[] CheckMasks;

        static SpotifyCodeDecoder()
        {
            var gen = new List<ulong>(60);
            for (int t = 0; t < 90; t++)
            {
                if (t % 3 == 2) continue; // puncture
                var g = (t & 1) == 0 ? Gen1 : Gen2;
                int k = t / 2;
                ulong m = 0;
                for (int j = 0; j < 7; j++)
                    if (g[j] == 1) m ^= 1UL << (((k - j) % 45 + 45) % 45);
                gen.Add(m);
            }

            // Gauss-Jordan on [G | I]: pivot rows yield data bits, leftover rows are parity checks.
            var m45 = gen.ToArray();
            var e60 = Enumerable.Range(0, 60).Select(r => 1UL << r).ToArray();
            for (int c = 0; c < 45; c++)
            {
                int p = Array.FindIndex(m45, c, r => ((r >> c) & 1) == 1);
                (m45[c], m45[p]) = (m45[p], m45[c]);
                (e60[c], e60[p]) = (e60[p], e60[c]);
                for (int r = 0; r < 60; r++)
                {
                    if (r == c || ((m45[r] >> c) & 1) == 0) continue;
                    m45[r] ^= m45[c];
                    e60[r] ^= e60[c];
                }
            }
            Array.Copy(e60, DataMasks, 45);
            CheckMasks = e60.Skip(45).ToArray();
        }

        /// <summary>Finds and decodes a Spotify Code in the image (ideally a crop around the code). Returns the media reference.</summary>
        public static long? DetectSpotifyCode(Bitmap bitmap, Action<string> logFunction = null)
        {
            try
            {
                var tilts = new List<float>();
                var result = Decode(bitmap, tilts);
                if (result.HasValue) return result;

                // Tilted code: rotate so the dominant bar direction is vertical and retry.
                var dominant = tilts.GroupBy(a => (int)Math.Round(a / 5f)).Where(g => g.Count() >= 5)
                    .OrderByDescending(g => g.Count()).Take(1).Select(g => g.Average());
                foreach (var tilt in dominant)
                {
                    if (Math.Abs(tilt) < 3) continue;
                    using var upright = Rotate(bitmap, -tilt);
                    result = Decode(upright, null);
                    if (result.HasValue) return result;
                }
                logFunction?.Invoke($"SpotifyCodeDecoder: no valid code in {bitmap.Width}x{bitmap.Height} image");
                return null;
            }
            catch (Exception ex)
            {
                logFunction?.Invoke($"SpotifyCodeDecoder: {ex.Message}");
                return null;
            }
        }

        private static long? Decode(Bitmap bitmap, List<float> tilts)
        {
            var (gray, w, h) = ToGray(bitmap);
            int threshold = Otsu(gray);
            foreach (bool lightBars in new[] { true, false })
            {
                var comps = Components(gray, w, h, threshold, lightBars);
                var result = DecodeFromComponents(comps);
                if (result.HasValue) return result;
                tilts?.AddRange(comps.Where(c => !float.IsNaN(c.Tilt)).Select(c => c.Tilt));
            }
            return null;
        }

        private static Bitmap Rotate(Bitmap src, float degrees)
        {
            double rad = degrees * Math.PI / 180, cos = Math.Abs(Math.Cos(rad)), sin = Math.Abs(Math.Sin(rad));
            int w = (int)(src.Width * cos + src.Height * sin), h = (int)(src.Width * sin + src.Height * cos);
            var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(dst);
            g.Clear(Color.Gray);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            g.TranslateTransform(w / 2f, h / 2f);
            g.RotateTransform(degrees);
            g.DrawImage(src, -src.Width / 2f, -src.Height / 2f, src.Width, src.Height);
            return dst;
        }

        /// <summary>Decodes 23 bar levels (0-7, reference bars included). Tolerates one misread level. Null on CRC failure.</summary>
        public static long? DecodeLevels(IReadOnlyList<int> levels)
        {
            if (levels.Count != 23 || levels[0] != 0 || levels[22] != 0 || levels[11] != 7) return null;

            ulong bits = 0; // 60-bit stream, MSB of each Gray triplet first
            int n = 0;
            for (int i = 1; i < 22; i++)
            {
                if (i == 11) continue;
                int gray = levels[i] ^ (levels[i] >> 1);
                for (int b = 2; b >= 0; b--) bits |= (ulong)((gray >> b) & 1) << n++;
            }
            ulong y = 0;
            for (int i = 0; i < 60; i++) y |= ((bits >> (43 * i % 60)) & 1) << i;

            return DecodeCodeword(y) ?? Enumerable.Range(0, 60).Select(i => DecodeCodeword(y ^ (1UL << i))).FirstOrDefault(r => r.HasValue);
        }

        private static long? DecodeCodeword(ulong y)
        {
            foreach (var c in CheckMasks)
                if ((BitOperations.PopCount(c & y) & 1) != 0) return null;

            long mediaRef = 0;
            int check = 0;
            for (int i = 0; i < 45; i++)
            {
                long bit = BitOperations.PopCount(DataMasks[i] & y) & 1;
                if (i < 37) mediaRef |= bit << i;
                else check |= (int)bit << (i - 37);
            }

            int crc = 0;
            for (int i = 0; i < 5; i++)
            {
                crc ^= (int)((mediaRef >> (8 * i)) & 0xFF);
                for (int k = 0; k < 8; k++) crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
            }
            return (crc ^ 0xFF) == check ? mediaRef : null;
        }

        // Tilt: degrees the blob's long axis deviates from vertical (NaN unless clearly elongated).
        private record struct Comp(int MinX, int MinY, int MaxX, int MaxY, float Tilt)
        {
            public int W => MaxX - MinX + 1;
            public int H => MaxY - MinY + 1;
            public float Cx => (MinX + MaxX) / 2f;
            public float Cy => (MinY + MaxY) / 2f;
        }

        // Tries every run of 23 aligned, similar-width, evenly spaced blobs; the CRC is the final judge.
        // ponytail: rotation is corrected by the caller; strong perspective (foreshortened bar spacing) is not.
        private static long? DecodeFromComponents(List<Comp> comps)
        {
            comps.Sort((a, b) => a.Cx.CompareTo(b.Cx));
            var tried = new HashSet<(Comp, Comp)>();
            foreach (var seed in comps)
            {
                int bw = seed.W;
                var run = comps.Where(c => Math.Abs(c.W - bw) <= Math.Max(2, bw / 2) && Math.Abs(c.Cy - seed.Cy) <= 4 * bw + 2).ToList();
                for (int s = 0; s + 23 <= run.Count; s++)
                {
                    var bars = run.GetRange(s, 23);
                    if (!tried.Add((bars[0], bars[22]))) continue;
                    var levels = ToLevels(bars);
                    if (levels == null) continue;
                    var r = DecodeLevels(levels);
                    if (r.HasValue) return r;
                    levels.Reverse(); // upside-down code
                    r = DecodeLevels(levels);
                    if (r.HasValue) return r;
                }
            }
            return null;
        }

        private static List<int> ToLevels(List<Comp> bars)
        {
            var gaps = Enumerable.Range(1, 22).Select(i => bars[i].Cx - bars[i - 1].Cx).OrderBy(g => g).ToList();
            float med = gaps[11];
            if (med <= 0 || gaps[0] < 0.6f * med || gaps[21] > 1.6f * med) return null;

            // Reference bars: ends are level 0, middle is level 7. Compensate linear perspective scale along the strip.
            float d0 = bars[0].H, d22 = bars[22].H, mid = (d0 + d22) / 2f;
            float Scale(int i) => (d0 + (d22 - d0) * i / 22f) / mid;
            float lo = mid, hi = bars[11].H / Scale(11);
            if (hi < lo * 3) return null; // real codes: tallest bar is several times the dots

            var levels = new List<int>(23);
            for (int i = 0; i < 23; i++)
                levels.Add(Math.Clamp((int)Math.Round((bars[i].H / Scale(i) - lo) / (hi - lo) * 7), 0, 7));
            return levels;
        }

        private static (byte[] gray, int w, int h) ToGray(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[w * 4];
                var gray = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                        gray[y * w + x] = (byte)((row[x * 4] * 29 + row[x * 4 + 1] * 150 + row[x * 4 + 2] * 77) >> 8);
                }
                return (gray, w, h);
            }
            finally { bmp.UnlockBits(data); }
        }

        private static int Otsu(byte[] gray)
        {
            var hist = new long[256];
            foreach (var g in gray) hist[g]++;
            long total = gray.Length, sumAll = 0;
            for (int i = 0; i < 256; i++) sumAll += i * hist[i];
            long wB = 0, sumB = 0;
            double best = -1;
            int t = 128;
            for (int i = 0; i < 256; i++)
            {
                wB += hist[i];
                if (wB == 0) continue;
                long wF = total - wB;
                if (wF == 0) break;
                sumB += i * hist[i];
                double mB = (double)sumB / wB, mF = (double)(sumAll - sumB) / wF;
                double between = (double)wB * wF * (mB - mF) * (mB - mF);
                if (between > best) { best = between; t = i; }
            }
            return t;
        }

        private static List<Comp> Components(byte[] gray, int w, int h, int threshold, bool light)
        {
            var seen = new bool[gray.Length];
            var comps = new List<Comp>();
            var stack = new Stack<int>();
            for (int start = 0; start < gray.Length; start++)
            {
                if (seen[start] || (gray[start] > threshold) != light) continue;
                seen[start] = true;
                stack.Push(start);
                int minX = w, minY = h, maxX = 0, maxY = 0, area = 0;
                double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
                while (stack.Count > 0)
                {
                    int p = stack.Pop(), px = p % w, py = p / w;
                    area++;
                    sx += px; sy += py; sxx += (double)px * px; syy += (double)py * py; sxy += (double)px * py;
                    minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                    minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                    for (int k = 0; k < 4; k++) // 4-connected: blurred neighbouring bars touching at corners stay separate
                        {
                            int nx = px + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = py + (k == 2 ? -1 : k == 3 ? 1 : 0);
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int q = ny * w + nx;
                            if (seen[q] || (gray[q] > threshold) != light) continue;
                            seen[q] = true;
                            stack.Push(q);
                        }
                }
                // Second moments give the long-axis direction; only trust it for bar-like (elongated) blobs.
                double mx = sx / area, my = sy / area;
                double mu20 = sxx / area - mx * mx, mu02 = syy / area - my * my, mu11 = sxy / area - mx * my;
                double spread = Math.Sqrt((mu20 - mu02) * (mu20 - mu02) / 4 + mu11 * mu11), mean = (mu20 + mu02) / 2;
                float tilt = float.NaN;
                if (area >= 12 && mean + spread > 6 * (mean - spread))
                {
                    tilt = (float)(0.5 * Math.Atan2(2 * mu11, mu20 - mu02) * 180 / Math.PI) - 90; // from vertical
                    if (tilt <= -87.5f) tilt += 180; // keep near-horizontal bars in one cluster (~+90)
                }
                var c = new Comp(minX, minY, maxX, maxY, tilt);
                if (area >= 3 && c.H < h && (c.W < w / 8 || !float.IsNaN(tilt))) comps.Add(c);
            }
            return comps;
        }

        /// <summary>Resolves a media reference to track/playlist info via the Spotify API. Null if not found.</summary>
        public static async Task<SpotifyTrackInfo> GetTrackInfoAsync(long mediaRef, string accessToken, Action<string> logFunction = null)
        {
            try
            {
                using var httpClient = new HttpClient();
                
                var clientId = CredentialManager.ClientId;
                if (string.IsNullOrEmpty(clientId))
                {
                    clientId = DefaultSpotifyClientId;
                }
                
                // Add headers to mimic Spotify mobile app
                httpClient.DefaultRequestHeaders.Add("X-Client-Id", clientId);
                httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
                httpClient.DefaultRequestHeaders.Add("Connection", "close");
                httpClient.DefaultRequestHeaders.Add("App-Platform", "iOS");
                httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Spotify/8.5.68 iOS/13.4 (iPhone9,3)");
                httpClient.DefaultRequestHeaders.Add("Accept-Language", "en");
                httpClient.DefaultRequestHeaders.Add("Spotify-App-Version", "8.5.68");
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                
                var uriResponse = await httpClient.GetAsync($"{MediaRefLutUrl}/{mediaRef}?format=json");
                uriResponse.EnsureSuccessStatusCode();
                
                var uriJson = await uriResponse.Content.ReadAsStringAsync();
                var uriData = JsonSerializer.Deserialize<JsonElement>(uriJson);
                
                if (!uriData.TryGetProperty("target", out var targetElement))
                    return null;
                    
                var targetUri = targetElement.GetString();
                if (string.IsNullOrEmpty(targetUri))
                    return null;
                
                return await GetSpotifyInfoAsync(targetUri, accessToken, httpClient);
            }
            catch
            {
                return null;
            }
        }
        

        private static async Task<SpotifyTrackInfo> GetSpotifyInfoAsync(string uri, string accessToken, HttpClient httpClient)
        {
            try
            {
                var parts = uri.Split(':');
                if (parts.Length < 3)
                    return null;
                    
                var contentType = parts[1];
                var apiContentType = GetApiContentType(contentType);
                var id = parts[2];
                
                var response = await httpClient.GetAsync($"https://api.spotify.com/v1/{apiContentType}/{id}");
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(json);
                
                var info = new SpotifyTrackInfo
                {
                    Name = data.GetProperty("name").GetString(),
                    Type = contentType,
                    Url = data.GetProperty("external_urls").GetProperty("spotify").GetString()
                };
                
                switch (contentType.ToLower())
                {
                    case "track":
                        if (data.TryGetProperty("artists", out var trackArtists))
                        {
                            info.Artists = new List<string>();
                            foreach (var artist in trackArtists.EnumerateArray())
                            {
                                info.Artists.Add(artist.GetProperty("name").GetString());
                            }
                        }
                        if (data.TryGetProperty("album", out var albumElement))
                        {
                            info.Album = albumElement.GetProperty("name").GetString();
                        }
                        break;
                        
                    case "artist":
                        if (data.TryGetProperty("genres", out var genresElement))
                        {
                            var genres = new List<string>();
                            foreach (var genre in genresElement.EnumerateArray())
                            {
                                genres.Add(genre.GetString());
                            }
                            info.Description = $"Genres: {string.Join(", ", genres.Take(3))}";
                        }
                        break;
                        
                    case "playlist":
                        if (data.TryGetProperty("description", out var descElement))
                        {
                            info.Description = descElement.GetString();
                        }
                        if (data.TryGetProperty("owner", out var ownerElement))
                        {
                            var ownerName = ownerElement.GetProperty("display_name").GetString();
                            if (!string.IsNullOrEmpty(ownerName))
                            {
                                info.Description = string.IsNullOrEmpty(info.Description) 
                                    ? $"Created by {ownerName}"
                                    : $"{info.Description} (by {ownerName})";
                            }
                        }
                        if (data.TryGetProperty("tracks", out var tracksElement))
                        {
                            var totalTracks = tracksElement.GetProperty("total").GetInt32();
                            info.Album = $"{totalTracks} tracks";
                        }
                        break;
                        
                    case "album":
                        if (data.TryGetProperty("artists", out var albumArtists))
                        {
                            info.Artists = new List<string>();
                            foreach (var artist in albumArtists.EnumerateArray())
                            {
                                info.Artists.Add(artist.GetProperty("name").GetString());
                            }
                        }
                        if (data.TryGetProperty("release_date", out var releaseDate))
                        {
                            info.Description = $"Released: {releaseDate.GetString()}";
                        }
                        if (data.TryGetProperty("total_tracks", out var totalTracksElement))
                        {
                            info.Album = $"{totalTracksElement.GetInt32()} tracks";
                        }
                        break;
                        
                    case "show":
                        if (data.TryGetProperty("publisher", out var publisherElement))
                        {
                            info.Artists = new List<string> { publisherElement.GetString() };
                        }
                        if (data.TryGetProperty("description", out var showDescElement))
                        {
                            info.Description = showDescElement.GetString();
                        }
                        break;
                        
                    case "episode":
                        if (data.TryGetProperty("show", out var showElement))
                        {
                            info.Album = showElement.GetProperty("name").GetString();
                        }
                        if (data.TryGetProperty("description", out var episodeDescElement))
                        {
                            info.Description = episodeDescElement.GetString();
                        }
                        break;
                }
                
                return info;
            }
            catch
            {
                return null;
            }
        }
        
        private static string GetApiContentType(string contentType)
        {
            switch (contentType.ToLower())
            {
                case "track": return "tracks";
                case "artist": return "artists";
                case "playlist": return "playlists";
                case "album": return "albums";
                case "show": return "shows";
                case "episode": return "episodes";
                default: return contentType + "s";
            }
        }
    }

    public class SpotifyTrackInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Url { get; set; }
        public List<string> Artists { get; set; }
        public string Album { get; set; }
        public string Description { get; set; }
        
        public string ArtistsText
        {
            get
            {
                if (Artists == null || Artists.Count == 0)
                    return string.Empty;
                return "Artists: " + string.Join(", ", Artists);
            }
        }
    }
}
