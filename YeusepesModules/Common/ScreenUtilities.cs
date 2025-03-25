using ScreenCapture.NET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using YeusepesLowLevelTools;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using static YeusepesModules.OSCQR.OSCQR;
using YeusepesModules.IDC.Encoder;
using VRCOSC.App.SDK.Parameters;
using HPPH;
using System.Drawing.Imaging;

namespace YeusepesModules.Common.ScreenUtilities
{
    public class ScreenUtilities
    {
        private IScreenCaptureService screenCaptureService;
        private GraphicsCard? selectedGraphicsCard;
        private Display? selectedDisplay;

        private Thread? captureThread;

        private Action<string> Log;
        private readonly Func<Enum, String> GetSettingValue;
        Action<ScreenUtilitiesParameters, bool> sendBoolParameter;
        Action<HPPH.IImage> whatDoInCapture;

        private bool isCapturing = false;

        public enum ScreenUtilitiesSettings
        {
            SelectedGraphicsCard,
            SelectedDisplay
        }
        public enum ScreenUtilitiesParameters
        {
            StartRecording,
            Error
        }
        public enum GraphicsCardOption
        {
            Default
        }

        public enum DisplayOption
        {
            Default
        }

        public ScreenUtilities(Action<Enum, ModuleSetting> createCustomSetting, Action<string> log, Func<Enum, String> getSettingValue, Action<Enum, string, ParameterMode, string, string> registerBoolParameter)
        {
            Log = log;
            GetSettingValue = getSettingValue;

            createCustomSetting(
               ScreenUtilitiesSettings.SelectedGraphicsCard,
               new StringModuleSetting(
                   "Graphics Card",
                   "Select or type the name of your GPU. Suggestions will appear as you type.",
                   typeof(GraphicsCardSettingView), // The view class now supports the required constructor
                   "Default" // Default value
               )
           );

            createCustomSetting(
                ScreenUtilitiesSettings.SelectedDisplay,
                new StringModuleSetting(
                    "Display",
                    "Select or type the name of your display. Suggestions will appear as you type.",
                    typeof(DisplaySettingView), // The custom view class
                    "Default" // Default value
                )
            );

            sendBoolParameter = (parameter, value) => registerBoolParameter(parameter, value.ToString(), ParameterMode.Write, "Bool", "Bool");
            registerBoolParameter(ScreenUtilitiesParameters.StartRecording, "ScreenUtilities/StartRecording", ParameterMode.Write, "Start Recording", "Start or stop screen recording");
            registerBoolParameter(ScreenUtilitiesParameters.Error, "ScreenUtilities/Error", ParameterMode.Read, "Error", "Indicates an error occurred during screen capture");

            // Initialize screen capture service
            screenCaptureService = new DX11ScreenCaptureService();
        }

        public void SetWhatDoInCapture(Action<HPPH.IImage> whatDoInCapture)
        {
            this.whatDoInCapture = whatDoInCapture;
        }

        public Task<bool> OnModuleStart()
        {
            /*
            // Automatically determine GPU and display
            if (!TryFindVRChatWindow())
            {
                Log("VRChat window not found or could not determine GPU/Display.");
                return Task.FromResult(false);
            }*/

            if (screenCaptureService == null)
            {
                Log("Error: screenCaptureService could not be initialized.");
                return Task.FromResult(false);
            }

            Log($"Automatically selected GPU: {selectedGraphicsCard?.Name}");
            Log($"Automatically selected Display: {selectedDisplay?.DeviceName}");

            InitializeGraphicsAndDisplay();
            return Task.FromResult(true);
        }

        public void HandleParameter(RegisteredParameter parameter)
        {
            switch (parameter.Lookup)
            {
                case ScreenUtilitiesParameters.StartRecording:
                    // LogDebug("Start Recording parameter received. Value: " + parameter.GetValue<bool>());
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
            }
        }

        public void StartCapture()
        {
            sendBoolParameter(ScreenUtilitiesParameters.Error, false);
            if (screenCaptureService == null)
            {
                Log("Cannot start capture. screenCaptureService is null.");
                sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                return;
            }

            if (selectedGraphicsCard == null)
            {
                Log("Cannot start capture. No GPU selected.");
                sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                return;
            }

            if (selectedDisplay == null || !selectedDisplay.HasValue)
            {
                Log("Cannot start capture. No display selected.");
                sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                return;
            }

            if (selectedDisplay.Value.Width <= 0 || selectedDisplay.Value.Height <= 0)
            {
                Log($"Cannot start capture. Invalid display dimensions: Width={selectedDisplay.Value.Width}, Height={selectedDisplay.Value.Height}");
                sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                return;
            }

            isCapturing = true;

            captureThread = new Thread(() => RunCaptureLoop());
            captureThread.IsBackground = true;
            captureThread.Start();

            // LogDebug("Started screen capture.");
        }

        public bool TryFindVRChatWindow()
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

        public string GetSelectedDisplay()
        {
            Log($"Selected Display: {GetSettingValue(ScreenUtilitiesSettings.SelectedDisplay)}");
            return GetSettingValue(ScreenUtilitiesSettings.SelectedDisplay); // Retrieve the saved string
        }
        public string GetSelectedGraphicsCard()
        {
            Log($"Selected GPU: {GetSettingValue(ScreenUtilitiesSettings.SelectedGraphicsCard)}");
            return GetSettingValue(ScreenUtilitiesSettings.SelectedGraphicsCard);
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
            Log($"Displays: {string.Join(", ", displays)}");
            return displays ?? new List<string> { "Default" };
        }



