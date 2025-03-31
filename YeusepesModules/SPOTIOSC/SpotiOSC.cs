using System.Security;
using System.Net;
using VRCOSC.App.SDK.Modules;
using YeusepesModules.SPOTIOSC.Credentials;
using System.Net.Http;
using VRCOSC.App.SDK.Parameters;
using YeusepesModules.SPOTIOSC.Utils;
using YeusepesModules.IDC.Encoder;
using YeusepesModules.SPOTIOSC.UI;
using YeusepesModules.SPOTIOSC.Utils.Requests.Profiles;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using YeusepesModules.Common;
using YeusepesModules.SPOTIOSC.Utils.Events;
using System.Text.Json;
using YeusepesModules.Common.ScreenUtilities;
using VRCOSC.App.Settings;


namespace YeusepesModules.SPOTIOSC
{
    [ModuleTitle("SpotiOSC")]
    [ModuleDescription("A module to control your Spotify Through OSC.")]
    [ModuleType(ModuleType.Integrations)]
    [ModuleInfo("https://github.com/Yeusepe/Yeusepes-Modules/wiki/SPOTIOSC")]
    [ModuleSettingsWindow(typeof(SignInWindow))]
    public class SpotiOSC : Module
    {
        private static SecureString AccessToken;
        private static SecureString ClientToken;
        private static HttpClient _httpClient = new HttpClient();
        public SpotifyRequestContext spotifyRequestContext;
        public SpotifyUtilities spotifyUtilities;
        public EncodingUtilities encodingUtilities;
        private PlayerEventSubscriber _playerEventSubscriber;


        private StringEncoder encoder;
        private StringDecoder decoder;
        private bool isTouching = false;

        private HashSet<Enum> _activeParameterUpdates = new HashSet<Enum>();

        private readonly HashSet<string> _processedEventKeys = new();
        private readonly object _deduplicationLock = new();

        public ScreenUtilities screenUtilities;
        
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public enum SpotiSettings
        {
            SignInButton,
            PopUpJam
        }
        public enum SpotiParameters
        {
            Enabled,
            WantJam,
            InAJam,
            IsJamOwner,
            Error,
            Touching,
            Play,
            Pause,
            NextTrack,
            PreviousTrack,
            ShuffleEnabled,
            RepeatMode,
            Volume,
            PlaybackPosition,
            CurrentTrackName,
            CurrentTrackArtist,
            AvailableDevices
        }


