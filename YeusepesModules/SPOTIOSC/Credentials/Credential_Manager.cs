﻿using System.Security;
using System.Security.Cryptography;
using System.Text;
using YeusepesLowLevelTools;
using System.Text.Json;
using System.Net.Http;
using System.IO;
using VRCOSC.App.Utils;
using PuppeteerSharp;
using System.Text.RegularExpressions;

namespace YeusepesModules.SPOTIOSC.Credentials
{
    public class CredentialManager
    {
        #region Constants and Globals

        // Base URLs for login and token retrieval.
        private const string SpotifyLoginUrl = "https://accounts.spotify.com/en/login";
        // Note: the access token endpoint requires these query parameters.
        private const string AccessTokenEndpoint = "https://open.spotify.com/";        
        private const string ClientTokenEndpoint = "https://clienttoken.spotify.com/v1/clienttoken";

        private const string AccessTokenFile = "access_token.dat";
        private const string ClientTokenFile = "client_token.dat";

        // Global token holders.
        public static SecureString AccessToken = new SecureString();
        public static SecureString ClientToken = new SecureString();
        private static string clientID = null;

        #endregion

        #region Public API

        /// <summary>
        /// Headful login flow.
        /// Opens the Spotify login page so the user can sign in.
        /// Polls the page URL until it contains "/status?" (indicating a redirect after login),
        /// then closes the headful browser.
        /// </summary>
        public static async Task LoginAsync()
        {
            ////Logger("Starting headful login flow...");

            IBrowser browser = null;
            IPage loginPage = null;
            bool redirectDetected = false;
            try
            {
                // Launch a headful browser using persistent storage.
                browser = await LaunchBrowserAsync(headless: false, useAppMode: true);

                // Open the login page.
                loginPage = await InitializePageAsync(browser, SpotifyLoginUrl);                

                try
                {
                    // Wait for up to 60 seconds for the URL to include "/status?"
                    await loginPage.WaitForFunctionAsync(
                        "() => window.location.href.includes('/status?')",
                        new WaitForFunctionOptions { Timeout = 60000 }
                    );
                    ////Logger("Login detected (URL contains '/status?').");
                    redirectDetected = true;
                }
                catch (Exception ex)
                {
                    ////Logger("Login status not detected within the timeout period.");
                }


                if (!redirectDetected)
                {
                    ////Logger("Login status not detected within the timeout period.");
                }
                else
                {
                    ////Logger("Closing login page...");
                    try
                    {
                        await loginPage.CloseAsync();
                    }
                    catch (Exception ex)
                    {
                        ////Logger($"Error closing login page: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                ////Logger($"Error during LoginAsync: {ex.Message}");
                throw;
            }
            finally
            {
                if (browser != null)
                {
                    try
                    {
                        await browser.CloseAsync();
                    }
                    catch (Exception ex)
                    {
                        ////Logger($"Error closing browser in LoginAsync: {ex.Message}");
                    }
                }
            }

            // At this point, the user's cookies have been saved to disk.
            await AuthenticateAsync();
        }

        /// <summary>
        /// Headless authentication flow.
        /// Launches a headless browser (using the same persistent user data directory)
        /// so that it loads the cookies saved during login and navigates to the token endpoint
        /// to capture the JSON response with the access token.
        /// </summary>
        public static async Task AuthenticateAsync()
        {
            ////Logger("Starting headless authentication flow...");

            IBrowser browser = null;
            IPage tokenPage = null;
            var tokenReady = new TaskCompletionSource<bool>();

            try
            {
                // Launch a headless browser that uses the same persistent user data directory.
                browser = await LaunchBrowserAsync(headless: true);
                tokenPage = await InitializePageAsync(browser, AccessTokenEndpoint);
                await NavigateAndCaptureJsonResponseAsync(tokenPage, tokenReady);

                // Wait until the token has been captured (or timeout/cancellation if desired).
                await tokenReady.Task;
            }
            catch (Exception ex)
            {
                ////Logger($"Error during AuthenticateAsync: {ex.Message}");
                throw;
            }
            finally
            {
                if (browser != null)
                {
                    try
                    {
                        await browser.CloseAsync();
                    }
                    catch (Exception ex)
                    {
                        ////Logger($"Error closing headless browser in AuthenticateAsync: {ex.Message}");
                    }
                }
            }
            if (string.IsNullOrEmpty(LoadClientToken()))
            {                
                await GetClientTokenAsync();                
            }
        }

        /// <summary>
        /// Signs out the user by deleting stored tokens and thoroughly sanitizing browser data.
        /// </summary>
        public static void SignOut()
        {
            ////Logger("Signing out...");

            // Delete token files.
            try
            {
                if (File.Exists(AccessTokenFile))
                {
                    File.Delete(AccessTokenFile);
                    ////Logger("Access token file deleted.");
                }
                if (File.Exists(ClientTokenFile))
                {
                    File.Delete(ClientTokenFile);
                    ////Logger("Client token file deleted.");
                }
            }
            catch (Exception ex)
            {
                ////Logger($"Error deleting token files: {ex.Message}");
            }

            // Clear secure strings.
            try
            {
                ClearSecureString(ref AccessToken);
                ClearSecureString(ref ClientToken);
            }
            catch (Exception ex)
            {
                ////Logger($"Error clearing secure tokens: {ex.Message}");
            }

            // Thoroughly delete the persistent browser data directory.
            const string UserDataDirectory = "user_data";
            try
            {
                if (Directory.Exists(UserDataDirectory))
                {
                    DeleteDirectory(UserDataDirectory);
                    ////Logger("User data directory sanitized successfully.");
                }
                else
                {
                    ////Logger("User data directory not found, nothing to sanitize.");
                }
            }
            catch (Exception ex)
            {
                ////Logger($"Error deleting user data directory: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns true if both access and client tokens exist.
        /// </summary>
        public static bool IsUserSignedIn()
        {
            return !string.IsNullOrEmpty(LoadAccessToken()) &&
                   !string.IsNullOrEmpty(LoadClientToken());
        }      


        #endregion

        #region Browser and Navigation Helpers

        /// <summary>
        /// Launches a Chromium browser with persistence enabled.
        /// Both headful and headless flows use the same persistent user data directory.
        /// </summary>
        /// <summary>
        /// Launches a Chromium browser with persistence enabled and anti-detection flags.
        /// </summary>
        private static async Task<IBrowser> LaunchBrowserAsync(bool headless, bool useAppMode = false)
        {
            const string UserDataDirectory = "user_data";

            try
            {
                ////Logger("Initializing browser fetcher...");
                var browserFetcher = new BrowserFetcher();
                ////Logger("Downloading supported Chromium version...");
                var installedBrowser = await browserFetcher.DownloadAsync();
                string browserPath = installedBrowser.GetExecutablePath();
                ////Logger($"Chromium downloaded to: {browserPath}");

                ////Logger("Launching browser with persistence enabled...");

                var launchArgs = new List<string>();

                // Add the app mode argument if needed.
                if (useAppMode)
                {
                    launchArgs.Add($"--app={SpotifyLoginUrl}");
                }

                // Anti-detection: disable blink features that reveal automation.
                launchArgs.Add("--disable-blink-features=AutomationControlled");

                launchArgs.AddRange(new List<string>
        {
            "--enable-features=NetworkService,NetworkServiceInProcess",
            "--disable-extensions",
            "--disable-infobars",
            "--disable-popup-blocking",
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage"
        });

                var launchOptions = new LaunchOptions
                {
                    ExecutablePath = browserPath,
                    Headless = headless,
                    Args = launchArgs.ToArray(),
                    DefaultViewport = null,
                    UserDataDir = Path.GetFullPath(UserDataDirectory),
                    DumpIO = true
                };

                ////Logger("Attempting to launch the browser...");
                var browser = await Puppeteer.LaunchAsync(launchOptions);
                ////Logger("Browser launched successfully!");
                return browser;
            }
            catch (PuppeteerSharp.ProcessException ex)
            {
                ////Logger($"ProcessException: Failed to launch browser! {ex.Message}");
                if (ex.InnerException != null)
                {
                    ////Logger($"Inner Exception: {ex.InnerException.Message}");
                }
                throw;
            }
            catch (UnauthorizedAccessException ex)
            {
                ////Logger($"UnauthorizedAccessException: Permission issue detected! {ex.Message}");
                throw;
            }
            catch (IOException ex)
            {
                ////Logger($"IOException: Error accessing files or directories! {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                ////Logger($"Exception: An unexpected error occurred! {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Opens a new page in the browser, sets anti-detection overrides, and navigates to the specified URL.
        /// </summary>
        private static async Task<IPage> InitializePageAsync(IBrowser browser, string url)
        {
            IPage page = null;
            try
            {
                var pages = await browser.PagesAsync();
                page = pages.FirstOrDefault() ?? await browser.NewPageAsync();

                // Override navigator.webdriver on every new document to hide automation.
                try
                {
                    await page.EvaluateFunctionOnNewDocumentAsync(@"() => {
                Object.defineProperty(navigator, 'webdriver', { get: () => false });
            }");
                }
                catch (Exception ex)
                {
                    ////Logger("Failed to override navigator.webdriver: " + ex.Message);
                }

                // Set a standard user agent string.
                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36 Edg/134.0.0.0");

                // Set extra HTTP headers WITHOUT "upgrade-insecure-requests"
                await page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
                {
                    // Remove "accept" header to avoid CORS issues.
                    { "accept-language", "en-US,en;q=0.9,es-CO;q=0.8,es;q=0.7" },
                    { "dnt", "1" },
                    { "sec-ch-ua", "\"Chromium\";v=\"134\", \"Not:A-Brand\";v=\"24\", \"Microsoft Edge\";v=\"134\"" },
                    { "sec-ch-ua-mobile", "?0" },
                    { "sec-ch-ua-platform", "\"Windows\"" },
                    { "sec-fetch-dest", "document" },
                    { "sec-fetch-mode", "navigate" },
                    { "sec-fetch-site", "none" },
                    { "sec-fetch-user", "?1" }
                });


                // Navigate to the target URL.
                try
                {
                    await page.GoToAsync(url, WaitUntilNavigation.Networkidle2);
                }
                catch (PuppeteerSharp.NavigationException ex)
                {
                    ////Logger($"Navigation exception ignored: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                ////Logger($"Error in InitializePageAsync: {ex.Message}");
                throw;
            }
            return page;
        }

        private static async Task NavigateAndCaptureJsonResponseAsync(IPage page, TaskCompletionSource<bool> tokenReady)
        {
            ////Logger("Setting up request interception for token retrieval...");
            try
            {
                await page.SetRequestInterceptionAsync(true);
            }
            catch (Exception ex)
            {
                ////Logger($"Error setting request interception: {ex.Message}");
            }

            // Handler for all outgoing requests – simply passes them along.
            EventHandler<PuppeteerSharp.RequestEventArgs> requestHandler = async (sender, e) =>
            {
                try
                {
                    // Proceed with the request.
                    await e.Request.ContinueAsync();
                }
                catch (Exception ex)
                {
                    ////Logger($"Error in request handler: {ex.Message}");
                }
            };
            page.Request += requestHandler;

            // Handler for responses to capture JSON from token endpoint.
            EventHandler<PuppeteerSharp.ResponseCreatedEventArgs> responseHandler = null;
            responseHandler = async (sender, e) =>
            {
                try
                {
                    // Check if this response is from the token endpoint and is successful.
                    if (e.Response.Url.Contains("access_token") &&
                        e.Response.Status == System.Net.HttpStatusCode.OK)
                    {
                        string responseBody = await e.Response.TextAsync();
                        ////Logger($"JSON response captured: {responseBody}");

                        try
                        {
                            var json = JsonSerializer.Deserialize<JsonElement>(responseBody);

                            // Extract and save the access token.
                            if (json.TryGetProperty("accessToken", out JsonElement tokenElement))
                            {
                                string accessToken = tokenElement.GetString();
                                ////Logger($"Access token found: {accessToken}");
                                SaveAccessToken(accessToken);
                            }
                            else
                            {
                                ////Logger("Access token not found in JSON response.");
                            }

                            // Extract and save the client ID.
                            if (json.TryGetProperty("clientId", out JsonElement clientIDElement))
                            {
                                clientID = clientIDElement.GetString();
                                ////Logger($"ClientID found and saved: {clientID}");
                            }
                            else
                            {
                                ////Logger("ClientID not found in JSON response.");
                            }

                            // Signal that token (and clientID) capture is complete.
                            tokenReady.TrySetResult(true);

                            // Unregister event handlers to prevent duplicate triggers.
                            page.Response -= responseHandler;
                            page.Request -= requestHandler;

                            // Close the browser.
                            try
                            {
                                await page.Browser.CloseAsync();
                            }
                            catch (Exception closeEx)
                            {
                                ////Logger($"Error closing browser in response handler: {closeEx.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ////Logger($"Error parsing JSON response: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ////Logger($"Error in response handler: {ex.Message}");
                }
            };
            page.Response += responseHandler;

            // Navigate to the access token endpoint.
            ////Logger("Navigating to access token endpoint...");
            try
            {
                await page.GoToAsync(AccessTokenEndpoint, WaitUntilNavigation.Networkidle0);
            }
            catch (PuppeteerSharp.NavigationException ex)
            {
                ////Logger($"Navigation exception ignored: {ex.Message}");
            }
            catch (Exception ex)
            {
                ////Logger($"Unexpected error navigating to access token endpoint: {ex.Message}");
            }
        }


        #endregion

        #region Token and Cookie Management

        /// <summary>
        /// Saves the access token securely.
        /// </summary>
        private static void SaveAccessToken(string accessToken)
        {
            try
            {
                NativeMethods.SaveToSecureString(accessToken, ref AccessToken);
                var encryptedData = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(accessToken),
                    null,
                    DataProtectionScope.CurrentUser
                );
                File.WriteAllBytes(AccessTokenFile, encryptedData);
                ////Logger("Access token saved securely.");
            }
            catch (Exception ex)
            {
                ////Logger($"Error saving access token: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the access token from secure storage.
        /// </summary>
        public static string LoadAccessToken()
        {
            try
            {
                if (!File.Exists(AccessTokenFile))
                {
                    ////Logger("Access token file not found.");
                    return null;
                }
                var encryptedData = File.ReadAllBytes(AccessTokenFile);
                var accessToken = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser)
                );
                NativeMethods.SaveToSecureString(accessToken, ref AccessToken);
                ////Logger("Access token loaded successfully.");
                return accessToken;
            }
            catch (Exception ex)
            {
                ////Logger($"Error loading access token: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Saves the client token securely.
        /// </summary>
        public static void SaveClientToken(string clientToken)
        {
            try
            {
                NativeMethods.SaveToSecureString(clientToken, ref ClientToken);
                byte[] encryptedData = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(clientToken),
                    null,
                    DataProtectionScope.CurrentUser
                );
                File.WriteAllBytes(ClientTokenFile, encryptedData);
                ////Logger("Client token securely saved.");
            }
            catch (Exception ex)
            {
                ////Logger($"Error saving client token: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the client token from secure storage.
        /// </summary>
        public static string LoadClientToken()
        {
            try
            {
                if (!File.Exists(ClientTokenFile))
                {
                    ////Logger("Client token file not found.");
                    return null;
                }
                byte[] encryptedData = File.ReadAllBytes(ClientTokenFile);
                string clientToken = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser)
                );
                NativeMethods.SaveToSecureString(clientToken, ref ClientToken);
                ////Logger("Client token successfully loaded.");
                return clientToken;
            }
            catch (Exception ex)
            {
                ////Logger($"Error loading client token: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Client Token and ID Retrieval

        /// <summary>
        /// Fetches the client token from Spotify’s client token endpoint.
        /// </summary>
        public static async Task<bool> GetClientTokenAsync()
        {
            ////Logger("Attempting to fetch client token...");
            using var httpClient = new HttpClient();
            string accessToken = LoadAccessToken();
            string deviceId = Guid.NewGuid().ToString();

            try
            {
                ////Logger("Fetching Client Token...");
                var optionsRequest = new HttpRequestMessage(HttpMethod.Options, ClientTokenEndpoint);
                optionsRequest.Headers.Add("Accept", "*/*");
                optionsRequest.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                optionsRequest.Headers.Add("Sec-Fetch-Dest", "empty");
                optionsRequest.Headers.Add("Sec-Fetch-Mode", "cors");
                optionsRequest.Headers.Add("Sec-Fetch-Site", "same-site");
                optionsRequest.Headers.Referrer = new Uri(SpotifyLoginUrl);
                optionsRequest.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

                HttpResponseMessage optionsResponse = await httpClient.SendAsync(optionsRequest);
                if (!optionsResponse.IsSuccessStatusCode)
                {
                    ////Logger($"OPTIONS request failed: {optionsResponse.StatusCode}");
                    return false;
                }
                ////Logger("OPTIONS request successful.");
                ////Logger("Retrieving Client ID...");

                var postBody = new
                {
                    client_data = new
                    {
                        client_version = "1.2.54.124.gc8ffdbcb",
                        client_id = clientID,
                        js_sdk_data = new
                        {
                            device_brand = "unknown",
                            device_model = "unknown",
                            os = "windows",
                            os_version = "NT 10.0",
                            device_id = deviceId,
                            device_type = "computer"
                        }
                    }
                };

                ////Logger("Client Token Request Body:");
                ////Logger(JsonSerializer.Serialize(postBody));

                string postBodyJson = JsonSerializer.Serialize(postBody);
                var postRequest = new HttpRequestMessage(HttpMethod.Post, ClientTokenEndpoint)
                {
                    Content = new StringContent(postBodyJson, Encoding.UTF8, "application/json")
                };

                ////Logger("Sending POST request to client token endpoint...");
                postRequest.Headers.Add("Accept", "application/json");
                postRequest.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                postRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                postRequest.Headers.Add("Sec-CH-UA", "\"Microsoft Edge\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
                postRequest.Headers.Add("Sec-CH-UA-Mobile", "?0");
                postRequest.Headers.Add("Sec-CH-UA-Platform", "\"Windows\"");
                postRequest.Headers.Add("Sec-Fetch-Dest", "empty");
                postRequest.Headers.Add("Sec-Fetch-Mode", "cors");
                postRequest.Headers.Add("Sec-Fetch-Site", "same-site");
                postRequest.Headers.Referrer = new Uri(SpotifyLoginUrl);
                postRequest.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

                ////Logger("Client Token Request Headers:");
                foreach (var header in postRequest.Headers)
                {
                    ////Logger($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                HttpResponseMessage postResponse = await httpClient.SendAsync(postRequest);

                ////Logger("Client Token Response Headers:");
                foreach (var header in postResponse.Headers)
                {
                    ////Logger($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                ////Logger("Client Token Response Status Code:");
                ////Logger(postResponse.StatusCode.ToString());
                if (postResponse.IsSuccessStatusCode)
                {
                    string postResponseContent = await postResponse.Content.ReadAsStringAsync();
                    ////Logger("Client Token Response Content:");
                    ////Logger(postResponseContent);

                    var tokenResponse = JsonSerializer.Deserialize<JsonElement>(postResponseContent);
                    if (tokenResponse.TryGetProperty("granted_token", out JsonElement grantedTokenElement) &&
                        grantedTokenElement.TryGetProperty("token", out JsonElement tokenElement))
                    {
                        string clientToken = tokenElement.GetString();
                        ////Logger($"Client Token: {clientToken}");
                        SaveClientToken(clientToken);
                        NativeMethods.SaveToSecureString(clientToken, ref ClientToken);
                        return true;
                    }
                    ////Logger("Client token not found in the response.");
                }
                ////Logger(postBodyJson);
                ////Logger("Client token not found in the response.");
            }
            catch (Exception ex)
            {
                ////Logger($"Error during client token retrieval: {ex.Message}");
            }
            return false;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Clears the contents of a SecureString and reinitializes it.
        /// If the SecureString is read-only, simply reassign a new instance.
        /// </summary>
        private static void ClearSecureString(ref SecureString secureStr)
        {
            try
            {
                if (secureStr != null && !secureStr.IsReadOnly())
                {
                    secureStr.Clear();
                }
            }
            catch (Exception ex)
            {
                ////Logger($"Error clearing secure token: {ex.Message}");
            }
            // Always assign a new SecureString instance.
            secureStr = new SecureString();
        }

        /// <summary>
        /// Deletes all stored tokens and resets the in-memory SecureStrings.
        /// </summary>
        public static void DeleteTokens()
        {
            try
            {
                if (File.Exists(AccessTokenFile))
                {
                    File.Delete(AccessTokenFile);
                    ////Logger("Access token deleted.");
                }
                if (File.Exists(ClientTokenFile))
                {
                    File.Delete(ClientTokenFile);
                    ////Logger("Client token deleted.");
                }
                ClearSecureString(ref AccessToken);
                ClearSecureString(ref ClientToken);
                ////Logger("All tokens cleared from memory and storage.");
            }
            catch (Exception ex)
            {
                ////Logger($"Error deleting tokens: {ex.Message}");
            }
        }





        /// <summary>
        /// Recursively deletes a directory by first removing read-only attributes.
        /// </summary>
        private static void DeleteDirectory(string path)
        {
            var di = new DirectoryInfo(path);
            foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }
            di.Delete(true);
        }

        #endregion
    }
}
