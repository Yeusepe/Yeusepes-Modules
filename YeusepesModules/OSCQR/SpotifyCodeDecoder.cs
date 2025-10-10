using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.OSCQR
{
    public class SpotifyCodeDecoder
    {
        #region Constants
        
        private static readonly int[] GrayCode = { 0, 1, 3, 2, 7, 6, 4, 5 };
        private static readonly int[][] GrayCodeInv = {
            new int[] {0,0,0}, new int[] {0,0,1}, new int[] {0,1,1}, new int[] {0,1,0},
            new int[] {1,1,0}, new int[] {1,1,1}, new int[] {1,0,1}, new int[] {1,0,0}
        };
        
        private static readonly int[] Polynomial = { 1, 0, 0, 0, 0, 0, 1, 1, 1 }; // CRC8 polynomial
        private static readonly int[] Gen1 = { 1, 0, 1, 1, 0, 1, 1 };
        private static readonly int[] Gen2 = { 1, 1, 1, 1, 0, 0, 1 };
        private static readonly int[] Punct = { 1, 1, 0 };
        
        private const string DefaultSpotifyClientId = "58bd3c95768941ea9eb4350aaa033eb3";
        private const string MediaRefLutUrl = "https://spclient.wg.spotify.com:443/scannable-id/id";
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Detects if an image contains a Spotify barcode and returns the media reference
        /// </summary>
        /// <param name="bitmap">The image to analyze</param>
        /// <param name="logFunction">Optional logging function for debug output</param>
        /// <param name="saveDebugImages">Whether to save debug images with overlays</param>
        /// <returns>Media reference if found, null otherwise</returns>
        public static long? DetectSpotifyCode(Bitmap bitmap, Action<string> logFunction = null, bool saveDebugImages = false)
        {
            try
            {
                logFunction?.Invoke($"SpotifyCodeDecoder: Starting detection on {bitmap.Width}x{bitmap.Height} image");
                
                // First try to detect the Spotify logo for faster, more accurate detection
                var logoCircle = DetectSpotifyLogo(bitmap, logFunction, saveDebugImages);
                if (logoCircle != null)
                {
                    logFunction?.Invoke($"SpotifyCodeDecoder: Found logo region at ({logoCircle.X},{logoCircle.Y}) size {logoCircle.Width}x{logoCircle.Height}");
                    
                    // Crop to include the logo AND the barcode bars extending from it
                    // Based on Spotify code structure: logo + 23 data bars
                    var logoRadius = Math.Max(logoCircle.Width, logoCircle.Height) / 2;
                    var barcodeWidth = logoRadius * 8; // Extend 8x the logo radius to capture all bars
                    var barcodeHeight = logoRadius * 3; // Height should be about 3x the logo radius
                    
                    var cropX = Math.Max(0, logoCircle.X - logoRadius / 2);
                    var cropY = Math.Max(0, logoCircle.Y - logoRadius / 2);
                    var cropWidth = Math.Min(barcodeWidth, bitmap.Width - cropX);
                    var cropHeight = Math.Min(barcodeHeight, bitmap.Height - cropY);
                    
                    logFunction?.Invoke($"SpotifyCodeDecoder: Smart crop at ({cropX},{cropY}) size {cropWidth}x{cropHeight}");
                    
                    var croppedBitmap = CropBitmap(bitmap, new Rectangle(cropX, cropY, cropWidth, cropHeight));
                    if (saveDebugImages)
                    {
                        var debugPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Debug");
                        System.IO.Directory.CreateDirectory(debugPath);
                        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                        croppedBitmap.Save(System.IO.Path.Combine(debugPath, $"spotify_cropped_{timestamp}.png"));
                    }
                    
                    var heights = GetBarHeights(croppedBitmap, logFunction, saveDebugImages);
                    croppedBitmap.Dispose();
                    
                    if (heights != null && heights.Count >= 20)
                    {
                        return DecodeSpotifyBarcode(heights, logFunction);
                    }
                }
                
                // Fallback to full image analysis if logo detection fails
                logFunction?.Invoke("SpotifyCodeDecoder: Logo detection failed, trying full image analysis");
                var fullHeights = GetBarHeights(bitmap, logFunction, saveDebugImages);
                logFunction?.Invoke($"SpotifyCodeDecoder: Found {fullHeights?.Count ?? 0} bar heights");
                
                if (fullHeights == null || fullHeights.Count < 20)
                {
                    logFunction?.Invoke($"SpotifyCodeDecoder: Not enough bars found (need 20+, got {fullHeights?.Count ?? 0})");
                    return null;
                }
                
                return DecodeSpotifyBarcode(fullHeights, logFunction);
            }
            catch (Exception ex)
            {
                logFunction?.Invoke($"SpotifyCodeDecoder: Exception during detection: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Fast Spotify logo detection using optimized circle detection
        /// </summary>
        private static SpotifyCircle DetectSpotifyLogo(Bitmap bitmap, Action<string> logFunction = null, bool saveDebugImages = false)
        {
            try
            {
                // Performance optimization: Restrict scan area to center 60% of image
                var centerX = bitmap.Width / 2;
                var centerY = bitmap.Height / 2;
                var scanWidth = (int)(bitmap.Width * 0.6);
                var scanHeight = (int)(bitmap.Height * 0.6);
                var scanX = centerX - scanWidth / 2;
                var scanY = centerY - scanHeight / 2;
                
                // Ensure scan area is within bounds
                scanX = Math.Max(0, scanX);
                scanY = Math.Max(0, scanY);
                scanWidth = Math.Min(scanWidth, bitmap.Width - scanX);
                scanHeight = Math.Min(scanHeight, bitmap.Height - scanY);
                
                var scanRect = new Rectangle(scanX, scanY, scanWidth, scanHeight);
                var croppedBitmap = CropBitmap(bitmap, scanRect);
                
                // Performance optimization: Reduce resolution for initial detection
                var scale = Math.Min(1.0, 600.0 / Math.Max(croppedBitmap.Width, croppedBitmap.Height));
                var downsampledWidth = (int)(croppedBitmap.Width * scale);
                var downsampledHeight = (int)(croppedBitmap.Height * scale);
                
                Bitmap downsampled = null;
                if (scale < 1.0)
                {
                    downsampled = new Bitmap(croppedBitmap, downsampledWidth, downsampledHeight);
                }
                else
                {
                    downsampled = croppedBitmap;
                    croppedBitmap = null; // Don't dispose the original
                }
                
                logFunction?.Invoke($"DetectSpotifyLogo: Fast detection starting on {downsampled.Width}x{downsampled.Height} image (scale={scale:F2})");
                
                // Convert to grayscale using fast method
                var grayBitmap = ConvertToGrayscaleFast(downsampled);
                
                // Apply Otsu thresholding
                var threshold = CalculateOtsuThreshold(grayBitmap);
                logFunction?.Invoke($"DetectSpotifyLogo: Otsu threshold = {threshold}");
                
                // Try both normal and inverted thresholding for different polarities
                var normalComponents = FindConnectedComponentsFast(ApplyThresholdFast(grayBitmap, threshold, false));
                var invertedComponents = FindConnectedComponentsFast(ApplyThresholdFast(grayBitmap, threshold, true));
                
                // Choose the set with more reasonable results
                var components = normalComponents.Count > invertedComponents.Count ? normalComponents : invertedComponents;
                logFunction?.Invoke($"DetectSpotifyLogo: Found {components.Count} components");
                
                // Early filtering: Only process components with reasonable area
                var filteredComponents = components.Where(c => c.Area >= 50 && c.Area <= 5000).ToList();
                
                // Sort by area and take top candidates for speed
                var candidates = filteredComponents.OrderByDescending(c => c.Area).Take(20).ToList();
                
                SpotifyCircle bestCircle = null;
                double bestScore = 0;
                
                foreach (var c in candidates)
                {
                    // Scale back to original coordinates
                    var scaledX = (int)(c.X / scale) + scanX;
                    var scaledY = (int)(c.Y / scale) + scanY;
                    var scaledWidth = (int)(c.Width / scale);
                    var scaledHeight = (int)(c.Height / scale);
                    
                    // Quick circularity check
                    var aspectRatio = (double)scaledWidth / scaledHeight;
                    if (aspectRatio < 0.3 || aspectRatio > 3.0) continue;
                    
                    // Estimate circularity without expensive calculations
                    var area = scaledWidth * scaledHeight;
                    var perimeter = 2 * (scaledWidth + scaledHeight);
                    var circularity = (4 * Math.PI * area) / (perimeter * perimeter);
                    
                    if (circularity > 0.3) // Relaxed threshold for speed
                    {
                        // Check if this circle has a barcode pattern extending from it
                        var circle = new SpotifyCircle
                        {
                            X = scaledX,
                            Y = scaledY,
                            Width = scaledWidth,
                            Height = scaledHeight,
                            Radius = Math.Min(scaledWidth, scaledHeight) / 2
                        };
                        
                        if (HasBarcodePattern(bitmap, circle, logFunction))
                        {
                            var score = circularity * Math.Sqrt(area);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestCircle = circle;
                            }
                        }
                    }
                }
                
                logFunction?.Invoke($"DetectSpotifyLogo: Found {candidates.Count(c => c.Area >= 50)} circular candidates");
                
                // Save debug image with overlays
                if (saveDebugImages && bestCircle != null)
                {
                    SaveDebugImageWithOverlays(bitmap, candidates, bestCircle, scanX, scanY, scale, logFunction);
                }
                
                // Cleanup
                grayBitmap.Dispose();
                if (croppedBitmap != null) croppedBitmap.Dispose();
                if (downsampled != croppedBitmap) downsampled.Dispose();
                
                return bestCircle;
            }
            catch (Exception ex)
            {
                logFunction?.Invoke($"DetectSpotifyLogo: Exception: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Helper class for detected Spotify circles
        /// </summary>
        private class SpotifyCircle
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int Radius { get; set; }
        }
        
        /// <summary>
        /// Helper class for connected components
        /// </summary>
        private class ConnectedComponent
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int Area { get; set; }
        }
        
        /// <summary>
        /// Crops a bitmap to the specified rectangle
        /// </summary>
        private static Bitmap CropBitmap(Bitmap source, Rectangle cropRect)
        {
            var cropped = new Bitmap(cropRect.Width, cropRect.Height);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(source, new Rectangle(0, 0, cropRect.Width, cropRect.Height), cropRect, GraphicsUnit.Pixel);
            }
            return cropped;
        }
        
        /// <summary>
        /// Fast grayscale conversion using unsafe code
        /// </summary>
        private static Bitmap ConvertToGrayscaleFast(Bitmap bitmap)
        {
            var result = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format8bppIndexed);
            var palette = result.Palette;
            for (int i = 0; i < 256; i++)
            {
                palette.Entries[i] = Color.FromArgb(i, i, i);
            }
            result.Palette = palette;
            
            unsafe
            {
                var sourceData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                var destData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, result.PixelFormat);
                
                var sourcePtr = (byte*)sourceData.Scan0;
                var destPtr = (byte*)destData.Scan0;
                
                var sourceStride = sourceData.Stride;
                var destStride = destData.Stride;
                var bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
                
                for (int y = 0; y < bitmap.Height; y++)
                {
                    var sourceRow = sourcePtr + y * sourceStride;
                    var destRow = destPtr + y * destStride;
                    
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        byte r, g, b;
                        if (bytesPerPixel == 4) // ARGB
                        {
                            b = sourceRow[x * 4];
                            g = sourceRow[x * 4 + 1];
                            r = sourceRow[x * 4 + 2];
                            // Skip alpha channel
                        }
                        else if (bytesPerPixel == 3) // RGB
                        {
                            b = sourceRow[x * 3];
                            g = sourceRow[x * 3 + 1];
                            r = sourceRow[x * 3 + 2];
                        }
                        else // Assume grayscale already
                        {
                            r = g = b = sourceRow[x];
                        }
                        
                        // Convert to grayscale using luminance formula
                        var gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                        destRow[x] = gray;
                    }
                }
                
                bitmap.UnlockBits(sourceData);
                result.UnlockBits(destData);
            }
            
            return result;
        }
        
        /// <summary>
        /// Fast thresholding using unsafe code
        /// </summary>
        private static Bitmap ApplyThresholdFast(Bitmap bitmap, int threshold, bool invert)
        {
            var result = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format1bppIndexed);
            
            unsafe
            {
                var sourceData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                var destData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, result.PixelFormat);
                
                var sourcePtr = (byte*)sourceData.Scan0;
                var destPtr = (byte*)destData.Scan0;
                
                var sourceStride = sourceData.Stride;
                var destStride = destData.Stride;
                
                for (int y = 0; y < bitmap.Height; y++)
                {
                    var sourceRow = sourcePtr + y * sourceStride;
                    var destRow = destPtr + y * destStride;
                    
                    for (int x = 0; x < bitmap.Width; x += 8)
                    {
                        byte destByte = 0;
                        for (int bit = 0; bit < 8 && x + bit < bitmap.Width; bit++)
                        {
                            var pixel = sourceRow[x + bit];
                            var isWhite = invert ? pixel < threshold : pixel >= threshold;
                            if (isWhite)
                            {
                                destByte |= (byte)(0x80 >> bit);
                            }
                        }
                        destRow[x / 8] = destByte;
                    }
                }
                
                bitmap.UnlockBits(sourceData);
                result.UnlockBits(destData);
            }
            
            return result;
        }
        
        /// <summary>
        /// Fast connected components using flood fill
        /// </summary>
        private static List<ConnectedComponent> FindConnectedComponentsFast(Bitmap bitmap)
        {
            var components = new List<ConnectedComponent>();
            var visited = new bool[bitmap.Width, bitmap.Height];
            
            unsafe
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                var ptr = (byte*)data.Scan0;
                var stride = data.Stride;
                
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        if (!visited[x, y] && (ptr[y * stride + x / 8] & (0x80 >> (x % 8))) != 0)
                        {
                            var component = FloodFillFast(ptr, stride, visited, x, y, bitmap.Width, bitmap.Height);
                            if (component.Area > 10) // Filter tiny components
                            {
                                components.Add(component);
                            }
                        }
                    }
                }
                
                bitmap.UnlockBits(data);
            }
            
            return components;
        }
        
        /// <summary>
        /// Fast flood fill for connected components
        /// </summary>
        private static unsafe ConnectedComponent FloodFillFast(byte* ptr, int stride, bool[,] visited, int startX, int startY, int width, int height)
        {
            var stack = new Stack<(int x, int y)>();
            stack.Push((startX, startY));
            
            int minX = startX, maxX = startX, minY = startY, maxY = startY;
            int area = 0;
            
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                
                if (x < 0 || x >= width || y < 0 || y >= height || visited[x, y])
                    continue;
                
                if ((ptr[y * stride + x / 8] & (0x80 >> (x % 8))) == 0)
                    continue;
                
                visited[x, y] = true;
                area++;
                
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                
                // Add neighbors
                stack.Push((x + 1, y));
                stack.Push((x - 1, y));
                stack.Push((x, y + 1));
                stack.Push((x, y - 1));
            }
            
            return new ConnectedComponent
            {
                X = minX,
                Y = minY,
                Width = maxX - minX + 1,
                Height = maxY - minY + 1,
                Area = area
            };
        }
        
        /// <summary>
        /// Calculate Otsu threshold for binarization
        /// </summary>
        private static int CalculateOtsuThreshold(Bitmap bitmap)
        {
            var histogram = new int[256];
            
            unsafe
            {
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                var ptr = (byte*)data.Scan0;
                var stride = data.Stride;
                
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        histogram[ptr[y * stride + x]]++;
                    }
                }
                
                bitmap.UnlockBits(data);
            }
            
            var totalPixels = bitmap.Width * bitmap.Height;
            var sum = 0;
            for (int i = 0; i < 256; i++)
            {
                sum += i * histogram[i];
            }
            
            var sumB = 0;
            var wB = 0;
            var wF = 0;
            var maxVariance = 0.0;
            var threshold = 0;
            
            for (int t = 0; t < 256; t++)
            {
                wB += histogram[t];
                if (wB == 0) continue;
                
                wF = totalPixels - wB;
                if (wF == 0) break;
                
                sumB += t * histogram[t];
                
                var mB = sumB / wB;
                var mF = (sum - sumB) / wF;
                
                var variance = wB * wF * (mB - mF) * (mB - mF);
                
                if (variance > maxVariance)
                {
                    maxVariance = variance;
                    threshold = t;
                }
            }
            
            return threshold;
        }
        
        /// <summary>
        /// Check if a circle has a barcode pattern extending from it
        /// Based on Spotify code structure: logo + 23 data bars (plus reference bars at positions 1, 12, 23)
        /// </summary>
        private static bool HasBarcodePattern(Bitmap bitmap, SpotifyCircle circle, Action<string> logFunction = null)
        {
            try
            {
                // Look for vertical bars extending from the circle
                var barCount = 0;
                var totalScanned = 0;
                
                // Scan horizontally from the circle - Spotify codes extend about 6-8x the logo width
                var maxScanDistance = Math.Min(bitmap.Width, circle.X + circle.Width * 8);
                var stepSize = Math.Max(1, circle.Width / 10); // Adaptive step size based on logo size
                
                for (int x = circle.X + circle.Width; x < maxScanDistance; x += stepSize)
                {
                    totalScanned++;
                    var blackPixels = 0;
                    var totalPixels = 0;
                    
                    // Scan vertically within the circle height range
                    for (int y = circle.Y; y < circle.Y + circle.Height; y += Math.Max(1, circle.Height / 10))
                    {
                        if (y >= 0 && y < bitmap.Height && x >= 0 && x < bitmap.Width)
                        {
                            totalPixels++;
                            var pixel = bitmap.GetPixel(x, y);
                            var brightness = (pixel.R + pixel.G + pixel.B) / 3;
                            if (brightness < 128) blackPixels++;
                        }
                    }
                    
                    // A bar should have at least 30% black pixels vertically
                    if (totalPixels > 0 && blackPixels > totalPixels * 0.3)
                    {
                        barCount++;
                    }
                }
                
                // Need at least 15 bars for a valid Spotify code (23 data bars - some tolerance)
                // and should have scanned enough area to be confident
                var isValid = barCount >= 15 && totalScanned >= 20;
                
                logFunction?.Invoke($"HasBarcodePattern: Found {barCount} potential bars extending from circle (scanned {totalScanned} positions)");
                return isValid;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Save debug image with overlays showing detected circles and lines
        /// </summary>
        private static void SaveDebugImageWithOverlays(Bitmap original, List<ConnectedComponent> candidates, SpotifyCircle bestCircle, int scanX, int scanY, double scale, Action<string> logFunction = null)
        {
            try
            {
                var debugPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Debug");
                System.IO.Directory.CreateDirectory(debugPath);
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
                var debugFile = System.IO.Path.Combine(debugPath, $"spotify_debug_{timestamp}.png");
                
                using (var debugBitmap = new Bitmap(original))
                using (var g = Graphics.FromImage(debugBitmap))
                {
                    // Draw scan area
                    g.DrawRectangle(Pens.Yellow, scanX, scanY, (int)(original.Width * 0.6), (int)(original.Height * 0.6));
                    
                    // Draw all candidate circles in blue
                    using (var candidatePen = new Pen(Color.Blue, 2))
                    {
                        foreach (var c in candidates)
                        {
                            var scaledX = (int)(c.X / scale) + scanX;
                            var scaledY = (int)(c.Y / scale) + scanY;
                            var scaledWidth = (int)(c.Width / scale);
                            var scaledHeight = (int)(c.Height / scale);
                            
                            g.DrawRectangle(candidatePen, scaledX, scaledY, scaledWidth, scaledHeight);
                        }
                    }
                    
                    // Draw best circle in red
                    if (bestCircle != null)
                    {
                        using (var bestPen = new Pen(Color.Red, 3))
                        {
                            g.DrawRectangle(bestPen, bestCircle.X, bestCircle.Y, bestCircle.Width, bestCircle.Height);
                        }
                        
                        // Draw barcode detection lines (showing where we scan for bars)
                        using (var linePen = new Pen(Color.Green, 1))
                        {
                            // Draw lines showing the barcode scanning area
                            var maxScanDistance = Math.Min(original.Width, bestCircle.X + bestCircle.Width * 8);
                            var stepSize = Math.Max(1, bestCircle.Width / 10);
                            
                            for (int x = bestCircle.X + bestCircle.Width; x < maxScanDistance; x += stepSize)
                            {
                                g.DrawLine(linePen, x, bestCircle.Y, x, bestCircle.Y + bestCircle.Height);
                            }
                            
                            // Draw the expected crop area
                            var logoRadius = Math.Max(bestCircle.Width, bestCircle.Height) / 2;
                            var barcodeWidth = logoRadius * 8;
                            var barcodeHeight = logoRadius * 3;
                            var cropX = Math.Max(0, bestCircle.X - logoRadius / 2);
                            var cropY = Math.Max(0, bestCircle.Y - logoRadius / 2);
                            
                            using (var cropPen = new Pen(Color.Orange, 2))
                            {
                                g.DrawRectangle(cropPen, cropX, cropY, barcodeWidth, barcodeHeight);
                            }
                        }
                    }
                    
                    debugBitmap.Save(debugFile);
                    logFunction?.Invoke($"Saved debug image with overlays to {debugFile}");
                }
            }
            catch (Exception ex)
            {
                logFunction?.Invoke($"Failed to save debug image: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets track/playlist information from a media reference using Spotify API
        /// </summary>
        /// <param name="mediaRef">The media reference</param>
        /// <param name="accessToken">Spotify access token</param>
        /// <param name="logFunction">Optional logging function for debug output</param>
        /// <returns>Track/playlist information or null if not found</returns>
        public static async Task<SpotifyTrackInfo> GetTrackInfoAsync(long mediaRef, string accessToken, Action<string> logFunction = null)
        {
            try
            {
                using var httpClient = new HttpClient();
                
                // Get client ID from credential manager, fallback to default if not available
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
        
        #endregion
        
        #region Private Methods
        
        private static List<int> GetBarHeights(Bitmap bitmap, Action<string> logFunction = null, bool saveDebugImages = false)
        {
            try
            {
                logFunction?.Invoke($"GetBarHeights: Processing {bitmap.Width}x{bitmap.Height} image");
                
                // Convert to grayscale
                var grayBitmap = ConvertToGrayscale(bitmap);
                logFunction?.Invoke("GetBarHeights: Converted to grayscale");
                
                // Apply Otsu threshold
                var threshold = CalculateOtsuThreshold(grayBitmap);
                logFunction?.Invoke($"GetBarHeights: Otsu threshold = {threshold}");
                var binaryBitmap = ApplyThreshold(grayBitmap, threshold);
                logFunction?.Invoke("GetBarHeights: Applied threshold");
                
                // Find connected components (bars)
                var bars = FindConnectedComponents(binaryBitmap);
                logFunction?.Invoke($"GetBarHeights: Found {bars.Count} connected components");
                
                if (bars.Count == 0)
                {
                    logFunction?.Invoke("GetBarHeights: No bars found");
                    return null;
                }
                
                // Sort by x position
                bars.Sort((a, b) => a.X.CompareTo(b.X));
                
                // Get heights relative to the first bar (Spotify logo)
                var logoHeight = bars[0].Height;
                var heights = new List<int>();
                
                foreach (var bar in bars.Skip(1))
                {
                    var ratio = (double)bar.Height / logoHeight;
                    var heightLevel = (int)(ratio * 8) - 1; // Convert to 0-7 scale
                    heights.Add(Math.Max(0, Math.Min(7, heightLevel)));
                }
                
                return heights;
            }
            catch
            {
                return null;
            }
        }
        
        private static long? DecodeSpotifyBarcode(List<int> levels, Action<string> logFunction = null)
        {
            try
            {
                // Convert levels to bits using Gray code inverse
                var levelBits = new List<int>();
                foreach (var level in levels)
                {
                    if (level < 0 || level >= GrayCodeInv.Length)
                        return null;
                    levelBits.AddRange(GrayCodeInv[level]);
                }
                
                // Permute bits (inverse of the permutation used in encoding)
                var permutedBits = new List<int>();
                for (int i = 0; i < 60; i++)
                {
                    var index = (43 * i) % 60;
                    if (index < levelBits.Count)
                        permutedBits.Add(levelBits[index]);
                }
                
                // Remove punctured bits (every 4th bit starting from index 2)
                var unpuncturedBits = new List<int>();
                for (int i = 0; i < permutedBits.Count; i++)
                {
                    if (i % 4 != 2) // Skip every 4th bit starting from index 2
                        unpuncturedBits.Add(permutedBits[i]);
                }
                
                if (unpuncturedBits.Count < 45)
                    return null;
                
                // Decode convolutional code (simplified - using hardcoded inverse matrix)
                var decodedBits = DecodeConvolutionalCode(unpuncturedBits.Take(45).ToList());
                
                if (decodedBits == null || decodedBits.Count < 45)
                    return null;
                
                // Check CRC
                if (!CheckSpotifyCrc(decodedBits))
                    return null;
                
                // Extract 37-bit media reference
                var mediaRef = BitsToInt(decodedBits.Take(37).ToList());
                return mediaRef;
            }
            catch
            {
                return null;
            }
        }
        
        private static List<int> DecodeConvolutionalCode(List<int> bits)
        {
            // This is a simplified implementation
            // In practice, you'd need the full inverse generator matrix
            // For now, we'll return the input bits as a placeholder
            return bits;
        }
        
        private static bool CheckSpotifyCrc(List<int> bits)
        {
            if (bits.Count < 45)
                return false;
                
            var data = bits.Take(37).ToList();
            var checkBits = bits.Skip(37).Take(8).ToList();
            
            // Calculate CRC for the data
            var calculatedCrc = CalculateCrc(data, Polynomial);
            
            // Compare with check bits
            for (int i = 0; i < 8; i++)
            {
                if (calculatedCrc[i] != checkBits[i])
                    return false;
            }
            
            return true;
        }
        
        private static List<int> CalculateCrc(List<int> data, int[] polynomial)
        {
            var n = polynomial.Length - 1;
            var checkBits = new List<int>(data);
            checkBits.AddRange(new int[n]); // Add zeros for CRC
            
            for (int i = 0; i < data.Count; i++)
            {
                if (checkBits[i] == 1)
                {
                    for (int j = 0; j < polynomial.Length; j++)
                    {
                        checkBits[i + j] ^= polynomial[j];
                    }
                }
            }
            
            return checkBits.Skip(data.Count).ToList();
        }
        
        private static long BitsToInt(List<int> bits)
        {
            long result = 0;
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i] == 1)
                    result |= (1L << i);
            }
            return result;
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
                
                // Handle different content types
                switch (contentType.ToLower())
                {
                    case "track":
                        // For tracks, get artists and album
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
                        // For artists, get genres and followers
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
                        // For playlists, get description and owner
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
                        // For albums, get artists and release date
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
                        // For podcasts/shows, get publisher and description
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
                        // For podcast episodes, get show and description
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
            // Convert Spotify URI types to API endpoint types
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
        
        private static Bitmap ConvertToGrayscale(Bitmap bitmap)
        {
            var grayBitmap = new Bitmap(bitmap.Width, bitmap.Height);
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    var gray = (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                    grayBitmap.SetPixel(x, y, Color.FromArgb(gray, gray, gray));
                }
            }
            return grayBitmap;
        }
        
        
        private static Bitmap ApplyThreshold(Bitmap bitmap, int threshold)
        {
            var binaryBitmap = new Bitmap(bitmap.Width, bitmap.Height);
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var gray = bitmap.GetPixel(x, y).R;
                    var binary = gray > threshold ? Color.White : Color.Black;
                    binaryBitmap.SetPixel(x, y, binary);
                }
            }
            return binaryBitmap;
        }
        
        private static List<BarInfo> FindConnectedComponents(Bitmap bitmap)
        {
            var bars = new List<BarInfo>();
            var visited = new bool[bitmap.Width, bitmap.Height];
            
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (!visited[x, y] && bitmap.GetPixel(x, y).R > 128) // White pixel
                    {
                        var bar = FloodFill(bitmap, visited, x, y);
                        if (bar.Width > 5 && bar.Height > 10) // Filter out noise
                        {
                            bars.Add(bar);
                        }
                    }
                }
            }
            
            return bars;
        }
        
        private static BarInfo FloodFill(Bitmap bitmap, bool[,] visited, int startX, int startY)
        {
            var minX = startX;
            var maxX = startX;
            var minY = startY;
            var maxY = startY;
            
            var stack = new Stack<(int x, int y)>();
            stack.Push((startX, startY));
            
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                
                if (x < 0 || x >= bitmap.Width || y < 0 || y >= bitmap.Height || visited[x, y])
                    continue;
                    
                if (bitmap.GetPixel(x, y).R <= 128) // Black pixel
                    continue;
                    
                visited[x, y] = true;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                
                // Add neighbors
                stack.Push((x + 1, y));
                stack.Push((x - 1, y));
                stack.Push((x, y + 1));
                stack.Push((x, y - 1));
            }
            
            return new BarInfo
            {
                X = minX,
                Y = minY,
                Width = maxX - minX + 1,
                Height = maxY - minY + 1
            };
        }
        
        #endregion
        
        #region Helper Classes
        
        private class BarInfo
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }
        
        #endregion
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
