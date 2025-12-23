using System.Net.Http;
using System.Text;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Credentials;


namespace YeusepesModules.SPOTIOSC.Utils.Requests.Profiles
{
    public class ProfileAttributesRequest : SpotifyRequest
    {
        public ProfileAttributesRequest(HttpClient httpClient, string accessToken, string clientToken)
            : base(httpClient, accessToken, clientToken) { }

        public static async Task<bool> FetchProfileAttributesAsync(HttpClient httpClient, string accessToken, string clientToken, Action<string> log, Action<string> logDebug)
        {
            try
            {
                // Use Pathfinder API endpoint
                const string pathfinderUrl = "https://api-partner.spotify.com/pathfinder/v2/query";
                
                // Prepare the GraphQL query body
                var queryBody = new
                {
                    variables = new { },
                    operationName = "profileAttributes",
                    extensions = new
                    {
                        persistedQuery = new
                        {
                            version = 1,
                            sha256Hash = "53bcb064f6cd18c23f752bc324a791194d20df612d8e1239c735144ab0399ced"
                        }
                    }
                };

                string jsonBody = JsonSerializer.Serialize(queryBody);
                var request = new HttpRequestMessage(HttpMethod.Post, pathfinderUrl);
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                request.Content.Headers.ContentType.CharSet = "UTF-8";
                
                // Set headers
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                request.Headers.Add("client-token", clientToken);
                request.Headers.Add("app-platform", "WebPlayer");
                request.Headers.Add("spotify-app-version", "1.2.80.289.gd6b01cc3");
                request.Headers.Add("accept", "application/json");
                request.Headers.Add("accept-language", "en");
                request.Headers.Add("priority", "u=1, i");
                request.Headers.Add("sec-fetch-dest", "empty");
                request.Headers.Add("sec-fetch-mode", "cors");
                request.Headers.Add("sec-fetch-site", "same-site");

                logDebug("Fetching profile attributes from Pathfinder API...");
                HttpResponseMessage response = await httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                logDebug("Response received: " + responseBody);

                // Check for rate limit errors (429) before parsing JSON
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    log("API rate limit exceeded (429). Please wait before trying again.");
                    logDebug($"Rate limit response: {responseBody}");
                    var rateLimitEx = new HttpRequestException("API rate limit exceeded (429)");
                    rateLimitEx.Data["StatusCode"] = response.StatusCode;
                    throw rateLimitEx;
                }

                // Check for errors in JSON response
                if (!response.IsSuccessStatusCode)
                {
                    try
                    {
                        using (JsonDocument errorDoc = JsonDocument.Parse(responseBody))
                        {
                            JsonElement errorRoot = errorDoc.RootElement;
                            if (errorRoot.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var error in errors.EnumerateArray())
                                {
                                    if (error.TryGetProperty("extensions", out var extensions) &&
                                        extensions.TryGetProperty("statusCode", out var statusCode))
                                    {
                                        int status = statusCode.GetInt32();
                                        if (status == 429)
                                        {
                                            log("API rate limit exceeded (429) in error response.");
                                            logDebug($"Rate limit error response: {responseBody}");
                                            var rateLimitEx = new HttpRequestException("API rate limit exceeded (429)");
                                            rateLimitEx.Data["StatusCode"] = System.Net.HttpStatusCode.TooManyRequests;
                                            throw rateLimitEx;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // If we can't parse the error, continue with normal error handling
                    }
                }

                response.EnsureSuccessStatusCode();

                using (JsonDocument jsonDocument = JsonDocument.Parse(responseBody))
                {
                    JsonElement root = jsonDocument.RootElement;
                    
                    // Pathfinder response structure: data.me.profile
                    if (root.TryGetProperty("data", out JsonElement dataElement))
                    {
                        if (dataElement.TryGetProperty("me", out JsonElement meElement))
                        {
                            if (meElement.TryGetProperty("profile", out JsonElement profileElement))
                            {
                                // Check for name and uri/username in profile
                                if (profileElement.TryGetProperty("name", out JsonElement nameElement))
                                {
                                    string displayName = nameElement.GetString();
                                    
                                    // Try to get ID from uri or username
                                    string userId = null;
                                    if (profileElement.TryGetProperty("uri", out JsonElement uriElement))
                                    {
                                        string uri = uriElement.GetString();
                                        // Extract user ID from spotify:user:12139851907 format
                                        if (uri != null && uri.StartsWith("spotify:user:", StringComparison.OrdinalIgnoreCase))
                                        {
                                            userId = uri.Substring("spotify:user:".Length);
                                        }
                                        else
                                        {
                                            userId = uri;
                                        }
                                    }
                                    else if (profileElement.TryGetProperty("username", out JsonElement usernameElement))
                                    {
                                        userId = usernameElement.GetString();
                                    }
                                    
                                    if (!string.IsNullOrEmpty(displayName) && !string.IsNullOrEmpty(userId))
                                    {
                                        log($"Fetched user successfully! Display Name: {displayName}, ID: {userId}");
                                        return true;
                                    }
                                    else
                                    {
                                        log("Response missing 'name' or user ID in profile.");
                                        logDebug("Full response body: " + responseBody);
                                        return false;
                                    }
                                }
                                else
                                {
                                    log("Response missing 'name' in profile.");
                                    logDebug("Full response body: " + responseBody);
                                    return false;
                                }
                            }
                            else
                            {
                                log("Response missing 'profile' in 'me'.");
                                logDebug("Full response body: " + responseBody);
                                return false;
                            }
                        }
                        else
                        {
                            log("Response missing 'me' in 'data'.");
                            logDebug("Full response body: " + responseBody);
                            return false;
                        }
                    }
                    else
                    {
                        log("Response missing 'data' property.");
                        logDebug("Full response body: " + responseBody);
                        return false;
                    }
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("429") || ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || 
                                                  (ex.Data.Contains("StatusCode") && ex.Data["StatusCode"] is System.Net.HttpStatusCode statusCode && statusCode == System.Net.HttpStatusCode.TooManyRequests))
            {
                // Re-throw rate limit exceptions so they can be handled by the caller
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                log("Access token refresh failed. Please sign in again.");
                return false;
            }
            catch (Exception ex)
            {
                log($"Error fetching profile attributes: {ex.Message}");
                return false;
            }
        }
    }
}
