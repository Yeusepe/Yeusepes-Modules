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
using VRCOSC.App.Utils;


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

        // Define an enum for your state groups.
        private enum SpotiState
        {
            DeviceState,
            ShuffleState,
            RepeatState,
            PlaybackProgressState,
            ContextState,
            TrackInfoState,
            AlbumInfoState,
            ArtistInfoState,
            JamState
        }

        // Map each enum value to the corresponding state key used in CreateState.
        private readonly Dictionary<SpotiState, string> stateMapping = new Dictionary<SpotiState, string>
        {
        { SpotiState.DeviceState, "DeviceState" },
        { SpotiState.ShuffleState, "ShuffleState" },
        { SpotiState.RepeatState, "RepeatState" },
        { SpotiState.PlaybackProgressState, "PlaybackProgressState" },
        { SpotiState.ContextState, "ContextState" },
        { SpotiState.TrackInfoState, "TrackInfoState" },
        { SpotiState.AlbumInfoState, "AlbumInfoState" },
        { SpotiState.ArtistInfoState, "ArtistInfoState" },
        { SpotiState.JamState, "JamState" }
        };

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
            // Create clip variables for device info
            var deviceIdVar = CreateVariable<string>("DeviceId", "Device ID");
            var deviceNameVar = CreateVariable<string>("DeviceName", "Device Name");
            var isActiveDeviceVar = CreateVariable<bool>("IsActiveDevice", "Active Device");
            var volumePercentVar = CreateVariable<int>("VolumePercent", "Volume (%)");

            // Create clip variables for playback state
            var shuffleStateVar = CreateVariable<bool>("ShuffleState", "Shuffle");
            var smartShuffleVar = CreateVariable<bool>("SmartShuffle", "Smart Shuffle");
            var repeatStateVar = CreateVariable<string>("RepeatState", "Repeat Mode");
            var timestampVar = CreateVariable<string>("Timestamp", "Timestamp");
            var progressMsVar = CreateVariable<int>("ProgressMs", "Progress (ms)");

            // Create clip variables for context details
            var contextUrlVar = CreateVariable<string>("ContextExternalUrl", "Context URL");
            var contextHrefVar = CreateVariable<string>("ContextHref", "Context Href");
            var contextTypeVar = CreateVariable<string>("ContextType", "Context Type");
            var contextUriVar = CreateVariable<string>("ContextUri", "Context URI");

            // Create clip variables for track details
            var trackNameVar = CreateVariable<string>("TrackName", "Track Name");
            // (If you need a separate variable for track artist, create it here)
            var trackArtistVar = CreateVariable<string>("TrackArtist", "Track Artist");
            var trackDurationVar = CreateVariable<int>("TrackDurationMs", "Track Duration (ms)");
            var discNumberVar = CreateVariable<int>("DiscNumber", "Disc Number");
            var isExplicitVar = CreateVariable<bool>("IsExplicit", "Explicit");
            var popularityVar = CreateVariable<int>("Popularity", "Popularity");
            var previewUrlVar = CreateVariable<string>("PreviewUrl", "Preview URL");
            var trackNumberVar = CreateVariable<int>("TrackNumber", "Track Number");
            var trackUriVar = CreateVariable<string>("TrackUri", "Track URI");
            var playingTypeVar = CreateVariable<string>("CurrentlyPlayingType", "Playing Type");

            // Create clip variables for album details
            var albumNameVar = CreateVariable<string>("AlbumName", "Album Name");
            var albumArtworkUrlVar = CreateVariable<string>("AlbumArtworkUrl", "Album Artwork URL");
            var albumTypeVar = CreateVariable<string>("AlbumType", "Album Type");
            var albumReleaseDateVar = CreateVariable<string>("AlbumReleaseDate", "Album Release Date");
            var albumTotalTracksVar = CreateVariable<int>("AlbumTotalTracks", "Album Total Tracks");

            // Create clip variable for artists (combined)
            var artistsVar = CreateVariable<string>("Artists", "Artists");

            // Device info state
            CreateState("DeviceState", "Device State",
                "ID: {0}, Name: {1}, Active: {2}, Volume: {3}%",
                new[] { deviceIdVar, deviceNameVar, isActiveDeviceVar, volumePercentVar });
            // Playback state
            CreateState("ShuffleState", "Shuffle State",
                "Shuffle: {0}, Smart Shuffle: {1}",
                new[] { shuffleStateVar, smartShuffleVar });
            CreateState("RepeatState", "Repeat State",
                "Repeat mode: {0}",
                new[] { repeatStateVar });
            CreateState("PlaybackProgressState", "Playback Progress State",
                "Timestamp: {0}, Progress: {1} ms",
                new[] { timestampVar, progressMsVar });
            CreateState("ContextState", "Context State",
                "URL: {0}, Href: {1}, Type: {2}, URI: {3}",
                new[] { contextUrlVar, contextHrefVar, contextTypeVar, contextUriVar });
            // Track details state
            CreateState("TrackInfoState", "Track Info",
                "Name: {0}, Artist: {1}, Duration: {2} ms, Disc: {3}, Explicit: {4}, Popularity: {5}, Track#: {6}, URI: {7}, Type: {8}",
                new[] { trackNameVar, trackArtistVar, trackDurationVar, discNumberVar, isExplicitVar, popularityVar, trackNumberVar, trackUriVar, playingTypeVar });
            // Album details state
            CreateState("AlbumInfoState", "Album Info",
                "Album: {0}, Artwork: {1}, Type: {2}, Release: {3}, Total Tracks: {4}",
                new[] { albumNameVar, albumArtworkUrlVar, albumTypeVar, albumReleaseDateVar, albumTotalTracksVar });
            // Artists state
            CreateState("ArtistInfoState", "Artist Info",
                "Artists: {0}",
                new[] { artistsVar });

            // --- Events for changes ---            
            CreateEvent("PlayEvent", "Play Event", "Playback started: {0}", new[] { trackNameVar });
            CreateEvent("PauseEvent", "Pause Event", "Playback paused: {0}", new[] { trackNameVar });
            CreateEvent("TrackChangedEvent", "Track Changed Event", "Now playing: {0}", new[] { trackNameVar });
            CreateEvent("VolumeEvent", "Volume Event", "Volume changed to {0}%.", new[] { volumePercentVar });
            CreateEvent("RepeatEvent", "Repeat Event", "Repeat mode set to {0}.", new[] { repeatStateVar });
            CreateEvent("ShuffleEvent", "Shuffle Event", "Shuffle is {0}.", new[] { shuffleStateVar });

            // Create a clip variable for Jam status
            var inAJamVar = CreateVariable<bool>("InAJam", "In a Jam");

            // Create a state to display the jam status
            CreateState("JamState", "Jam State",
                "In a Jam: {0}",
                new[] { inAJamVar });

            // Register a dedicated Jam event
            CreateEvent("JamEvent", "Jam Event",
                "Jam status updated: {0}",
                new[] { inAJamVar });


            base.OnPostLoad();
        }

        /// <summary>
        /// Updates all of the module's state displays based on the current context.
        /// </summary>
        private void UpdateModuleStates()
        {
            // Always update the device state.
            ChangeState(stateMapping[SpotiState.DeviceState]);

            // Update other state groups that show fixed information.
            ChangeState(stateMapping[SpotiState.ShuffleState]);
            ChangeState(stateMapping[SpotiState.RepeatState]);
            ChangeState(stateMapping[SpotiState.PlaybackProgressState]);
            ChangeState(stateMapping[SpotiState.ContextState]);

            // Update track info and trigger play/pause events based on playback status.
            if (spotifyRequestContext.IsPlaying)
            {
                ChangeState(stateMapping[SpotiState.TrackInfoState]);
                TriggerEvent("PlayEvent");
            }
            else
            {
                ChangeState(stateMapping[SpotiState.TrackInfoState]);
                TriggerEvent("PauseEvent");
            }

            // Update album and artist info states.
            ChangeState(stateMapping[SpotiState.AlbumInfoState]);
            ChangeState(stateMapping[SpotiState.ArtistInfoState]);

            // Update Jam state and trigger an event if in a jam.
            if (spotifyRequestContext.IsInJam)
            {
                ChangeState(stateMapping[SpotiState.JamState]);
                TriggerEvent("JamEvent");
            }
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
                                if (parameter.GetValue<bool>())
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

                    // Update the Jam state variable and trigger the Jam event.
                    SetVariableValue("InAJam", true);
                    TriggerEvent("JamEvent");
                }
                else
                {
                    SpotifyJamRequests._isInJam = false;
                    spotifyRequestContext.IsInJam = false;
                    SendParameter(SpotiParameters.InAJam, false);

                    // Update the Jam state variable and trigger the Jam event.
                    SetVariableValue("InAJam", false);
                    TriggerEvent("JamEvent");
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
            // --- Device info ---
            if (state.TryGetProperty("device", out JsonElement device))
            {
                spotifyRequestContext.DeviceId = device.GetProperty("id").GetString();
                spotifyRequestContext.DeviceName = device.GetProperty("name").GetString();
                spotifyRequestContext.IsActiveDevice = device.GetProperty("is_active").GetBoolean();
                spotifyRequestContext.VolumePercent = device.GetProperty("volume_percent").GetInt32();

                SetVariableValue("DeviceId", spotifyRequestContext.DeviceId);
                SetVariableValue("DeviceName", spotifyRequestContext.DeviceName);
                SetVariableValue("IsActiveDevice", spotifyRequestContext.IsActiveDevice);
                SetVariableValue("VolumePercent", spotifyRequestContext.VolumePercent);
            }

            // --- Shuffle and Smart Shuffle ---
            if (state.TryGetProperty("shuffle_state", out JsonElement shuffle))
            {
                spotifyRequestContext.ShuffleState = shuffle.GetBoolean();
                LogDebug($"Shuffle state: {spotifyRequestContext.ShuffleState}");
                SetVariableValue("ShuffleState", spotifyRequestContext.ShuffleState);
            }
            if (state.TryGetProperty("smart_shuffle", out JsonElement smartShuffle))
            {
                spotifyRequestContext.SmartShuffle = smartShuffle.GetBoolean();
                LogDebug($"Smart Shuffle state: {spotifyRequestContext.SmartShuffle}");
                SetVariableValue("SmartShuffle", spotifyRequestContext.SmartShuffle);
            }

            // --- Repeat state ---
            if (state.TryGetProperty("repeat_state", out JsonElement repeat))
            {
                spotifyRequestContext.RepeatState = repeat.GetString();
                LogDebug($"Repeat state: {spotifyRequestContext.RepeatState}");
                SetVariableValue("RepeatState", spotifyRequestContext.RepeatState);
            }

            // --- Timestamp and progress ---
            if (state.TryGetProperty("timestamp", out JsonElement timestamp))
            {
                spotifyRequestContext.Timestamp = timestamp.GetInt64();
                LogDebug($"Timestamp: {spotifyRequestContext.Timestamp}");
                SetVariableValue("Timestamp", spotifyRequestContext.Timestamp.ToString());
            }
            if (state.TryGetProperty("progress_ms", out JsonElement progress))
            {
                spotifyRequestContext.ProgressMs = progress.GetInt32();
                LogDebug($"Progress: {spotifyRequestContext.ProgressMs}");
                SetVariableValue("ProgressMs", spotifyRequestContext.ProgressMs);
            }

            // --- Context details ---
            if (state.TryGetProperty("context", out JsonElement contextElement))
            {
                if (contextElement.TryGetProperty("external_urls", out JsonElement extUrls) &&
                    extUrls.TryGetProperty("spotify", out JsonElement contextSpotify))
                {
                    spotifyRequestContext.ContextExternalUrl = contextSpotify.GetString();
                    LogDebug($"Context external URL: {spotifyRequestContext.ContextExternalUrl}");
                    SetVariableValue("ContextExternalUrl", spotifyRequestContext.ContextExternalUrl);
                }
                if (contextElement.TryGetProperty("href", out JsonElement contextHref))
                {
                    spotifyRequestContext.ContextHref = contextHref.GetString();
                    LogDebug($"Context href: {spotifyRequestContext.ContextHref}");
                    SetVariableValue("ContextHref", spotifyRequestContext.ContextHref);
                }
                if (contextElement.TryGetProperty("type", out JsonElement contextType))
                {
                    spotifyRequestContext.ContextType = contextType.GetString();
                    LogDebug($"Context type: {spotifyRequestContext.ContextType}");
                    SetVariableValue("ContextType", spotifyRequestContext.ContextType);
                }
                if (contextElement.TryGetProperty("uri", out JsonElement contextUri))
                {
                    spotifyRequestContext.ContextUri = contextUri.GetString();
                    LogDebug($"Context URI: {spotifyRequestContext.ContextUri}");
                    SetVariableValue("ContextUri", spotifyRequestContext.ContextUri);
                }
            }

            // --- Playing status ---
            spotifyRequestContext.IsPlaying = state.GetProperty("is_playing").GetBoolean();
            if (spotifyRequestContext.IsPlaying)
            {
                // Trigger play event
                TriggerEvent("PlayEvent");
                LogDebug("Playback started.");
            }
            else
            {
                // Trigger pause event
                TriggerEvent("PauseEvent");
                LogDebug("Playback paused.");
            }

            // --- Trigger other events ---
            TriggerEvent("ShuffleEvent");
            TriggerEvent("RepeatEvent");
            TriggerEvent("VolumeEvent");

            // --- Update states for each group ---
            ChangeState("DeviceState");
            ChangeState("ShuffleState");
            ChangeState("RepeatState");
            ChangeState("PlaybackProgressState");
            ChangeState("ContextState");
        }




        private void ExtractTrackDetails(JsonElement state)
        {
            if (!state.TryGetProperty("item", out JsonElement item))
                return;

            // --- Track basic info ---
            spotifyRequestContext.TrackName = item.GetProperty("name").GetString();
            SetVariableValue("TrackName", spotifyRequestContext.TrackName);

            // --- Track Artist ---
            if (item.TryGetProperty("artists", out JsonElement artistsElement))
            {
                spotifyRequestContext.Artists = artistsElement.EnumerateArray()
                    .Select(artist => (artist.GetProperty("name").GetString(), artist.GetProperty("uri").GetString()))
                    .ToList();
                string artistsCombined = string.Join(", ", spotifyRequestContext.Artists);
                SetVariableValue("Artists", artistsCombined);
                LogDebug($"Artists: {artistsCombined}");
                string mainArtist = spotifyRequestContext.Artists.FirstOrDefault().Item1;
                SetVariableValue("TrackArtist", mainArtist);
                LogDebug($"Main Artist: {mainArtist}");
            }

            // --- Track-level properties ---
            if (item.TryGetProperty("duration_ms", out JsonElement duration))
            {
                spotifyRequestContext.TrackDurationMs = duration.GetInt32();
                SetVariableValue("TrackDurationMs", spotifyRequestContext.TrackDurationMs);
                LogDebug($"Track duration: {spotifyRequestContext.TrackDurationMs}");
            }
            if (item.TryGetProperty("disc_number", out JsonElement disc))
            {
                spotifyRequestContext.DiscNumber = disc.GetInt32();
                SetVariableValue("DiscNumber", spotifyRequestContext.DiscNumber);
                LogDebug($"Disc number: {spotifyRequestContext.DiscNumber}");
            }
            if (item.TryGetProperty("explicit", out JsonElement explicitElem))
            {
                spotifyRequestContext.IsExplicit = explicitElem.GetBoolean();
                SetVariableValue("IsExplicit", spotifyRequestContext.IsExplicit);
                LogDebug($"Explicit: {spotifyRequestContext.IsExplicit}");
            }
            if (item.TryGetProperty("popularity", out JsonElement popularity))
            {
                spotifyRequestContext.Popularity = popularity.GetInt32();
                SetVariableValue("Popularity", spotifyRequestContext.Popularity);
                LogDebug($"Popularity: {spotifyRequestContext.Popularity}");
            }
            if (item.TryGetProperty("preview_url", out JsonElement previewUrl))
            {
                spotifyRequestContext.PreviewUrl = previewUrl.GetString();
                SetVariableValue("PreviewUrl", spotifyRequestContext.PreviewUrl);
                LogDebug($"Preview URL: {spotifyRequestContext.PreviewUrl}");
            }
            if (item.TryGetProperty("track_number", out JsonElement trackNumber))
            {
                spotifyRequestContext.TrackNumber = trackNumber.GetInt32();
                SetVariableValue("TrackNumber", spotifyRequestContext.TrackNumber);
                LogDebug($"Track number: {spotifyRequestContext.TrackNumber}");
            }
            if (item.TryGetProperty("uri", out JsonElement trackUri))
            {
                spotifyRequestContext.TrackUri = trackUri.GetString();
                SetVariableValue("TrackUri", spotifyRequestContext.TrackUri);
                LogDebug($"Track URI: {spotifyRequestContext.TrackUri}");
            }
            if (item.TryGetProperty("currently_playing_type", out JsonElement playingType))
            {
                spotifyRequestContext.CurrentlyPlayingType = playingType.GetString();
                SetVariableValue("CurrentlyPlayingType", spotifyRequestContext.CurrentlyPlayingType);
                LogDebug($"Currently playing type: {spotifyRequestContext.CurrentlyPlayingType}");
            }

            // --- Album details ---
            if (item.TryGetProperty("album", out JsonElement album))
            {
                spotifyRequestContext.AlbumName = album.GetProperty("name").GetString();
                LogDebug($"Album name: {spotifyRequestContext.AlbumName}");
                SetVariableValue("AlbumName", spotifyRequestContext.AlbumName);
                if (album.TryGetProperty("images", out JsonElement images))
                {
                    var imageUrl = images.EnumerateArray().FirstOrDefault().GetProperty("url").GetString();
                    spotifyRequestContext.AlbumArtworkUrl = imageUrl;
                    LogDebug($"Album artwork URL: {imageUrl}");
                    SetVariableValue("AlbumArtworkUrl", imageUrl);
                }
                if (album.TryGetProperty("album_type", out JsonElement albumType))
                {
                    spotifyRequestContext.AlbumType = albumType.GetString();
                    LogDebug($"Album type: {spotifyRequestContext.AlbumType}");
                    SetVariableValue("AlbumType", spotifyRequestContext.AlbumType);
                }
                if (album.TryGetProperty("release_date", out JsonElement releaseDate))
                {
                    spotifyRequestContext.AlbumReleaseDate = releaseDate.GetString();
                    LogDebug($"Album release date: {spotifyRequestContext.AlbumReleaseDate}");
                    SetVariableValue("AlbumReleaseDate", spotifyRequestContext.AlbumReleaseDate);
                }
                if (album.TryGetProperty("total_tracks", out JsonElement totalTracks))
                {
                    spotifyRequestContext.AlbumTotalTracks = totalTracks.GetInt32();
                    LogDebug($"Album total tracks: {spotifyRequestContext.AlbumTotalTracks}");
                    SetVariableValue("AlbumTotalTracks", spotifyRequestContext.AlbumTotalTracks);
                }
            }

            // --- Finally, update track state and trigger track change event ---
            ChangeState("TrackInfoState");
            TriggerEvent("TrackChangedEvent");
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

                // Log the context properties before making the call.
                LogDebug("SpotifyRequestContext.HttpClient is " + (spotifyRequestContext.HttpClient != null ? "initialized" : "null"));
                LogDebug("SpotifyRequestContext.AccessToken is " + (spotifyRequestContext.AccessToken != null ? "initialized" : "null"));
                LogDebug("SpotifyRequestContext.ClientToken is " + (spotifyRequestContext.ClientToken != null ? "initialized" : "null"));

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

        [ModuleUpdate(ModuleUpdateMode.ChatBox)]
        private void ChatBoxUpdate()
        {
            LogDebug("ChatBoxUpdate is running.");

            if (spotifyRequestContext == null)
            {
                Log("SpotifyRequestContext is not initialized yet.");
                return;
            }

            // --- Update Device Info ---
            try
            {
                // Update Device ID
                SetVariableValue("DeviceId", spotifyRequestContext.DeviceId);
                SetVariableValue("DeviceName", spotifyRequestContext.DeviceName);
                // Update Active Device flag
                SetVariableValue("IsActiveDevice", spotifyRequestContext.IsActiveDevice);
                // Update Volume Percent
                SetVariableValue("VolumePercent", spotifyRequestContext.VolumePercent);

            }
            catch (Exception ex)
            {
                LogDebug("Error updating Device Info: " + ex.Message);
            }

            // --- Update Playback State ---
            try
            {
                // Update Shuffle state
                SetVariableValue("ShuffleState", spotifyRequestContext.ShuffleState);
                // Update Repeat state
                SetVariableValue("RepeatState", spotifyRequestContext.RepeatState);
                // Update Timestamp (as string)
                SetVariableValue("Timestamp", spotifyRequestContext.Timestamp.ToString());
                // Update Progress (ms)
                SetVariableValue("ProgressMs", spotifyRequestContext.ProgressMs);
                // Update Context details
                SetVariableValue("ContextExternalUrl", spotifyRequestContext.ContextExternalUrl);
                SetVariableValue("ContextHref", spotifyRequestContext.ContextHref);
                SetVariableValue("ContextType", spotifyRequestContext.ContextType);
                SetVariableValue("ContextUri", spotifyRequestContext.ContextUri);
            }
            catch (Exception ex)
            {
                LogDebug("Error updating Playback State: " + ex.Message);
            }

            // --- Update Track Information ---
            try
            {
                string value = string.IsNullOrEmpty(spotifyRequestContext.TrackName)
                        ? "No Track"
                        : spotifyRequestContext.TrackName;
                SetVariableValue("TrackName", value);
                // Update Track Artist
                string artists = (spotifyRequestContext.Artists != null && spotifyRequestContext.Artists.Any())
                    ? string.Join(", ", spotifyRequestContext.Artists.Select(a => a.Name))
                    : "Unknown Artist";
                SetVariableValue("TrackArtist", artists);
                // Update Track Duration
                SetVariableValue("TrackDurationMs", spotifyRequestContext.TrackDurationMs);
                // Update Disc Number
                SetVariableValue("DiscNumber", spotifyRequestContext.DiscNumber);
                // Update Explicit flag
                SetVariableValue("IsExplicit", spotifyRequestContext.IsExplicit);
                // Update Popularity
                SetVariableValue("Popularity", spotifyRequestContext.Popularity);
                // Update Preview URL
                SetVariableValue("PreviewUrl", spotifyRequestContext.PreviewUrl);
                // Update Track Number
                SetVariableValue("TrackNumber", spotifyRequestContext.TrackNumber);
                // Update Track URI
                SetVariableValue("TrackUri", spotifyRequestContext.TrackUri);
                // Update Playing Type
                SetVariableValue("CurrentlyPlayingType", spotifyRequestContext.CurrentlyPlayingType);
            }
            catch (Exception ex)
            {
                LogDebug("Error updating Track Information: " + ex.Message);
            }

            // --- Update Album Information ---
            try
            {
                SetVariableValue("AlbumName", spotifyRequestContext.AlbumName);
                SetVariableValue("AlbumArtworkUrl", spotifyRequestContext.AlbumArtworkUrl);
                SetVariableValue("AlbumType", spotifyRequestContext.AlbumType);
                SetVariableValue("AlbumReleaseDate", spotifyRequestContext.AlbumReleaseDate);
                SetVariableValue("AlbumTotalTracks", spotifyRequestContext.AlbumTotalTracks);
            }
            catch (Exception ex)
            {
                LogDebug("Error updating Album Information: " + ex.Message);
            }

            // --- Update Artists (combined) ---
            try
            {
                // Assuming your context holds a list of artist names
                string artists = (spotifyRequestContext.Artists != null && spotifyRequestContext.Artists.Any())
                    ? string.Join(", ", spotifyRequestContext.Artists.Select(a => a.Name))
                    : "Unknown Artist";
                SetVariableValue("Artists", artists);
            }
            catch (Exception ex)
            {
                LogDebug("Error updating Artists: " + ex.Message);
            }

            UpdateModuleStates();
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