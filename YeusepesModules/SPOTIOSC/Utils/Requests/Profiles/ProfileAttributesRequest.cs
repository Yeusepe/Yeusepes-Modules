using System.Net.Http;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Credentials;


namespace YeusepesModules.SPOTIOSC.Utils.Requests.Profiles
{
    public class ProfileAttributesRequest : SpotifyRequest
    {

        private const string profileUrl =
            "https://api.spotify.com/v1/me";
        public ProfileAttributesRequest(HttpClient httpClient, string accessToken, string clientToken)
            : base(httpClient, accessToken, clientToken) { }

        public async Task<string> FetchAsync()
        {
            var request = CreateRequest(HttpMethod.Get, profileUrl);
            return await SendAsync(request);
        }


        public static async Task<bool> FetchProfileAttributesAsync(HttpClient httpClient, string accessToken, string clientToken, Action<string> log, Action<string> logDebug)
        {
            logDebug("Fetching profile attributes from /v1/me...");

            try
            {                                
                var request = new HttpRequestMessage(HttpMethod.Get, profileUrl);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                HttpResponseMessage response = await httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                logDebug("Response received: " + responseBody);

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
