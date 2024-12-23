using System.Diagnostics;
using System.Drawing;
using System.Linq;
using VRCOSC.App.SDK.Modules;
using ScreenCapture.NET;
using YeusepesLowLevelTools;
using System.Windows.Forms;
using VRCOSC.App.SDK.Parameters;
using HPPH;
using ZXing;
using System.Drawing.Imaging;
using ZXing.Common;
using ZXing.Windows.Compatibility;
using System.IO;
using YeusepesModules.OSCQR.UI;
using VRCOSC.App.SDK.Modules.Attributes.Settings;

#pragma warning disable CA1416 // Validate platform compatibility

namespace YeusepesModules.OSCQR
{
    [ModuleTitle("OSCQR")]
    [ModuleDescription("A module to scan QR Codes using OSC.")]
    [ModuleType(ModuleType.Generic)]
    [ModuleInfo("https://github.com/Yeusepe/Yeusepes-Modules/wiki/OSCQR")]
    public class OSCQR : Module
    {
        private IScreenCaptureService screenCaptureService;
        private GraphicsCard? selectedGraphicsCard;
        private Display? selectedDisplay;

        private bool isCapturing = false;
        private Thread? captureThread;

        [ModulePersistent("SavedQRCodes")]
        private List<string> savedQRCodesPersistent { get; set; } = new List<string>();
        private DateTime lastSaveTime = DateTime.MinValue; // Store last save time


        // Runtime list to keep track of detected QR codes during operation
        private List<string> savedQRCodes = new List<string>();

        private string lastDetectedQRCode = string.Empty;

        public enum OSCQRSettings
        {
            SaveImagesToggle,
            SelectedGraphicsCard,
            SelectedDisplay,
            SavedQRCodes
        }

        public enum OSCQRGraphicsCardOption
        {
            Default
        }

        public enum OSCQRDisplayOption
        {
            Default
        }


        public enum OSCQRParameter
        {
            StartRecording,
            QRCodeFound,
            ReadQRCode,
            Error
        }


        protected override void OnPreLoad()
        {
            #region Parameters

            RegisterParameter<bool>(
                OSCQRParameter.StartRecording,
                "OSCQR/StartRecording",
                ParameterMode.ReadWrite,
                "Start Recording",
                "Trigger to start/stop recording. True to start, False to stop."
            );

            RegisterParameter<bool>(
                OSCQRParameter.QRCodeFound,
                "OSCQR/QRCodeFound",
                ParameterMode.Write,
                "QR Code Found",
                "Set to True when a QR Code is detected."
            );

            RegisterParameter<bool>(
                OSCQRParameter.ReadQRCode,
                "OSCQR/ReadQRCode",
                ParameterMode.Read,
                "Read QR Code",
                "Set to True when the user wants to save a qrcode."
            );

            RegisterParameter<bool>(
                OSCQRParameter.Error,
                "OSCQR/Error",
                ParameterMode.Write,
                "Error",
                "Set to True when an error occurs."
            );

            #endregion

            #region Settings

            CreateCustomSetting(
                OSCQRSettings.SavedQRCodes,
                new StringModuleSetting(
                    "Saved QR Codes",
                    "View and open saved QR codes.",
                    typeof(SavedQRCodesView),
                    string.Join(";", savedQRCodesPersistent) // Save as a semicolon-delimited string
                 )
             );

            CreateToggle(
                OSCQRSettings.SaveImagesToggle,
                "Save Captured Images",
                "Enable or disable saving debug images to the Pictures folder.",
                false // Default value: false (do not save images)
            );

            CreateCustomSetting(
                OSCQRSettings.SelectedGraphicsCard,
                new StringModuleSetting(
                    "Graphics Card",
                    "Select or type the name of your GPU. Suggestions will appear as you type.",
                    typeof(GraphicsCardSettingView), // The view class now supports the required constructor
                    "Default" // Default value
                )
            );

            CreateCustomSetting(
                OSCQRSettings.SelectedDisplay,
                new StringModuleSetting(
                    "Display",
                    "Select or type the name of your display. Suggestions will appear as you type.",
                    typeof(DisplaySettingView), // The custom view class
                    "Default" // Default value
                )
            );



            CreateGroup("Saved QR Codes", OSCQRSettings.SavedQRCodes);
            CreateGroup("Capture settings", OSCQRSettings.SaveImagesToggle, OSCQRSettings.SelectedGraphicsCard, OSCQRSettings.SelectedDisplay);

            SetRuntimeView(typeof(SavedQRCodesRuntimeView));

            #endregion

            // Initialize screen capture service
            screenCaptureService = new DX11ScreenCaptureService();

            // Automatically determine GPU and display
            if (!TryFindVRChatWindow())
            {
                Log("VRChat window not found or could not determine GPU/Display.");
                return;
            }

            if (screenCaptureService == null)
            {
                Log("Error: screenCaptureService could not be initialized.");
                return;
            }

            Log($"Automatically selected GPU: {selectedGraphicsCard?.Name}");
            Log($"Automatically selected Display: {selectedDisplay?.DeviceName}");

            // Restore persistent data
            foreach (var qrCodeText in savedQRCodesPersistent)
            {
                if (!savedQRCodes.Contains(qrCodeText))
                {
                    savedQRCodes.Add(qrCodeText);
                    Log($"Restored QR Code: {qrCodeText}");
                }
            }

            LogDebug($"Restored {savedQRCodes.Count} QR Codes from persistence.");
        }

