using System.Drawing;
using System.Drawing.Imaging;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Parameters;
using YeusepesModules.Common.ScreenUtilities;
using ZBar;
using HPPH;
using YeusepesModules.OSCQR.UI;
using YeusepesModules.SPOTIOSC.Credentials;
using System.Text.Json;
using System.IO;



namespace YeusepesModules.OSCQR
{
    [ModuleTitle("OSCQR")]
    [ModuleDescription("A module to scan QR Codes using OSC.")]
    [ModuleType(ModuleType.Generic)]
    [ModuleInfo("https://github.com/Yeusepe/Yeusepes-Modules/wiki/OSCQR")]
    [ModuleSettingsWindow(typeof(SavedQRCodesWindow))]
    public class OSCQR : Module
    {
        public ScreenUtilities screenUtilities;

        private List<string> savedQRCodes = new List<string>();
        private string lastDetectedQRCode = string.Empty;

        private SpotifyTrackInfo lastSpotifyTrackInfo = null;
        private long? lastDetectedSpotifyCode = null;
        private SpotifyStripDetector _spcodeDetector;

        public event Action QRCodesUpdated;

        #region Module Enums

        public enum OSCQRSettings
        {
            SavedQRCodes,
            SaveImagesToggle
        }

        public enum OSCQRParameter
        {
            StartRecording,
            QRCodeFound,
            ReadQRCode,
            Error,
        SpotifyCodeFound
        }


        #endregion

        #region Module Setup