        protected override void OnPreLoad()
        {            
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("libusb-1.0.dll", message => Log(message));                               
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("cvextern.dll", message => Log(message));
            screenUtilities = ScreenUtilities.EnsureInitialized(
                LogDebug,         // Logging delegate
                GetSettingValue<String>,  // Function to retrieve settings
                SetSettingValue,  // Function to save settings
                CreateTextBox,    // Function to create a text box
                (parameter, name, mode, title, description) =>
                {                    
                    RegisterParameter<bool>(parameter, name, mode, title, description);
                }
            );

            /// ThreadPool.SetMinThreads(100, 100); // Set a higher minimum thread pool size
            encodingUtilities = new EncodingUtilities
            {
                Log = message => Log(message),
                LogDebug = message => LogDebug(message),
                FindParameter = async (parameterEnum) => await FindParameter(parameterEnum),
                FindParameterByString = (parameter) => FindParameter(parameter),
                ScreenUtilities = screenUtilities
            };


            encoder = new StringEncoder(
                    encodingUtilities,
                    // Delegate signature: (Enum lookup, string name, string description, int defaultValue, int min, int max)
                    (lookup, name, description, defaultValue) =>
                    {
                        // In this case, we already created the setting.
                        // Optionally, if you need to update or recreate it, call:
                        CreateTextBox(lookup, name, description, defaultValue);
                    },
                    SetSettingValue,
                    GetSettingValue<String>
                );

            decoder = new StringDecoder(
                encodingUtilities
            );

            spotifyUtilities = new SpotifyUtilities
            {
                Log = message => Log(message),
                LogDebug = message => LogDebug(message),
                SendParameter = (param, value) => SetParameterSafe(param, value),
                Encoder = encoder
            };

            CredentialManager.SpotifyUtils = spotifyUtilities;

            #region Parameters


            RegisterParameter<bool>(SpotiParameters.Enabled, "SpotiOSC/Enabled", ParameterMode.ReadWrite, "Enabled", "Set to true to enable the module.");
            RegisterParameter<bool>(SpotiParameters.WantJam, "SpotiOSC/WantJam", ParameterMode.ReadWrite, "Want Jam", "Set to true if you want to join a jam.");
            RegisterParameter<bool>(SpotiParameters.InAJam, "SpotiOSC/InAJam", ParameterMode.Write, "In A Jam", "Set to true if you are in a jam.");
            RegisterParameter<bool>(SpotiParameters.IsJamOwner, "SpotiOSC/IsJamOwner", ParameterMode.Write, "Is Jam Owner", "Set to true if you are the owner of the jam.");
            RegisterParameter<bool>(SpotiParameters.Error, "SpotiOSC/Error", ParameterMode.Write, "Error", "Triggered when an error occurs.");
            RegisterParameter<bool>(SpotiParameters.Touching, "SpotiOSC/Touching", ParameterMode.ReadWrite, "Touching", "Set to true when two compatible devices tap eachother.");
            RegisterParameter<bool>(SpotiParameters.Play, "SpotiOSC/Play", ParameterMode.ReadWrite, "Play", "Triggers playback.");
            RegisterParameter<bool>(SpotiParameters.Pause, "SpotiOSC/Pause", ParameterMode.ReadWrite, "Pause", "Pauses playback.");
            RegisterParameter<bool>(SpotiParameters.NextTrack, "SpotiOSC/NextTrack", ParameterMode.Read, "Next Track", "Skips to the next track.");
            RegisterParameter<bool>(SpotiParameters.PreviousTrack, "SpotiOSC/PreviousTrack", ParameterMode.Read, "Previous Track", "Skips to the previous track.");
            RegisterParameter<bool>(SpotiParameters.ShuffleEnabled, "SpotiOSC/ShuffleEnabled", ParameterMode.ReadWrite, "Shuffle", "Enables or disables shuffle.");
            RegisterParameter<int>(SpotiParameters.Volume, "SpotiOSC/Volume", ParameterMode.ReadWrite, "Volume", "Sets the playback volume (0-100).");
            RegisterParameter<int>(SpotiParameters.PlaybackPosition, "SpotiOSC/PlaybackPosition", ParameterMode.ReadWrite, "Playback Position", "Sets the playback position in milliseconds.");


            #endregion

            #region Settings

            // Create a custom setting for the button
            CreateCustomSetting(
                SpotiSettings.SignInButton,
                new CustomModuleSetting(
                    String.Empty,
                    String.Empty,
                    typeof(SignIn),
                    true
                )
            );            
            #endregion


            Log("Registering parameters...");
            encoder.RegisterParameters((parameter, name, mode, title, description) =>
            {
                RegisterParameter<int>(parameter, name, mode, title, description);
            }, (parameter, name, mode, title, description) =>
            {
                RegisterParameter<bool>(parameter, name, mode, title, description);
            });

            Log("Initializing StringDecoder...");

            SetRuntimeView(typeof(NowPlayingRuntimeView));
            base.OnPreLoad();
        }