        protected override Task<bool> OnModuleStart()
        {
            InitializeGraphicsAndDisplay();
            SendParameter(OSCQRParameter.Error, false);
            return Task.FromResult(true);
        }

        public string GetSelectedGraphicsCard()
        {
            return GetSettingValue<string>(OSCQRSettings.SelectedGraphicsCard);
        }

        public string GetSelectedDisplay()
        {
            return GetSettingValue<string>(OSCQRSettings.SelectedDisplay); // Retrieve the saved string
        }

        public List<string> GetGraphicsCards()
        {
            var cards = screenCaptureService?.GetGraphicsCards().Select(gc => gc.Name).ToList();
            if (cards != null)
            {
                cards.Insert(0, "Default"); // Add "Default" option for auto-detection
            }
            return cards ?? new List<string> { "Default" };
        }

        public List<string> GetSavedQRCodes()
        {
            return new List<string>(savedQRCodes); // Return a copy of the saved QR codes
        }


        public List<string> GetDisplays()
        {
            var defaultCard = screenCaptureService?.GetGraphicsCards().FirstOrDefault();
            var displays = defaultCard != null
                ? screenCaptureService.GetDisplays(defaultCard.Value).Select(d => d.DeviceName).ToList()
                : null;

            if (displays != null)
            {
                displays.Insert(0, "Default"); // Add "Default" option for auto-detection
            }

            return displays ?? new List<string> { "Default" };
        }


        private bool TryFindVRChatWindow()
        {
            IntPtr vrChatWindowHandle = NativeMethods.GetVRChatWindowHandle();
            if (vrChatWindowHandle == IntPtr.Zero)
            {
                Log("VRChat window handle not found.");
                return false;
            }

            var windowRect = NativeMethods.GetWindowRectangle(vrChatWindowHandle);
            if (windowRect == Rectangle.Empty)
            {
                Log("Failed to retrieve VRChat window rectangle.");
                return false;
            }

            LogDebug($"VRChat window rect: {windowRect}");

            var graphicsCards = screenCaptureService.GetGraphicsCards();
            if (!graphicsCards.Any())
            {
                Log("No graphics cards found.");
                return false;
            }

            selectedGraphicsCard = graphicsCards.First();

            if (selectedGraphicsCard == null)
            {
                Log("No valid graphics card found.");
                return false;
            }

            var displays = screenCaptureService.GetDisplays(selectedGraphicsCard.Value);
            if (!displays.Any())
            {
                Log("No displays found for the selected graphics card.");
                return false;
            }

            var vrChatCenter = new Point(windowRect.Left + windowRect.Width / 2, windowRect.Top + windowRect.Height / 2);

            var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds.Contains(vrChatCenter));
            if (screen == null)
            {
                Log("VRChat window is not on any detected screen.");
                return false;
            }

