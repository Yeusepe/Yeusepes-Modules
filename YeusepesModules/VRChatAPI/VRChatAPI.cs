using System.Security;
using System.Net;
using VRCOSC.App.SDK.Modules;
using YeusepesModules.VRChatAPI.Credentials;
using System.Net.Http;
using VRCOSC.App.SDK.Parameters;
using YeusepesModules.VRChatAPI.Utils;
using YeusepesModules.VRChatAPI.UI;
using YeusepesModules.VRChatAPI.Utils.Requests;
using YeusepesModules.Common;
using System.Text.Json;
using VRCOSC.App.Settings;

namespace YeusepesModules.VRChatAPI
{
    [ModuleTitle("VRChat API Interactor")]
    [ModuleDescription("A module to interact with the VRChat API through OSC.")]
    [ModuleType(ModuleType.Integrations)]
    [ModuleInfo("https://github.com/Yeusepe/Yeusepes-Modules/wiki/VRChatAPI")]
    [ModuleSettingsWindow(typeof(SignInWindow))]
    public class VRChatAPI : Module
    {
        private static SecureString AuthToken;
        private static HttpClient _httpClient = new HttpClient();
        public VRChatRequestContext vrchatRequestContext;
        public VRChatUtilities vrchatUtilities;

        private HashSet<Enum> _activeParameterUpdates = new HashSet<Enum>();

        public enum VRChatSettings
        {
            SignInButton
        }

        public enum VRChatParameters
        {
            // Authentication
            Enabled,
            Error,
            SignedIn,
            
            // User Info - Only boolean/numeric data
            IsFriend,
            UserTrustLevel,
            UserDeveloperType,
            UserStatus,
            UserLocation,
            
            // World Info - Only boolean/numeric data
            WorldCapacity,
            WorldOccupants,
            WorldIsPublic,
            WorldIsPrivate,
            WorldIsFeatured,
            WorldIsLabs,
            WorldIsCommunityLabs,
            WorldIsLive,
            
            // Instance Info - Only boolean/numeric data
            InstanceCapacity,
            InstanceOccupants,
            InstanceCanRequestInvite,
            InstanceIsFull,
            InstanceIsHidden,
            InstanceIsFriendsOnly,
            InstanceIsFriendsOfFriends,
            InstanceIsInviteOnly,
            InstanceIsActive,
            
            // Friends & Social
            FriendsCount,
            OnlineFriendsCount,
            BlockedUsersCount,
            MutedUsersCount,
            NotificationsCount,
            UnreadNotificationsCount,
            
            // Calendar & Events
            CalendarEventsCount,
            UpcomingEventsCount,
            FeaturedEventsCount,
            
            // Favorites & Inventory
            FavoritesCount,
            AvatarFavoritesCount,
            WorldFavoritesCount,
            InventoryItemsCount,
            
            // Groups & Communities
            GroupsCount,
            ActiveGroupsCount,
            
            // Actions - Only boolean triggers
            GetUserInfo,
            GetWorldInfo,
            GetInstanceInfo,
            GetFriends,
            GetBlockedUsers,
            GetMutedUsers,
            GetNotifications,
            GetCalendarEvents,
            GetFavorites,
            GetGroups,
            GetAvatars,
            GetWorlds,
            GetInstances,
            RefreshAllData,
            
            // Wildcard Actions
            GetUserById,
            GetWorldById,
            GetInstanceById,
            GetAvatarById,
            GetGroupById,
            JoinWorld,
            JoinInstance,
            SendFriendRequest,
            AcceptFriendRequest,
            BlockUser,
            UnblockUser,
            MuteUser,
            UnmuteUser,
            SetUserStatus,
            SetUserLocation,
            
            // Avatar Management
            SelectAvatar,
            GetCurrentAvatar,
            FavoriteAvatar,
            UnfavoriteAvatar,
            GetAvatarFavorites,
            
            // World Management
            FavoriteWorld,
            UnfavoriteWorld,
            GetWorldFavorites,
            GetWorldInstances,
            
            // Notification Management
            MarkNotificationAsRead,
            DeleteNotification,
            ClearAllNotifications,
            
            // Group Management
            JoinGroup,
            LeaveGroup,
            GetGroupMembers,
            GetGroupInvites,
            AcceptGroupInvite,
            DeclineGroupInvite,
            
            // User Management
            GetUserAvatar,
            GetUserStatus,
            GetUserLocation,
            GetUserPresence,
            GetUserPresenceById,
            
            // Instance Management
            GetInstanceUsers,
            GetInstanceInvite,
            SendInstanceInvite,
            RequestInvite,
            RespondToInvite,
            
            // Economy
            GetUserEconomy,
            GetUserInventory,
            GetUserPurchases,
            
            // Moderation
            GetModerationFlags,
            ReportUser,
            ReportWorld,
            ReportAvatar,
            
            // Search
            SearchUsers,
            SearchWorlds,
            SearchAvatars,
            SearchGroups
        }

