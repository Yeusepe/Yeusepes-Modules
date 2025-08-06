using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;


namespace YeusepesLowLevelTools
{
    public class SecretsMarker { }

    public static class GlobalSecrets
    {
        private static readonly IConfiguration _config = new ConfigurationBuilder()
            .AddUserSecrets<SecretsMarker>()    // picks up your secrets.json by GUID
            .Build();

        /// <summary>Fetch any secret by its full key path, e.g. "Discord:ClientId" or "SK1".</summary>
        public static string Get(string key) => _config[key];
    }

    public static class NativeMethods
    {
        public const int SHOWNORMAL = 1;
        public const int SHOWMINIMIZED = 2;
        public const int SHOWMAXIMIZED = 3;
        public const int SW_RESTORE = 9; // Restore window if minimized

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool IsIconic(IntPtr hWnd);
        
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);


        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // Get the VRChat window handle by process name
        public static IntPtr GetVRChatWindowHandle()
        {
            var process = Process.GetProcessesByName("VRChat").FirstOrDefault();
            return process?.MainWindowHandle ?? IntPtr.Zero;
        }
        public static void ForceForegroundWindow(IntPtr hWnd)
        {
            // 1. Figure out which thread is currently in the foreground
            IntPtr fg = GetForegroundWindow();
            uint fgThread = GetWindowThreadProcessId(fg, out _);
            uint thisThread = GetCurrentThreadId();

            // 2. If they’re different, attach them
            if (fgThread != thisThread)
            {
                AttachThreadInput(fgThread, thisThread, true);
                BringWindowToTop(hWnd);
                ShowWindow(hWnd, SW_RESTORE);
                AttachThreadInput(fgThread, thisThread, false);
            }
            else
            {
                // Same thread, just force it
                BringWindowToTop(hWnd);
                ShowWindow(hWnd, SW_RESTORE);
            }

            // 3. Finally attempt the normal call
            SetForegroundWindow(hWnd);
        }

        public static IntPtr FindWindowByTitle(string title)
        {
            return FindWindow(null, title);
        }