            LogDebug($"VRChat detected on screen: {screen.DeviceName}");

            selectedDisplay = displays.FirstOrDefault(d => d.DeviceName == screen.DeviceName);

            // Handle user-selected display
            string selectedDisplayName = null;
            try
            {
                selectedDisplayName = GetSelectedDisplay(); // Safely get the selected display setting
            }
            catch (Exception ex)
            {
                Log($"Error retrieving selected display: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(selectedDisplayName) && selectedDisplayName != "Default")
            {
                selectedDisplay = displays.FirstOrDefault(d => d.DeviceName == selectedDisplayName);
                if (selectedDisplay.HasValue)
                {
                    Log($"Using user-selected display: {selectedDisplay.Value.DeviceName}");
                    return true;
                }
                else
                {
                    Log($"User-selected display '{selectedDisplayName}' not found. Falling back to auto-detection.");
                }
            }

            return selectedDisplay.HasValue;
        }



        protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
        {
            switch (parameter.Lookup)
            {
                case OSCQRParameter.StartRecording:
                    LogDebug("Start Recording parameter received. Value: " + parameter.GetValue<bool>());
                    bool shouldStartRecording = parameter.GetValue<bool>();
                    if (shouldStartRecording && !isCapturing)
                    {
                        Log("Start Recording triggered.");
                        StartCapture();
                    }
                    else if (!shouldStartRecording && isCapturing)
                    {
                        Log("Stop Recording triggered.");
                        StopCapture();
                    }
                    break;

                case OSCQRParameter.ReadQRCode:
                    if (isCapturing)
                    {
                        SaveCurrentQRCode();
                    }
                    break;
            }
        }


        private void StartCapture()
        {
            SendParameter(OSCQRParameter.Error, false);
            if (screenCaptureService == null)
            {
                Log("Cannot start capture. screenCaptureService is null.");
                SendParameter(OSCQRParameter.Error, true);
                return;
            }

            if (selectedGraphicsCard == null)
            {
                Log("Cannot start capture. No GPU selected.");
                SendParameter(OSCQRParameter.Error, true);
                return;
            }

            if (selectedDisplay == null || !selectedDisplay.HasValue)
            {
                Log("Cannot start capture. No display selected.");
                SendParameter(OSCQRParameter.Error, true);
                return;
            }

            if (selectedDisplay.Value.Width <= 0 || selectedDisplay.Value.Height <= 0)
            {
                Log($"Cannot start capture. Invalid display dimensions: Width={selectedDisplay.Value.Width}, Height={selectedDisplay.Value.Height}");
                SendParameter(OSCQRParameter.Error, true);
                return;
            }

            isCapturing = true;

            captureThread = new Thread(() => RunCaptureLoop());
            captureThread.IsBackground = true;
            captureThread.Start();

            LogDebug("Started screen capture.");
        }


        private void RunCaptureLoop()
        {
            try
            {
                LogDebug("Entering RunCaptureLoop.");

                // Validate the screen capture service
                if (screenCaptureService == null)
                {
                    Log("Error: screenCaptureService is null. Cannot continue.");
                    SendParameter(OSCQRParameter.Error, true);
                    isCapturing = false;
                    return;
                }

                // Validate the selected display
                if (selectedDisplay == null || !selectedDisplay.HasValue)
                {
                    Log("Error: selectedDisplay is null or invalid. Cannot continue.");
                    SendParameter(OSCQRParameter.Error, true);
                    isCapturing = false;
                    return;
                }

                if (selectedDisplay.Value.Width <= 0 || selectedDisplay.Value.Height <= 0)
                {
                    Log($"Error: Invalid display dimensions: Width={selectedDisplay.Value.Width}, Height={selectedDisplay.Value.Height}");
                    SendParameter(OSCQRParameter.Error, true);
                    isCapturing = false;
                    return;
                }

                // Initialize screen capture
                var screenCapture = screenCaptureService.GetScreenCapture(selectedDisplay.Value);
                if (screenCapture == null)
                {
                    Log("Error: screenCapture is null. Cannot initialize screen capture.");
                    SendParameter(OSCQRParameter.Error, true);
                    isCapturing = false;
                    return;
                }

                if (screenCapture.Display == null || screenCapture.Display.Width <= 0 || screenCapture.Display.Height <= 0)
                {
                    Log($"Error: Invalid screen capture display dimensions: Width={screenCapture.Display.Width}, Height={screenCapture.Display.Height}");
                    SendParameter(OSCQRParameter.Error, true);
                    isCapturing = false;
                    return;
                }

                LogDebug($"Screen capture initialized. Display Width: {screenCapture.Display.Width}, Display Height: {screenCapture.Display.Height}");

                // Attempt to register the capture zone
                ICaptureZone? captureZone = null;
                try
                {
                    LogDebug("Attempting to register capture zone.");
                    captureZone = screenCapture.RegisterCaptureZone(
                        0, 0, screenCapture.Display.Width, screenCapture.Display.Height
                    );

                    if (captureZone == null)
                    {
                        SendParameter(OSCQRParameter.Error, true);
                        throw new NullReferenceException("RegisterCaptureZone returned null.");                        
                    }

                    LogDebug("Capture zone registered successfully.");
                }
                catch (Exception ex)
                {
                    Log($"Error during RegisterCaptureZone: {ex.Message}. Stack Trace: {ex.StackTrace}");
                    SendParameter(OSCQRParameter.Error, true);
                    isCapturing = false;
                    return;
                }

                BringVRChatToFront();

                // Main capture loop
                while (isCapturing)
                {
                    try
                    {
                        screenCapture.CaptureScreen();
                        LogDebug("Screen captured successfully.");

                        using (captureZone.Lock())
                        {
                            var image = captureZone.Image;
                            if (image != null)
                            {
                                LogDebug("Processing captured image.");
                                DetectQRCode(image);
                            }
                            else
                            {
                                LogDebug("No image captured in the current frame.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error during screen capture or processing: {ex.Message}");
                        SendParameter(OSCQRParameter.Error, true);
                    }

                    // Adjust the sleep interval based on performance needs
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                Log($"Critical error in capture loop: {ex.Message}. Stack Trace: {ex.StackTrace}");
                SendParameter(OSCQRParameter.Error, true);
                isCapturing = false;
            }
        }









        private void ProcessFrame(IScreenCapture screenCapture, ICaptureZone captureZone)
        {
            try
            {
                screenCapture.CaptureScreen();
                LogDebug("Screen captured successfully.");

                using (captureZone.Lock())
                {
                    var image = captureZone.Image;
                    if (image != null)
                    {
                        LogDebug("Processing captured image.");
                        DetectQRCode(image);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error during screen capture or processing: {ex.Message}");
            }
        }


        private void SaveCurrentQRCode()
        {
            if (string.IsNullOrEmpty(lastDetectedQRCode))
            {
                LogDebug("No QR code detected to save.");
                return;
            }

            try
            {
                // Avoid adding duplicates
                if (!savedQRCodes.Contains(lastDetectedQRCode))
                {
                    // Add to runtime list
                    savedQRCodes.Add(lastDetectedQRCode);

                    // Add to persistent list
                    savedQRCodesPersistent.Add(lastDetectedQRCode);

                    Log($"QR Code '{lastDetectedQRCode}' saved. Total saved: {savedQRCodes.Count}");
                }
                else
                {
                    Log($"QR Code '{lastDetectedQRCode}' is already in the saved list.");
                }
            }
            catch (Exception ex)
            {
                Log($"Error saving QR Code: {ex.Message}");
                SendParameter(OSCQRParameter.Error, true);
            }
        }



        private void DetectQRCode(IImage image)
        {
            if (image == null)
            {
                Log("Error: Input image is null.");
                SendParameter(OSCQRParameter.QRCodeFound, false);
                return;
            }

            try
            {
                using (var bitmap = TransformIImageToBitmap(image))
                {
                    if (bitmap == null)
                    {
                        Log("Error: Transformed bitmap is null.");
                        SendParameter(OSCQRParameter.QRCodeFound, false);
                        return;
                    }

                    // Preprocess the image for better QR code detection
                    Bitmap preprocessedBitmap = PreprocessImage(bitmap);

                    // Check the save images setting
                    bool saveImages = GetSettingValue<bool>(OSCQRSettings.SaveImagesToggle);

                    if (saveImages)
                    {
                        if ((DateTime.Now - lastSaveTime).TotalSeconds >= 1)
                        {
                            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                            string debugFolderPath = Path.Combine(documentsPath, "OSCQR_DebugImages");
                            Directory.CreateDirectory(debugFolderPath);
                            string debugImagePath = Path.Combine(debugFolderPath, $"debug_{DateTime.Now:yyyyMMdd_HHmmssfff}_preprocessed.png");
                            preprocessedBitmap.Save(debugImagePath, ImageFormat.Png);
                            LogDebug($"Saved preprocessed image for debugging: {debugImagePath}");
                            lastSaveTime = DateTime.Now;
                        }
                    }

                    // Process the image to detect QR codes
                    var qrCodeResult = ScanQRCode(preprocessedBitmap);
                    if (!string.IsNullOrEmpty(qrCodeResult))
                    {
                        LogDebug($"QR Code Found: {qrCodeResult}");
                        SendParameter(OSCQRParameter.QRCodeFound, true);

                        // Update the last detected QR code
                        lastDetectedQRCode = qrCodeResult;
                    }
                    else
                    {
                        LogDebug("No QR code detected in the processed image.");
                        SendParameter(OSCQRParameter.QRCodeFound, false);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error in DetectQRCode: {ex.Message}. Stack Trace: {ex.StackTrace}");
                SendParameter(OSCQRParameter.Error, true);
            }
        }

        private Bitmap PreprocessImage(Bitmap bitmap)
        {
            try
            {
                Bitmap grayBitmap = new Bitmap(bitmap.Width, bitmap.Height);
                using (Graphics g = Graphics.FromImage(grayBitmap))
                {
                    // Convert to grayscale
                    var colorMatrix = new ColorMatrix(
                        new float[][]
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
                        g.DrawImage(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
                    }
                }

                // Increase contrast
                return AdjustContrast(grayBitmap, 1.5f);
            }
            catch (Exception ex)
            {
                Log($"Error in PreprocessImage: {ex.Message}");
                SendParameter(OSCQRParameter.Error, true);
                return bitmap; // Return the original if preprocessing fails
            }
        }

        private Bitmap AdjustContrast(Bitmap image, float contrast)
        {
            Bitmap adjustedImage = new Bitmap(image.Width, image.Height);
            float t = (1.0f - contrast) / 2.0f;

            var colorMatrix = new ColorMatrix(
                new float[][]
                {
            new float[] {contrast, 0, 0, 0, 0},
            new float[] {0, contrast, 0, 0, 0},
            new float[] {0, 0, contrast, 0, 0},
            new float[] {0, 0, 0, 1, 0},
            new float[] {t, t, t, 0, 1}
                });

            using (Graphics g = Graphics.FromImage(adjustedImage))
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(colorMatrix);
                g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }

            return adjustedImage;
        }






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
                    int bytesPerPixel = Image.GetPixelFormatSize(bitmap.PixelFormat) / 8;
                    int totalBytes = bitmapData.Stride * height;
                    byte[] pixelBuffer = new byte[totalBytes];

                    for (int y = 0; y < height; y++)
                    {
                        IImageRow row = image.Rows[y];
                        for (int x = 0; x < width; x++)
                        {
                            IColor color = row[x];

                            int pixelIndex = (y * bitmapData.Stride) + (x * bytesPerPixel);
                            pixelBuffer[pixelIndex + 3] = color.A; // Alpha
                            pixelBuffer[pixelIndex + 2] = color.R; // Red
                            pixelBuffer[pixelIndex + 1] = color.G; // Green
                            pixelBuffer[pixelIndex] = color.B;     // Blue
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




        // QR Code scanning method
        public static string ScanQRCode(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap), "Bitmap is null.");

            try
            {
                var barcodeReader = new BarcodeReader
                {
                    AutoRotate = true,
                    TryInverted = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = true,
                        PureBarcode = false,
                        PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                        ReturnCodabarStartEnd = false
                    }
                };


                var result = barcodeReader.Decode(bitmap);
                return result?.Text ?? string.Empty;
            }
            catch (Exception ex)
            {                
                throw new InvalidOperationException("Error scanning QR code from bitmap.", ex);
            }
        }





        private void InitializeGraphicsAndDisplay()
        {
            string selectedGPUName = GetSelectedGraphicsCard();
            string selectedDisplayName = GetSelectedDisplay();

            // If GPU is "Default," autodetect VRChat
            if (selectedGPUName == "Default")
            {
                if (!TryFindVRChatWindow())
                {
                    Log("Failed to autodetect VRChat window for GPU.");
                    return;
                }
                Log($"Autodetected GPU: {selectedGraphicsCard?.Name}");
            }
            else
            {
                // Manually set the GPU
                var graphicsCards = screenCaptureService?.GetGraphicsCards();
                selectedGraphicsCard = graphicsCards?.FirstOrDefault(gc => gc.Name.Equals(selectedGPUName, StringComparison.OrdinalIgnoreCase));

                if (selectedGraphicsCard == null)
                {
                    Log($"Failed to find GPU: {selectedGPUName}. Defaulting to autodetection.");
                    if (!TryFindVRChatWindow())
                    {
                        return;
                    }
                }
                else
                {
                    Log($"Manually selected GPU: {selectedGraphicsCard?.Name}");
                }
            }

            // If Display is "Default," autodetect VRChat display
            if (selectedDisplayName == "Default")
            {
                if (selectedGraphicsCard != null && !TryFindVRChatWindow())
                {
                    Log("Failed to autodetect VRChat window for display.");
                }
                Log($"Autodetected Display: {selectedDisplay?.DeviceName}");
            }
            else
            {
                // Manually set the display
                var displays = screenCaptureService?.GetDisplays(selectedGraphicsCard.Value);
                selectedDisplay = displays?.FirstOrDefault(d => d.DeviceName.Equals(selectedDisplayName, StringComparison.OrdinalIgnoreCase));

                if (selectedDisplay == null)
                {
                    Log($"Failed to find display: {selectedDisplayName}. Defaulting to autodetection.");
                    if (!TryFindVRChatWindow())
                    {
                        return;
                    }
                }
                else
                {
                    Log($"Manually selected Display: {selectedDisplay?.DeviceName}");
                }
            }
        }

        private void BringVRChatToFront()
        {
            IntPtr vrChatWindowHandle = NativeMethods.GetVRChatWindowHandle();

            if (vrChatWindowHandle == IntPtr.Zero)
            {
                Log("VRChat window handle not found. Unable to bring VRChat to the front.");
                return;
            }

            // Check if the window is minimized, and restore it
            if (NativeMethods.IsIconic(vrChatWindowHandle))
            {
                NativeMethods.ShowWindowAsync(vrChatWindowHandle, NativeMethods.SW_RESTORE);
                Log("Restored minimized VRChat window.");
            }

            // Bring the window to the foreground
            if (!NativeMethods.SetForegroundWindow(vrChatWindowHandle))
            {
                Log("Failed to bring VRChat window to the front.");
            }
            else
            {
                Log("VRChat window brought to the front successfully.");
            }
        }



        private void StopCapture()
        {
            isCapturing = false;
            // Wait for the thread, if it takes too long, abort it
            if (captureThread != null && !captureThread.Join(5000))
            {
                try
                {
                    captureThread.Interrupt();
                }
                catch (Exception ex)
                {
                    LogDebug($"Aborting capture thread: {ex.Message}");
                }
                
            }
            captureThread = null;
            Log("Stopped screen capture.");
        }

        


    }
}
