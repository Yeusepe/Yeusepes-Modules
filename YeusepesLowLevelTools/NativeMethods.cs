using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security;

namespace YeusepesLowLevelTools
{
    public static class NativeMethods
    {
        public const int SHOWNORMAL = 1;
        public const int SHOWMINIMIZED = 2;
        public const int SHOWMAXIMIZED = 3;
        public const int SW_RESTORE = 9; // Restore window if minimized

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
}
