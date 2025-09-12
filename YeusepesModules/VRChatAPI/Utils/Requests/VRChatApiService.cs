using System;
using System.Collections.Generic;
using System.Linq;
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
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/worlds/active");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var worlds = new List<WorldInfo>();
                    
                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var worldElement in dataArray.EnumerateArray())
                        {
                            var world = new WorldInfo
                            {
                                Id = worldElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Name = worldElement.TryGetProperty("name", out var name) ? name.GetString() : "",
                                Description = worldElement.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                Capacity = worldElement.TryGetProperty("capacity", out var cap) ? cap.GetInt32() : 0,
                                Occupants = worldElement.TryGetProperty("occupants", out var occ) ? occ.GetInt32() : 0,
                                Tags = worldElement.TryGetProperty("tags", out var tags) ? 
                                    tags.EnumerateArray().Select(t => t.GetString()).ToArray() : new string[0],
                                ImageUrl = worldElement.TryGetProperty("imageUrl", out var img) ? img.GetString() : "",
                                AuthorName = worldElement.TryGetProperty("authorName", out var author) ? author.GetString() : "",
                                AuthorId = worldElement.TryGetProperty("authorId", out var authorId) ? authorId.GetString() : "",
                                IsPublic = worldElement.TryGetProperty("public", out var isPublic) ? isPublic.GetBoolean() : false,
                                IsPrivate = worldElement.TryGetProperty("private", out var isPrivate) ? isPrivate.GetBoolean() : false
                            };
                            worlds.Add(world);
                        }
                    }

                    _context.Worlds = worlds;
                    _utilities.LogDebug($"Worlds fetched: {worlds.Count} worlds");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch worlds: {response.StatusCode}");
                }
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
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/instances/active");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var instances = new List<InstanceInfo>();
                    
                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var instanceElement in dataArray.EnumerateArray())
                        {
                            var instance = new InstanceInfo
                            {
                                Id = instanceElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Type = instanceElement.TryGetProperty("type", out var type) ? type.GetString() : "",
                                Owner = instanceElement.TryGetProperty("ownerId", out var owner) ? owner.GetString() : "",
                                Capacity = instanceElement.TryGetProperty("capacity", out var cap) ? cap.GetInt32() : 0,
                                Occupants = instanceElement.TryGetProperty("occupants", out var occ) ? occ.GetInt32() : 0,
                                CanRequestInvite = instanceElement.TryGetProperty("canRequestInvite", out var canReq) ? canReq.GetBoolean() : false,
                                IsFull = instanceElement.TryGetProperty("isFull", out var isFull) ? isFull.GetBoolean() : false,
                                IsHidden = instanceElement.TryGetProperty("isHidden", out var isHidden) ? isHidden.GetBoolean() : false,
                                IsFriendsOnly = instanceElement.TryGetProperty("isFriendsOnly", out var isFriendsOnly) ? isFriendsOnly.GetBoolean() : false,
                                IsFriendsOfFriends = instanceElement.TryGetProperty("isFriendsOfFriends", out var isFriendsOfFriends) ? isFriendsOfFriends.GetBoolean() : false,
                                IsInviteOnly = instanceElement.TryGetProperty("isInviteOnly", out var isInviteOnly) ? isInviteOnly.GetBoolean() : false
                            };
                            instances.Add(instance);
                        }
                    }

                    _context.Instances = instances;
                    _utilities.LogDebug($"Instances fetched: {instances.Count} instances");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch instances: {response.StatusCode}");
                }
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

                    var friends = new List<FriendInfo>();
                    
                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var friendElement in dataArray.EnumerateArray())
                        {
                            var friend = new FriendInfo
                            {
                                Id = friendElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Username = friendElement.TryGetProperty("username", out var username) ? username.GetString() : "",
                                DisplayName = friendElement.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : "",
                                Status = friendElement.TryGetProperty("status", out var status) ? status.GetString() : "",
                                StatusDescription = friendElement.TryGetProperty("statusDescription", out var statusDesc) ? statusDesc.GetString() : "",
                                Location = friendElement.TryGetProperty("location", out var location) ? location.GetString() : "",
                                UserIcon = friendElement.TryGetProperty("userIcon", out var userIcon) ? userIcon.GetString() : "",
                                IsOnline = friendElement.TryGetProperty("isOnline", out var isOnline) ? isOnline.GetBoolean() : false,
                                LastLogin = friendElement.TryGetProperty("lastLogin", out var lastLogin) ? 
                                    DateTime.Parse(lastLogin.GetString()) : DateTime.MinValue
                            };
                            friends.Add(friend);
                        }
                    }

                    _context.Friends = friends;
                    _utilities.LogDebug($"Friends list fetched: {friends.Count} friends");
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
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/auth/user/mutelist");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    _utilities.LogDebug($"Muted users list fetched: {json.GetArrayLength()} muted users");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch muted users: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching muted users: {ex.Message}");
            }
        }

        public async Task GetCalendarEventsAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching calendar events...");
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/calendar/featured");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var calendarEvents = new List<CalendarEvent>();
                    
                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var eventElement in dataArray.EnumerateArray())
                        {
                            var calendarEvent = new CalendarEvent
                            {
                                Id = eventElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Name = eventElement.TryGetProperty("name", out var name) ? name.GetString() : "",
                                Description = eventElement.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                StartTime = eventElement.TryGetProperty("startTime", out var startTime) ? 
                                    DateTime.Parse(startTime.GetString()) : DateTime.MinValue,
                                EndTime = eventElement.TryGetProperty("endTime", out var endTime) ? 
                                    DateTime.Parse(endTime.GetString()) : DateTime.MinValue,
                                Location = eventElement.TryGetProperty("location", out var location) ? location.GetString() : "",
                                WorldId = eventElement.TryGetProperty("worldId", out var worldId) ? worldId.GetString() : "",
                                InstanceId = eventElement.TryGetProperty("instanceId", out var instanceId) ? instanceId.GetString() : ""
                            };
                            calendarEvents.Add(calendarEvent);
                        }
                    }

                    _context.CalendarEvents = calendarEvents;
                    _utilities.LogDebug($"Calendar events fetched: {calendarEvents.Count} events");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch calendar events: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching calendar events: {ex.Message}");
            }
        }

        public async Task GetNotificationsAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching notifications...");
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/auth/user/notifications");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var notifications = new List<Notification>();
                    
                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var notificationElement in dataArray.EnumerateArray())
                        {
                            var notification = new Notification
                            {
                                Id = notificationElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Type = notificationElement.TryGetProperty("type", out var type) ? type.GetString() : "",
                                Message = notificationElement.TryGetProperty("message", out var message) ? message.GetString() : "",
                                CreatedAt = notificationElement.TryGetProperty("created_at", out var createdAt) ? 
                                    DateTime.Parse(createdAt.GetString()) : DateTime.MinValue,
                                IsRead = notificationElement.TryGetProperty("isRead", out var isRead) ? isRead.GetBoolean() : false
                            };
                            notifications.Add(notification);
                        }
                    }

                    _context.Notifications = notifications;
                    _utilities.LogDebug($"Notifications fetched: {notifications.Count} notifications");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch notifications: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching notifications: {ex.Message}");
            }
        }

        public async Task GetFavoritesAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching favorites...");
                
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/favorites");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var favorites = new List<Favorite>();
                    
                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var favoriteElement in dataArray.EnumerateArray())
                        {
                            var favorite = new Favorite
                            {
                                Id = favoriteElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Type = favoriteElement.TryGetProperty("type", out var type) ? type.GetString() : "",
                                Name = favoriteElement.TryGetProperty("name", out var name) ? name.GetString() : "",
                                Description = favoriteElement.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                WorldId = favoriteElement.TryGetProperty("worldId", out var worldId) ? worldId.GetString() : "",
                                InstanceId = favoriteElement.TryGetProperty("instanceId", out var instanceId) ? instanceId.GetString() : ""
                            };
                            favorites.Add(favorite);
                        }
                    }

                    _context.Favorites = favorites;
                    _utilities.LogDebug($"Favorites fetched: {favorites.Count} favorites");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch favorites: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching favorites: {ex.Message}");
            }
        }

        // New comprehensive API methods
        public async Task GetGroupsAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching groups...");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/groups");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var groups = new List<GroupInfo>();

                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var groupElement in dataArray.EnumerateArray())
                        {
                            var group = new GroupInfo
                            {
                                Id = groupElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Name = groupElement.TryGetProperty("name", out var name) ? name.GetString() : "",
                                Description = groupElement.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                MemberCount = groupElement.TryGetProperty("memberCount", out var memberCount) ? memberCount.GetInt32() : 0,
                                IsActive = groupElement.TryGetProperty("isActive", out var isActive) ? isActive.GetBoolean() : false
                            };
                            groups.Add(group);
                        }
                    }

                    _context.Groups = groups;
                    _utilities.LogDebug($"Groups fetched: {groups.Count} groups");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch groups: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching groups: {ex.Message}");
            }
        }

        public async Task GetAvatarsAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching avatars...");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/avatars");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var avatars = new List<AvatarInfo>();

                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var avatarElement in dataArray.EnumerateArray())
                        {
                            var avatar = new AvatarInfo
                            {
                                Id = avatarElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Name = avatarElement.TryGetProperty("name", out var name) ? name.GetString() : "",
                                Description = avatarElement.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                AuthorName = avatarElement.TryGetProperty("authorName", out var authorName) ? authorName.GetString() : "",
                                AuthorId = avatarElement.TryGetProperty("authorId", out var authorId) ? authorId.GetString() : "",
                                ImageUrl = avatarElement.TryGetProperty("imageUrl", out var imageUrl) ? imageUrl.GetString() : "",
                                IsPublic = avatarElement.TryGetProperty("isPublic", out var isPublic) ? isPublic.GetBoolean() : false,
                                IsFeatured = avatarElement.TryGetProperty("isFeatured", out var isFeatured) ? isFeatured.GetBoolean() : false
                            };
                            avatars.Add(avatar);
                        }
                    }

                    _context.Avatars = avatars;
                    _utilities.LogDebug($"Avatars fetched: {avatars.Count} avatars");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch avatars: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching avatars: {ex.Message}");
            }
        }

        public async Task GetWorldsAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching worlds...");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/worlds");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var worlds = new List<WorldInfo>();

                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var worldElement in dataArray.EnumerateArray())
                        {
                            var world = new WorldInfo
                            {
                                Id = worldElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Name = worldElement.TryGetProperty("name", out var name) ? name.GetString() : "",
                                Description = worldElement.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                Capacity = worldElement.TryGetProperty("capacity", out var capacity) ? capacity.GetInt32() : 0,
                                Occupants = worldElement.TryGetProperty("occupants", out var occupants) ? occupants.GetInt32() : 0,
                                Tags = worldElement.TryGetProperty("tags", out var tags) ? 
                                    tags.EnumerateArray().Select(t => t.GetString()).ToArray() : new string[0],
                                ImageUrl = worldElement.TryGetProperty("imageUrl", out var imageUrl) ? imageUrl.GetString() : "",
                                AuthorName = worldElement.TryGetProperty("authorName", out var authorName) ? authorName.GetString() : "",
                                AuthorId = worldElement.TryGetProperty("authorId", out var authorId) ? authorId.GetString() : "",
                                IsPublic = worldElement.TryGetProperty("isPublic", out var isPublic) ? isPublic.GetBoolean() : false,
                                IsPrivate = worldElement.TryGetProperty("isPrivate", out var isPrivate) ? isPrivate.GetBoolean() : false,
                                IsFeatured = worldElement.TryGetProperty("isFeatured", out var isFeatured) ? isFeatured.GetBoolean() : false,
                                IsLabs = worldElement.TryGetProperty("isLabs", out var isLabs) ? isLabs.GetBoolean() : false,
                                IsCommunityLabs = worldElement.TryGetProperty("isCommunityLabs", out var isCommunityLabs) ? isCommunityLabs.GetBoolean() : false,
                                IsLive = worldElement.TryGetProperty("isLive", out var isLive) ? isLive.GetBoolean() : false
                            };
                            worlds.Add(world);
                        }
                    }

                    _context.Worlds = worlds;
                    _utilities.LogDebug($"Worlds fetched: {worlds.Count} worlds");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch worlds: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching worlds: {ex.Message}");
            }
        }

        public async Task GetInstancesAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching instances...");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/instances");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var instances = new List<InstanceInfo>();

                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var instanceElement in dataArray.EnumerateArray())
                        {
                            var instance = new InstanceInfo
                            {
                                Id = instanceElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Type = instanceElement.TryGetProperty("type", out var type) ? type.GetString() : "",
                                Owner = instanceElement.TryGetProperty("owner", out var owner) ? owner.GetString() : "",
                                Capacity = instanceElement.TryGetProperty("capacity", out var capacity) ? capacity.GetInt32() : 0,
                                Occupants = instanceElement.TryGetProperty("occupants", out var occupants) ? occupants.GetInt32() : 0,
                                CanRequestInvite = instanceElement.TryGetProperty("canRequestInvite", out var canRequestInvite) ? canRequestInvite.GetBoolean() : false,
                                IsFull = instanceElement.TryGetProperty("isFull", out var isFull) ? isFull.GetBoolean() : false,
                                IsHidden = instanceElement.TryGetProperty("isHidden", out var isHidden) ? isHidden.GetBoolean() : false,
                                IsFriendsOnly = instanceElement.TryGetProperty("isFriendsOnly", out var isFriendsOnly) ? isFriendsOnly.GetBoolean() : false,
                                IsFriendsOfFriends = instanceElement.TryGetProperty("isFriendsOfFriends", out var isFriendsOfFriends) ? isFriendsOfFriends.GetBoolean() : false,
                                IsInviteOnly = instanceElement.TryGetProperty("isInviteOnly", out var isInviteOnly) ? isInviteOnly.GetBoolean() : false,
                                IsActive = instanceElement.TryGetProperty("isActive", out var isActive) ? isActive.GetBoolean() : false
                            };
                            instances.Add(instance);
                        }
                    }

                    _context.Instances = instances;
                    _utilities.LogDebug($"Instances fetched: {instances.Count} instances");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch instances: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching instances: {ex.Message}");
            }
        }

        // Wildcard-based methods for dynamic interactions
        public async Task GetUserByIdAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Fetching user by ID: {userId}");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/users/{userId}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    // Update context with user info
                    _context.UserId = json.TryGetProperty("id", out var id) ? id.GetString() : "";
                    _context.Username = json.TryGetProperty("username", out var username) ? username.GetString() : "";
                    _context.DisplayName = json.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : "";
                    _context.UserStatus = json.TryGetProperty("status", out var status) ? status.GetString() : "";
                    _context.Location = json.TryGetProperty("location", out var location) ? location.GetString() : "";

                    _utilities.LogDebug($"User fetched: {_context.DisplayName}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch user {userId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user {userId}: {ex.Message}");
            }
        }

        public async Task GetWorldByIdAsync(string worldId)
        {
            try
            {
                _utilities.LogDebug($"Fetching world by ID: {worldId}");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/worlds/{worldId}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    // Update context with world info
                    _context.WorldId = json.TryGetProperty("id", out var id) ? id.GetString() : "";
                    _context.WorldName = json.TryGetProperty("name", out var name) ? name.GetString() : "";
                    // Note: WorldDescription is not a property of VRChatRequestContext, it's part of WorldInfo
                    _context.WorldCapacity = json.TryGetProperty("capacity", out var capacity) ? capacity.GetInt32() : 0;
                    _context.WorldOccupants = json.TryGetProperty("occupants", out var occupants) ? occupants.GetInt32() : 0;

                    _utilities.LogDebug($"World fetched: {_context.WorldName}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch world {worldId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching world {worldId}: {ex.Message}");
            }
        }

        public async Task SendFriendRequestAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Sending friend request to user: {userId}");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/user/{userId}/friendRequest");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _utilities.LogDebug($"Friend request sent to user: {userId}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to send friend request to {userId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error sending friend request to {userId}: {ex.Message}");
            }
        }

        public async Task BlockUserAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Blocking user: {userId}");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/user/{userId}/block");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _utilities.LogDebug($"User blocked: {userId}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to block user {userId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error blocking user {userId}: {ex.Message}");
            }
        }

        public async Task MuteUserAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Muting user: {userId}");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/user/{userId}/mute");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _utilities.LogDebug($"User muted: {userId}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to mute user {userId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error muting user {userId}: {ex.Message}");
            }
        }

        public async Task SetUserStatusAsync(int status)
        {
            try
            {
                _utilities.LogDebug($"Setting user status to: {status}");

                var request = new HttpRequestMessage(HttpMethod.Put, $"{VRChatApiBaseUrl}/user");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var statusText = status switch
                {
                    0 => "offline",
                    1 => "online",
                    2 => "busy",
                    3 => "away",
                    _ => "online"
                };

                var json = JsonSerializer.Serialize(new { status = statusText });
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _utilities.LogDebug($"User status set to: {statusText}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to set user status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error setting user status: {ex.Message}");
            }
        }

        // Avatar Management
        public async Task SelectAvatarAsync(string avatarId)
        {
            try
            {
                _utilities.LogDebug($"Selecting avatar: {avatarId}");

                var request = new HttpRequestMessage(HttpMethod.Put, $"{VRChatApiBaseUrl}/avatars/{avatarId}/select");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _utilities.LogDebug($"Avatar selected: {avatarId}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to select avatar {avatarId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error selecting avatar {avatarId}: {ex.Message}");
            }
        }

        public async Task GetCurrentAvatarAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching current avatar...");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/user/avatar");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    // Update context with current avatar info
                    _context.CurrentAvatarId = json.TryGetProperty("id", out var id) ? id.GetString() : "";
                    _context.CurrentAvatarName = json.TryGetProperty("name", out var name) ? name.GetString() : "";

                    _utilities.LogDebug($"Current avatar: {_context.CurrentAvatarName}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch current avatar: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching current avatar: {ex.Message}");
            }
        }

        public async Task FavoriteAvatarAsync(string avatarId)
        {
            try
            {
                _utilities.LogDebug($"Adding avatar to favorites: {avatarId}");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/favorites/avatar/{avatarId}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _utilities.LogDebug($"Avatar added to favorites: {avatarId}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to favorite avatar {avatarId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error favoriting avatar {avatarId}: {ex.Message}");
            }
        }

        public async Task UnfavoriteAvatarAsync(string avatarId)
        {
            try
            {
                _utilities.LogDebug($"Removing avatar from favorites: {avatarId}");

                var request = new HttpRequestMessage(HttpMethod.Delete, $"{VRChatApiBaseUrl}/favorites/avatar/{avatarId}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    _utilities.LogDebug($"Avatar removed from favorites: {avatarId}");
                }
                else
                {
                    _utilities.LogDebug($"Failed to unfavorite avatar {avatarId}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error unfavoriting avatar {avatarId}: {ex.Message}");
            }
        }

        public async Task GetAvatarFavoritesAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching avatar favorites...");

                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/favorites/avatar");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");

                var response = await _context.HttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var json = JsonDocument.Parse(content).RootElement;

                    var avatarFavorites = new List<AvatarInfo>();
                    
                    if (json.TryGetProperty("data", out var dataArray))
                    {
                        foreach (var avatarElement in dataArray.EnumerateArray())
                        {
                            var avatar = new AvatarInfo
                            {
                                Id = avatarElement.TryGetProperty("id", out var id) ? id.GetString() : "",
                                Name = avatarElement.TryGetProperty("name", out var name) ? name.GetString() : "",
                                Description = avatarElement.TryGetProperty("description", out var desc) ? desc.GetString() : "",
                                AuthorName = avatarElement.TryGetProperty("authorName", out var authorName) ? authorName.GetString() : "",
                                AuthorId = avatarElement.TryGetProperty("authorId", out var authorId) ? authorId.GetString() : "",
                                ImageUrl = avatarElement.TryGetProperty("imageUrl", out var imageUrl) ? imageUrl.GetString() : "",
                                IsPublic = avatarElement.TryGetProperty("isPublic", out var isPublic) ? isPublic.GetBoolean() : false,
                                IsFeatured = avatarElement.TryGetProperty("isFeatured", out var isFeatured) ? isFeatured.GetBoolean() : false
                            };
                            avatarFavorites.Add(avatar);
                        }
                    }

                    _context.AvatarFavorites = avatarFavorites;
                    _utilities.LogDebug($"Avatar favorites fetched: {avatarFavorites.Count} avatars");
                }
                else
                {
                    _utilities.LogDebug($"Failed to fetch avatar favorites: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching avatar favorites: {ex.Message}");
            }
        }

        // World Management
        public async Task FavoriteWorldAsync(string worldId)
        {
            try
            {
                _utilities.LogDebug($"Adding world to favorites: {worldId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/favorites/world/{worldId}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"World added to favorites: {worldId}" : $"Failed to favorite world {worldId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error favoriting world {worldId}: {ex.Message}");
            }
        }

        public async Task UnfavoriteWorldAsync(string worldId)
        {
            try
            {
                _utilities.LogDebug($"Removing world from favorites: {worldId}");
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{VRChatApiBaseUrl}/favorites/world/{worldId}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"World removed from favorites: {worldId}" : $"Failed to unfavorite world {worldId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error unfavoriting world {worldId}: {ex.Message}");
            }
        }

        public async Task GetWorldFavoritesAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching world favorites...");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/favorites/world");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? "World favorites fetched" : $"Failed to fetch world favorites: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching world favorites: {ex.Message}");
            }
        }

        public async Task GetWorldInstancesAsync(string worldId)
        {
            try
            {
                _utilities.LogDebug($"Fetching instances for world: {worldId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/worlds/{worldId}/instances");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"World instances fetched for {worldId}" : $"Failed to fetch world instances for {worldId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching world instances for {worldId}: {ex.Message}");
            }
        }

        // Notification Management
        public async Task MarkNotificationAsReadAsync(string notificationId)
        {
            try
            {
                _utilities.LogDebug($"Marking notification as read: {notificationId}");
                var request = new HttpRequestMessage(HttpMethod.Put, $"{VRChatApiBaseUrl}/notifications/{notificationId}/see");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Notification marked as read: {notificationId}" : $"Failed to mark notification as read {notificationId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error marking notification as read {notificationId}: {ex.Message}");
            }
        }

        public async Task DeleteNotificationAsync(string notificationId)
        {
            try
            {
                _utilities.LogDebug($"Deleting notification: {notificationId}");
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{VRChatApiBaseUrl}/notifications/{notificationId}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Notification deleted: {notificationId}" : $"Failed to delete notification {notificationId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error deleting notification {notificationId}: {ex.Message}");
            }
        }

        public async Task ClearAllNotificationsAsync()
        {
            try
            {
                _utilities.LogDebug("Clearing all notifications...");
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{VRChatApiBaseUrl}/notifications/all");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? "All notifications cleared" : $"Failed to clear all notifications: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error clearing all notifications: {ex.Message}");
            }
        }

        // Group Management
        public async Task JoinGroupAsync(string groupId)
        {
            try
            {
                _utilities.LogDebug($"Joining group: {groupId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/groups/{groupId}/join");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Joined group: {groupId}" : $"Failed to join group {groupId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error joining group {groupId}: {ex.Message}");
            }
        }

        public async Task LeaveGroupAsync(string groupId)
        {
            try
            {
                _utilities.LogDebug($"Leaving group: {groupId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/groups/{groupId}/leave");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Left group: {groupId}" : $"Failed to leave group {groupId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error leaving group {groupId}: {ex.Message}");
            }
        }

        public async Task GetGroupMembersAsync(string groupId)
        {
            try
            {
                _utilities.LogDebug($"Fetching group members: {groupId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/groups/{groupId}/members");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Group members fetched for {groupId}" : $"Failed to fetch group members for {groupId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching group members for {groupId}: {ex.Message}");
            }
        }

        public async Task GetGroupInvitesAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching group invites...");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/groups/invites");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? "Group invites fetched" : $"Failed to fetch group invites: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching group invites: {ex.Message}");
            }
        }

        public async Task AcceptGroupInviteAsync(string groupId)
        {
            try
            {
                _utilities.LogDebug($"Accepting group invite: {groupId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/groups/{groupId}/invite/accept");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Group invite accepted: {groupId}" : $"Failed to accept group invite {groupId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error accepting group invite {groupId}: {ex.Message}");
            }
        }

        public async Task DeclineGroupInviteAsync(string groupId)
        {
            try
            {
                _utilities.LogDebug($"Declining group invite: {groupId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/groups/{groupId}/invite/decline");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Group invite declined: {groupId}" : $"Failed to decline group invite {groupId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error declining group invite {groupId}: {ex.Message}");
            }
        }

        // User Management
        public async Task GetUserAvatarAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Fetching user avatar: {userId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/users/{userId}/avatar");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"User avatar fetched for {userId}" : $"Failed to fetch user avatar for {userId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user avatar for {userId}: {ex.Message}");
            }
        }

        public async Task GetUserStatusAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Fetching user status: {userId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/users/{userId}/status");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"User status fetched for {userId}" : $"Failed to fetch user status for {userId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user status for {userId}: {ex.Message}");
            }
        }

        public async Task GetUserLocationAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Fetching user location: {userId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/users/{userId}/location");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"User location fetched for {userId}" : $"Failed to fetch user location for {userId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user location for {userId}: {ex.Message}");
            }
        }

        public async Task GetUserPresenceAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Fetching user presence: {userId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/users/{userId}/presence");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"User presence fetched for {userId}" : $"Failed to fetch user presence for {userId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user presence for {userId}: {ex.Message}");
            }
        }

        public async Task GetUserPresenceByIdAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Fetching user presence by ID: {userId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/users/{userId}/presence");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"User presence by ID fetched for {userId}" : $"Failed to fetch user presence by ID for {userId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user presence by ID for {userId}: {ex.Message}");
            }
        }

        // Instance Management
        public async Task GetInstanceUsersAsync(string instanceId)
        {
            try
            {
                _utilities.LogDebug($"Fetching instance users: {instanceId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/instances/{instanceId}/users");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Instance users fetched for {instanceId}" : $"Failed to fetch instance users for {instanceId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching instance users for {instanceId}: {ex.Message}");
            }
        }

        public async Task GetInstanceInviteAsync(string instanceId)
        {
            try
            {
                _utilities.LogDebug($"Fetching instance invite: {instanceId}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/instances/{instanceId}/invite");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Instance invite fetched for {instanceId}" : $"Failed to fetch instance invite for {instanceId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching instance invite for {instanceId}: {ex.Message}");
            }
        }

        public async Task SendInstanceInviteAsync(string instanceId)
        {
            try
            {
                _utilities.LogDebug($"Sending instance invite: {instanceId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/instances/{instanceId}/invite");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Instance invite sent for {instanceId}" : $"Failed to send instance invite for {instanceId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error sending instance invite for {instanceId}: {ex.Message}");
            }
        }

        public async Task RequestInviteAsync(string instanceId)
        {
            try
            {
                _utilities.LogDebug($"Requesting invite to instance: {instanceId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/instances/{instanceId}/request");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Invite requested for {instanceId}" : $"Failed to request invite for {instanceId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error requesting invite for {instanceId}: {ex.Message}");
            }
        }

        public async Task RespondToInviteAsync(string instanceId)
        {
            try
            {
                _utilities.LogDebug($"Responding to invite: {instanceId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/instances/{instanceId}/respond");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Responded to invite for {instanceId}" : $"Failed to respond to invite for {instanceId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error responding to invite for {instanceId}: {ex.Message}");
            }
        }

        // Economy
        public async Task GetUserEconomyAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching user economy...");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/user/economy");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? "User economy fetched" : $"Failed to fetch user economy: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user economy: {ex.Message}");
            }
        }

        public async Task GetUserInventoryAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching user inventory...");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/user/inventory");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? "User inventory fetched" : $"Failed to fetch user inventory: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user inventory: {ex.Message}");
            }
        }

        public async Task GetUserPurchasesAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching user purchases...");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/user/purchases");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? "User purchases fetched" : $"Failed to fetch user purchases: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching user purchases: {ex.Message}");
            }
        }

        // Moderation
        public async Task GetModerationFlagsAsync()
        {
            try
            {
                _utilities.LogDebug("Fetching moderation flags...");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/moderation");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? "Moderation flags fetched" : $"Failed to fetch moderation flags: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error fetching moderation flags: {ex.Message}");
            }
        }

        public async Task ReportUserAsync(string userId)
        {
            try
            {
                _utilities.LogDebug($"Reporting user: {userId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/users/{userId}/report");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"User reported: {userId}" : $"Failed to report user {userId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error reporting user {userId}: {ex.Message}");
            }
        }

        public async Task ReportWorldAsync(string worldId)
        {
            try
            {
                _utilities.LogDebug($"Reporting world: {worldId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/worlds/{worldId}/report");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"World reported: {worldId}" : $"Failed to report world {worldId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error reporting world {worldId}: {ex.Message}");
            }
        }

        public async Task ReportAvatarAsync(string avatarId)
        {
            try
            {
                _utilities.LogDebug($"Reporting avatar: {avatarId}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{VRChatApiBaseUrl}/avatars/{avatarId}/report");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Avatar reported: {avatarId}" : $"Failed to report avatar {avatarId}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error reporting avatar {avatarId}: {ex.Message}");
            }
        }

        // Search
        public async Task SearchUsersAsync(string query)
        {
            try
            {
                _utilities.LogDebug($"Searching users: {query}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/users/search?search={Uri.EscapeDataString(query)}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Users searched for: {query}" : $"Failed to search users for {query}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error searching users for {query}: {ex.Message}");
            }
        }

        public async Task SearchWorldsAsync(string query)
        {
            try
            {
                _utilities.LogDebug($"Searching worlds: {query}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/worlds/search?search={Uri.EscapeDataString(query)}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Worlds searched for: {query}" : $"Failed to search worlds for {query}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error searching worlds for {query}: {ex.Message}");
            }
        }

        public async Task SearchAvatarsAsync(string query)
        {
            try
            {
                _utilities.LogDebug($"Searching avatars: {query}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/avatars/search?search={Uri.EscapeDataString(query)}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Avatars searched for: {query}" : $"Failed to search avatars for {query}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error searching avatars for {query}: {ex.Message}");
            }
        }

        public async Task SearchGroupsAsync(string query)
        {
            try
            {
                _utilities.LogDebug($"Searching groups: {query}");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{VRChatApiBaseUrl}/groups/search?search={Uri.EscapeDataString(query)}");
                request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                request.Headers.Add("Cookie", $"auth={_context.AuthToken}");
                var response = await _context.HttpClient.SendAsync(request);
                _utilities.LogDebug(response.IsSuccessStatusCode ? $"Groups searched for: {query}" : $"Failed to search groups for {query}: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                _utilities.LogDebug($"Error searching groups for {query}: {ex.Message}");
            }
        }
    }
}
