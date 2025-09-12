using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PuppeteerSharp;
using YeusepesLowLevelTools;
using VRCOSC.App.Utils;
using System.Security.Cryptography;
using YeusepesModules.VRChatAPI.Utils;

namespace YeusepesModules.VRChatAPI.Credentials
{
    public class VRChatCredentialManager
    {
        #region Constants

        private const string VRChatLoginUrl = "https://vrchat.com/home/login";
        private const string VRChatApiBaseUrl = "https://api.vrchat.cloud/api/1";
        private const string AuthEndpoint = "/auth/user";
        private const string CookieFilePath = "user_data/vrchat_auth_cookie.dat";

        #endregion

        #region In-Memory Tokens

        public static SecureString AuthToken = new SecureString();
        public static VRChatUtilities VRChatUtils { get; set; }

        #endregion

        #region Public API

        /// <summary>
        /// Head-ful login → capture cookies → authenticate with VRChat API → stash token
        /// </summary>
        public static async Task LoginAndCaptureCookiesAsync()
        {
            VRChatUtils?.LogDebug("[Login] Starting VRChat login flow");

                List<CookieParam> cookies;
                string authCookie;
                CookieParam foundAuthCookie = null;

            // 1) If we've saved auth cookie previously, load it and skip browser
            if (File.Exists(CookieFilePath))
            {
                authCookie = LoadEncryptedString(CookieFilePath);
                cookies = new List<CookieParam>
                {
                    new CookieParam { Name = "auth", Value = authCookie, Domain = ".vrchat.com", Path = "/" }
                };
                VRChatUtils?.LogDebug("[Login] Loaded and decrypted auth cookie from file; skipping browser.");
            }
            else
            {
                // 2) Otherwise launch Puppeteer and capture cookies after login
                VRChatUtils?.LogDebug("[Login] No saved cookie found; launching browser login flow");
                await new BrowserFetcher().DownloadAsync();

                var browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = false,
                    DefaultViewport = null,
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        $"--app={VRChatLoginUrl}"
                    },
                    UserDataDir = Path.GetFullPath("user_data")
                });

                // We'll complete this TaskSource once we see the user is logged in
                var homeUrl = "https://vrchat.com/home";
                var tcs = new TaskCompletionSource<bool>();

                // Helper to hook the Request and Response events on any IPage
                void AttachListener(IPage p)
                {
                    p.Request += (sender, e) =>
                    {
                        // Check if we've reached the home page (indicating successful login)
                        if (e.Request.Url.StartsWith(homeUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            VRChatUtils?.LogDebug("[Login] Detected redirect to home page - login successful");
                            tcs.TrySetResult(true);
                        }
                    };

                    p.Response += (sender, e) =>
                    {
                        // Also check response URLs in case the request URL doesn't match
                        if (e.Response.Url.StartsWith(homeUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            VRChatUtils?.LogDebug("[Login] Detected home page response - login successful");
                            tcs.TrySetResult(true);
                        }
                    };
                }

                // Attach to all existing pages
                var existingPages = await browser.PagesAsync();
                foreach (var p in existingPages)
                    AttachListener(p);

                // Attach to any new popup (social‐login) pages as they open
                browser.TargetCreated += async (sender, e) =>
                {
                    if (e.Target.Type == TargetType.Page)
                    {
                        var newPage = await e.Target.PageAsync();
                        AttachListener(newPage);
                    }
                };

                try
                {
                    // Use the first page or open a new one
                    IPage page = existingPages.FirstOrDefault() ?? await browser.NewPageAsync();
                    VRChatUtils?.LogDebug($"[Login] Navigating to {VRChatLoginUrl}");
                    await page.GoToAsync(VRChatLoginUrl, WaitUntilNavigation.Networkidle2);

                    VRChatUtils?.LogDebug("[Login] Waiting for successful login (redirect to home page)…");
                    
                    // Add timeout to prevent browser from staying open indefinitely
                    var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5)); // 5 minute timeout
                    var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        throw new TimeoutException("Authentication timeout - please try again");
                    }

