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
        // Instance of the generic screen utilities
        public ScreenUtilities screenUtilities;

        // Runtime storage for detected QR codes
        private List<string> savedQRCodes = new List<string>();
        private string lastDetectedQRCode = string.Empty;

        // Spotify code detection
        private SpotifyTrackInfo lastSpotifyTrackInfo = null;
        private long? lastDetectedSpotifyCode = null;

        // Event to notify when QR codes list is updated
        public event Action QRCodesUpdated;

        #region Module Enums

        public enum OSCQRSettings
        {
            SavedQRCodes,
            SaveImagesToggle
            // GPU/Display settings are now handled by ScreenUtilities.
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
                LogDebug,         // Logging delegate
                GetSettingValue<String>,  // Function to retrieve settings
                SetSettingValue,  // Function to save settings
                CreateTextBox
            );

            // Register our module parameters.
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


            // Register a custom setting for viewing saved QR codes.
            CreateCustomSetting(
                OSCQRSettings.SavedQRCodes,
                new StringModuleSetting(
                    "Saved QR Codes",
                    "View and open saved QR codes.",
                    typeof(SavedQRCodesView),
                    string.Join(";", savedQRCodes)
                )
            );

            // Register a toggle for saving debug images.
            CreateToggle(
                OSCQRSettings.SaveImagesToggle,
                "Save Captured Images",
                "Enable or disable saving debug images.",
                false
            );
           
            // Provide a callback so that every time a new image is captured,
            // the OSCQR module runs its QR detection logic.
            screenUtilities.SetWhatDoInCapture((IImage image) =>
            {
                DetectQRCode(image);
            });

            // Register the runtime view to show saved QR codes in the runtime UI
            SetRuntimeView(typeof(SavedQRCodesRuntimeView));
        }

        protected override Task<bool> OnModuleStart()
        {
            // Initialize the generic screen utilities.
            var result = screenUtilities.OnModuleStart();
            Log($"Selected GPU: {screenUtilities.GetSelectedGraphicsCard()}");
            Log($"Selected Display: {screenUtilities.GetSelectedDisplay()}");
            // Clear any previous error.
            SendParameter(OSCQRParameter.Error, false);
            
            // Check if Spotify credentials are available
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
                // Check if we have a valid access token from the credential manager
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
                // Try to get access token from credential manager
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

        private void DetectQRCode(IImage image)
        {
            if (image == null)
            {
                Log("Received null image.");
                SendParameter(OSCQRParameter.QRCodeFound, false);
                return;
            }
            try
            {
                using (Bitmap bitmap = TransformIImageToBitmap(image))
                {
                    if (bitmap == null)
                    {
                        Log("Failed to convert IImage to Bitmap.");
                        SendParameter(OSCQRParameter.QRCodeFound, false);
                        return;
                    }

                    // Preprocess the image (for example, convert to grayscale)
                    Bitmap processedBitmap = PreprocessImage(bitmap);

                    // Optionally, save the image for debugging if the setting is enabled.
                    bool saveImages = GetSettingValue<bool>(OSCQRSettings.SaveImagesToggle);
                    if (saveImages)
                    {
                        SaveDebugImage(processedBitmap);
                    }

                    // Attempt to scan for a QR code.
                    string qrResult = ScanQRCode(processedBitmap);
                    if (!string.IsNullOrEmpty(qrResult))
                    {                        
                        SendParameter(OSCQRParameter.QRCodeFound, true);
                        lastDetectedQRCode = qrResult;
                    }
                    else
                    {                        
                        SendParameter(OSCQRParameter.QRCodeFound, false);
                    }

                    // Check for Spotify codes if credentials are available
                    bool hasCredentials = IsSpotifyCredentialsAvailable();
                    Log($"Spotify credentials available: {hasCredentials}");
                    
                    if (hasCredentials)
                    {
                        Log("Attempting Spotify code detection...");
                        var spotifyMediaRef = SpotifyCodeDecoder.DetectSpotifyCode(processedBitmap, Log);
                        Log($"Spotify code detection result: {(spotifyMediaRef.HasValue ? $"Found media ref {spotifyMediaRef.Value}" : "No code detected")}");
                        
                        if (spotifyMediaRef.HasValue && spotifyMediaRef.Value != lastDetectedSpotifyCode)
                        {
                            lastDetectedSpotifyCode = spotifyMediaRef.Value;
                            SendParameter(OSCQRParameter.SpotifyCodeFound, true);
                            Log($"New Spotify code detected: {spotifyMediaRef.Value}");
                            
                            // Get track info asynchronously
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var trackInfo = await GetSpotifyTrackInfoAsync(spotifyMediaRef.Value);
                                    if (trackInfo != null)
                                    {
                                        lastSpotifyTrackInfo = trackInfo;
                                        Log($"Spotify code detected - {trackInfo.Type}: {trackInfo.Name}");
                                    }
                                    else
                                    {
                                        Log("Failed to retrieve track info for Spotify code");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log($"Error processing Spotify code: {ex.Message}");
                                }
                            });
                        }
                        else if (!spotifyMediaRef.HasValue)
                        {
                            SendParameter(OSCQRParameter.SpotifyCodeFound, false);
                        }
                    }
                    else
                    {
                        Log("Skipping Spotify code detection - no credentials available");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error in DetectQRCode: {ex.Message}");
                SendParameter(OSCQRParameter.Error, true);
            }
        }

        private Bitmap PreprocessImage(Bitmap bitmap)
        {
            // Example: convert the image to grayscale.
            Bitmap grayBitmap = new Bitmap(bitmap.Width, bitmap.Height);
            using (Graphics g = Graphics.FromImage(grayBitmap))
            {
                var colorMatrix = new ColorMatrix(new float[][]
                {
                    new float[] {0.3f, 0.3f, 0.3f, 0, 0},
                    new float[] {0.59f, 0.59f, 0.59f, 0, 0},
                    new float[] {0.11f, 0.11f, 0.11f, 0, 0},
                    new float[] {0, 0, 0, 1, 0},
                    new float[] {0, 0, 0, 0, 1}
                });

                using (var attributes = new ImageAttributes())
                {
                    attributes.SetColorMatrix(colorMatrix);
                    g.DrawImage(bitmap,
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        0, 0, bitmap.Width, bitmap.Height,
                        GraphicsUnit.Pixel, attributes);
                }
            }
            return grayBitmap;
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

        public static string ScanQRCode(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            try
            {
                // Force a 24‑bit RGB copy (ZBarSharp will convert it to Y800 internally)
                using var rgb = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(rgb))
                    g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);

                using var scanner = new ImageScanner { Cache = true };
                var symbols = scanner.Scan(rgb);
                return symbols.FirstOrDefault()?.Data ?? string.Empty;
            }
            catch (Exception ex)
            {
                // Log the full inner exception message (and stacktrace if you like)
                throw new InvalidOperationException($"ZBar scan failed: {ex.GetBaseException().Message}");                
            }
        }



        private void SaveCurrentQRCode()
        {
            // Save QR code if available
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
            
            // Save Spotify code if available
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
            
            // Notify runtime view that codes list has been updated
            if (!string.IsNullOrEmpty(lastDetectedQRCode) || (lastSpotifyTrackInfo != null && lastDetectedSpotifyCode.HasValue))
            {
                QRCodesUpdated?.Invoke();
            }
        }

        public List<string> GetSavedQRCodes()
        {
            return new List<string>(savedQRCodes); // Return a copy of the saved QR codes
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
            
            // Test 1: Check credentials
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
            
            // Test 2: Real Spotify code image detection
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
            
            // Test 3: Basic barcode detection algorithm
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
            // Create a simple test bitmap
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

        #region Image Conversion Helpers

        public static Bitmap TransformIImageToBitmap(IImage image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image), "Input image is null.");

            try
            {
                int width = image.Width;
                int height = image.Height;
                if (width <= 0 || height <= 0)
                    throw new ArgumentException("Invalid image dimensions.");

                Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                try
                {
                    int bytesPerPixel = System.Drawing.Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
                    int totalBytes = bitmapData.Stride * height;
                    byte[] pixelBuffer = new byte[totalBytes];

                    for (int y = 0; y < height; y++)
                    {
                        IImageRow row = image.Rows[y];
                        for (int x = 0; x < width; x++)
                        {
                            IColor color = row[x];
                            int pixelIndex = (y * bitmapData.Stride) + (x * bytesPerPixel);
                            pixelBuffer[pixelIndex + 3] = color.A;
                            pixelBuffer[pixelIndex + 2] = color.R;
                            pixelBuffer[pixelIndex + 1] = color.G;
                            pixelBuffer[pixelIndex] = color.B;
                        }
                    }

                    System.Runtime.InteropServices.Marshal.Copy(pixelBuffer, 0, bitmapData.Scan0, totalBytes);
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Error transforming IImage to Bitmap.", ex);
            }
        }

        #endregion
    }
}
