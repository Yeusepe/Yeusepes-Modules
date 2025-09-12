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
            
            // World Info - Only boolean/numeric data
            WorldCapacity,
            WorldOccupants,
            
            // Instance Info - Only boolean/numeric data
            InstanceCapacity,
            InstanceOccupants,
            InstanceCanRequestInvite,
            InstanceIsFull,
            InstanceIsHidden,
            InstanceIsFriendsOnly,
            InstanceIsFriendsOfFriends,
            InstanceIsInviteOnly,
            
            // Actions - Only boolean triggers
            GetUserInfo,
            GetWorldInfo,
            GetInstanceInfo,
            GetFriends,
            GetBlockedUsers,
            GetMutedUsers
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

            // World Info - Only boolean and numeric parameters supported
            RegisterParameter<int>(VRChatParameters.WorldCapacity, "VRChatAPI/WorldCapacity", ParameterMode.Write, "World Capacity", "Current world's capacity.");
            RegisterParameter<int>(VRChatParameters.WorldOccupants, "VRChatAPI/WorldOccupants", ParameterMode.Write, "World Occupants", "Number of occupants in current world.");

            // Instance Info - Only boolean and numeric parameters supported
            RegisterParameter<int>(VRChatParameters.InstanceCapacity, "VRChatAPI/InstanceCapacity", ParameterMode.Write, "Instance Capacity", "Current instance's capacity.");
            RegisterParameter<int>(VRChatParameters.InstanceOccupants, "VRChatAPI/InstanceOccupants", ParameterMode.Write, "Instance Occupants", "Number of occupants in current instance.");
            RegisterParameter<bool>(VRChatParameters.InstanceCanRequestInvite, "VRChatAPI/InstanceCanRequestInvite", ParameterMode.Write, "Can Request Invite", "Whether you can request an invite to this instance.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsFull, "VRChatAPI/InstanceIsFull", ParameterMode.Write, "Instance Is Full", "Whether the instance is full.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsHidden, "VRChatAPI/InstanceIsHidden", ParameterMode.Write, "Instance Is Hidden", "Whether the instance is hidden.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsFriendsOnly, "VRChatAPI/InstanceIsFriendsOnly", ParameterMode.Write, "Instance Is Friends Only", "Whether the instance is friends only.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsFriendsOfFriends, "VRChatAPI/InstanceIsFriendsOfFriends", ParameterMode.Write, "Instance Is Friends Of Friends", "Whether the instance is friends of friends only.");
            RegisterParameter<bool>(VRChatParameters.InstanceIsInviteOnly, "VRChatAPI/InstanceIsInviteOnly", ParameterMode.Write, "Instance Is Invite Only", "Whether the instance is invite only.");

            // Actions - Only boolean parameters supported for triggers
            RegisterParameter<bool>(VRChatParameters.GetUserInfo, "VRChatAPI/GetUserInfo", ParameterMode.ReadWrite, "Get User Info", "Trigger to get current user information.");
            RegisterParameter<bool>(VRChatParameters.GetWorldInfo, "VRChatAPI/GetWorldInfo", ParameterMode.ReadWrite, "Get World Info", "Trigger to get current world information.");
            RegisterParameter<bool>(VRChatParameters.GetInstanceInfo, "VRChatAPI/GetInstanceInfo", ParameterMode.ReadWrite, "Get Instance Info", "Trigger to get current instance information.");
            RegisterParameter<bool>(VRChatParameters.GetFriends, "VRChatAPI/GetFriends", ParameterMode.ReadWrite, "Get Friends", "Trigger to get friends list.");
            RegisterParameter<bool>(VRChatParameters.GetBlockedUsers, "VRChatAPI/GetBlockedUsers", ParameterMode.ReadWrite, "Get Blocked Users", "Trigger to get blocked users list.");
            RegisterParameter<bool>(VRChatParameters.GetMutedUsers, "VRChatAPI/GetMutedUsers", ParameterMode.ReadWrite, "Get Muted Users", "Trigger to get muted users list.");

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
                    var apiService = new VRChatApiService(vrchatRequestContext, vrchatUtilities);
                    Do(svc => svc.GetUserInfoAsync());
                    break;

                case VRChatParameters.GetWorldInfo when parameter.GetValue<bool>():
                    var apiService2 = new VRChatApiService(vrchatRequestContext, vrchatUtilities);
                    Do(svc => svc.GetWorldInfoAsync());
                    break;

                case VRChatParameters.GetInstanceInfo when parameter.GetValue<bool>():
                    var apiService3 = new VRChatApiService(vrchatRequestContext, vrchatUtilities);
                    Do(svc => svc.GetInstanceInfoAsync());
                    break;

                case VRChatParameters.GetFriends when parameter.GetValue<bool>():
                    var apiService4 = new VRChatApiService(vrchatRequestContext, vrchatUtilities);
                    Do(svc => svc.GetFriendsAsync());
                    break;

                case VRChatParameters.GetBlockedUsers when parameter.GetValue<bool>():
                    var apiService5 = new VRChatApiService(vrchatRequestContext, vrchatUtilities);
                    Do(svc => svc.GetBlockedUsersAsync());
                    break;

                case VRChatParameters.GetMutedUsers when parameter.GetValue<bool>():
                    var apiService6 = new VRChatApiService(vrchatRequestContext, vrchatUtilities);
                    Do(svc => svc.GetMutedUsersAsync());
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
