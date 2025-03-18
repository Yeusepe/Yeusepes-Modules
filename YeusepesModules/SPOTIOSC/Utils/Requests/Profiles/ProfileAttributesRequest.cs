using System.Net.Http;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Credentials;


namespace YeusepesModules.SPOTIOSC.Utils.Requests.Profiles
{
    public class ProfileAttributesRequest : SpotifyRequest
    {

        private const string ProfileAttributesUrl =
            "https://api-partner.spotify.com/pathfinder/v1/query?operationName=profileAttributes&variables=%7B%7D&extensions=%7B%22persistedQuery%22%3A%7B%22version%22%3A1%2C%22sha256Hash%22%3A%2253bcb064f6cd18c23f752bc324a791194d20df612d8e1239c735144ab0399ced%22%7D%7D";
        public ProfileAttributesRequest(HttpClient httpClient, string accessToken, string clientToken)
            : base(httpClient, accessToken, clientToken) { }

        public async Task<string> FetchAsync()
        {
            var request = CreateRequest(HttpMethod.Get, ProfileAttributesUrl);
            return await SendAsync(request);
        }


        public static async Task<bool> FetchProfileAttributesAsync(HttpClient httpClient, string accessToken, string clientToken, Action<string> log, Action<string> logDebug)
        {
            logDebug("Fetching profile attributes...");

            try
            {
                // Create an instance of ProfileAttributesRequest, which uses the centralized SendAsync method.
                var profileRequest = new ProfileAttributesRequest(httpClient, accessToken, clientToken);
                string responseBody = await profileRequest.FetchAsync();

                logDebug("Response received: " + responseBody);

                // Parse the JSON response
                using (JsonDocument jsonDocument = JsonDocument.Parse(responseBody))
                {
                    if (jsonDocument.RootElement.TryGetProperty("data", out JsonElement dataElement) &&
                        dataElement.TryGetProperty("me", out JsonElement meElement) &&
                        meElement.TryGetProperty("profile", out JsonElement profileElement))
                    {
                        if (profileElement.TryGetProperty("name", out JsonElement nameProperty) &&
                            profileElement.TryGetProperty("username", out JsonElement usernameProperty))
                        {
                            string userName = nameProperty.GetString();
                            string userId = usernameProperty.GetString();
                            log($"Fetched user: {userName} ({userId})");
                            return true;
                        }
                        else
                        {
                            log("Profile element is missing 'name' or 'username' properties.");
                        }
                    }
                    else
                    {
                        log("Failed to extract user profile attributes: Missing 'data', 'me', or 'profile' in response.");
                    }
                }

                // Optionally log the full response for debugging
                logDebug("Full response body: " + responseBody);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Handle the case where token refresh fails and user needs to sign in.
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