        protected override void OnPreLoad()
        {
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("libiconv.dll", message => Log(message));
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("libzbar.dll", message => Log(message));


            screenUtilities = ScreenUtilities.EnsureInitialized(
                LogDebug,
                GetSettingValue<String>,
                SetSettingValue,
                CreateTextBox
            );

            RegisterParameter<bool>(
                OSCQRParameter.StartRecording,
                "OSCQR/StartRecording",
                ParameterMode.ReadWrite,
                "Start Recording",
                "Trigger to start/stop screen capture."
            );

            RegisterParameter<bool>(
                OSCQRParameter.QRCodeFound,
                "OSCQR/QRCodeFound",
                ParameterMode.Write,
                "QR Code Found",
                "Indicates when a QR code has been detected."
            );

            RegisterParameter<bool>(
                OSCQRParameter.ReadQRCode,
                "OSCQR/ReadQRCode",
                ParameterMode.Read,
                "Read QR Code",
                "Trigger to save the current QR code."
            );

            RegisterParameter<bool>(
                OSCQRParameter.Error,
                "OSCQR/Error",
                ParameterMode.Write,
                "Error",
                "Indicates an error occurred during capture or processing."
            );

            RegisterParameter<bool>(
                OSCQRParameter.SpotifyCodeFound,
                "OSCQR/SpotifyCodeFound",
                ParameterMode.Write,
                "Spotify Code Found",
                "Indicates when a Spotify barcode has been detected."
            );            


            CreateCustomSetting(
                OSCQRSettings.SavedQRCodes,
                new StringModuleSetting(
                    "Saved QR Codes",
                    "View and open saved QR codes.",
                    typeof(SavedQRCodesView),
                    string.Join(";", savedQRCodes)
                )
            );

            CreateToggle(
                OSCQRSettings.SaveImagesToggle,
                "Save Captured Images",
                "Enable or disable saving debug images.",
                false
            );
           
            screenUtilities.SetWhatDoInCapture(DetectCodes);

            SetRuntimeView(typeof(SavedQRCodesRuntimeView));

            // Spotify strip detector (ONNX) narrows the search; decoding works without it (full-frame fallback).
            _ = Task.Run(async () =>
            {
                try
                {
                    var onnx = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "spcode_yolov8n.onnx");
                    if (!File.Exists(onnx))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(onnx));
                        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(2) };
                        var bytes = await client.GetByteArrayAsync("https://raw.githubusercontent.com/Yeusepe/spcode-detector/refs/heads/main/models/spcode_yolov8n.onnx");
                        await File.WriteAllBytesAsync(onnx + ".tmp", bytes);
                        File.Move(onnx + ".tmp", onnx, true);
                    }
                    _spcodeDetector = SpotifyStripDetector.TryCreate(onnx, LogDebug);
                    LogDebug($"Spotify strip detector {(_spcodeDetector != null ? "ready" : "unavailable, using full-frame decode")}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Spotify strip detector unavailable ({ex.Message}), using full-frame decode");
                }
            });
        }

        protected override Task<bool> OnModuleStart()
        {
            var result = screenUtilities.OnModuleStart();
            Log($"Selected GPU: {screenUtilities.GetSelectedGraphicsCard()}");
            Log($"Selected Display: {screenUtilities.GetSelectedDisplay()}");
            SendParameter(OSCQRParameter.Error, false);
            
            if (IsSpotifyCredentialsAvailable())
            {
                Log("Spotify credentials found - Spotify code scanning enabled");
            }
            else
            {
                Log("No Spotify credentials found - Spotify code scanning disabled");
            }
            
            return Task.FromResult(true);
        }

        #endregion

        #region Parameter Handling

        protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
        {
            switch (parameter.Lookup)
            {
                case OSCQRParameter.StartRecording:
                    bool shouldStart = parameter.GetValue<bool>();
                    if (shouldStart)
                    {
                        Log("Starting capture via ScreenUtilities.");
                        screenUtilities.StartCapture();
                    }
                    else
                    {
                        Log("Stopping capture via ScreenUtilities.");
                        screenUtilities.StopCapture();
                    }
                    break;

                case OSCQRParameter.ReadQRCode:
                    if (!string.IsNullOrEmpty(lastDetectedQRCode))
                    {
                        SaveCurrentQRCode();
                    }
                    break;
            }
        }

        #endregion

        #region Spotify Integration

        private bool IsSpotifyCredentialsAvailable()
        {
            try
            {
                var accessToken = CredentialManager.LoadAccessToken();
                var apiAccessToken = CredentialManager.LoadApiAccessToken();
                
                return !string.IsNullOrEmpty(accessToken) || !string.IsNullOrEmpty(apiAccessToken);
            }
            catch
            {
                return false;
            }
        }

        private async Task<SpotifyTrackInfo> GetSpotifyTrackInfoAsync(long mediaRef)
        {
            try
            {
                var accessToken = CredentialManager.LoadAccessToken();
                if (string.IsNullOrEmpty(accessToken))
                {
                    accessToken = CredentialManager.LoadApiAccessToken();
                }

                if (string.IsNullOrEmpty(accessToken))
                {
                    Log("No Spotify access token available");
                    return null;
                }

                return await SpotifyCodeDecoder.GetTrackInfoAsync(mediaRef, accessToken);
            }
            catch (Exception ex)
            {
                Log($"Error getting Spotify track info: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region QR Code Detection

        // Runs on the capture thread for each scanned frame; true keeps the fast scan rate.
        private bool DetectCodes(Bitmap bitmap)
        {
            try
            {
                if (GetSettingValue<bool>(OSCQRSettings.SaveImagesToggle))
                    SaveDebugImage(bitmap);

                string qrResult = ScanQRCode(bitmap);
                bool qrFound = !string.IsNullOrEmpty(qrResult);
                SendParameter(OSCQRParameter.QRCodeFound, qrFound);
                if (qrFound) lastDetectedQRCode = qrResult;

                bool spotifyFound = false;
                if (IsSpotifyCredentialsAvailable())
                {
                    var spotifyMediaRef = DetectSpotifyCode(bitmap);
                    spotifyFound = spotifyMediaRef.HasValue;
                    SendParameter(OSCQRParameter.SpotifyCodeFound, spotifyFound);
                    if (spotifyMediaRef.HasValue && spotifyMediaRef.Value != lastDetectedSpotifyCode)
                    {
                        lastDetectedSpotifyCode = spotifyMediaRef.Value;
                        Log($"New Spotify code detected: {spotifyMediaRef.Value}");
                        _ = Task.Run(async () =>
                        {
                            var trackInfo = await GetSpotifyTrackInfoAsync(spotifyMediaRef.Value);
                            if (trackInfo != null)
                            {
                                lastSpotifyTrackInfo = trackInfo;
                                Log($"Spotify code detected - {trackInfo.Type}: {trackInfo.Name}");
                            }
                        });
                    }
                }
                return qrFound || spotifyFound;
            }
            catch (Exception ex)
            {
                Log($"Error in DetectCodes: {ex.Message}");
                SendParameter(OSCQRParameter.Error, true);
                return false;
            }
        }

        // Detector ROIs first (colour frame, as the model was trained); full frame catches codes the model misses.
        private long? DetectSpotifyCode(Bitmap frame)
        {
            try
            {
                foreach (var roi in _spcodeDetector?.Detect(frame) ?? new List<Rectangle>())
                {
                    using var crop = frame.Clone(roi, frame.PixelFormat);
                    var mediaRef = SpotifyCodeDecoder.DetectSpotifyCode(crop);
                    if (mediaRef.HasValue) return mediaRef;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Spotify strip detector error: {ex.Message}");
            }
            // ponytail: full-frame pass capped at 1920px wide (4K took ~1.6s per frame); codes under ~100px at 4K are lost.
            if (frame.Width <= 1920) return SpotifyCodeDecoder.DetectSpotifyCode(frame);
            using var small = new Bitmap(frame, 1920, frame.Height * 1920 / frame.Width);
            return SpotifyCodeDecoder.DetectSpotifyCode(small);
        }

        private void SaveDebugImage(Bitmap bitmap)
        {
            try
            {
                string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string debugFolder = System.IO.Path.Combine(picturesPath, "OSCQR_DebugImages");
                System.IO.Directory.CreateDirectory(debugFolder);
                string fileName = $"debug_{DateTime.Now:yyyyMMdd_HHmmssfff}.png";
                string fullPath = System.IO.Path.Combine(debugFolder, fileName);
                bitmap.Save(fullPath, ImageFormat.Png);
                Log($"Saved debug image: {fullPath}");
            }
            catch (Exception ex)
            {
                Log($"Error saving debug image: {ex.Message}");
            }
        }

        // Capture thread only (ZBar scanners aren't thread-safe). QR only: skips ZBar's 1D barcode passes.
        [ThreadStatic] private static ImageScanner _qrScanner;

        public static string ScanQRCode(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            try
            {
                if (_qrScanner == null)
                {
                    _qrScanner = new ImageScanner { Cache = true };
                    _qrScanner.SetConfiguration(SymbolType.None, Config.Enable, 0);
                    _qrScanner.SetConfiguration(SymbolType.QRCODE, Config.Enable, 1);
                }

                int w = bitmap.Width, h = bitmap.Height;
                var gray = new byte[w * h];
                var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    unsafe
                    {
                        for (int y = 0; y < h; y++)
                        {
                            byte* row = (byte*)data.Scan0 + y * data.Stride;
                            for (int x = 0; x < w; x++)
                                gray[y * w + x] = (byte)((row[x * 4] * 29 + row[x * 4 + 1] * 150 + row[x * 4 + 2] * 77) >> 8);
                        }
                    }
                }
                finally { bitmap.UnlockBits(data); }

                using var image = new ZBar.Image { Width = (uint)w, Height = (uint)h, Format = ZBar.Image.FourCC('Y', '8', '0', '0'), Data = gray };
                _qrScanner.Scan(image);
                return image.Symbols.FirstOrDefault()?.Data ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"ZBar scan failed: {ex.GetBaseException().Message}");
            }
        }

        private void SaveCurrentQRCode()
        {
            if (!string.IsNullOrEmpty(lastDetectedQRCode))
            {
                if (!savedQRCodes.Contains(lastDetectedQRCode))
                {
                    savedQRCodes.Add(lastDetectedQRCode);
                    Log($"QR Code saved: {lastDetectedQRCode}");
                }
                else
                {
                    Log($"QR Code already exists: {lastDetectedQRCode}");
                }
            }
            
            if (lastSpotifyTrackInfo != null && lastDetectedSpotifyCode.HasValue)
            {
                var spotifyCodeInfo = $"Spotify {lastSpotifyTrackInfo.Type}: {lastSpotifyTrackInfo.Name} - {lastSpotifyTrackInfo.Url}";
                
                if (!savedQRCodes.Contains(spotifyCodeInfo))
                {
                    savedQRCodes.Add(spotifyCodeInfo);
                    Log($"Spotify Code saved: {spotifyCodeInfo}");
                }
                else
                {
                    Log($"Spotify Code already exists: {spotifyCodeInfo}");
                }
            }
            
            if (!string.IsNullOrEmpty(lastDetectedQRCode) || (lastSpotifyTrackInfo != null && lastDetectedSpotifyCode.HasValue))
            {
                QRCodesUpdated?.Invoke();
            }
        }

        public List<string> GetSavedQRCodes()
        {
            return new List<string>(savedQRCodes);
        }

        public SpotifyTrackInfo GetLastSpotifyTrackInfo()
        {
            return lastSpotifyTrackInfo;
        }

        public long? GetLastDetectedSpotifyCode()
        {
            return lastDetectedSpotifyCode;
        }

        public async Task TestSpotifyCodeFunctionality()
        {
            Console.WriteLine("=== Spotify Code Detection Test ===");
            
            var hasCredentials = IsSpotifyCredentialsAvailable();
            Console.WriteLine($"1. Spotify credentials available: {hasCredentials}");
            
            if (hasCredentials)
            {
                var accessToken = CredentialManager.LoadAccessToken() ?? CredentialManager.LoadApiAccessToken();
                var clientId = CredentialManager.ClientId ?? "58bd3c95768941ea9eb4350aaa033eb3";
                Console.WriteLine($"   Access Token: {(string.IsNullOrEmpty(accessToken) ? "None" : "Available")}");
                Console.WriteLine($"   Client ID: {clientId}");
            }
            else
            {
                Console.WriteLine("   No credentials available - some tests will be skipped");
            }
            
            Console.WriteLine("\n2. Testing with real Spotify code image...");
            try
            {
                var imagePath = "spcode-7ocNC8jszuZKlwz7vvgI7R.jpeg";
                if (File.Exists(imagePath))
                {
                    Console.WriteLine($"   Loading image: {imagePath}");
                    using (var bitmap = new Bitmap(imagePath))
                    {
                        Console.WriteLine($"   Image loaded: {bitmap.Width}x{bitmap.Height} pixels");
                        
                        var mediaRef = SpotifyCodeDecoder.DetectSpotifyCode(bitmap);
                        if (mediaRef.HasValue)
                        {
                            Console.WriteLine($"   SUCCESS: Detected media reference: {mediaRef.Value}");
                            
                            if (hasCredentials)
                            {
                                Console.WriteLine("   Fetching track info from Spotify API...");
                                var trackInfo = await GetSpotifyTrackInfoAsync(mediaRef.Value);
                                if (trackInfo != null)
                                {
                                    Console.WriteLine($"   SUCCESS: Found {trackInfo.Type} - {trackInfo.Name}");
                                    Console.WriteLine($"   URL: {trackInfo.Url}");
                                    if (trackInfo.Artists != null && trackInfo.Artists.Count > 0)
                                        Console.WriteLine($"   Artists: {string.Join(", ", trackInfo.Artists)}");
                                    if (!string.IsNullOrEmpty(trackInfo.Album))
                                        Console.WriteLine($"   Album: {trackInfo.Album}");
                                    if (!string.IsNullOrEmpty(trackInfo.Description))
                                        Console.WriteLine($"   Description: {trackInfo.Description}");
                                }
                                else
                                {
                                    Console.WriteLine("   FAILED: Could not retrieve track info from API");
                                }
                            }
                            else
                            {
                                Console.WriteLine("   Skipping API test - no credentials available");
                            }
                        }
                        else
                        {
                            Console.WriteLine("   FAILED: Could not detect media reference from image");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"   ERROR: Image file not found: {imagePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ERROR: {ex.Message}");
                Console.WriteLine($"   Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("\n3. Testing basic barcode detection algorithm...");
            try
            {
                var testBitmap = CreateTestBarcode();
                var mediaRef = SpotifyCodeDecoder.DetectSpotifyCode(testBitmap);
                Console.WriteLine($"   Algorithm test: {(mediaRef.HasValue ? $"Found media ref: {mediaRef.Value}" : "No barcode detected (expected)")}");
                testBitmap.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ERROR: {ex.Message}");
            }
            
            Console.WriteLine("\n=== Test Complete ===");
        }
        
        private Bitmap CreateTestBarcode()
        {
            var bitmap = new Bitmap(200, 100);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                var brush = new SolidBrush(Color.Black);
                for (int i = 0; i < 20; i++)
                {
                    var height = 20 + (i % 4) * 15;
                    g.FillRectangle(brush, i * 8, (100 - height) / 2, 6, height);
                }
            }
            return bitmap;
        }

        #endregion

    }
}
