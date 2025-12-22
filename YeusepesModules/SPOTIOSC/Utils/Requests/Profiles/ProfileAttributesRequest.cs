using System.Net.Http;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.SPOTIOSC.Utils.Requests.Profiles
{
    public class RateLimitException : Exception
    {
        public RateLimitException(string message) : base(message) { }
    }

    public class ProfileAttributesRequest : SpotifyRequest
    {

        private const string profileUrl =
            "https://api.spotify.com/v1/me";
        public ProfileAttributesRequest(HttpClient httpClient, string accessToken, string clientToken)
            : base(httpClient, accessToken, clientToken) { }


        public static async Task<bool> FetchProfileAttributesAsync(HttpClient httpClient, string accessToken, string clientToken, Action<string> log, Action<string> logDebug)
        {
            logDebug("Fetching profile attributes from /v1/me...");
            
            // Try using OAuth2 API token first if available, fallback to web player token
            string tokenToUse = accessToken;
            string apiAccessToken = CredentialManager.LoadApiAccessToken();
            if (!string.IsNullOrEmpty(apiAccessToken))
            {
                logDebug("Using OAuth2 API access token for profile request");
                tokenToUse = apiAccessToken;
            }
            else
            {
                logDebug("Using web player access token for profile request (OAuth2 token not available)");
            }

            try
            {                                
                var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenToUse);

                HttpResponseMessage response = await httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                logDebug("Response received: " + responseBody);

                // Check status code before attempting JSON parse
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    log("Rate limit exceeded (429). Please wait before retrying.");
                    logDebug("Rate limit response body: " + responseBody);
                    throw new RateLimitException("Rate limit exceeded (429). Please wait before retrying.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    log($"HTTP error {(int)response.StatusCode}: {responseBody}");
                    return false;
                }

                using (JsonDocument jsonDocument = JsonDocument.Parse(responseBody))
                {
                    JsonElement root = jsonDocument.RootElement;
                    // Check for the essential properties; adjust these as needed.
                    if (root.TryGetProperty("display_name", out JsonElement displayNameElement) &&
                        root.TryGetProperty("id", out JsonElement idElement))
                    {
                        string displayName = displayNameElement.GetString();
                        string userId = idElement.GetString();
                        log($"Fetched user successfully! Display Name: {displayName}, ID: {userId}");
                        return true;
                    }
                    else
                    {
                        log("Response missing 'display_name' or 'id' properties.");
                        logDebug("Full response body: " + responseBody);
                        return false;
                    }
                }
            }
            catch (RateLimitException)
            {
                // Re-throw rate limit exceptions so caller can handle them differently
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
