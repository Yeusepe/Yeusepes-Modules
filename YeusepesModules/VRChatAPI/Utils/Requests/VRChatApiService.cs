using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using YeusepesModules.VRChatAPI.Utils;

namespace YeusepesModules.VRChatAPI.Utils.Requests
{
    public class VRChatApiService
    {
        private readonly VRChatRequestContext _context;
        private readonly VRChatUtilities _utilities;
        private const string VRChatApiBaseUrl = "https://api.vrchat.cloud/api/1";

        public VRChatApiService(VRChatRequestContext context, VRChatUtilities utilities)
        {
            _context = context;
            _utilities = utilities;
        }

        public async Task GetUserInfoAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching user information...");
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/auth/user");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    // Update context with user information
                    if (json.TryGetProperty("id", out var userId))
                        _context.UserId = userId.GetString();
                    
                    if (json.TryGetProperty("username", out var username))
                        _context.Username = username.GetString();
                    
                    if (json.TryGetProperty("displayName", out var displayName))
                        _context.DisplayName = displayName.GetString();
                    
                    if (json.TryGetProperty("bio", out var bio))
                        _context.Bio = bio.GetString();
                    
                    if (json.TryGetProperty("userIcon", out var userIcon))
                        _context.UserIcon = userIcon.GetString();
                    
                    if (json.TryGetProperty("status", out var status))
                        _context.UserStatus = status.GetString();
                    
                    if (json.TryGetProperty("location", out var location))
                        _context.Location = location.GetString();

                    _utilities.LogDebug($"User info updated: {_context.DisplayName}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch user info: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user info: {ex.Message}");
            }
        }

        public async Task GetWorldInfoAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching world information...");
                
                // This would need to be implemented based on VRChat API endpoints
                // For now, just log that it's not implemented
                _utilities.LogDebug("World info fetching not yet implemented");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching world info: {ex.Message}");
            }
        }

        public async Task GetInstanceInfoAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching instance information...");
                
                // This would need to be implemented based on VRChat API endpoints
                // For now, just log that it's not implemented
                _utilities.LogDebug("Instance info fetching not yet implemented");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching instance info: {ex.Message}");
            }
        }

        public async Task GetFriendsAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching friends list...");
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/auth/user/friends");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    _utilities.LogDebug($"Friends list fetched: {json.GetArrayLength()} friends");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch friends: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching friends: {ex.Message}");
            }
        }

        public async Task GetBlockedUsersAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching blocked users list...");
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/auth/user/blocklist");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    _utilities.LogDebug($"Blocked users list fetched: {json.GetArrayLength()} blocked users");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch blocked users: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching blocked users: {ex.Message}");
            }
        }

        public async Task GetMutedUsersAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching muted users list...");
                
                // This would need to be implemented based on VRChat API endpoints
                // For now, just log that it's not implemented
                _utilities.LogDebug("Muted users fetching not yet implemented");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching muted users: {ex.Message}");
            }
        }
    }
}
