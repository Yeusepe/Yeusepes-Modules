using System;
using Microsoft.Extensions.Configuration;
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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PuppeteerSharp;
using YeusepesLowLevelTools;
using VRCOSC.App.Utils;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using System.Security.Cryptography;
using ZBar;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace YeusepesModules.SPOTIOSC.Credentials
{
    public class CredentialManager
    {

        #region Configuration
        private static IConfiguration _internal = new ConfigurationBuilder()
            .AddUserSecrets("95a0142d-ef1a-437e-961e-1322c4a8427d")
            .Build();
        #endregion




        #region In-Memory Tokens

        public static SecureString AccessToken = new SecureString();
        public static SecureString ClientToken = new SecureString();
        public static SecureString RefreshToken = new SecureString();
        public static string ClientId = null;

        // For legacy /api/token OAuth2
        private static SecureString ApiAccessToken = new SecureString();
        private static SecureString ApiRefreshToken = new SecureString();
        public static string ApiClientId = "cfe923b2d660439caf2b557b21f31221";

        private const int TotpVer = 5;
        private const int TotpPeriod = 30;

        public static SpotifyUtilities SpotifyUtils { get; set; }

        #endregion

        #region Public API

        /// <summary>
        /// Head-ful login → capture cookies → call server-time & get_access_token → call OAuth2 → stash
        /// </summary>
        public static async Task LoginAndCaptureCookiesAsync()
        {
            SpotifyUtils?.LogDebug("[Login] Starting login flow");

            List<CookieParam> cookies;
            string f;
            
            if (File.Exists(_internal["SK7"]))
            {
                f = LoadEncryptedString(_internal["SK7"]);
                cookies = new List<CookieParam>
                {
                    new CookieParam { Name = _internal["SK9"], Value = f, Domain = ".spotify.com", Path = "/" }
                };
                SpotifyUtils?.LogDebug("[Login] Loaded and decrypted profile from file.");
            }
            else
            {
                // 2) Otherwise run Puppeteer to log in and capture cookies
                SpotifyUtils?.LogDebug("[Login] No saved profile found.");
                await new BrowserFetcher().DownloadAsync();
                var browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = false,
                    DefaultViewport = null,
                    Args = new[] { "--disable-blink-features=AutomationControlled", $"--app={_internal["SK1"]}" },
                    UserDataDir = Path.GetFullPath("user_data")
                });

                try
                {
                    var page = (await browser.PagesAsync()).FirstOrDefault()
                               ?? await browser.NewPageAsync();
                    SpotifyUtils?.LogDebug($"[Login] Navigating to {_internal["SK1"]}");
                    await page.GoToAsync(_internal["SK1"], WaitUntilNavigation.Networkidle2);
                    while (page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase))
                        await Task.Delay(500);

                    SpotifyUtils?.LogDebug("[Login] Detected post-login redirect, capturing cookies");
                    cookies = (await page.GetCookiesAsync()).ToList();
                }
                finally
                {
                    await browser.CloseAsync();
                    SpotifyUtils?.LogDebug("[Login] Browser closed");
                }
                
                f = cookies.FirstOrDefault(c => c.Name == _internal["SK9"])?.Value
                       ?? throw new InvalidOperationException("Profile missing");                
                SaveEncryptedString(_internal["SK7"], f);
                SpotifyUtils?.LogDebug("[Login] Saved and encrypted to file");
            }
            
            SpotifyUtils?.LogDebug("[Login] Requesting server-time");
            var stJson = await RequestWithInfoAsync(
                _internal["SK2"] + _internal["SK5"],
                HttpMethod.Get,
                cookies
            );
            var serverTimeSec = stJson.GetProperty("serverTime").GetInt64();
            SpotifyUtils?.LogDebug($"[Login] Server time (s): {serverTimeSec}");

            SpotifyUtils?.LogDebug("[Login] Generating TOTP code");
            var totp = TOTP.Generate(serverTimeSec * 1000);
            SpotifyUtils?.LogDebug($"[Login] totp={totp}");

            var clientTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var qs = new Dictionary<string, string>
            {
                ["reason"] = "transport",
                ["productType"] = "web-player",
                ["totp"] = totp,
                ["totpVer"] = TotpVer.ToString(),
                ["ts"] = clientTs.ToString()
            };
            var tokenUrl = _internal["SK2"] + _internal["SK6"] + "?" +
                           string.Join("&", qs.Select(kv => $"{kv.Key}={WebUtility.UrlEncode(kv.Value)}"));
            
            using var handler = new HttpClientHandler { UseCookies = false };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add(_internal["SK28"],
                _internal["SK29"]);
            client.DefaultRequestHeaders.Add(_internal["SK30"], $"{_internal["SK9"]}={f}");
            SpotifyUtils?.LogDebug("[Login] Requesting token");
            try
            {                
                var resp = await client.GetAsync(tokenUrl);                

                var body = await resp.Content.ReadAsStringAsync();                

                resp.EnsureSuccessStatusCode();
                var webTokenJson = JsonDocument.Parse(body).RootElement;

                if (webTokenJson.TryGetProperty("accessToken", out var at))
                {
                    SaveToSecureString(ref AccessToken, at.GetString());
                    SpotifyUtils?.LogDebug("[Login] Access Token saved");
                }
                if (webTokenJson.TryGetProperty("clientId", out var cid))
                {
                    ClientId = cid.GetString();
                    SpotifyUtils?.LogDebug($"[Login] ClientId={ClientId}");
                }

                await GetClientTokenAsync();
                await GetOAuth2TokensWithCookiesAsync();
            }
            catch (Exception ex)
            {
                SpotifyUtils?.LogDebug($"[Login] ERROR fetching token: {ex.GetType().Name}: {ex.Message}");
                throw;
            }

            SpotifyUtils?.LogDebug("[Login] Completed login→token flow");
        }

        /// <summary>
        /// Exchanges authorization code for OAuth2 access and refresh tokens using saved cookies
        /// </summary>
        /// <param name="code">Authorization code obtained from /oauth2 flow</param>
        /// <param name="redirectUri">Redirect URI registered in Spotify application</param>
        /// <param name="codeVerifier">PKCE code verifier used in the initial auth request</param>
        public static async Task<bool> ExchangeAuthorizationCodeForTokensAsync(
            string code,
            string redirectUri,
            string codeVerifier)
        {
            SpotifyUtils?.LogDebug("[OAuth2] Exchanging authorization code for tokens");

            if (!File.Exists(_internal["SK7"]))
                throw new InvalidOperationException("Profile file missing");

            var f = LoadEncryptedString(_internal["SK7"]);
            using var handler = new HttpClientHandler { UseCookies = false };
            using var client = new HttpClient(handler);

            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            client.DefaultRequestHeaders.Add("Referer", _internal["SK2"]);
            client.DefaultRequestHeaders.Add(_internal["SK30"], $"{ _internal["SK9"]}={f}");

            var body = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ApiClientId,
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            };

            var content = new FormUrlEncodedContent(body);
            var response = await client.PostAsync(_internal["SK48"], content);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

            response.EnsureSuccessStatusCode();

            var at = json.GetProperty(_internal["SK49"]).GetString();
            var rt = json.GetProperty(_internal["SK50"]).GetString();

            SaveApiAccessToken(at);
            SaveApiRefreshToken(rt);

            SpotifyUtils?.LogDebug("[OAuth2] Tokens saved");
            return true;
        }

        /// <summary>
        /// Call this after your LoginAndCaptureCookiesAsync has set
        /// AccessToken and ClientId.
        /// </summary>
        public static async Task<bool> GetClientTokenAsync()
        {
            SpotifyUtils?.LogDebug("Attempting to fetch client token...");
            using var httpClient = new HttpClient();
            string accessToken = LoadAccessToken();
            string deviceId = Guid.NewGuid().ToString();

            try
            {
                SpotifyUtils?.LogDebug("Fetching Client Token...");
                var optionsRequest = new HttpRequestMessage(HttpMethod.Options, _internal["SK3"]);
                optionsRequest.Headers.Add("Accept", "*/*");
                optionsRequest.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                optionsRequest.Headers.Add("Sec-Fetch-Dest", "empty");
                optionsRequest.Headers.Add("Sec-Fetch-Mode", "cors");
                optionsRequest.Headers.Add("Sec-Fetch-Site", "same-site");
                optionsRequest.Headers.Referrer = new Uri(_internal["SK1"]);
                optionsRequest.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

                HttpResponseMessage optionsResponse = await httpClient.SendAsync(optionsRequest);
                if (!optionsResponse.IsSuccessStatusCode)
                {
                    SpotifyUtils?.LogDebug($"OPTIONS request failed: {optionsResponse.StatusCode}");
                    return false;
                }
                SpotifyUtils?.LogDebug("OPTIONS request successful.");
                SpotifyUtils?.LogDebug("Retrieving Client ID...");

                var postBody = new
                {
                    client_data = new
                    {
                        client_version = "1.2.54.124.gc8ffdbcb",
                        client_id = ClientId,
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

                SpotifyUtils?.LogDebug("Client Token Request Body:");
                SpotifyUtils?.LogDebug(JsonSerializer.Serialize(postBody));

                string postBodyJson = JsonSerializer.Serialize(postBody);
                var postRequest = new HttpRequestMessage(HttpMethod.Post, _internal["SK3"])
                {
                    Content = new StringContent(postBodyJson, Encoding.UTF8, "application/json")
                };

                SpotifyUtils?.LogDebug("Sending POST request to client token endpoint...");
                postRequest.Headers.Add("Accept", "application/json");
                postRequest.Headers.Add("Accept-Language", "en-US,en;q=0.9");
                postRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                postRequest.Headers.Add("Sec-CH-UA", "\"Microsoft Edge\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
                postRequest.Headers.Add("Sec-CH-UA-Mobile", "?0");
                postRequest.Headers.Add("Sec-CH-UA-Platform", "\"Windows\"");
                postRequest.Headers.Add("Sec-Fetch-Dest", "empty");
                postRequest.Headers.Add("Sec-Fetch-Mode", "cors");
                postRequest.Headers.Add("Sec-Fetch-Site", "same-site");
                postRequest.Headers.Referrer = new Uri(_internal["SK1"]);
                postRequest.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");

                SpotifyUtils?.LogDebug("Client Token Request Headers:");
                foreach (var header in postRequest.Headers)
                {
                    SpotifyUtils?.LogDebug($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                HttpResponseMessage postResponse = await httpClient.SendAsync(postRequest);

                SpotifyUtils?.LogDebug("Client Token Response Headers:");
                foreach (var header in postResponse.Headers)
                {
                    SpotifyUtils?.LogDebug($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                SpotifyUtils?.LogDebug("Client Token Response Status Code:");
                SpotifyUtils?.LogDebug(postResponse.StatusCode.ToString());
                if (postResponse.IsSuccessStatusCode)
                {
                    string postResponseContent = await postResponse.Content.ReadAsStringAsync();
                    SpotifyUtils?.LogDebug("Client Token Response Content:");
                    SpotifyUtils?.LogDebug(postResponseContent);

                    var tokenResponse = JsonSerializer.Deserialize<JsonElement>(postResponseContent);
                    if (tokenResponse.TryGetProperty("granted_token", out JsonElement grantedTokenElement) &&
                        grantedTokenElement.TryGetProperty("token", out JsonElement tokenElement))
                    {
                        string clientToken = tokenElement.GetString();
                        SpotifyUtils?.LogDebug($"Client Token: {clientToken}");
                        SaveToSecureString(ref ClientToken, clientToken);
                        NativeMethods.SaveToSecureString(clientToken, ref ClientToken);
                        return true;
                    }
                    SpotifyUtils?.LogDebug("Client token not found in the response.");
                }
                SpotifyUtils?.LogDebug(postBodyJson);
                SpotifyUtils?.LogDebug("Client token not found in the response.");
            }
            catch (Exception ex)
            {
                SpotifyUtils?.LogDebug($"ErrorDuringClientTokenRetrieval: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Legacy wrapper so other code calling this still works.
        /// </summary>
        public static Task LoginAsync() => LoginAndCaptureCookiesAsync();

        public static void SignOut()
        {
            SpotifyUtils?.LogDebug("[SignOut] Clearing all tokens and cookies");
            ClearAllTokensAndCookies();
        }

        public static bool HasSavedCookie() => File.Exists(_internal["SK7"]);

        public static bool IsUserSignedIn()
        {
            bool hasToken = !string.IsNullOrEmpty(LoadAccessToken());
            SpotifyUtils?.LogDebug($"[SignIn] Has access token: {hasToken}");
            return hasToken;
        }

        #endregion

        #region Legacy Helpers

        public static void ClearAllTokensAndCookies()
        {
            AccessToken = new SecureString();
            ClientToken = new SecureString();
            RefreshToken = new SecureString();
            ApiAccessToken = new SecureString();
            ApiRefreshToken = new SecureString();

            const string UserDataDirectory = "user_data";
            try
            {
                if (Directory.Exists(UserDataDirectory))
                    DeleteDirectory(UserDataDirectory);
                SpotifyUtils?.LogDebug("User data directory sanitized successfully.");
            }
            catch (Exception ex)
            {
                SpotifyUtils?.LogDebug($"Error deleting user data directory: {ex.Message}");
            }
        }

        public static string LoadAccessToken() => LoadFromSecureString(AccessToken);
        public static string LoadClientToken() => LoadFromSecureString(ClientToken);
        public static string LoadApiAccessToken() => LoadFromSecureString(ApiAccessToken);
        public static string LoadApiRefreshToken() => LoadFromSecureString(ApiRefreshToken);
        public static void SaveApiAccessToken(string token) => SaveToSecureString(ref ApiAccessToken, token);
        public static void SaveApiRefreshToken(string token) => SaveToSecureString(ref ApiRefreshToken, token);

        #endregion

        #region HTTP + Cookies

        private static async Task<JsonElement> RequestWithInfoAsync(
            string url,
            HttpMethod method,
            IEnumerable<CookieParam> cookies,
            Dictionary<string, string> headers = null)
        {
            var container = new CookieContainer();
            foreach (var c in cookies)
                container.Add(new Cookie(c.Name, c.Value, c.Path, c.Domain));

            using var handler = new HttpClientHandler { CookieContainer = container };
            using var client = new HttpClient(handler);

            // use SK93 == "Origin"
            client.DefaultRequestHeaders.Add(_internal["SK93"], _internal["SK2"]);
            // use SK42 == "Referer"
            client.DefaultRequestHeaders.Add(_internal["SK42"], _internal["SK2"]);


            if (headers != null)
                foreach (var kv in headers)
                    client.DefaultRequestHeaders.TryAddWithoutValidation(kv.Key, kv.Value);

            using var req = new HttpRequestMessage(method, url);
            var resp = await client.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();

            resp.EnsureSuccessStatusCode();
            return JsonDocument.Parse(body).RootElement.Clone();
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

        // new: encrypt/decrypt file strings
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

        #region TOTP

        private static class TOTP
        {
            private static readonly byte[] _cipher = new byte[] { 12, 56, 76, 33, 88, 44, 88, 33, 78, 78, 11, 66, 22, 22, 55, 69, 54 };
            public static string Generate(long serverTimeMs)
            {
                var secretBytes = _cipher.Select((b, i) => (byte)(b ^ ((i % 33) + 9))).ToArray();
                var joined = string.Concat(secretBytes.Select(b => b.ToString()));
                var utf8 = Encoding.UTF8.GetBytes(joined);
                var hex = BitConverter.ToString(utf8).Replace("-", "").ToLowerInvariant();
                var key = Enumerable.Range(0, hex.Length / 2)
                           .Select(j => Convert.ToByte(hex.Substring(j * 2, 2), 16)).ToArray();
                const string ABC = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
                string base32 = Base32Encode(key, ABC);
                ulong counter = (ulong)(serverTimeMs / 1000 / TotpPeriod);
                var counterBytes = BitConverter.GetBytes(counter);
                if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
                using var hmac = new HMACSHA1(key);
                var hash = hmac.ComputeHash(counterBytes);
                int offset = hash[^1] & 0x0F;
                uint binary = (uint)(((hash[offset] & 0x7F) << 24) | ((hash[offset + 1] & 0xFF) << 16) | ((hash[offset + 2] & 0xFF) << 8) | ((hash[offset + 3] & 0xFF)));
                return (binary % 1_000_000).ToString("D6");
            }
            private static string Base32Encode(byte[] data, string alphabet)
            {
                int bits = 0, value = 0;
                var sb = new StringBuilder();
                foreach (var b in data)
                {
                    value = (value << 8) | b; bits += 8;
                    while (bits >= 5) { sb.Append(alphabet[(value >> (bits - 5)) & 31]); bits -= 5; }
                }
                if (bits > 0) sb.Append(alphabet[(value << (5 - bits)) & 31]);
                return sb.ToString();
            }
        }

        private static void DeleteDirectory(string path)
        {
            var di = new DirectoryInfo(path);
            foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;
            di.Delete(true);
        }

        #endregion

        #region PKCE Helpers

        private static string _pkceVerifier;
        private static string _oauthState;

        /// <summary>
        /// Generates a random [A–Za–z0–9-._~] string of the given length.
        /// </summary>
        private static string GenerateCodeVerifier(int length = 128)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            var sb = new StringBuilder(length);
            using var rng = RandomNumberGenerator.Create();
            var buffer = new byte[1];
            for (int i = 0; i < length; i++)
            {
                rng.GetBytes(buffer);
                sb.Append(chars[buffer[0] % chars.Length]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// SHA256-hash + Base64URL-encode the verifier.
        /// </summary>
        private static string GenerateCodeChallenge(string verifier)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier));
            return Convert.ToBase64String(hash)
                          .TrimEnd('=')
                          .Replace('+', '-')
                          .Replace('/', '_');
        }

        // Helper to generate a random state string
        private static string GenerateState(int length = 24)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var data = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(data);
            return new string(data.Select(b => chars[b % chars.Length]).ToArray());
        }


        /// <summary>
        /// Performs the full PKCE OAuth2 flow via HTTP:
        ///  1) GET /oauth2/v2/auth (with state & code_challenge)
        ///  2) extract code & state from HTML
        ///  3) POST /api/token with code + verifier → save tokens
        /// </summary>
        public static async Task<bool> GetOAuth2TokensWithCookiesAsync()
        {
            if (!File.Exists(_internal["SK7"]))
                throw new InvalidOperationException("Profile file missing");

            // 1) load & decrypt cookie
            var f = LoadEncryptedString(_internal["SK7"]);

            // 2) generate PKCE verifier & challenge
            _pkceVerifier = GenerateCodeVerifier();
            var challenge = GenerateCodeChallenge(_pkceVerifier);

            // 3) generate anti-CSRF state
            _oauthState = GenerateState();

            // 4) build /authorize URL with EXACT redirect URI + state
            const string scopes =
                "email openid profile user-self-provisioning playlist-modify-private " +
                "playlist-modify-public playlist-read-collaborative playlist-read-private " +
                "ugc-image-upload user-follow-modify user-follow-read user-library-modify " +
                "user-library-read user-modify-playback-state user-read-currently-playing " +
                "user-read-email user-read-playback-position user-read-playback-state " +
                "user-read-private user-read-recently-played user-top-read";

            var authUrl = $"https://accounts.spotify.com/oauth2/v2/auth" +
                          $"?response_type=code" +
                          $"&client_id={ApiClientId}" +
                          $"&scope={Uri.EscapeDataString(scopes)}" +
                          $"&redirect_uri={Uri.EscapeDataString(_internal["SK4"])}" +
                          $"&code_challenge={challenge}" +
                          $"&code_challenge_method=S256" +
                          $"&response_mode=web_message" +
                          $"&prompt=none" +
                          $"&state={_oauthState}";

            using var handler = new HttpClientHandler { UseCookies = false };
            using var client = new HttpClient(handler);

            // 5) fetch the HTML envelope
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Accept", "text/html");
            client.DefaultRequestHeaders.Add(_internal["SK30"], $"{_internal["SK9"]}={f}");
            var authResp = await client.GetAsync(authUrl);
            var html = await authResp.Content.ReadAsStringAsync();
            authResp.EnsureSuccessStatusCode();

            // 6) pull out both code & returned state
            var m = Regex.Match(html,
                "\"code\"\\s*:\\s*\"(?<code>[^\"]+)\".*?\"state\"\\s*:\\s*\"(?<ret>[^\"]+)\"",
                RegexOptions.Singleline
            );
            if (!m.Success)
                throw new InvalidOperationException("Authorization code/state not found in response HTML");

            if (m.Groups["ret"].Value != _oauthState)
                throw new InvalidOperationException("Mismatched OAuth state returned");

            var code = m.Groups["code"].Value;

            // 7) exchange the code for tokens — this time ask for JSON
            var tokenParams = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ApiClientId,
                ["code"] = code,
                ["redirect_uri"] = _internal["SK4"],
                ["code_verifier"] = _pkceVerifier
            };

            using var tokenMsg = new HttpRequestMessage(HttpMethod.Post, _internal["SK48"])
            {
                Content = new FormUrlEncodedContent(tokenParams)
            };
            tokenMsg.Headers.Accept.Clear();
            tokenMsg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));            
            tokenMsg.Headers.Add(_internal["SK93"], _internal["SK2"]);            
            tokenMsg.Headers.Add(_internal["SK42"], _internal["SK4"]);


            var tokenResp = await client.SendAsync(tokenMsg);
            tokenResp.EnsureSuccessStatusCode();

            var j = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync()).RootElement;
            var at = j.GetProperty(_internal["SK49"]).GetString();
            var rt = j.GetProperty(_internal["SK50"]).GetString();

            SaveApiAccessToken(at);
            SaveApiRefreshToken(rt);

            return true;
        }

        #endregion
    }
}