        protected override void OnPostLoad()
        {            
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames();
            foreach (var name in names)
            {
                Log(name);
            }

            // Define variables
            var playVariable = CreateVariable<bool>(SpotiParameters.Play, "Play");
            var pauseVariable = CreateVariable<bool>(SpotiParameters.Pause, "Pause");
            var nextTrackVariable = CreateVariable<bool>(SpotiParameters.NextTrack, "Next Track");
            var previousTrackVariable = CreateVariable<bool>(SpotiParameters.PreviousTrack, "Previous Track");
            var shuffleVariable = CreateVariable<bool>(SpotiParameters.ShuffleEnabled, "Shuffle Enabled");
            var repeatVariable = CreateVariable<string>(SpotiParameters.RepeatMode, "Repeat Mode");
            var volumeVariable = CreateVariable<int>(SpotiParameters.Volume, "Volume");
            var playbackPositionVariable = CreateVariable<int>(SpotiParameters.PlaybackPosition, "Playback Position");
            var currentTrackNameVariable = CreateVariable<string>(SpotiParameters.CurrentTrackName, "Current Track Name");
            var currentTrackArtistVariable = CreateVariable<string>(SpotiParameters.CurrentTrackArtist, "Current Track Artist");
            var availableDevicesVariable = CreateVariable<string>(SpotiParameters.AvailableDevices, "Available Devices");

            // Define states
            CreateState(SpotiParameters.Play, "Play State", "Now playing a track.");
            CreateState(SpotiParameters.Pause, "Pause State", "Playback paused.");
            CreateState(SpotiParameters.ShuffleEnabled, "Shuffle State", "Shuffle is {0}.", new[] { shuffleVariable });
            CreateState(SpotiParameters.RepeatMode, "Repeat State", "Repeat mode set to {0}.", new[] { repeatVariable });
            CreateState(SpotiParameters.Volume, "Volume State", "Volume set to {0}%.", new[] { volumeVariable });
            CreateState(SpotiParameters.PlaybackPosition, "Playback Position State", "Playback position: {0} ms.", new[] { playbackPositionVariable });

            // Define events
            CreateEvent(SpotiParameters.Play, "Play Event", "Track started playing: {0}.", new[] { currentTrackNameVariable });
            CreateEvent(SpotiParameters.Pause, "Pause Event", "Playback paused for track: {0}.", new[] { currentTrackNameVariable });
            CreateEvent(SpotiParameters.NextTrack, "Next Track Event", "Skipped to next track.");
            CreateEvent(SpotiParameters.PreviousTrack, "Previous Track Event", "Went back to the previous track.");
            CreateEvent(SpotiParameters.ShuffleEnabled, "Shuffle Event", "Shuffle {0}.", new[] { shuffleVariable });
            CreateEvent(SpotiParameters.RepeatMode, "Repeat Event", "Repeat mode set to {0}.", new[] { repeatVariable });
            CreateEvent(SpotiParameters.Volume, "Volume Event", "Volume changed to {0}%.", new[] { volumeVariable });
            CreateEvent(SpotiParameters.AvailableDevices, "Available Devices Event", "Devices available: {0}.", new[] { availableDevicesVariable });

            base.OnPostLoad();
        }


        protected override async Task<bool> OnModuleStart()
        {
            _cts = new CancellationTokenSource();

            // Reinitialize the HttpClient if it was disposed in a previous stop.
            if (_httpClient == null)
            {
                _httpClient = new HttpClient();
            }

            encodingUtilities.IsDebug = SettingsManager.GetInstance().GetValue<bool>(VRCOSCSetting.EnableAppDebug);
            
            Log("Starting Spotify Cookie Manager...");

            // Validate tokens and fetch profile
            bool isProfileFetched = await ValidateAndFetchProfileAsync();
            if (!isProfileFetched)
            {
                Log("Failed to validate tokens or fetch profile. Exiting.");
                return false;
            }

            Log("Spotify Cookie Manager initialized successfully.");

            AccessToken = CredentialManager.AccessToken;
            ClientToken = CredentialManager.ClientToken;

            // Initialize SpotifyRequestContext
            await UseTokensSecurely(async (accessToken, clientToken) =>
            {
                spotifyRequestContext = new SpotifyRequestContext
                {
                    HttpClient = _httpClient,
                    AccessToken = accessToken,
                    ClientToken = clientToken
                };
                
                return true;
            });            

            _playerEventSubscriber = new PlayerEventSubscriber(spotifyUtilities, spotifyRequestContext);
            _playerEventSubscriber.OnPlayerEventReceived += HandlePlayerEvent;

            LogDebug("Starting player event subscription...");
            await _playerEventSubscriber.StartAsync();
            SendParameter(SpotiParameters.Enabled, true);
            
            SendParameter(SpotiParameters.InAJam, false);
            SendParameter(SpotiParameters.IsJamOwner, false);
            SendParameter(SpotiParameters.Error, false);

            //
            SendParameter(SpotiParameters.InAJam, true);


            SendParameter(SpotiParameters.InAJam, false);
            SendParameter(SpotiParameters.IsJamOwner, false);
            SendParameter(SpotiParameters.Error, false);     
                       


            await decoder.OnModuleStart();                        
            return true;
        }

        protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
        {

            try
            {
                if (parameter.Lookup is EncodingParameter encodingParameter)
                {
                    if (parameter.GetValue<bool>())
                    {
                        switch (encodingParameter)
                        {
                            case EncodingParameter.CharIn:
                                // HandleCharIn(parameter.GetValue<int>());
                                break;
                            case EncodingParameter.Touching:
                                if(parameter.GetValue<bool>())
                                {                                    
                                    if (SpotifyJamRequests._isInJam && !string.IsNullOrEmpty(SpotifyJamRequests._joinSessionToken))
                                    {
                                        EncodeAndSendSessionId(SpotifyJamRequests._shareableUrl);
                                    }
                                }
                                else
                                {
                                    Log("Devices separated. Stopping decoding process.");
                                    StopDecodingProcess();
                                }
                                break;
                            case EncodingParameter.Ready:
                                Log("Decoder is ready to receive data.");
                                SpotifyJamRequests._shareableUrl = decoder.StartDecode();
                                Log($"Decoding process started. Session ID: {SpotifyJamRequests._shareableUrl}");
                                // Start the asynchronous join process.
                                HandleJoinJam();
                                break;
                        }

                    }
                }
            }

            catch (System.Exception ex)
            {
            }            
            // Ensure we are processing only relevant parameters
            if (parameter.Lookup is not SpotiParameters param)
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

            switch (parameter.Lookup)
            {
                case SpotiParameters.WantJam:
                    LogDebug($"WantJam parameter received: {parameter.GetValue<bool>()}");
                    HandleWantJam(parameter.GetValue<bool>());
                    break;

                case SpotiParameters.Touching:
                    LogDebug($"Touching parameter received: {parameter.GetValue<bool>()}");
                    HandleTouching(parameter.GetValue<bool>());
                    break;

            }
        }

        private void StopDecodingProcess()
        {
            Log("Decoding process halted.");
        }

        private async void EncodeAndSendSessionId(string sessionId)
        {
            // Use the encoder to send the session ID
            encoder.SendString(sessionId, true, (parameter, value) =>
            {
                SendParameter(parameter, value);
            });
        }

        protected override async Task OnModuleStop()
        {
            Log("Stopping SpotiOSC module...");
            _cts.Cancel(); // Cancel all ongoing operations

            try
            {
                // Stop player event subscription if active
                if (_playerEventSubscriber != null)
                {
                    LogDebug("Unsubscribing from player event subscription...");
                    await _playerEventSubscriber.StopAsync();
                    _playerEventSubscriber.OnPlayerEventReceived -= HandlePlayerEvent;
                    _playerEventSubscriber = null;
                }

                // Stop any active encoding/decoding processes
                LogDebug("Stopping decoding process...");
                StopDecodingProcess();

                // Clear active parameter updates
                _activeParameterUpdates.Clear();

                // Clean up processed event keys
                lock (_deduplicationLock)
                {
                    _processedEventKeys.Clear();
                }

                // Reset Spotify utilities and request context
                LogDebug("Resetting Spotify utilities and context...");
                spotifyUtilities = null;
                spotifyRequestContext = null;

                // Dispose of the HttpClient
                if (_httpClient != null)
                {
                    _httpClient.Dispose();
                    _httpClient = null;
                }

                Log("SpotiOSC module stopped successfully.");
                SendParameter(SpotiParameters.Enabled, false);
            }
            catch (Exception ex)
            {
                Log($"Error during module stop: {ex.Message}");
            }
            finally
            {
                _cts.Dispose();
            }
        }


