using Octokit;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YeusepesModules.IDC.Encoder;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using static YeusepesModules.SPOTIOSC.SpotiOSC;

namespace YeusepesModules.SPOTIOSC.Utils
{
    public static class SpotifyJamRequests
    {
        private const string JamSessionEndpoint = "https://gue1-spclient.spotify.com/social-connect/v2/sessions/current_or_new?activate=true";
        internal static int _maxMemberCount;
        internal static bool _isListening;
        internal static bool _isControlling;
        internal static bool _queueOnlyMode;
        internal static int _participantCount;
        internal static bool _hostIsGroup;

        public static string _currentSessionId { get; set; }
        public static string _joinSessionToken { get; set; }
        public static string _shareableUrl { get; set; }
        public static bool _isInJam { get; set; } = false;
        public static async Task<bool> CreateSpotifyJam(SpotifyRequestContext context, SpotifyUtilities utilities)
        {
            try
            {
                utilities.LogDebug("Sending request to create Spotify Jam session...");

                // Create a GenericSpotifyRequest using the current context tokens.
                var genericRequest = new GenericSpotifyRequest(context.HttpClient, context.AccessToken, context.ClientToken);
                // Build the request using the base helper.
                using var request = genericRequest.CreateRequest(HttpMethod.Get, JamSessionEndpoint);
                // Send the request via the centralized SendAsync method.
                string responseBody = await genericRequest.SendRequestAsync(request);

                utilities.LogDebug("Spotify Jam session created successfully");
                utilities.LogDebug(responseBody);

                // Parse JSON response.
                var sessionData = JsonSerializer.Deserialize<JsonElement>(responseBody);

                utilities.LogDebug("Session data:");
                utilities.LogDebug(sessionData.ToString());

                // Extract sessionId and join token.
                if (sessionData.TryGetProperty("session_id", out JsonElement sessionIdElement))
                {
                    _currentSessionId = sessionIdElement.GetString();
                    utilities.LogDebug($"Session ID: {_currentSessionId}");
                }

                if (sessionData.TryGetProperty("join_session_token", out JsonElement joinTokenElement))
                {
                    _joinSessionToken = joinTokenElement.GetString();
                    utilities.LogDebug($"Join token: {_joinSessionToken}");
                }

                if (sessionData.TryGetProperty("shareable_url", out JsonElement shareableUrlElement))
                {
                    _shareableUrl = shareableUrlElement.GetString();
                    utilities.LogDebug($"Join through this URL: {_shareableUrl}");
                }

                if (sessionData.TryGetProperty("active", out JsonElement activeElement))
                {
                    _isInJam = activeElement.GetBoolean();
                    context.IsInJam = _isInJam;
                    utilities.SendParameter(SpotiOSC.SpotiParameters.InAJam, true);
                    utilities.SendParameter(SpotiOSC.SpotiParameters.IsJamOwner, true);
                    utilities.LogDebug($"Is in Jam: {_isInJam}");
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                utilities.LogDebug("Token refresh failed. Please sign in again.");
                return false;
            }
            catch (Exception ex)
            {
                utilities.LogDebug($"An error occurred: {ex.Message}");
                utilities.SendParameter(SpotiOSC.SpotiParameters.Error, true);
                return false;
            }
        }



        public static void HandleJamJoin(SpotifyRequestContext context, SpotifyUtilities utilities)
        {
            utilities.LogDebug("Joining the current jam...");
            _isInJam = true;
            context.IsInJam = _isInJam;
            utilities.SendParameter(SpotiOSC.SpotiParameters.InAJam, true);
            utilities.SendParameter(SpotiOSC.SpotiParameters.IsJamOwner, false);
            utilities.LogDebug("Successfully joined the jam.");
        }

        public static void HandleJamLeave(SpotifyRequestContext context, SpotifyUtilities utilities)
        {
            utilities.LogDebug("Leaving the current jam...");
            _currentSessionId = null;
            _joinSessionToken = null;
            _isInJam = false;
            context.IsInJam = _isInJam;
            utilities.SendParameter(SpotiOSC.SpotiParameters.InAJam, false);
            utilities.SendParameter(SpotiOSC.SpotiParameters.IsJamOwner, false);
            utilities.SendParameter(SpotiOSC.SpotiParameters.WantJam, false);
            utilities.LogDebug("Successfully left the jam.");
        }


        public static async Task<bool> LeaveSpotifyJam(string sessionId, SpotifyRequestContext context, SpotifyUtilities utilities)
        {
            string optionsUrl = $"https://gue1-spclient.spotify.com/social-connect/v3/sessions/{sessionId}/leave";
            try
            {
                // Step 1: OPTIONS Request (can remain manual if no token handling is needed)
                using (var optionsRequest = new HttpRequestMessage(HttpMethod.Options, optionsUrl))
                {
                    optionsRequest.Headers.Add("Accept", "*/*");
                    optionsRequest.Headers.Add("Accept-Language", "en-Latn-US,en-US;q=0.9,en-Latn;q=0.8,en;q=0.7");
                    optionsRequest.Headers.Add("Access-Control-Request-Headers", "app-platform,authorization,client-token,content-type,spotify-app-version");
                    optionsRequest.Headers.Add("Access-Control-Request-Method", "POST");
                    optionsRequest.Headers.Add("Origin", "https://xpui.app.spotify.com");
                    optionsRequest.Headers.Add("Referer", "https://xpui.app.spotify.com/");
                    optionsRequest.Headers.Add("Sec-Fetch-Dest", "empty");
                    optionsRequest.Headers.Add("Sec-Fetch-Mode", "cors");
                    optionsRequest.Headers.Add("Sec-Fetch-Site", "same-site");
                    optionsRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.6723.117 Spotify/1.2.52.442 Safari/537.36");

                    utilities.LogDebug("Sending OPTIONS request for leaving Spotify Jam...");
                    var optionsResponse = await context.HttpClient.SendAsync(optionsRequest);
                    if (!optionsResponse.IsSuccessStatusCode)
                    {
                        utilities.LogDebug($"Failed OPTIONS request. Status: {optionsResponse.StatusCode}");
                        return false;
                    }
                }

                // Step 2: POST Request using centralized token handling.
                var genericRequest = new GenericSpotifyRequest(context.HttpClient, context.AccessToken, context.ClientToken);
                using var postRequest = genericRequest.CreateRequest(HttpMethod.Post, optionsUrl, new StringContent("{}", Encoding.UTF8, "application/json"));

                // Add additional headers specific to the POST request
                postRequest.Headers.Remove("Accept");
                postRequest.Headers.Add("Accept", "application/json");
                postRequest.Headers.Add("Accept-Language", "en");
                postRequest.Headers.Add("Origin", "https://xpui.app.spotify.com");
                postRequest.Headers.Add("Referer", "https://xpui.app.spotify.com/");
                postRequest.Headers.Add("Sec-CH-UA", "\"Not?A_Brand\";v=\"99\", \"Chromium\";v=\"130\"");
                postRequest.Headers.Add("Sec-CH-UA-Mobile", "?0");
                postRequest.Headers.Add("Sec-CH-UA-Platform", "\"Windows\"");
                postRequest.Headers.Add("Sec-Fetch-Dest", "empty");
                postRequest.Headers.Add("Sec-Fetch-Mode", "cors");
                postRequest.Headers.Add("Sec-Fetch-Site", "same-site");
                postRequest.Headers.Add("Spotify-App-Version", "1.2.52.442");

                utilities.LogDebug("Sending POST request to leave Spotify Jam...");
                string postResponseBody = await genericRequest.SendRequestAsync(postRequest);

                utilities.LogDebug("Successfully left the Spotify Jam session.");
                utilities.SendParameter(SpotiOSC.SpotiParameters.InAJam, false);
                _currentSessionId = null;
                _isInJam = false;
                context.IsInJam = _isInJam;

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                utilities.LogDebug("Token refresh failed. Please sign in again.");
                return false;
            }
            catch (Exception ex)
            {
                utilities.LogDebug($"An error occurred while leaving the Spotify Jam session: {ex.Message}");
                utilities.SendParameter(SpotiOSC.SpotiParameters.Error, true);
                return false;
            }
        }



        public static async Task<bool> JoinSpotifyJam(string sessionId, SpotifyRequestContext context, SpotifyUtilities utilities)
        {
            try
            {
                // Update current playback state to get active device ID
                await SpotifyRequest.ExtractCurrentlyPlayingState(context, utilities);
                string deviceId = context.DeviceId;
                if (string.IsNullOrEmpty(deviceId))
                {
                    utilities.LogDebug("Failed to find an active device. Cannot join Spotify Jam.");
                    return false;
                }

                // Construct URL with query parameters
                string joinJamUrl = $"https://gue1-spclient.spotify.com/social-connect/v2/sessions/join/{sessionId}" +
                                      $"?playback_control=listen_and_control" +
                                      $"&join_type=deeplinking" +
                                      $"&local_device_id={deviceId}";
                utilities.LogDebug("Constructed Join URL: " + joinJamUrl);

                // Create the request using POST with an empty JSON body
                var genericRequest = new GenericSpotifyRequest(context.HttpClient, context.AccessToken, context.ClientToken);
                using (var request = genericRequest.CreateRequest(
                           HttpMethod.Post,
                           joinJamUrl,
                           new StringContent("{}", Encoding.UTF8, "application/json")))
                {
                    // Set headers to mimic the browser request from the cURL sample
                    request.Headers.Remove("User-Agent");
                    request.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.6778.109 Spotify/1.2.57.463 Safari/537.36");
                    request.Headers.Add("accept", "application/json");
                    request.Headers.Add("accept-language", "en");
                    request.Headers.Add("origin", "https://xpui.app.spotify.com");
                    request.Headers.Add("priority", "u=1, i");
                    request.Headers.Add("referer", "https://xpui.app.spotify.com/");
                    request.Headers.Add("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
                    request.Headers.Add("sec-ch-ua-mobile", "?0");
                    request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");
                    request.Headers.Add("sec-fetch-dest", "empty");
                    request.Headers.Add("sec-fetch-mode", "cors");
                    request.Headers.Add("sec-fetch-site", "same-site");
                    request.Headers.Add("spotify-app-version", "1.2.57.463");

                    utilities.LogDebug("Sending request to join Spotify Jam session...");

                    // Use the generic request's SendRequestAsync to handle sending and token refresh logic.
                    string responseBody = await genericRequest.SendRequestAsync(request);
                    utilities.LogDebug("Response Content: " + responseBody);

                    // Update the session state if the request was successful.
                    utilities.LogDebug("Successfully joined the Spotify Jam session.");
                    _currentSessionId = sessionId;
                    _isInJam = true;
                    context.IsInJam = _isInJam;
                    return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                utilities.LogDebug("Token refresh failed. Please sign in again.");
                return false;
            }
            catch (Exception ex)
            {
                utilities.LogDebug($"An error occurred while joining the Spotify Jam session: {ex.Message}");
                utilities.SendParameter(SpotiOSC.SpotiParameters.Error, true);
                return false;
            }
        }






        public static void UpdateSessionDetails(JsonElement payload, SpotifyRequestContext context, SpotifyUtilities utilities)
        {
            try
            {
                var session = payload.GetProperty("session");

                // Extract session owner ID
                string sessionOwnerId = session.GetProperty("session_owner_id").GetString();

                // Find the owner's display name
                var members = session.GetProperty("session_members").EnumerateArray();
                foreach (var member in members)
                {
                    if (member.GetProperty("id").GetString() == sessionOwnerId)
                    {
                        context.JamOwnerName = member.GetProperty("display_name").GetString();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating session details: {ex.Message}");
                utilities.SendParameter(SpotiOSC.SpotiParameters.Error, true);
            }
        }


        public async static Task<string> GetJoinSessionIdAsync(string shareableCode, SpotifyUtilities utilities)
        {
            // Log raw details of the shareableCode
            utilities.LogDebug("Raw shareableCode: " + shareableCode);
            utilities.LogDebug("Raw shareableCode length: " + shareableCode.Length);
            byte[] rawBytes = Encoding.UTF8.GetBytes(shareableCode);
            utilities.LogDebug("Raw shareableCode bytes: " + BitConverter.ToString(rawBytes));

            // Trim any potential whitespace or newline characters
            shareableCode = shareableCode.Trim();
            utilities.LogDebug("Trimmed shareableCode: " + shareableCode);
            utilities.LogDebug("Trimmed shareableCode length: " + shareableCode.Length);
            byte[] trimmedBytes = Encoding.UTF8.GetBytes(shareableCode);
            utilities.LogDebug("Trimmed shareableCode bytes: " + BitConverter.ToString(trimmedBytes));

            // Construct the initial URL
            string url = $"https://spotify.link/{shareableCode}";
            utilities.LogDebug("Constructed URL: " + url);

            try
            {
                // Create an HttpClientHandler that disables automatic redirection
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    AllowAutoRedirect = false
                };

                using (var httpClient = new HttpClient(handler))
                {
                    // Force HTTP/1.1 and disable Expect: 100-continue
                    httpClient.DefaultRequestVersion = HttpVersion.Version11;
                    httpClient.DefaultRequestHeaders.ExpectContinue = false;

                    // Set headers to mimic Python's request
                    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                        "AppleWebKit/537.36 (KHTML, like Gecko) " +
                        "Chrome/134.0.0.0 Safari/537.36");
                    httpClient.DefaultRequestHeaders.Accept.Clear();
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

                    // Log the request headers
                    utilities.LogDebug("Request Headers:");
                    foreach (var header in httpClient.DefaultRequestHeaders)
                    {
                        utilities.LogDebug(header.Key + ": " + string.Join(", ", header.Value));
                    }

                    // Manually follow redirects
                    string currentUrl = url;
                    int redirectCount = 0;
                    const int maxRedirects = 10;
                    HttpResponseMessage response = null;

                    while (redirectCount < maxRedirects)
                    {
                        utilities.LogDebug("Requesting URL: " + currentUrl);
                        response = await httpClient.GetAsync(currentUrl);
                        utilities.LogDebug("Response Status Code: " + response.StatusCode);

                        // If the response is a redirect, update the URL and continue the loop
                        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                        {
                            if (response.Headers.Location != null)
                            {
                                // Build an absolute URL if necessary
                                currentUrl = response.Headers.Location.IsAbsoluteUri
                                    ? response.Headers.Location.ToString()
                                    : new Uri(new Uri(currentUrl), response.Headers.Location).ToString();
                                utilities.LogDebug("Redirecting to: " + currentUrl);
                                redirectCount++;
                                continue;
                            }
                            else
                            {
                                utilities.LogDebug("Redirect received but no Location header found.");
                                break;
                            }
                        }
                        else
                        {
                            // Non-redirect response received; exit the loop.
                            break;
                        }
                    }

                    if (response == null || !response.IsSuccessStatusCode)
                    {
                        utilities.LogDebug("Failed to load the short link page.");
                        return null;
                    }

                    // Read the final response content
                    string responseContent = await response.Content.ReadAsStringAsync();
                    utilities.LogDebug("Final Response Content:");
                    utilities.LogDebug(responseContent);

                    // Log a preview (first 1000 characters) of the HTML
                    int previewLength = Math.Min(1000, responseContent.Length);
                    utilities.LogDebug("HTML Response (first 1000 chars):");
                    utilities.LogDebug(responseContent.Substring(0, previewLength));

                    // Extract the share token using a regex
                    Regex regex = new Regex(@"https:\/\/shareables\.scdn\.co\/publish\/socialsession\/([a-zA-Z0-9]+)");
                    Match match = regex.Match(responseContent);

                    if (!match.Success)
                    {
                        utilities.LogDebug("No share token found in the HTML.");
                        return null;
                    }
                    string shareToken = match.Groups[1].Value;
                    utilities.LogDebug("Extracted Share Token: " + shareToken);

                    return shareToken;
                }
            }
            catch (Exception ex)
            {
                utilities.LogDebug("An error occurred in GetJoinSessionIdAsync: " + ex.Message);
                return null;
            }
        }











        /// <summary>
        /// Calls the Spotify URL dispenser API to generate a shareable URL,
        /// then extracts and returns the last segment of the URL.
        /// </summary>
        /// <param name="spotifyUri">The Spotify URI for the session.</param>
        /// <returns>The code part of the shareable URL (e.g. "M8CppzmmNRb").</returns>
        public async static Task<string> GenerateShareableUrlAsync(string spotifyUri, SpotifyRequestContext context, SpotifyUtilities utilities)
        {
            const string urlDispenserEndpoint = "https://gue1-spclient.spotify.com/url-dispenser/v1/generate-url";

            try
            {
                utilities.LogDebug("Starting URL generation for Spotify Jam session...");

                var sessionId = spotifyUri.Split(':').Last();
                utilities.LogDebug($"Session ID extracted: {sessionId}");

                var payload = new
                {
                    spotify_uri = spotifyUri,
                    custom_data = new[]
                    {
                new { key = "ssp", value = "1" },
                new { key = "app_destination", value = "socialsession" }
            },
                    link_preview = new
                    {
                        title = "Join my Jam on Spotify",
                        image_url = $"https://shareables.scdn.co/publish/socialsession/{sessionId}"
                    },
                    utm_parameters = new
                    {
                        utm_medium = "share-link",
                        utm_source = "share-options-sheet",
                        utm_campaign = (string)null,
                        utm_term = (string)null,
                        utm_content = (string)null
                    }
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var jsonPayload = JsonSerializer.Serialize(payload, jsonOptions);

                using var request = new HttpRequestMessage(HttpMethod.Post, urlDispenserEndpoint)
                {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                };

                // Set headers properly using proper header methods
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");

                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
                request.Headers.Add("app-platform", "Win32_x86_64");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
                request.Headers.Add("client-token", context.ClientToken);
                request.Headers.Add("origin", "https://xpui.app.spotify.com");
                request.Headers.Add("priority", "u=1, i");
                request.Headers.Add("referer", "https://xpui.app.spotify.com/");
                request.Headers.Add("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
                request.Headers.Add("sec-ch-ua-mobile", "?0");
                request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");
                request.Headers.Add("sec-fetch-dest", "empty");
                request.Headers.Add("sec-fetch-mode", "cors");
                request.Headers.Add("sec-fetch-site", "same-site");
                request.Headers.Add("spotify-app-version", "1.2.57.463");
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.6778.109 Spotify/1.2.57.463 Safari/537.36");

                utilities.LogDebug("Sending request to URL dispenser endpoint...");
                var response = await context.HttpClient.SendAsync(request);

                // Handle response with safe charset decoding
                byte[] responseBytes = await response.Content.ReadAsByteArrayAsync();
                string responseBody = Encoding.UTF8.GetString(responseBytes);

                utilities.LogDebug("API response: " + responseBody);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Request failed with status code {response.StatusCode}");
                }

                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseBody);
                return responseJson.GetProperty("shareable_url").GetString().Split('/').Last();
            }
            catch (Exception ex)
            {
                utilities.LogDebug($"Failed to generate shareable URL: {ex.Message}");
                throw;
            }
        }
    }
}