                    // Double-check that we're actually on the home page
                    var currentUrl = page.Url;
                    if (!currentUrl.StartsWith(homeUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        VRChatUtils?.LogDebug($"[Login] Current URL: {currentUrl} - waiting for home page");
                        // Wait a bit more and check again
                        await Task.Delay(2000);
                        currentUrl = page.Url;
                        if (!currentUrl.StartsWith(homeUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"Expected to be on home page but got: {currentUrl}");
                        }
                    }

                    VRChatUtils?.LogDebug("[Login] Login successful; capturing cookies");
                    cookies = (await page.GetCookiesAsync()).ToList();
                    
                    // Verify we have the auth cookie
                    foundAuthCookie = cookies.FirstOrDefault(c => c.Name == "auth");
                    if (foundAuthCookie == null)
                    {
                        VRChatUtils?.LogDebug("[Login] No auth cookie found, checking for alternative authentication");
                        // Sometimes the cookie might be named differently or stored differently
                        var allCookies = string.Join(", ", cookies.Select(c => $"{c.Name}={c.Value}"));
                        VRChatUtils?.LogDebug($"[Login] Available cookies: {allCookies}");
                        
                        // Look for any cookie that might contain authentication info
                        foundAuthCookie = cookies.FirstOrDefault(c => 
                            c.Name.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                            c.Name.Contains("session", StringComparison.OrdinalIgnoreCase) ||
                            c.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
                    }
                    
                    // Close browser immediately after capturing cookies
                    await browser.CloseAsync();
                    VRChatUtils?.LogDebug("[Login] Browser closed after successful authentication");
                }
                catch (Exception ex)
                {
                    VRChatUtils?.LogDebug($"[Login] Error during authentication: {ex.Message}");
                    await browser.CloseAsync();
                    VRChatUtils?.LogDebug("[Login] Browser closed due to error");
                    throw;
                }

                authCookie = foundAuthCookie?.Value ?? throw new InvalidOperationException("No authentication cookie found");
                SaveEncryptedString(CookieFilePath, authCookie);
                VRChatUtils?.LogDebug("[Login] Saved and encrypted auth cookie to file");
            }

            // 3) Test the auth cookie with VRChat API
            VRChatUtils?.LogDebug("[Login] Testing auth cookie with VRChat API");
            var isAuthenticated = await TestAuthCookieAsync(authCookie);
            
            if (isAuthenticated)
            {
                SaveToSecureString(ref AuthToken, authCookie);
                VRChatUtils?.LogDebug("[Login] Auth token saved successfully");
            }
            else
            {
                throw new InvalidOperationException("Authentication failed - invalid or expired cookie");
            }

            VRChatUtils?.LogDebug("[Login] Completed VRChat login→token flow");
        }

        /// <summary>
        /// Test if the auth cookie is valid by making a request to the VRChat API
        /// </summary>
        public static async Task<bool> TestAuthCookieAsync(string authCookie)
        {
            try
            {
                using var handler = new HttpClientHandler { UseCookies = false };
                using var http = new HttpClient(handler);
                
                http.DefaultRequestHeaders.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                http.DefaultRequestHeaders.Add("Cookie", $"auth={authCookie}");

                var response = await http.GetAsync($"{VRChatApiBaseUrl}{AuthEndpoint}");
                VRChatUtils?.LogDebug($"[Login] Auth test response: {(int)response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;
                    
                    // Check if we got a valid user response
                    if (json.TryGetProperty("id", out var userId))
                    {
                        VRChatUtils?.LogDebug($"[Login] Authenticated as user: {userId}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                VRChatUtils?.LogDebug($"[Login] Error testing auth cookie: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Legacy wrapper so other code calling this still works.
        /// </summary>
        public static Task LoginAsync() => LoginAndCaptureCookiesAsync();

        public static void SignOut()
        {
            VRChatUtils?.LogDebug("[SignOut] Clearing all tokens and cookies");
            ClearAllTokensAndCookies();
        }

        public static bool HasSavedCookie() => File.Exists(CookieFilePath);

        public static bool IsUserSignedIn()
        {
            bool hasToken = !string.IsNullOrEmpty(LoadAuthToken());
            VRChatUtils?.LogDebug($"[SignIn] Has auth token: {hasToken}");
            return hasToken;
        }

        #endregion

        #region Legacy Helpers

        public static void ClearAllTokensAndCookies()
        {
            AuthToken = new SecureString();

            const string UserDataDirectory = "user_data";
            try
            {
                if (Directory.Exists(UserDataDirectory))
                    DeleteDirectory(UserDataDirectory);
                VRChatUtils?.LogDebug("User data directory sanitized successfully.");
            }
            catch (Exception ex)
            {
                VRChatUtils?.LogDebug($"Error deleting user data directory: {ex.Message}");
            }
        }

        public static string LoadAuthToken() => LoadFromSecureString(AuthToken);

        private static void DeleteDirectory(string path)
        {
            var di = new DirectoryInfo(path);
            foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;
            di.Delete(true);
        }

        #endregion

        #region SecureString Helpers

        private static void SaveToSecureString(ref SecureString target, string plaintext)
        {
            var newSs = new SecureString();
            foreach (var c in plaintext)
                newSs.AppendChar(c);
            newSs.MakeReadOnly();
            target = newSs;
        }

        private static string LoadFromSecureString(SecureString ss)
        {
            if (ss == null || ss.Length == 0)
                return null;
            var ptr = Marshal.SecureStringToBSTR(ss);
            try { return Marshal.PtrToStringBSTR(ptr); }
            finally { Marshal.ZeroFreeBSTR(ptr); }
        }

        // Encrypt/decrypt file strings
        private static void SaveEncryptedString(string path, string plaintext)
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
        }

        private static string LoadEncryptedString(string path)
        {
            var encrypted = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        #endregion
    }
}