        public void RunCaptureLoop()
        {
            try
            {
                // Validate the screen capture service
                if (screenCaptureService == null)
                {
                    Log("Error: screenCaptureService is null. Cannot continue.");
                    sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                    isCapturing = false;
                    return;
                }

                // Validate the selected display
                if (selectedDisplay == null || !selectedDisplay.HasValue)
                {
                    Log("Error: selectedDisplay is null or invalid. Cannot continue.");
                    sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                    isCapturing = false;
                    return;
                }

                if (selectedDisplay.Value.Width <= 0 || selectedDisplay.Value.Height <= 0)
                {
                    Log($"Error: Invalid display dimensions: Width={selectedDisplay.Value.Width}, Height={selectedDisplay.Value.Height}");
                    sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                    isCapturing = false;
                    return;
                }

                // Initialize screen capture
                var screenCapture = screenCaptureService.GetScreenCapture(selectedDisplay.Value);
                if (screenCapture == null)
                {
                    Log("Error: screenCapture is null. Cannot initialize screen capture.");
                    sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                    isCapturing = false;
                    return;
                }

                if (screenCapture.Display == null || screenCapture.Display.Width <= 0 || screenCapture.Display.Height <= 0)
                {
                    Log($"Error: Invalid screen capture display dimensions: Width={screenCapture.Display.Width}, Height={screenCapture.Display.Height}");
                    sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                    isCapturing = false;
                    return;
                }

                // Attempt to register the capture zone
                ICaptureZone? captureZone = null;
                try
                {
                    captureZone = screenCapture.RegisterCaptureZone(
                        0, 0, screenCapture.Display.Width, screenCapture.Display.Height
                    );

                    if (captureZone == null)
                    {
                        sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                        throw new NullReferenceException("RegisterCaptureZone returned null.");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error during RegisterCaptureZone: {ex.Message}. Stack Trace: {ex.StackTrace}");
                    sendBoolParameter(ScreenUtilitiesParameters.Error, true);
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

                        using (captureZone.Lock())
                        {
                            var image = captureZone.Image;
                            if (image != null)
                            {
                                whatDoInCapture(image);
                            }
                            else
                            {
                                // LogDebug("No image captured in the current frame.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error during screen capture or processing: {ex.Message}");
                        sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                    }

                    // Adjust the sleep interval based on performance needs
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                Log($"Critical error in capture loop: {ex.Message}. Stack Trace: {ex.StackTrace}");
                sendBoolParameter(ScreenUtilitiesParameters.Error, true);
                isCapturing = false;
            }

        }

        // In ScreenUtilities class:
        public Bitmap TakeScreenshot()
        {
            Log("Taking screenshot...");
            if (GetSelectedDisplay() == "Default")
                selectedDisplay = screenCaptureService.GetDisplays(selectedGraphicsCard.Value).FirstOrDefault();
            else
                selectedDisplay = screenCaptureService.GetDisplays(selectedGraphicsCard.Value)
                                    .FirstOrDefault(d => d.DeviceName == GetSelectedDisplay());

            if (screenCaptureService == null || selectedDisplay == null || !selectedDisplay.HasValue)
            {
                Log("Cannot take screenshot. Invalid screen capture service or display.");
                return null;
            }

            BringVRChatToFront();

            try
            {
                var screenCapture = screenCaptureService.GetScreenCapture(selectedDisplay.Value);
                if (screenCapture == null)
                {
                    Log("Failed to get screen capture.");
                    return null;
                }
                if (screenCapture.Display == null)
                {
                    Log("Screen capture's Display is null.");
                    return null;
                }



                ICaptureZone captureZone = null;
                try
                {
                    captureZone = screenCapture.RegisterCaptureZone(
                        0, 0, screenCapture.Display.Width, screenCapture.Display.Height);
                }
                catch (Exception ex)
                {
                    Log($"Error during RegisterCaptureZone: {ex.Message}. Stack Trace: {ex.StackTrace}");
                    return null;
                }
                if (captureZone == null)
                {
                    Log("RegisterCaptureZone returned null.");
                    return null;
                }

                // Capture the current screen content.
                screenCapture.CaptureScreen();

                Log($"Capturing screen: {screenCapture.Display.DeviceName} ({screenCapture.Display.Width}x{screenCapture.Display.Height})");
                // Lock the capture zone to access the captured image safely.
                using (var zoneLock = captureZone.Lock())
                {
                    IImage image = captureZone.Image;
                    if (image == null)
                    {
                        Log("No image captured.");
                        return null;
                    }

                    // Convert the IImage to a Bitmap.
                    // This helper method should mimic the logic from OSCQR's TransformIImageToBitmap.
                    Bitmap bmp = TransformIImageToBitmap(image);
                    Log("Screenshot taken.");
                    return bmp;
                }                              
            }
            catch (Exception ex)
            {
                Log($"Error taking screenshot: {ex.Message}");
                return null;
            }
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

        public void StopCapture()
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
                    Log($"Aborting capture thread: {ex.Message}");
                }

            }
            captureThread = null;
            Log("Stopped screen capture.");
        }
    }

}