        protected override void OnPreLoad()
        {
            vrchatUtilities = new VRChatUtilities
            {
                Log = message => Log(message),
                LogDebug = message => LogDebug(message),
                SendParameter = (param, value) => SetParameterSafe(param, value)
            };

            VRChatCredentialManager.VRChatUtils = vrchatUtilities;

            #region Parameters

            // Authentication
            RegisterParameter<bool>(VRChatParameters.Enabled, "VRChatAPI/Enabled", ParameterMode.Write, "Enabled", "Set to true if the module is enabled.");
            RegisterParameter<bool>(VRChatParameters.Error, "VRChatAPI/Error", ParameterMode.Write, "Error", "Triggered when an error occurs.");
            RegisterParameter<bool>(VRChatParameters.SignedIn, "VRChatAPI/SignedIn", ParameterMode.Write, "Signed In", "Set to true when successfully signed in.");

            // User Info - Only boolean and numeric parameters supported
            RegisterParameter<bool>(VRChatParameters.IsFriend, "VRChatAPI/IsFriend", ParameterMode.Write, "Is Friend", "Whether the current user is a friend.");
            RegisterParameter<int>(VRChatParameters.UserTrustLevel, "VRChatAPI/UserTrustLevel", ParameterMode.Write, "User Trust Level", "Current user's trust level (0-4).");
            RegisterParameter<int>(VRChatParameters.UserDeveloperType, "VRChatAPI/UserDeveloperType", ParameterMode.Write, "User Developer Type", "Current user's developer type (0-3).");
            RegisterParameter<int>(VRChatParameters.UserStatus, "VRChatAPI/UserStatus", ParameterMode.Write, "User Status", "Current user's status (0=offline, 1=online, 2=busy, 3=away).");
            RegisterParameter<int>(VRChatParameters.UserLocation, "VRChatAPI/UserLocation", ParameterMode.Write, "User Location", "Current user's location type (0=offline, 1=world, 2=traveling).");

            // World Info - Only boolean and numeric parameters supported
            RegisterParameter<int>(VRChatParameters.WorldCapacity, "VRChatAPI/WorldCapacity", ParameterMode.Write, "World Capacity", "Current world's capacity.");
            RegisterParameter<int>(VRChatParameters.WorldOccupants, "VRChatAPI/WorldOccupants", ParameterMode.Write, "World Occupants", "Number of occupants in current world.");
            RegisterParameter<bool>(VRChatParameters.WorldIsPublic, "VRChatAPI/WorldIsPublic", ParameterMode.Write, "World Is Public", "Whether the current world is public.");
            RegisterParameter<bool>(VRChatParameters.WorldIsPrivate, "VRChatAPI/WorldIsPrivate", ParameterMode.Write, "World Is Private", "Whether the current world is private.");
            RegisterParameter<bool>(VRChatParameters.WorldIsFeatured, "VRChatAPI/WorldIsFeatured", ParameterMode.Write, "World Is Featured", "Whether the current world is featured.");
            RegisterParameter<bool>(VRChatParameters.WorldIsLabs, "VRChatAPI/WorldIsLabs", ParameterMode.Write, "World Is Labs", "Whether the current world is in labs.");
            RegisterParameter<bool>(VRChatParameters.WorldIsCommunityLabs, "VRChatAPI/WorldIsCommunityLabs", ParameterMode.Write, "World Is Community Labs", "Whether the current world is in community labs.");
            RegisterParameter<bool>(VRChatParameters.WorldIsLive, "VRChatAPI/WorldIsLive", ParameterMode.Write, "World Is Live", "Whether the current world is live.");

            // Instance Info - Only boolean and numeric parameters supported
            RegisterParameter<int>(VRChatParameters.InstanceCapacity, "VRChatAPI/InstanceCapacity", ParameterMode.Write, "Instance Capacity", "Current instance's capacity.");
            RegisterParameter<int>(VRChatParameters.InstanceOccupants, "VRChatAPI/InstanceOccupants", ParameterMode.Write, "Instance Occupants", "Number of occupants in current instance.");
            RegisterParameter<bool>(VRChatParameters.InstanceCanRequestInvite, "VRChatAPI/InstanceCanRequestInvite", ParameterMode.Write, "Can Request Invite", "Whether you can request an invite to this instance.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsFull, "VRChatAPI/InstanceIsFull", ParameterMode.Write, "Instance Is Full", "Whether the instance is full.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsHidden, "VRChatAPI/InstanceIsHidden", ParameterMode.Write, "Instance Is Hidden", "Whether the instance is hidden.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsFriendsOnly, "VRChatAPI/InstanceIsFriendsOnly", ParameterMode.Write, "Instance Is Friends Only", "Whether the instance is friends only.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsFriendsOfFriends, "VRChatAPI/InstanceIsFriendsOfFriends", ParameterMode.Write, "Instance Is Friends Of Friends", "Whether the instance is friends of friends only.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsInviteOnly, "VRChatAPI/InstanceIsInviteOnly", ParameterMode.Write, "Instance Is Invite Only", "Whether the instance is invite only.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsActive, "VRChatAPI/InstanceIsActive", ParameterMode.Write, "Instance Is Active", "Whether the instance is currently active.");

            // Friends & Social
            RegisterParameter<int>(VRChatParameters.FriendsCount, "VRChatAPI/FriendsCount", ParameterMode.Write, "Friends Count", "Total number of friends.");
            RegisterParameter<int>(VRChatParameters.OnlineFriendsCount, "VRChatAPI/OnlineFriendsCount", ParameterMode.Write, "Online Friends Count", "Number of friends currently online.");
            RegisterParameter<int>(VRChatParameters.BlockedUsersCount, "VRChatAPI/BlockedUsersCount", ParameterMode.Write, "Blocked Users Count", "Number of blocked users.");
            RegisterParameter<int>(VRChatParameters.MutedUsersCount, "VRChatAPI/MutedUsersCount", ParameterMode.Write, "Muted Users Count", "Number of muted users.");
            RegisterParameter<int>(VRChatParameters.NotificationsCount, "VRChatAPI/NotificationsCount", ParameterMode.Write, "Notifications Count", "Total number of notifications.");
            RegisterParameter<int>(VRChatParameters.UnreadNotificationsCount, "VRChatAPI/UnreadNotificationsCount", ParameterMode.Write, "Unread Notifications Count", "Number of unread notifications.");

            // Calendar & Events
            RegisterParameter<int>(VRChatParameters.CalendarEventsCount, "VRChatAPI/CalendarEventsCount", ParameterMode.Write, "Calendar Events Count", "Total number of calendar events.");
            RegisterParameter<int>(VRChatParameters.UpcomingEventsCount, "VRChatAPI/UpcomingEventsCount", ParameterMode.Write, "Upcoming Events Count", "Number of upcoming events.");
            RegisterParameter<int>(VRChatParameters.FeaturedEventsCount, "VRChatAPI/FeaturedEventsCount", ParameterMode.Write, "Featured Events Count", "Number of featured events.");

            // Favorites & Inventory
            RegisterParameter<int>(VRChatParameters.FavoritesCount, "VRChatAPI/FavoritesCount", ParameterMode.Write, "Favorites Count", "Total number of favorites.");
            RegisterParameter<int>(VRChatParameters.AvatarFavoritesCount, "VRChatAPI/AvatarFavoritesCount", ParameterMode.Write, "Avatar Favorites Count", "Number of avatar favorites.");
            RegisterParameter<int>(VRChatParameters.WorldFavoritesCount, "VRChatAPI/WorldFavoritesCount", ParameterMode.Write, "World Favorites Count", "Number of world favorites.");
            RegisterParameter<int>(VRChatParameters.InventoryItemsCount, "VRChatAPI/InventoryItemsCount", ParameterMode.Write, "Inventory Items Count", "Number of inventory items.");

            // Groups & Communities
            RegisterParameter<int>(VRChatParameters.GroupsCount, "VRChatAPI/GroupsCount", ParameterMode.Write, "Groups Count", "Total number of groups.");
            RegisterParameter<int>(VRChatParameters.ActiveGroupsCount, "VRChatAPI/ActiveGroupsCount", ParameterMode.Write, "Active Groups Count", "Number of active groups.");

            // Actions - Only boolean parameters supported for triggers
            RegisterParameter<bool>(VRChatParameters.GetUserInfo, "VRChatAPI/GetUserInfo", ParameterMode.ReadWrite, "Get User Info", "Trigger to get current user information.");
            RegisterParameter<bool>(VRChatParameters.GetWorldInfo, "VRChatAPI/GetWorldInfo", ParameterMode.ReadWrite, "Get World Info", "Trigger to get current world information.");
            RegisterParameter<bool>(VRChatParameters.GetInstanceInfo, "VRChatAPI/GetInstanceInfo", ParameterMode.ReadWrite, "Get Instance Info", "Trigger to get current instance information.");
            RegisterParameter<bool>(VRChatParameters.GetFriends, "VRChatAPI/GetFriends", ParameterMode.ReadWrite, "Get Friends", "Trigger to get friends list.");
            RegisterParameter<bool>(VRChatParameters.GetBlockedUsers, "VRChatAPI/GetBlockedUsers", ParameterMode.ReadWrite, "Get Blocked Users", "Trigger to get blocked users list.");
            RegisterParameter<bool>(VRChatParameters.GetMutedUsers, "VRChatAPI/GetMutedUsers", ParameterMode.ReadWrite, "Get Muted Users", "Trigger to get muted users list.");
            RegisterParameter<bool>(VRChatParameters.GetNotifications, "VRChatAPI/GetNotifications", ParameterMode.ReadWrite, "Get Notifications", "Trigger to get notifications.");
            RegisterParameter<bool>(VRChatParameters.GetCalendarEvents, "VRChatAPI/GetCalendarEvents", ParameterMode.ReadWrite, "Get Calendar Events", "Trigger to get calendar events.");
            RegisterParameter<bool>(VRChatParameters.GetFavorites, "VRChatAPI/GetFavorites", ParameterMode.ReadWrite, "Get Favorites", "Trigger to get favorites list.");
            RegisterParameter<bool>(VRChatParameters.GetGroups, "VRChatAPI/GetGroups", ParameterMode.ReadWrite, "Get Groups", "Trigger to get groups list.");
            RegisterParameter<bool>(VRChatParameters.GetAvatars, "VRChatAPI/GetAvatars", ParameterMode.ReadWrite, "Get Avatars", "Trigger to get avatars list.");
            RegisterParameter<bool>(VRChatParameters.GetWorlds, "VRChatAPI/GetWorlds", ParameterMode.ReadWrite, "Get Worlds", "Trigger to get worlds list.");
            RegisterParameter<bool>(VRChatParameters.GetInstances, "VRChatAPI/GetInstances", ParameterMode.ReadWrite, "Get Instances", "Trigger to get instances list.");
            RegisterParameter<bool>(VRChatParameters.RefreshAllData, "VRChatAPI/RefreshAllData", ParameterMode.ReadWrite, "Refresh All Data", "Trigger to refresh all data.");

            // Wildcard Actions - Using wildcards for dynamic user interactions
            RegisterParameter<bool>(VRChatParameters.GetUserById, "VRChatAPI/GetUser/*", ParameterMode.ReadWrite, "Get User By ID", "Trigger to get user by ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.GetWorldById, "VRChatAPI/GetWorld/*", ParameterMode.ReadWrite, "Get World By ID", "Trigger to get world by ID. Use wildcard for world ID.");
            RegisterParameter<bool>(VRChatParameters.GetInstanceById, "VRChatAPI/GetInstance/*", ParameterMode.ReadWrite, "Get Instance By ID", "Trigger to get instance by ID. Use wildcard for instance ID.");
            RegisterParameter<bool>(VRChatParameters.GetAvatarById, "VRChatAPI/GetAvatar/*", ParameterMode.ReadWrite, "Get Avatar By ID", "Trigger to get avatar by ID. Use wildcard for avatar ID.");
            RegisterParameter<bool>(VRChatParameters.GetGroupById, "VRChatAPI/GetGroup/*", ParameterMode.ReadWrite, "Get Group By ID", "Trigger to get group by ID. Use wildcard for group ID.");
            RegisterParameter<bool>(VRChatParameters.JoinWorld, "VRChatAPI/JoinWorld/*", ParameterMode.ReadWrite, "Join World", "Trigger to join world by ID. Use wildcard for world ID.");
            RegisterParameter<bool>(VRChatParameters.JoinInstance, "VRChatAPI/JoinInstance/*", ParameterMode.ReadWrite, "Join Instance", "Trigger to join instance by ID. Use wildcard for instance ID.");
            RegisterParameter<bool>(VRChatParameters.SendFriendRequest, "VRChatAPI/SendFriendRequest/*", ParameterMode.ReadWrite, "Send Friend Request", "Trigger to send friend request to user ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.AcceptFriendRequest, "VRChatAPI/AcceptFriendRequest/*", ParameterMode.ReadWrite, "Accept Friend Request", "Trigger to accept friend request from user ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.BlockUser, "VRChatAPI/BlockUser/*", ParameterMode.ReadWrite, "Block User", "Trigger to block user by ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.UnblockUser, "VRChatAPI/UnblockUser/*", ParameterMode.ReadWrite, "Unblock User", "Trigger to unblock user by ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.MuteUser, "VRChatAPI/MuteUser/*", ParameterMode.ReadWrite, "Mute User", "Trigger to mute user by ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.UnmuteUser, "VRChatAPI/UnmuteUser/*", ParameterMode.ReadWrite, "Unmute User", "Trigger to unmute user by ID. Use wildcard for user ID.");
            RegisterParameter<int>(VRChatParameters.SetUserStatus, "VRChatAPI/SetUserStatus/*", ParameterMode.ReadWrite, "Set User Status", "Set user status. Use wildcard for status type (0=offline, 1=online, 2=busy, 3=away).");
            RegisterParameter<int>(VRChatParameters.SetUserLocation, "VRChatAPI/SetUserLocation/*", ParameterMode.ReadWrite, "Set User Location", "Set user location. Use wildcard for location type (0=offline, 1=world, 2=traveling).");

            // Avatar Management
            RegisterParameter<bool>(VRChatParameters.SelectAvatar, "VRChatAPI/SelectAvatar/*", ParameterMode.ReadWrite, "Select Avatar", "Select avatar by ID. Use wildcard for avatar ID.");
            RegisterParameter<bool>(VRChatParameters.GetCurrentAvatar, "VRChatAPI/GetCurrentAvatar", ParameterMode.ReadWrite, "Get Current Avatar", "Get current user's avatar information.");
            RegisterParameter<bool>(VRChatParameters.FavoriteAvatar, "VRChatAPI/FavoriteAvatar/*", ParameterMode.ReadWrite, "Favorite Avatar", "Add avatar to favorites by ID. Use wildcard for avatar ID.");
            RegisterParameter<bool>(VRChatParameters.UnfavoriteAvatar, "VRChatAPI/UnfavoriteAvatar/*", ParameterMode.ReadWrite, "Unfavorite Avatar", "Remove avatar from favorites by ID. Use wildcard for avatar ID.");
            RegisterParameter<bool>(VRChatParameters.GetAvatarFavorites, "VRChatAPI/GetAvatarFavorites", ParameterMode.ReadWrite, "Get Avatar Favorites", "Get user's avatar favorites.");

            // World Management
            RegisterParameter<bool>(VRChatParameters.FavoriteWorld, "VRChatAPI/FavoriteWorld/*", ParameterMode.ReadWrite, "Favorite World", "Add world to favorites by ID. Use wildcard for world ID.");
            RegisterParameter<bool>(VRChatParameters.UnfavoriteWorld, "VRChatAPI/UnfavoriteWorld/*", ParameterMode.ReadWrite, "Unfavorite World", "Remove world from favorites by ID. Use wildcard for world ID.");
            RegisterParameter<bool>(VRChatParameters.GetWorldFavorites, "VRChatAPI/GetWorldFavorites", ParameterMode.ReadWrite, "Get World Favorites", "Get user's world favorites.");
            RegisterParameter<bool>(VRChatParameters.GetWorldInstances, "VRChatAPI/GetWorldInstances/*", ParameterMode.ReadWrite, "Get World Instances", "Get instances for a world by ID. Use wildcard for world ID.");

            // Notification Management
            RegisterParameter<bool>(VRChatParameters.MarkNotificationAsRead, "VRChatAPI/MarkNotificationAsRead/*", ParameterMode.ReadWrite, "Mark Notification As Read", "Mark notification as read by ID. Use wildcard for notification ID.");
            RegisterParameter<bool>(VRChatParameters.DeleteNotification, "VRChatAPI/DeleteNotification/*", ParameterMode.ReadWrite, "Delete Notification", "Delete notification by ID. Use wildcard for notification ID.");
            RegisterParameter<bool>(VRChatParameters.ClearAllNotifications, "VRChatAPI/ClearAllNotifications", ParameterMode.ReadWrite, "Clear All Notifications", "Clear all notifications.");

            // Group Management
            RegisterParameter<bool>(VRChatParameters.JoinGroup, "VRChatAPI/JoinGroup/*", ParameterMode.ReadWrite, "Join Group", "Join group by ID. Use wildcard for group ID.");
            RegisterParameter<bool>(VRChatParameters.LeaveGroup, "VRChatAPI/LeaveGroup/*", ParameterMode.ReadWrite, "Leave Group", "Leave group by ID. Use wildcard for group ID.");
            RegisterParameter<bool>(VRChatParameters.GetGroupMembers, "VRChatAPI/GetGroupMembers/*", ParameterMode.ReadWrite, "Get Group Members", "Get group members by ID. Use wildcard for group ID.");
            RegisterParameter<bool>(VRChatParameters.GetGroupInvites, "VRChatAPI/GetGroupInvites", ParameterMode.ReadWrite, "Get Group Invites", "Get pending group invites.");
            RegisterParameter<bool>(VRChatParameters.AcceptGroupInvite, "VRChatAPI/AcceptGroupInvite/*", ParameterMode.ReadWrite, "Accept Group Invite", "Accept group invite by ID. Use wildcard for group ID.");
            RegisterParameter<bool>(VRChatParameters.DeclineGroupInvite, "VRChatAPI/DeclineGroupInvite/*", ParameterMode.ReadWrite, "Decline Group Invite", "Decline group invite by ID. Use wildcard for group ID.");

            // User Management
            RegisterParameter<bool>(VRChatParameters.GetUserAvatar, "VRChatAPI/GetUserAvatar/*", ParameterMode.ReadWrite, "Get User Avatar", "Get user's avatar by user ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.GetUserStatus, "VRChatAPI/GetUserStatus/*", ParameterMode.ReadWrite, "Get User Status", "Get user's status by user ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.GetUserLocation, "VRChatAPI/GetUserLocation/*", ParameterMode.ReadWrite, "Get User Location", "Get user's location by user ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.GetUserPresence, "VRChatAPI/GetUserPresence/*", ParameterMode.ReadWrite, "Get User Presence", "Get user's presence by user ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.GetUserPresenceById, "VRChatAPI/GetUserPresenceById/*", ParameterMode.ReadWrite, "Get User Presence By ID", "Get user's presence by user ID. Use wildcard for user ID.");

            // Instance Management
            RegisterParameter<bool>(VRChatParameters.GetInstanceUsers, "VRChatAPI/GetInstanceUsers/*", ParameterMode.ReadWrite, "Get Instance Users", "Get users in instance by instance ID. Use wildcard for instance ID.");
            RegisterParameter<bool>(VRChatParameters.GetInstanceInvite, "VRChatAPI/GetInstanceInvite/*", ParameterMode.ReadWrite, "Get Instance Invite", "Get instance invite by instance ID. Use wildcard for instance ID.");
            RegisterParameter<bool>(VRChatParameters.SendInstanceInvite, "VRChatAPI/SendInstanceInvite/*", ParameterMode.ReadWrite, "Send Instance Invite", "Send instance invite by instance ID. Use wildcard for instance ID.");
            RegisterParameter<bool>(VRChatParameters.RequestInvite, "VRChatAPI/RequestInvite/*", ParameterMode.ReadWrite, "Request Invite", "Request invite to instance by instance ID. Use wildcard for instance ID.");
            RegisterParameter<bool>(VRChatParameters.RespondToInvite, "VRChatAPI/RespondToInvite/*", ParameterMode.ReadWrite, "Respond To Invite", "Respond to invite by instance ID. Use wildcard for instance ID.");

            // Economy
            RegisterParameter<bool>(VRChatParameters.GetUserEconomy, "VRChatAPI/GetUserEconomy", ParameterMode.ReadWrite, "Get User Economy", "Get user's economy information.");
            RegisterParameter<bool>(VRChatParameters.GetUserInventory, "VRChatAPI/GetUserInventory", ParameterMode.ReadWrite, "Get User Inventory", "Get user's inventory.");
            RegisterParameter<bool>(VRChatParameters.GetUserPurchases, "VRChatAPI/GetUserPurchases", ParameterMode.ReadWrite, "Get User Purchases", "Get user's purchases.");

            // Moderation
            RegisterParameter<bool>(VRChatParameters.GetModerationFlags, "VRChatAPI/GetModerationFlags", ParameterMode.ReadWrite, "Get Moderation Flags", "Get moderation flags for current user.");
            RegisterParameter<bool>(VRChatParameters.ReportUser, "VRChatAPI/ReportUser/*", ParameterMode.ReadWrite, "Report User", "Report user by ID. Use wildcard for user ID.");
            RegisterParameter<bool>(VRChatParameters.ReportWorld, "VRChatAPI/ReportWorld/*", ParameterMode.ReadWrite, "Report World", "Report world by ID. Use wildcard for world ID.");
            RegisterParameter<bool>(VRChatParameters.ReportAvatar, "VRChatAPI/ReportAvatar/*", ParameterMode.ReadWrite, "Report Avatar", "Report avatar by ID. Use wildcard for avatar ID.");

            // Search
            RegisterParameter<bool>(VRChatParameters.SearchUsers, "VRChatAPI/SearchUsers/*", ParameterMode.ReadWrite, "Search Users", "Search users by query. Use wildcard for search query.");
            RegisterParameter<bool>(VRChatParameters.SearchWorlds, "VRChatAPI/SearchWorlds/*", ParameterMode.ReadWrite, "Search Worlds", "Search worlds by query. Use wildcard for search query.");
            RegisterParameter<bool>(VRChatParameters.SearchAvatars, "VRChatAPI/SearchAvatars/*", ParameterMode.ReadWrite, "Search Avatars", "Search avatars by query. Use wildcard for search query.");
            RegisterParameter<bool>(VRChatParameters.SearchGroups, "VRChatAPI/SearchGroups/*", ParameterMode.ReadWrite, "Search Groups", "Search groups by query. Use wildcard for search query.");

            #endregion

            #region Settings

            // Create a custom setting for the button
            CreateCustomSetting(
                VRChatSettings.SignInButton,
                new CustomModuleSetting(
                    String.Empty,
                    String.Empty,
                    typeof(SignIn),
                    true
                )
            );

            #endregion

            LogDebug("VRChat API Interactor module registered successfully.");
            base.OnPreLoad();
        }

        protected override void OnPostLoad()
        {
            // Variables for user information
            CreateVariable<string>("UserId", "User ID");
            CreateVariable<string>("Username", "Username");
            CreateVariable<string>("DisplayName", "Display Name");
            CreateVariable<string>("Bio", "Bio");
            CreateVariable<string>("BioLinks", "Bio Links");
            CreateVariable<string>("UserIcon", "User Icon");
            CreateVariable<string>("UserStatus", "User Status");
            CreateVariable<string>("UserStatusDescription", "User Status Description");
            CreateVariable<bool>("IsFriend", "Is Friend");
            CreateVariable<string>("Location", "Location");
            CreateVariable<string>("FriendKey", "Friend Key");

            // Variables for world information
            CreateVariable<string>("WorldId", "World ID");
            CreateVariable<string>("WorldName", "World Name");
            CreateVariable<string>("WorldDescription", "World Description");
            CreateVariable<int>("WorldCapacity", "World Capacity");
            CreateVariable<int>("WorldOccupants", "World Occupants");
            CreateVariable<string>("WorldTags", "World Tags");
            CreateVariable<string>("WorldImageUrl", "World Image URL");
            CreateVariable<string>("WorldAuthorName", "World Author Name");
            CreateVariable<string>("WorldAuthorId", "World Author ID");

            // Variables for instance information
            CreateVariable<string>("InstanceId", "Instance ID");
            CreateVariable<string>("InstanceType", "Instance Type");
            CreateVariable<string>("InstanceOwner", "Instance Owner");
            CreateVariable<int>("InstanceCapacity", "Instance Capacity");
            CreateVariable<int>("InstanceOccupants", "Instance Occupants");
            CreateVariable<bool>("InstanceCanRequestInvite", "Can Request Invite");
            CreateVariable<bool>("InstanceIsFull", "Instance Is Full");
            CreateVariable<bool>("InstanceIsHidden", "Instance Is Hidden");
            CreateVariable<bool>("InstanceIsFriendsOnly", "Instance Is Friends Only");
            CreateVariable<bool>("InstanceIsFriendsOfFriends", "Instance Is Friends Of Friends");
            CreateVariable<bool>("InstanceIsInviteOnly", "Instance Is Invite Only");

            // Events
            CreateEvent("UserInfoEvent", "User Info Event", "User information updated: {0}", new[] { CreateVariable<string>("UserInfoDisplayName", "Display Name") });
            CreateEvent("WorldInfoEvent", "World Info Event", "World information updated: {0}", new[] { CreateVariable<string>("WorldInfoName", "World Name") });
            CreateEvent("InstanceInfoEvent", "Instance Info Event", "Instance information updated: {0}", new[] { CreateVariable<string>("InstanceInfoId", "Instance ID") });
            CreateEvent("ErrorEvent", "Error Event", "An error occurred: {0}", new[] { CreateVariable<string>("ErrorMessage", "Error Message") });
        }

        protected override async Task<bool> OnModuleStart()
        {
            _httpClient = new HttpClient();

            vrchatUtilities = new VRChatUtilities
            {
                Log = message => Log(message),
                LogDebug = message => LogDebug(message),
                SendParameter = (param, value) => SetParameterSafe(param, value)
            };
            VRChatCredentialManager.VRChatUtils = vrchatUtilities;

            LogDebug("Starting VRChat API Interactor...");

            // Always start the module, authentication is handled dynamically in the UI
            SendParameter(VRChatParameters.Enabled, true);
            SendParameter(VRChatParameters.Error, false);

            // Check authentication status and update UI accordingly
            bool isAuthenticated = await ValidateAuthenticationAsync();
            SendParameter(VRChatParameters.SignedIn, isAuthenticated);

            if (isAuthenticated)
            {
                LogDebug("VRChat API Interactor initialized successfully.");
                
                // Initialize request context
                await UseTokenSecurely(async (authToken) =>
                {
                    vrchatRequestContext = new VRChatRequestContext
                    {
                        HttpClient = _httpClient,
                        AuthToken = authToken
                    };

                    return true;
                });
            }
            else
            {
                Log("Not authenticated. Please sign in through the settings window.");
            }

            return true;
        }

        protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
        {
            // Ensure we are processing only relevant parameters
            if (parameter.Lookup is not VRChatParameters param)
            {
                return;
            }

            // Prevent handling changes that originated from within the code
            if (_activeParameterUpdates.Contains(param))
            {
                _activeParameterUpdates.Remove(param);
                LogDebug($"Ignored internal update for parameter: {param}");
                return;
            }

            async void Do(Action<VRChatApiService> work)
            {
                try 
                { 
                    await Task.Yield(); 
                    var apiService = new VRChatApiService(vrchatRequestContext, vrchatUtilities);
                    work(apiService); 
                }
                catch (Exception ex) 
                { 
                    LogDebug($"VRChat API error: {ex.Message}"); 
                    SendParameter(VRChatParameters.Error, true);
                }
            }

            switch (parameter.Lookup)
            {
                case VRChatParameters.GetUserInfo when parameter.GetValue<bool>():
                    Do(svc => svc.GetUserInfoAsync());
                    break;

                case VRChatParameters.GetWorldInfo when parameter.GetValue<bool>():
                    Do(svc => svc.GetWorldInfoAsync());
                    break;

                case VRChatParameters.GetInstanceInfo when parameter.GetValue<bool>():
                    Do(svc => svc.GetInstanceInfoAsync());
                    break;

                case VRChatParameters.GetFriends when parameter.GetValue<bool>():
                    Do(svc => svc.GetFriendsAsync());
                    break;

                case VRChatParameters.GetBlockedUsers when parameter.GetValue<bool>():
                    Do(svc => svc.GetBlockedUsersAsync());
                    break;

                case VRChatParameters.GetMutedUsers when parameter.GetValue<bool>():
                    Do(svc => svc.GetMutedUsersAsync());
                    break;

                case VRChatParameters.GetNotifications when parameter.GetValue<bool>():
                    Do(svc => svc.GetNotificationsAsync());
                    break;

                case VRChatParameters.GetCalendarEvents when parameter.GetValue<bool>():
                    Do(svc => svc.GetCalendarEventsAsync());
                    break;

                case VRChatParameters.GetFavorites when parameter.GetValue<bool>():
                    Do(svc => svc.GetFavoritesAsync());
                    break;

                case VRChatParameters.GetGroups when parameter.GetValue<bool>():
                    Do(svc => svc.GetGroupsAsync());
                    break;

                case VRChatParameters.GetAvatars when parameter.GetValue<bool>():
                    Do(svc => svc.GetAvatarsAsync());
                    break;

                case VRChatParameters.GetWorlds when parameter.GetValue<bool>():
                    Do(svc => svc.GetWorldsAsync());
                    break;

                case VRChatParameters.GetInstances when parameter.GetValue<bool>():
                    Do(svc => svc.GetInstancesAsync());
                    break;

                case VRChatParameters.RefreshAllData when parameter.GetValue<bool>():
                    Do(async svc => {
                        await svc.GetUserInfoAsync();
                        await svc.GetFriendsAsync();
                        await svc.GetWorldInfoAsync();
                        await svc.GetInstanceInfoAsync();
                        await svc.GetCalendarEventsAsync();
                        await svc.GetNotificationsAsync();
                        await svc.GetFavoritesAsync();
                        await svc.GetGroupsAsync();
                        await svc.GetAvatarsAsync();
                        await svc.GetWorldsAsync();
                        await svc.GetInstancesAsync();
                    });
                    break;

                // Wildcard parameter handling
                case VRChatParameters.GetUserById when parameter.GetValue<bool>():
                    if (parameter.IsWildcardType<string>(0))
                    {
                        var userId = parameter.GetWildcard<string>(0);
                        Do(svc => svc.GetUserByIdAsync(userId));
                    }
                    break;

                case VRChatParameters.GetWorldById when parameter.GetValue<bool>():
                    if (parameter.IsWildcardType<string>(0))
                    {
                        var worldId = parameter.GetWildcard<string>(0);
                        Do(svc => svc.GetWorldByIdAsync(worldId));
                    }
                    break;

                case VRChatParameters.SendFriendRequest when parameter.GetValue<bool>():
                    if (parameter.IsWildcardType<string>(0))
                    {
                        var userId = parameter.GetWildcard<string>(0);
                        Do(svc => svc.SendFriendRequestAsync(userId));
                    }
                    break;

                case VRChatParameters.BlockUser when parameter.GetValue<bool>():
                    if (parameter.IsWildcardType<string>(0))
                    {
                        var userId = parameter.GetWildcard<string>(0);
                        Do(svc => svc.BlockUserAsync(userId));
                    }
                    break;

                case VRChatParameters.MuteUser when parameter.GetValue<bool>():
                    if (parameter.IsWildcardType<string>(0))
                    {
                        var userId = parameter.GetWildcard<string>(0);
                        Do(svc => svc.MuteUserAsync(userId));
                    }
                    break;

                case VRChatParameters.SetUserStatus when parameter.GetValue<int>() > 0:
                    var status = parameter.GetValue<int>();
                    Do(svc => svc.SetUserStatusAsync(status));
                    break;

                // Avatar Management
                case VRChatParameters.SelectAvatar when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var avatarId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.SelectAvatarAsync(avatarId));
                    break;

                case VRChatParameters.GetCurrentAvatar when parameter.GetValue<bool>():
                    Do(svc => svc.GetCurrentAvatarAsync());
                    break;

                case VRChatParameters.FavoriteAvatar when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var favAvatarId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.FavoriteAvatarAsync(favAvatarId));
                    break;

                case VRChatParameters.UnfavoriteAvatar when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var unfavAvatarId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.UnfavoriteAvatarAsync(unfavAvatarId));
                    break;

                case VRChatParameters.GetAvatarFavorites when parameter.GetValue<bool>():
                    Do(svc => svc.GetAvatarFavoritesAsync());
                    break;

                // World Management
                case VRChatParameters.FavoriteWorld when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var favWorldId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.FavoriteWorldAsync(favWorldId));
                    break;

                case VRChatParameters.UnfavoriteWorld when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var unfavWorldId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.UnfavoriteWorldAsync(unfavWorldId));
                    break;

                case VRChatParameters.GetWorldFavorites when parameter.GetValue<bool>():
                    Do(svc => svc.GetWorldFavoritesAsync());
                    break;

                case VRChatParameters.GetWorldInstances when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var worldInstanceId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetWorldInstancesAsync(worldInstanceId));
                    break;

                // Notification Management
                case VRChatParameters.MarkNotificationAsRead when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var notificationId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.MarkNotificationAsReadAsync(notificationId));
                    break;

                case VRChatParameters.DeleteNotification when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var delNotificationId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.DeleteNotificationAsync(delNotificationId));
                    break;

                case VRChatParameters.ClearAllNotifications when parameter.GetValue<bool>():
                    Do(svc => svc.ClearAllNotificationsAsync());
                    break;

                // Group Management
                case VRChatParameters.JoinGroup when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var joinGroupId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.JoinGroupAsync(joinGroupId));
                    break;

                case VRChatParameters.LeaveGroup when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var leaveGroupId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.LeaveGroupAsync(leaveGroupId));
                    break;

                case VRChatParameters.GetGroupMembers when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var groupMembersId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetGroupMembersAsync(groupMembersId));
                    break;

                case VRChatParameters.GetGroupInvites when parameter.GetValue<bool>():
                    Do(svc => svc.GetGroupInvitesAsync());
                    break;

                case VRChatParameters.AcceptGroupInvite when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var acceptGroupId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.AcceptGroupInviteAsync(acceptGroupId));
                    break;

                case VRChatParameters.DeclineGroupInvite when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var declineGroupId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.DeclineGroupInviteAsync(declineGroupId));
                    break;

                // User Management
                case VRChatParameters.GetUserAvatar when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var userAvatarId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetUserAvatarAsync(userAvatarId));
                    break;

                case VRChatParameters.GetUserStatus when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var userStatusId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetUserStatusAsync(userStatusId));
                    break;

                case VRChatParameters.GetUserLocation when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var userLocationId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetUserLocationAsync(userLocationId));
                    break;

                case VRChatParameters.GetUserPresence when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var userPresenceId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetUserPresenceAsync(userPresenceId));
                    break;

                case VRChatParameters.GetUserPresenceById when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var userPresenceById = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetUserPresenceByIdAsync(userPresenceById));
                    break;

                // Instance Management
                case VRChatParameters.GetInstanceUsers when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var instanceUsersId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetInstanceUsersAsync(instanceUsersId));
                    break;

                case VRChatParameters.GetInstanceInvite when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var instanceInviteId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.GetInstanceInviteAsync(instanceInviteId));
                    break;

                case VRChatParameters.SendInstanceInvite when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var sendInviteId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.SendInstanceInviteAsync(sendInviteId));
                    break;

                case VRChatParameters.RequestInvite when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var requestInviteId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.RequestInviteAsync(requestInviteId));
                    break;

                case VRChatParameters.RespondToInvite when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var respondInviteId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.RespondToInviteAsync(respondInviteId));
                    break;

                // Economy
                case VRChatParameters.GetUserEconomy when parameter.GetValue<bool>():
                    Do(svc => svc.GetUserEconomyAsync());
                    break;

                case VRChatParameters.GetUserInventory when parameter.GetValue<bool>():
                    Do(svc => svc.GetUserInventoryAsync());
                    break;

                case VRChatParameters.GetUserPurchases when parameter.GetValue<bool>():
                    Do(svc => svc.GetUserPurchasesAsync());
                    break;

                // Moderation
                case VRChatParameters.GetModerationFlags when parameter.GetValue<bool>():
                    Do(svc => svc.GetModerationFlagsAsync());
                    break;

                case VRChatParameters.ReportUser when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var reportUserId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.ReportUserAsync(reportUserId));
                    break;

                case VRChatParameters.ReportWorld when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var reportWorldId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.ReportWorldAsync(reportWorldId));
                    break;

                case VRChatParameters.ReportAvatar when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var reportAvatarId = parameter.GetWildcard<string>(0);
                    Do(svc => svc.ReportAvatarAsync(reportAvatarId));
                    break;

                // Search
                case VRChatParameters.SearchUsers when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var searchUsersQuery = parameter.GetWildcard<string>(0);
                    Do(svc => svc.SearchUsersAsync(searchUsersQuery));
                    break;

                case VRChatParameters.SearchWorlds when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var searchWorldsQuery = parameter.GetWildcard<string>(0);
                    Do(svc => svc.SearchWorldsAsync(searchWorldsQuery));
                    break;

                case VRChatParameters.SearchAvatars when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var searchAvatarsQuery = parameter.GetWildcard<string>(0);
                    Do(svc => svc.SearchAvatarsAsync(searchAvatarsQuery));
                    break;

                case VRChatParameters.SearchGroups when parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0):
                    var searchGroupsQuery = parameter.GetWildcard<string>(0);
                    Do(svc => svc.SearchGroupsAsync(searchGroupsQuery));
                    break;
            }
        }

        protected override async Task OnModuleStop()
        {
            LogDebug("Stopping VRChat API Interactor module...");

            try
            {
                // Clear active parameter updates
                _activeParameterUpdates.Clear();

                // Reset utilities and request context
                LogDebug("Resetting VRChat utilities and context...");
                vrchatUtilities = null;
                vrchatRequestContext = null;

                // Dispose of the HttpClient
                if (_httpClient != null)
                {
                    _httpClient.Dispose();
                    _httpClient = null;
                }

                LogDebug("VRChat API Interactor module stopped successfully.");
                SendParameter(VRChatParameters.Enabled, false);
            }
            catch (Exception ex)
            {
                LogDebug($"Error during module stop: {ex.Message}");
            }
        }

        private async Task<bool> ValidateAuthenticationAsync()
        {
            LogDebug("Validating VRChat authentication...");

            string authToken = VRChatCredentialManager.LoadAuthToken();

            if (string.IsNullOrEmpty(authToken))
            {
                LogDebug("No auth token found. User needs to sign in.");
                return false;
            }

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");
                httpClient.DefaultRequestHeaders.Add("User-Agent", "VRChatAPI-Interactor/1.0");

                var response = await httpClient.GetAsync("https://api.vrchat.cloud/api/1/auth/user");
                
                if (response.IsSuccessStatusCode)
                {
                    LogDebug("Authentication validated successfully.");
                    return true;
                }
                else
                {
                    LogDebug($"Authentication failed with status: {response.StatusCode}");
                    VRChatCredentialManager.SignOut();
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error validating authentication: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> UseTokenSecurely(Func<string, Task<bool>> operation)
        {
            string authToken = null;

            try
            {
                authToken = VRChatCredentialManager.LoadAuthToken();
                return await operation(authToken);
            }
            finally
            {
                if (authToken != null)
                {
                    // Clear sensitive data from memory
                    Array.Clear(authToken.ToCharArray(), 0, authToken.Length);
                }
            }
        }

        private void SetParameterSafe(Enum parameter, object value)
        {
            try
            {
                _activeParameterUpdates.Add(parameter);
                SendParameter(parameter, value);
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to set parameter {parameter}: {ex.Message}");
            }
        }

        /// <summary>
        /// Refreshes the authentication status and updates the UI accordingly
        /// </summary>
        public async Task RefreshAuthenticationStatusAsync()
        {
            bool isAuthenticated = await ValidateAuthenticationAsync();
            SendParameter(VRChatParameters.SignedIn, isAuthenticated);

            if (isAuthenticated && vrchatRequestContext == null)
            {
                // Initialize request context if we're authenticated but don't have one
                await UseTokenSecurely(async (authToken) =>
                {
                    vrchatRequestContext = new VRChatRequestContext
                    {
                        HttpClient = _httpClient,
                        AuthToken = authToken
                    };

                    return true;
                });
            }
            else if (!isAuthenticated)
            {
                // Clear request context if we're not authenticated
                vrchatRequestContext = null;
            }
        }
    }
}
