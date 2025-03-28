using System.Drawing;
using System.Drawing.Imaging;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Parameters;
using YeusepesModules.Common.ScreenUtilities;
using ZBar;
using HPPH;
using YeusepesModules.OSCQR.UI;

namespace YeusepesModules.OSCQR
{
    [ModuleTitle("OSCQR")]
    [ModuleDescription("A module to scan QR Codes using OSC.")]
    [ModuleType(ModuleType.Generic)]
    [ModuleInfo("https://github.com/Yeusepe/Yeusepes-Modules/wiki/OSCQR")]
    public class OSCQR : Module
    {
        // Instance of the generic screen utilities
        private ScreenUtilities screenUtilities;

        // Runtime storage for detected QR codes
        private List<string> savedQRCodes = new List<string>();
        private string lastDetectedQRCode = string.Empty;

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
            Error
        }


        #endregion

        #region Module Setup

        protected override void OnPreLoad()
        {
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("libiconv.dll", message => Log(message));
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("libzbar.dll", message => Log(message));

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

            // Instantiate the ScreenUtilities class.
            // NOTE: Instead of passing a lambda that calls RegisterParameter,
            // we pass a dummy lambda (or one that simply logs) so that parameter registration
            // isn’t attempted after the module is loaded.
            screenUtilities = new ScreenUtilities(
                LogDebug,
                GetSettingValue<string>,
                SetSettingValue,
                CreateTextBox,
                (parameter, name, mode, title, description) =>
                {
                    // Simply log the attempt. Parameters for ScreenUtilities were already registered.
                    LogDebug($"(ScreenUtilities) Skipped registering parameter: {parameter}");
                }
            );

            // Provide a callback so that every time a new image is captured,
            // the OSCQR module runs its QR detection logic.
            screenUtilities.SetWhatDoInCapture((IImage image) =>
            {
                DetectQRCode(image);
            });
        }

        protected override Task<bool> OnModuleStart()
        {
            // Initialize the generic screen utilities.
            var result = screenUtilities.OnModuleStart();
            Log($"Selected GPU: {screenUtilities.GetSelectedGraphicsCard()}");
            Log($"Selected Display: {screenUtilities.GetSelectedDisplay()}");
            // Clear any previous error.
            SendParameter(OSCQRParameter.Error, false);
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
            if (string.IsNullOrEmpty(lastDetectedQRCode))
            {
                Log("No QR code detected to save.");
                return;
            }

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

        public List<string> GetSavedQRCodes()
        {
            return new List<string>(savedQRCodes); // Return a copy of the saved QR codes
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