        private async Task<bool> ValidateAndFetchProfileAsync()
        {
            Log("Validating tokens and fetching profile data...");

            string accessToken = CredentialManager.LoadAccessToken();
            string clientToken = CredentialManager.LoadClientToken();

            if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientToken))
            {
                Log("Tokens are missing. Attempting to fetch new tokens...");
                if (!await RefreshTokensAsync())
                {
                    Log("Failed to fetch new tokens. Exiting.");
                    return false;
                }

                accessToken = CredentialManager.LoadAccessToken();
                clientToken = CredentialManager.LoadClientToken();
            }

            try
            {
                bool isAuthorized = await ProfileAttributesRequest.FetchProfileAttributesAsync(
                    _httpClient, accessToken, clientToken, Log, LogDebug);

                if (!isAuthorized)
                {
                    Log("Unauthorized response received. Deleting invalid tokens...");
                    CredentialManager.DeleteTokens(); // Remove invalid tokens

                    Log("Attempting to refresh tokens...");
                    if (!await RefreshTokensAsync())
                    {
                        Log("Failed to refresh tokens after unauthorized response. Exiting.");
                        return false;
                    }

                    accessToken = CredentialManager.LoadAccessToken();
                    clientToken = CredentialManager.LoadClientToken();

                    isAuthorized = await ProfileAttributesRequest.FetchProfileAttributesAsync(
                        _httpClient, accessToken, clientToken, Log, LogDebug);
                }

                return isAuthorized;
            }
            catch (Exception ex)
            {
                Log($"Error during token validation and profile fetch: {ex.Message}");
                return false;
            }
        }


        private async Task<bool> RefreshTokensAsync()
        {
            try
            {
                Log("Attempting to refresh tokens...");
                await CredentialManager.AuthenticateAsync();

                var newAccessToken = CredentialManager.LoadAccessToken();
                var newClientToken = CredentialManager.LoadClientToken();

                if (string.IsNullOrEmpty(newAccessToken) || string.IsNullOrEmpty(newClientToken))
                {
                    Log("Token refresh failed: Tokens are null or empty.");
                    return false;
                }

                Log("Tokens refreshed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error refreshing tokens: {ex.Message}");
                return false;
            }
        }


        private void HandlePlayerEvent(JsonElement playerEvent)
        {
            try
            {
                LogDebug($"Processing player event: {playerEvent}");

                if (playerEvent.TryGetProperty("payloads", out var payloads))
                {
                    foreach (var payload in payloads.EnumerateArray())
                    {
                        if (payload.TryGetProperty("session", out var session))
                        {
                            if (IsDuplicateEvent(session))
                            {
                                LogDebug("Duplicate session detected, skipping processing.");
                                continue;
                            }
                        }
                        HandleSession(payload);
                        HandleEvents(payload);
                        HandleSessionDeletion(payload);
                    }
                }
            }
            catch (Exception ex)
            {
                spotifyUtilities.Log($"Error processing player event: {ex.Message}");
            }
        }

        private void HandleSession(JsonElement payload)
        {
            if (!payload.TryGetProperty("session", out var session)) return;

            string currentUserId = null;
            bool isCurrentUserOwner = false;

            if (session.TryGetProperty("session_owner_id", out var sessionOwnerId))
            {
                string sessionOwner = sessionOwnerId.GetString();

                if (session.TryGetProperty("session_members", out var sessionMembers))
                {
                    foreach (var member in sessionMembers.EnumerateArray())
                    {
                        if (member.GetProperty("is_current_user").GetBoolean())
                        {
                            currentUserId = member.GetProperty("id").GetString();
                            isCurrentUserOwner = currentUserId == sessionOwner;
                            break;
                        }
                    }
                }

                SendParameter(SpotiParameters.IsJamOwner, isCurrentUserOwner);

                LogDebug($"Updated Jam Owner Status: {isCurrentUserOwner}");
            }

            UpdateSessionDetails(session);
        }

        private void UpdateSessionDetails(JsonElement session)
        {
            try
            {
                // Update session ID
                if (session.TryGetProperty("session_id", out var sessionId))
                {
                    SpotifyJamRequests._currentSessionId = sessionId.GetString();
                    LogDebug($"Updated session ID: {SpotifyJamRequests._currentSessionId}");
                }

                // Update join session token
                if (session.TryGetProperty("join_session_token", out var joinToken))
                {
                    SpotifyJamRequests._joinSessionToken = joinToken.GetString();
                    LogDebug($"Updated join session token: {SpotifyJamRequests._joinSessionToken}");
                }

                if (session.TryGetProperty("join_session_uri", out var joinSessionUriElement))
                {
                    string joinSessionUri = joinSessionUriElement.GetString();
                    LogDebug($"Extracted join session URI: {joinSessionUri}");
                    SpotifyJamRequests._shareableUrl = SpotifyJamRequests.GenerateShareableUrlAsync(joinSessionUri, spotifyRequestContext, spotifyUtilities).Result;                    
                }


                if (session.TryGetProperty("active", out var isActive) && isActive.GetBoolean())
                {
                    SpotifyJamRequests._isInJam = true;
                    spotifyRequestContext.IsInJam = true;
                    SendParameter(SpotiParameters.InAJam, true);
                }
                else
                {
                    SpotifyJamRequests._isInJam = false;
                    spotifyRequestContext.IsInJam = false;
                    SendParameter(SpotiParameters.InAJam, false);
                }

                // Extract session owner and images
                if (session.TryGetProperty("session_members", out var sessionMembers))
                {
                    var images = new List<string>();
                    foreach (var member in sessionMembers.EnumerateArray())
                    {
                        // Check if this member is the session owner
                        if (session.TryGetProperty("session_owner_id", out var ownerId) &&
                            member.GetProperty("id").GetString() == ownerId.GetString())
                        {
                            spotifyRequestContext.JamOwnerName = member.GetProperty("display_name").GetString();                            
                            LogDebug($"Updated jam owner: {spotifyRequestContext.JamOwnerName}");
                        }

                        // Add image URL to the list
                        if (member.TryGetProperty("image_url", out var imageUrlProperty) &&
                            !string.IsNullOrEmpty(imageUrlProperty.GetString()))
                        {
                            images.Add(imageUrlProperty.GetString());
                        }
                        else if (member.TryGetProperty("large_image_url", out var largeImageUrlProperty) &&
                                 !string.IsNullOrEmpty(largeImageUrlProperty.GetString()))
                        {
                            images.Add(largeImageUrlProperty.GetString());
                        }
                    }
                    spotifyRequestContext.JamParticipantImages = images;                   
                    LogDebug($"Updated participant images: {string.Join(", ", images)}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error updating session details: {ex.Message}");
            }
        }

        private async void HandleJoinJam()
        {
            await ExecuteWithErrorHandlingAsync(async () =>
            {
                string joinSessionId = await SpotifyJamRequests.GetJoinSessionIdAsync(SpotifyJamRequests._shareableUrl, spotifyUtilities);
                spotifyUtilities.LogDebug($"Join session ID retrieved: {joinSessionId}");

                bool joinResult = await SpotifyJamRequests.JoinSpotifyJam(joinSessionId, spotifyRequestContext, spotifyUtilities);
                if (joinResult)
                {
                    spotifyUtilities.Log("Successfully joined the jam session.");
                }
                else
                {
                    spotifyUtilities.Log("Failed to join the jam session.");
                }
            });
        }



        private void HandleEvents(JsonElement payload)
        {
            if (!payload.TryGetProperty("events", out var events)) return;

            foreach (var ev in events.EnumerateArray())
            {
                if (ev.TryGetProperty("event", out var eventDetails) &&
                    eventDetails.TryGetProperty("state", out var state))
                {
                    ExtractPlaybackState(state);
                    ExtractTrackDetails(state);
                }
            }
        }

        private void ExtractPlaybackState(JsonElement state)
        {
            // Use SafeSet to update UI-bound observables
            spotifyRequestContext.IsPlaying = state.GetProperty("is_playing").GetBoolean();
            // If you update other observable properties (e.g. progress), use SafeSet as well.
        }


        private void ExtractTrackDetails(JsonElement state)
        {
            if (!state.TryGetProperty("item", out var item))
                return;

            spotifyRequestContext.TrackName = item.GetProperty("name").GetString();

            if (item.TryGetProperty("album", out var album))
            {
                spotifyRequestContext.AlbumName = album.GetProperty("name").GetString();

                if (album.TryGetProperty("images", out var images))
                {
                    var imageUrl = images.EnumerateArray().FirstOrDefault().GetProperty("url").GetString();
                    spotifyRequestContext.AlbumArtworkUrl = imageUrl;
                }
            }

            if (item.TryGetProperty("artists", out var artists))
            {
                spotifyRequestContext.Artists = artists.EnumerateArray()
                    .Select(artist => (artist.GetProperty("name").GetString(), artist.GetProperty("uri").GetString()))
                    .ToList();
            }
        }



        private void HandleSessionDeletion(JsonElement payload)
        {
            if (payload.TryGetProperty("reason", out var reason) && reason.GetString() == "SESSION_DELETED")
            {
                SpotifyJamRequests.HandleJamLeave(spotifyRequestContext, spotifyUtilities);
            }
        }

        private void HandleTouching(bool touching)
        {
            if (touching && !isTouching)
            {
                isTouching = true;
                LogDebug("Touching detected. Starting encoding...");
                if (!string.IsNullOrEmpty(SpotifyJamRequests._joinSessionToken))
                {
                    EncodeAndSendSessionId(SpotifyJamRequests._joinSessionToken);
                }
            }
            else if (!touching && isTouching)
            {
                isTouching = false;
                LogDebug("Devices separated. Stopping decoding process.");
                StopDecodingProcess();
            }
        }

        private async void HandleWantJam(bool wantJam)
        {
            bool _isInJam = spotifyRequestContext.IsInJam;
            if (spotifyRequestContext.IsInJam == wantJam)
                return; // Avoid redundant actions

            if (wantJam && !_isInJam)
            {
                _isInJam = true;
                LogDebug("Starting jam request...");
                await SpotifyJamRequests.CreateSpotifyJam(spotifyRequestContext, spotifyUtilities);
            }
            else if (!wantJam && _isInJam)
            {
                _isInJam = false;
                LogDebug("Ending jam request...");
                string sessionId = SpotifyJamRequests._currentSessionId;
                await SpotifyJamRequests.LeaveSpotifyJam(sessionId, spotifyRequestContext, spotifyUtilities);
            }

            spotifyRequestContext.IsInJam = _isInJam;
            SetParameterSafe(SpotiParameters.InAJam, wantJam);
        }


        private async Task<bool> UseTokensSecurely(Func<string, string, Task<bool>> operation)
        {
            string accessToken = null;
            string clientToken = null;

            try
            {
                accessToken = new NetworkCredential(string.Empty, AccessToken).Password;
                clientToken = new NetworkCredential(string.Empty, ClientToken).Password;

                return await operation(accessToken, clientToken);
            }
            finally
            {
                if (accessToken != null)
                {
                    Array.Clear(accessToken.ToCharArray(), 0, accessToken.Length);
                }
                if (clientToken != null)
                {
                    Array.Clear(clientToken.ToCharArray(), 0, clientToken.Length);
                }
            }
        }

        public async Task<string> GetTopArtistsAsync()
        {
            var response = await _httpClient.GetAsync("https://api.spotify.com/v1/me/top/artists");
            return await HandleResponseAsync(response);
        }
        public async Task<string> GetPlaylistsAsync()
        {
            var response = await _httpClient.GetAsync("https://api.spotify.com/v1/me/playlists");
            return await HandleResponseAsync(response);
        }

        public async Task<bool> EnableShuffleAsync()
        {
            var response = await _httpClient.PutAsync("https://api.spotify.com/v1/me/player/shuffle?state=true", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SetVolumeAsync(int volumePercent)
        {
            var response = await _httpClient.PutAsync($"https://api.spotify.com/v1/me/player/volume?volume_percent={volumePercent}", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SetRepeatModeAsync(string state)
        {
            var response = await _httpClient.PutAsync($"https://api.spotify.com/v1/me/player/repeat?state={state}", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SkipToPreviousTrackAsync()
        {
            var response = await _httpClient.PostAsync("https://api.spotify.com/v1/me/player/previous", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> SkipToNextTrackAsync()
        {
            var response = await _httpClient.PostAsync("https://api.spotify.com/v1/me/player/next", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> PausePlaybackAsync()
        {
            var response = await _httpClient.PutAsync("https://api.spotify.com/v1/me/player/pause", null);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> StartPlaybackAsync(string contextUri, int offsetPosition, int positionMs)
        {
            var content = new StringContent($"{{\"context_uri\":\"{contextUri}\",\"offset\":{{\"position\":{offsetPosition}}},\"position_ms\":{positionMs}}}", System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync("https://api.spotify.com/v1/me/player/play", content);
            return response.IsSuccessStatusCode;
        }

        public async Task<string> GetCurrentlyPlayingTrackAsync()
        {
            var response = await _httpClient.GetAsync("https://api.spotify.com/v1/me/player/currently-playing");
            return await HandleResponseAsync(response);
        }

        public async Task<string> GetAvailableDevicesAsync()
        {
            var response = await _httpClient.GetAsync("https://api.spotify.com/v1/me/player/devices");
            return await HandleResponseAsync(response);
        }

        private async Task<string> HandleResponseAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            throw new System.Exception($"API call failed with status code {response.StatusCode}: {errorContent}");
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
                Log($"Failed to set parameter {parameter}: {ex.Message}");
            }
        }

        private bool IsDuplicateEvent(JsonElement session)
        {
            if (!session.TryGetProperty("session_id", out var sessionIdElement) ||
                !session.TryGetProperty("timestamp", out var timestampElement))
            {
                spotifyUtilities.LogDebug("Session missing required fields ('session_id' or 'timestamp'). Skipping.");
                return true; // Consider missing fields as invalid or duplicate
            }

            string sessionId = sessionIdElement.GetString();
            string timestamp = timestampElement.GetString();
            string eventKey = $"{sessionId}:{timestamp}";

            lock (_deduplicationLock)
            {
                if (_processedEventKeys.Contains(eventKey))
                {
                    spotifyUtilities.LogDebug($"Duplicate event detected: {eventKey}. Skipping.");
                    return true;
                }

                _processedEventKeys.Add(eventKey);

                // Optional: Clean up old events to prevent memory bloat
                if (_processedEventKeys.Count > 1000)
                {
                    {
                        _processedEventKeys.Clear(); // Simplified cleanup for large sets
                    }
                }

                spotifyUtilities.LogDebug($"Processing new event: {eventKey}.");
                return false;
            }
        }

        // Helper logging function that wraps the original Log method.
        private void LogWithError(string message, bool isError = false, Exception ex = null)
        {
            // Append exception message if provided.
            string fullMessage = message;
            if (ex != null)
            {
                fullMessage += $": {ex.Message}";
            }

            // Log the message using your existing Log method.
            Log(fullMessage);

            // If it's an error, send the error parameter.
            if (isError)
            {
                SendParameter(SpotiParameters.Error, true);
            }
        }

        // Helper method to wrap actions with error handling.
        private void ExecuteWithErrorHandling(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogWithError("An error occurred while executing action", true, ex);
                // Optionally, rethrow or further handle the exception.
            }
        }

        // Async version for asynchronous methods.
        private async Task ExecuteWithErrorHandlingAsync(Func<Task> asyncAction)
        {
            try
            {
                await asyncAction();
            }
            catch (Exception ex)
            {
                LogWithError("An error occurred during asynchronous execution", true, ex);
                // Optionally, rethrow or further handle the exception.
            }
        }

    }
}