        // Get the window rectangle
        public static Rectangle GetWindowRectangle(IntPtr hWnd)
        {
            if (GetWindowRect(hWnd, out RECT rect))
            {
                return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }

            return Rectangle.Empty;
        }
        public static void SaveToSecureString(string value, ref SecureString secureString)
        {
            // Dispose of the existing SecureString, if necessary
            if (secureString != null)
            {
                secureString.Dispose();
            }

            // Create a new SecureString instance
            secureString = new SecureString();
            foreach (char c in value)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            Console.WriteLine("Value saved securely.");
        }
        public static string CapitalizeFirstLetter(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            return char.ToUpper(input[0]) + input.Substring(1).ToLower();
        }


    }

    public static class Loader
    {
        private static bool _isRunning = false;

        public static void ShowLoader()
        {
            _isRunning = true;
            Thread loaderThread = new Thread(() =>
            {
                Console.CursorVisible = false;
                while (_isRunning)
                {
                    foreach (var cursor in new[] { "/", "-", "\\", "|" })
                    {
                        Console.Write($"\r{cursor} Loading...");
                        Thread.Sleep(100);
                    }
                }
                Console.CursorVisible = true;
                Console.Write("\r                \r"); // Clear the loader
            });
            loaderThread.IsBackground = true;
            loaderThread.Start();
        }

        public static class CursorManager
        {
            private const int IDC_WAIT = 32514; // Predefined ID for the "Wait" cursor (spinning)

            [DllImport("user32.dll")]
            public static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

            [DllImport("user32.dll")]
            public static extern IntPtr SetCursor(IntPtr hCursor);

            private static IntPtr _originalCursor;

            public static void SetSpinnerCursor()
            {
                // Load the spinning cursor
                IntPtr spinnerCursor = LoadCursor(IntPtr.Zero, IDC_WAIT);

                if (spinnerCursor != IntPtr.Zero)
                {
                    // Save the current cursor so it can be restored later
                    _originalCursor = SetCursor(spinnerCursor);
                }
            }

            public static void RestoreCursor()
            {
                // Restore the original cursor
                if (_originalCursor != IntPtr.Zero)
                {
                    SetCursor(_originalCursor);
                }
            }
        }

        public static void HideLoader()
        {
            _isRunning = false;
        }
    }
    public static class FileBlockChecker
    {
        private const string ZoneIdentifierStream = ":Zone.Identifier";

        public static bool IsFileBlocked(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                Console.WriteLine("File does not exist.");
                return false;
            }

            string alternateDataStream = filePath + ZoneIdentifierStream;
            return File.Exists(alternateDataStream);
        }

        public static void UnblockFile(string filePath)
        {
            if (IsFileBlocked(filePath))
            {
                try
                {
                    string alternateDataStream = filePath + ZoneIdentifierStream;
                    File.Delete(alternateDataStream);
                    Console.WriteLine("File successfully unblocked.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to unblock file: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("File is not blocked.");
            }
        }
    }

    public static class UriLauncher
    {
        // P/Invoke into ShellExecute:
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ShellExecute(
            IntPtr hwnd,
            string lpOperation,
            string lpFile,
            string lpParameters,
            string lpDirectory,
            int nShowCmd
        );

        public static void LaunchUri(string uri)
        {
            Console.WriteLine($"[LaunchUri] Received URI: '{uri}'");

            if (string.IsNullOrWhiteSpace(uri))
            {
                Console.WriteLine("[LaunchUri] URI is empty or null; aborting.");
                return;
            }

            try
            {
                const int SW_SHOWNORMAL = 1;
                Console.WriteLine("[LaunchUri] Calling ShellExecute(\"open\")...");
                IntPtr result = ShellExecute(
                    IntPtr.Zero,
                    "open",
                    uri,
                    null,
                    null,
                    SW_SHOWNORMAL
                );

                long code = result.ToInt64();
                if (code <= 32)
                {
                    // ShellExecute returns >32 if successful
                    throw new InvalidOperationException($"ShellExecute failed with code {code}");
                }

                Console.WriteLine("[LaunchUri] ShellExecute succeeded.");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LaunchUri] ShellExecute exception: {ex.Message}");
            }

            // Fallback using explorer.exe
            try
            {
                Console.WriteLine("[LaunchUri] Falling back to explorer.exe");
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = uri,
                    UseShellExecute = true
                });
                Console.WriteLine("[LaunchUri] explorer.exe launch succeeded.");
            }
            catch (Exception fallbackEx)
            {
                Console.WriteLine($"[LaunchUri] Fallback launch failed: {fallbackEx.Message}");
            }
        }
    }

    public static class EarlyLoader
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// Initializes the native libraries:
        /// 1. Extracts cvextern.dll from embedded resources into the designated folder.
        /// 2. Calls SetDllDirectory on that folder so the DLL (and its dependencies) can be found.
        /// </summary>
        public static void InitializeNativeLibraries(string dllFileName, Action<string> log)
        {
            try
            {
                // Define the folder where you'll extract the native DLL.
                string documentsFolder = Path.GetTempPath();
                // string documentsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YeusepesModules");
                string extractionFolder = Path.Combine(documentsFolder, "YeusepesNativeDlls");
                log($"Extraction folder: {extractionFolder}");

                // Create the folder if it doesn't exist.
                if (!Directory.Exists(extractionFolder))
                {
                    Directory.CreateDirectory(extractionFolder);
                    log("Created extraction folder.");
                }
                else
                {
                    log("Extraction folder already exists.");
                }

                // Define the DLL file name and the target path.                
                string extractedDllPath = Path.Combine(extractionFolder, dllFileName);

                // If the DLL isn't already extracted, extract it from the embedded resource.
                if (!File.Exists(extractedDllPath))
                {
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    // Ensure the resource name matches your embedded resource.
                    string resourceName = "YeusepesLowLevelTools.Natives." + dllFileName;
                    log($"Attempting to extract resource: {resourceName}");

                    using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            throw new Exception($"Embedded resource '{resourceName}' not found. Available resources:\n{string.Join("\n", assembly.GetManifestResourceNames())}");
                        }
                        using (FileStream fs = new FileStream(extractedDllPath, FileMode.Create, FileAccess.Write))
                        {
                            stream.CopyTo(fs);
                        }
                    }
                    log($"Extracted {dllFileName} to {extractedDllPath}");
                }
                else
                {
                    log($"{dllFileName} already exists at {extractedDllPath}");
                }

                // Now, add the extraction folder to the DLL search path.
                bool result = SetDllDirectory(extractionFolder);
                if (!result)
                {
                    int error = Marshal.GetLastWin32Error();
                    log($"SetDllDirectory failed with error code: {error}");
                }
                else
                {
                    log($"Successfully added '{extractionFolder}' to the DLL search path.");
                }
            }
            catch (Exception ex)
            {
                log("Error during native library initialization: " + ex.Message);
                throw;
            }

        }        

    }

}
