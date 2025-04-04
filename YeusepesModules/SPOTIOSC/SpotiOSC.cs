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
            // Pre‐existing control parameters
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

            // Playback state (from root level)
            ShuffleEnabled,         // from "shuffle_state"
            SmartShuffle,           // from "smart_shuffle"
            RepeatMode,             // mapped from "repeat_state": off=0, track=1, context=2
            Timestamp,              // from "timestamp" (modulo conversion to int)
            PlaybackPosition,       // from "progress_ms"
            IsPlaying,              // from "is_playing"

            // Device details (state.device)
            DeviceIsActive,         // from device.is_active
            DeviceIsPrivate,        // from device.is_private_session
            DeviceIsRestricted,     // from device.is_restricted
            DeviceSupportsVolume,   // from device.supports_volume
            DeviceVolumePercent,    // from device.volume_percent

            // Context details (state.context)
            ContextType,            // mapped from context.type (playlist=0, otherwise -1)

            // Track details (state.item)
            DiscNumber,             // from item.disc_number
            TrackDurationMs,        // from item.duration_ms
            IsExplicit,             // from item.explicit
            IsLocal,                // from item.is_local
            SongPopularity,         // from item.popularity
            TrackNumber,            // from item.track_number

            // Album details (item.album)
            AlbumTotalTracks,       // from album.total_tracks
            AlbumType,              // mapped from album.album_type (single=0, album=1, others=2)
                                    // (Optional: You might also add numeric values for the first image’s height and width)           

            // Actions (state.actions.disallows)
            DisallowPausing,        // from actions.disallows.pausing
            DisallowResuming,       // from actions.disallows.resuming (if present)
            DisallowSkippingPrev,   // from actions.disallows.skipping_prev (if present)
            
            JamParticipantCount,    // count of session_members            
            SessionMaxMemberCount,  // from session.maxMemberCount
            SessionIsOwner,         // from session.is_session_owner
            SessionIsListening,     // from session.is_listening
            SessionIsControlling,   // from session.is_controlling
            QueueOnlyMode,          // from session.queue_only_mode            
            HostIsGroup             // from session.host_device_info.is_group
        }


        protected override void OnPreLoad()
        {
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("libusb-1.0.dll", message => Log(message));
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("cvextern.dll", message => Log(message));
            screenUtilities = ScreenUtilities.EnsureInitialized(
                LogDebug,         // Logging delegate
                GetSettingValue<String>,  // Function to retrieve settings
                SetSettingValue,  // Function to save settings
                CreateTextBox
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


            RegisterParameter<bool>(SpotiParameters.Enabled, "SpotiOSC/Enabled", ParameterMode.Write, "Enabled", "Set to true if the module is enabled.");
            RegisterParameter<bool>(SpotiParameters.WantJam, "SpotiOSC/WantJam", ParameterMode.ReadWrite, "Want Jam", "Set to true if you want to join a jam.");
            RegisterParameter<bool>(SpotiParameters.InAJam, "SpotiOSC/InAJam", ParameterMode.Write, "In A Jam", "Set to true if you are in a jam.");
            RegisterParameter<bool>(SpotiParameters.IsJamOwner, "SpotiOSC/IsJamOwner", ParameterMode.Write, "Is Jam Owner", "Set to true if you are the owner of the jam.");
            RegisterParameter<bool>(SpotiParameters.Error, "SpotiOSC/Error", ParameterMode.Write, "Error", "Triggered when an error occurs.");
            RegisterParameter<bool>(SpotiParameters.Touching, "SpotiOSC/Touching", ParameterMode.ReadWrite, "Touching", "Set to true when two compatible devices tap eachother.");
            RegisterParameter<bool>(SpotiParameters.Play, "SpotiOSC/Play", ParameterMode.ReadWrite, "Play", "Triggers playback.");
            RegisterParameter<bool>(SpotiParameters.Pause, "SpotiOSC/Pause", ParameterMode.ReadWrite, "Pause", "Pauses playback.");
            RegisterParameter<bool>(SpotiParameters.NextTrack, "SpotiOSC/NextTrack", ParameterMode.Read, "Next Track", "Skips to the next track.");
            RegisterParameter<bool>(SpotiParameters.PreviousTrack, "SpotiOSC/PreviousTrack", ParameterMode.Read, "Previous Track", "Skips to the previous track.");                        

            // Playback state (root)
            RegisterParameter<bool>(SpotiParameters.ShuffleEnabled, "SpotiOSC/ShuffleEnabled", ParameterMode.ReadWrite, "Shuffle", "Shuffle state.");
            RegisterParameter<bool>(SpotiParameters.SmartShuffle, "SpotiOSC/SmartShuffle", ParameterMode.ReadWrite, "Smart Shuffle", "Smart shuffle state.");
            RegisterParameter<int>(SpotiParameters.RepeatMode, "SpotiOSC/RepeatMode", ParameterMode.ReadWrite, "Repeat Mode (Mapped)", "Mapped repeat state: off=0, track=1, context=2.");
            RegisterParameter<int>(SpotiParameters.Timestamp, "SpotiOSC/Timestamp", ParameterMode.ReadWrite, "Timestamp", "Playback timestamp.");
            RegisterParameter<int>(SpotiParameters.PlaybackPosition, "SpotiOSC/PlaybackPosition", ParameterMode.ReadWrite, "Playback Progress (ms)", "Playback progress in ms.");
            RegisterParameter<bool>(SpotiParameters.IsPlaying, "SpotiOSC/IsPlaying", ParameterMode.Write, "Is Playing", "Whether playback is active.");

            // Device details (state.device)
            RegisterParameter<bool>(SpotiParameters.DeviceIsActive, "SpotiOSC/DeviceIsActive", ParameterMode.ReadWrite, "Device Active", "Device is active.");
            RegisterParameter<bool>(SpotiParameters.DeviceIsPrivate, "SpotiOSC/DeviceIsPrivate", ParameterMode.ReadWrite, "Private Session", "Device is in a private session.");
            RegisterParameter<bool>(SpotiParameters.DeviceIsRestricted, "SpotiOSC/DeviceIsRestricted", ParameterMode.ReadWrite, "Restricted Device", "Device is restricted.");
            RegisterParameter<bool>(SpotiParameters.DeviceSupportsVolume, "SpotiOSC/DeviceSupportsVolume", ParameterMode.ReadWrite, "Volume Support", "Device supports volume.");
            RegisterParameter<int>(SpotiParameters.DeviceVolumePercent, "SpotiOSC/DeviceVolumePercent", ParameterMode.ReadWrite, "Device Volume (%)", "Device volume percentage.");

            // Context details (state.context)
            RegisterParameter<int>(SpotiParameters.ContextType, "SpotiOSC/ContextType", ParameterMode.Write, "Context Type (Mapped)", "Mapped context type (playlist=0, else -1).");

            // Track details (state.item)
            RegisterParameter<int>(SpotiParameters.DiscNumber, "SpotiOSC/DiscNumber", ParameterMode.Write, "Disc Number", "Track disc number.");
            RegisterParameter<int>(SpotiParameters.TrackDurationMs, "SpotiOSC/TrackDurationMs", ParameterMode.Write, "Track Duration (ms)", "Duration of the track in ms.");
            RegisterParameter<bool>(SpotiParameters.IsExplicit, "SpotiOSC/IsExplicit", ParameterMode.Write, "Explicit", "Whether track is explicit.");
            RegisterParameter<bool>(SpotiParameters.IsLocal, "SpotiOSC/IsLocal", ParameterMode.Write, "Is Local", "Whether track is local.");
            RegisterParameter<int>(SpotiParameters.SongPopularity, "SpotiOSC/SongPopularity", ParameterMode.Write, "Song Popularity", "Popularity of the current song.");
            RegisterParameter<int>(SpotiParameters.TrackNumber, "SpotiOSC/TrackNumber", ParameterMode.Write, "Track Number", "Track number.");

            // Album details (item.album)
            RegisterParameter<int>(SpotiParameters.AlbumTotalTracks, "SpotiOSC/AlbumTotalTracks", ParameterMode.Write, "Album Total Tracks", "Total number of tracks in the album.");
            RegisterParameter<int>(SpotiParameters.AlbumType, "SpotiOSC/AlbumType", ParameterMode.Write, "Album Type (Mapped)", "Mapped album type: single=0, album=1, other=2.");            

            // Actions (state.actions.disallows)
            RegisterParameter<bool>(SpotiParameters.DisallowPausing, "SpotiOSC/DisallowPausing", ParameterMode.Write, "Disallow Pausing", "Whether pausing is disallowed.");
            RegisterParameter<bool>(SpotiParameters.DisallowResuming, "SpotiOSC/DisallowResuming", ParameterMode.Write, "Disallow Resuming", "Whether resuming is disallowed.");
            RegisterParameter<bool>(SpotiParameters.DisallowSkippingPrev, "SpotiOSC/DisallowSkippingPrev", ParameterMode.Write, "Disallow Skipping Prev", "Whether skipping to previous track is disallowed.");

            // Session (jam) details (in session events)
            RegisterParameter<int>(SpotiParameters.JamParticipantCount, "SpotiOSC/JamParticipantCount", ParameterMode.Write, "Jam Participant Count", "Number of participants in the session.");            
            RegisterParameter<int>(SpotiParameters.SessionMaxMemberCount, "SpotiOSC/SessionMaxMemberCount", ParameterMode.Write, "Session Max Member Count", "Maximum allowed session members.");
            RegisterParameter<bool>(SpotiParameters.SessionIsOwner, "SpotiOSC/SessionIsOwner", ParameterMode.Write, "Session Is Owner", "Whether the current user is the session owner.");
            RegisterParameter<bool>(SpotiParameters.SessionIsListening, "SpotiOSC/SessionIsListening", ParameterMode.Write, "Session Is Listening", "Whether the session is listening.");
            RegisterParameter<bool>(SpotiParameters.SessionIsControlling, "SpotiOSC/SessionIsControlling", ParameterMode.Write, "Session Is Controlling", "Whether the session is controlling.");
            RegisterParameter<bool>(SpotiParameters.QueueOnlyMode, "SpotiOSC/QueueOnlyMode", ParameterMode.Write, "Queue Only Mode", "Whether the session is in queue-only mode.");            
            RegisterParameter<bool>(SpotiParameters.HostIsGroup, "SpotiOSC/HostIsGroup", ParameterMode.Write, "Host Is Group", "Whether the host device is a group device.");

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
            // --- Media-related variables ---
            var trackNameVar = CreateVariable<string>("TrackName", "Track Name");
            var trackArtistVar = CreateVariable<string>("TrackArtist", "Track Artist");
            var trackDurationVar = CreateVariable<int>("TrackDurationMs", "Track Duration (ms)");
            var discNumberVar = CreateVariable<int>("DiscNumber", "Disc Number");
            var isExplicitVar = CreateVariable<bool>("IsExplicit", "Explicit");
            var popularityVar = CreateVariable<int>("Popularity", "Popularity");
            var trackNumberVar = CreateVariable<int>("TrackNumber", "Track Number");
            var trackUriVar = CreateVariable<string>("TrackUri", "Track URI");
            var playingTypeVar = CreateVariable<string>("CurrentlyPlayingType", "Playing Type");

            var albumNameVar = CreateVariable<string>("AlbumName", "Album Name");
            var albumArtworkUrlVar = CreateVariable<string>("AlbumArtworkUrl", "Album Artwork URL");
            var albumTypeVar = CreateVariable<string>("AlbumType", "Album Type");
            var albumReleaseDateVar = CreateVariable<string>("AlbumReleaseDate", "Album Release Date");
            var albumTotalTracksVar = CreateVariable<int>("AlbumTotalTracks", "Album Total Tracks");

            var shuffleStateVar = CreateVariable<bool>("ShuffleState", "Shuffle");
            var smartShuffleVar = CreateVariable<bool>("SmartShuffle", "Smart Shuffle");
            var repeatStateVar = CreateVariable<string>("RepeatState", "Repeat Mode");
            var timestampVar = CreateVariable<string>("Timestamp", "Timestamp");
            var progressMsVar = CreateVariable<int>("ProgressMs", "Progress (ms)");

            var artistsVar = CreateVariable<string>("Artists", "Artists");

            // --- System-related variables ---
            var deviceIdVar = CreateVariable<string>("DeviceId", "Device ID");
            var deviceNameVar = CreateVariable<string>("DeviceName", "Device Name");
            var isActiveDeviceVar = CreateVariable<bool>("IsActiveDevice", "Active Device");
            var volumePercentVar = CreateVariable<int>("VolumePercent", "Volume (%)");

            var contextUrlVar = CreateVariable<string>("ContextExternalUrl", "Context URL");
            var contextHrefVar = CreateVariable<string>("ContextHref", "Context Href");
            var contextTypeVar = CreateVariable<string>("ContextType", "Context Type");
            var contextUriVar = CreateVariable<string>("ContextUri", "Context URI");

            // --- Jam-related variable ---
            var inAJamVar = CreateVariable<bool>("InAJam", "In a Jam");

            // --- Events for changes ---            
            CreateEvent("PlayEvent", "Play Event", "Playback started: {0}", new[] { trackNameVar });
            CreateEvent("PauseEvent", "Pause Event", "Playback paused: {0}", new[] { trackNameVar });
            CreateEvent("TrackChangedEvent", "Track Changed Event", "Now playing: {0}", new[] { trackNameVar });
            CreateEvent("VolumeEvent", "Volume Event", "Volume changed to {0}%.", new[] { volumePercentVar });
            CreateEvent("RepeatEvent", "Repeat Event", "Repeat mode set to {0}.", new[] { repeatStateVar });
            CreateEvent("ShuffleEvent", "Shuffle Event", "Shuffle is {0}.", new[] { shuffleStateVar });
            // Register a dedicated Jam event
            CreateEvent("JamEvent", "Jam Event", "Jam status updated: {0}", new[] { inAJamVar });

            string commonFormat =
            "Track: {0} - {1}\n" +
            "Album: {2}\n" +
            "Device: {3} ({4}%)\n" +
            "Playback: Shuffle: {5}, Repeat: {6}\n";

            // 1. Playing in a jam
            CreateState("Playing_Jam_Explicit_Shuffle", "In a Jam!: Explicit Song + Shuffle",
                "State: In a Jam! (Explicit, Shuffle)\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar});

            CreateState("Playing_Jam_Explicit_NoShuffle", "In a Jam!: Explicit Song, No Shuffle",
                "State: In a Jam! (Explicit, No Shuffle)\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });

            CreateState("Playing_Jam_Clean_Shuffle", "In a Jam!: Clean + Shuffle",
                "State: In a Jam! (Clean, Shuffle)\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });

            CreateState("Playing_Jam_Clean_NoShuffle", "In a Jam!: Clean, No Shuffle",
                "State: In a Jam! (Clean, No Shuffle)\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });

            CreateState("Paused_Jam", "In a Jam!: Paused Music",
                "State: In a Jam!: Paused Music\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });

            // 2. Playing normally (not in a jam)
            CreateState("Playing_Explicit_Shuffle", "Playing: Explicit Song + Shuffle",
                "State: Playing (Explicit, Shuffle)\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });

            CreateState("Playing_Explicit_NoShuffle", "Playing: Explicit Song, No Shuffle",
                "State: Playing (Explicit, No Shuffle)\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });

            CreateState("Playing_Clean_Shuffle", "Playing: Clean + Shuffle",
                "State: Playing (Clean, Shuffle)\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });

            CreateState("Playing_Clean_NoShuffle", "Playing: Clean, No Shuffle",
                "State: Playing (Clean, No Shuffle)\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });

            CreateState("Paused_Normal", "Paused Music",
                "State: Paused Music\n" + commonFormat,
                new[] { trackNameVar, trackArtistVar, albumNameVar, deviceNameVar, volumePercentVar, shuffleStateVar, repeatStateVar });
        }


        protected override async Task<bool> OnModuleStart()
        {
            _cts = new CancellationTokenSource();
            
            _httpClient = new HttpClient();

            spotifyUtilities = new SpotifyUtilities
            {
                Log = message => Log(message),
                LogDebug = message => LogDebug(message),
                SendParameter = (param, value) => SetParameterSafe(param, value),
                Encoder = encoder
            };
            CredentialManager.SpotifyUtils = spotifyUtilities;

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
                if (session.TryGetProperty("is_listening", out JsonElement isListening))
                {
                    SpotifyJamRequests._isListening = isListening.GetBoolean();
                    SetParameterSafe(SpotiParameters.SessionIsListening, isListening.GetBoolean());
                }
                if (session.TryGetProperty("is_controlling", out JsonElement isControlling))
                {
                    SpotifyJamRequests._isControlling = isControlling.GetBoolean();
                    SetParameterSafe(SpotiParameters.SessionIsControlling, isControlling.GetBoolean());
                }
                if (session.TryGetProperty("queue_only_mode", out JsonElement queueOnly))
                {
                    SpotifyJamRequests._queueOnlyMode = queueOnly.GetBoolean();
                    SetParameterSafe(SpotiParameters.QueueOnlyMode, queueOnly.GetBoolean());
                }

                if (session.TryGetProperty("maxMemberCount", out JsonElement maxCount))
                {
                    int maxMemberCount = maxCount.GetInt32();
                    SpotifyJamRequests._maxMemberCount = maxMemberCount;
                    LogDebug($"Updated max member count: {maxMemberCount}");
                    SendParameter(SpotiParameters.SessionMaxMemberCount, maxMemberCount);
                }

                // Host device info: check if host device is a group
                if (session.TryGetProperty("host_device_info", out JsonElement hostInfo))
                {
                    if (hostInfo.TryGetProperty("is_group", out JsonElement IsGroup))
                    {
                        bool isGroup = IsGroup.GetBoolean();
                        SpotifyJamRequests._hostIsGroup = isGroup;
                        SendParameter(SpotiParameters.HostIsGroup, isGroup);
                        LogDebug($"Updated host device group status: {isGroup}");
                    }

                    if (session.TryGetProperty("active", out var isActive) && isActive.GetBoolean())
                    {
                        SpotifyJamRequests._isInJam = true;
                        spotifyRequestContext.IsInJam = true;
                        SendParameter(SpotiParameters.InAJam, true);

                        // Update the Jam state variable and trigger the Jam event.                    
                        TriggerEvent("JamEvent");
                    }
                    else
                    {
                        SpotifyJamRequests._isInJam = false;
                        spotifyRequestContext.IsInJam = false;
                        SendParameter(SpotiParameters.InAJam, false);

                        // Update the Jam state variable and trigger the Jam event.                    
                        TriggerEvent("JamEvent");
                    }

                    // Extract session owner and images
                    if (session.TryGetProperty("session_members", out var sessionMembers))
                    {
                        int count = sessionMembers.GetArrayLength();
                        SpotifyJamRequests._participantCount = count;
                        SendParameter(SpotiParameters.JamParticipantCount, count);
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
                    SendParameter(SpotiParameters.Error, true);
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
                SetParameterSafe(SpotiParameters.DeviceIsActive, device.GetProperty("is_active").GetBoolean());
                SetParameterSafe(SpotiParameters.DeviceIsPrivate, device.GetProperty("is_private_session").GetBoolean());
                SetParameterSafe(SpotiParameters.DeviceIsRestricted, device.GetProperty("is_restricted").GetBoolean());
                SetParameterSafe(SpotiParameters.DeviceSupportsVolume, device.GetProperty("supports_volume").GetBoolean());
                SetParameterSafe(SpotiParameters.DeviceVolumePercent, device.GetProperty("volume_percent").GetInt32());
            }

            // --- Shuffle and Smart Shuffle ---
            if (state.TryGetProperty("shuffle_state", out JsonElement shuffle))
            {
                spotifyRequestContext.ShuffleState = shuffle.GetBoolean();
                SetParameterSafe(SpotiParameters.ShuffleEnabled, state.GetProperty("shuffle_state").GetBoolean());
                LogDebug($"Shuffle state: {spotifyRequestContext.ShuffleState}");

            }
            if (state.TryGetProperty("smart_shuffle", out JsonElement smartShuffle))
            {
                spotifyRequestContext.SmartShuffle = smartShuffle.GetBoolean();
                SetParameterSafe(SpotiParameters.SmartShuffle, state.GetProperty("smart_shuffle").GetBoolean());
                LogDebug($"Smart Shuffle state: {spotifyRequestContext.SmartShuffle}");                
            }

            // --- Repeat state ---
            if (state.TryGetProperty("repeat_state", out JsonElement repeat))
            {
                spotifyRequestContext.RepeatState = repeat.GetString();
                string repeatStr = state.GetProperty("repeat_state").GetString();
                int repeatMapped = repeatStr == "off" ? 0 : (repeatStr == "track" ? 1 : 2);
                SetParameterSafe(SpotiParameters.RepeatMode, repeatMapped);
                LogDebug($"Repeat state: {spotifyRequestContext.RepeatState}");                
            }

            // --- Timestamp and progress ---
            if (state.TryGetProperty("timestamp", out JsonElement timestamp))
            {
                spotifyRequestContext.Timestamp = timestamp.GetInt64();
                int ts = (int)(state.GetProperty("timestamp").GetInt64() % int.MaxValue);
                SetParameterSafe(SpotiParameters.Timestamp, ts);
                SetParameterSafe(SpotiParameters.PlaybackPosition, state.GetProperty("progress_ms").GetInt32());
                SetParameterSafe(SpotiParameters.IsPlaying, state.GetProperty("is_playing").GetBoolean());
                LogDebug($"Timestamp: {spotifyRequestContext.Timestamp}");                
            }
            if (state.TryGetProperty("progress_ms", out JsonElement progress))
            {
                spotifyRequestContext.ProgressMs = progress.GetInt32();
                LogDebug($"Progress: {spotifyRequestContext.ProgressMs}");                
            }

            // --- Context details ---
            if (state.TryGetProperty("context", out JsonElement contextElement))
            {
                if (contextElement.TryGetProperty("external_urls", out JsonElement extUrls) &&
                    extUrls.TryGetProperty("spotify", out JsonElement contextSpotify))
                {
                    spotifyRequestContext.ContextExternalUrl = contextSpotify.GetString();
                    LogDebug($"Context external URL: {spotifyRequestContext.ContextExternalUrl}");                    
                }
                if (contextElement.TryGetProperty("href", out JsonElement contextHref))
                {
                    spotifyRequestContext.ContextHref = contextHref.GetString();
                    LogDebug($"Context href: {spotifyRequestContext.ContextHref}");                    
                }
                if (contextElement.TryGetProperty("type", out JsonElement contextType))
                {
                    spotifyRequestContext.ContextType = contextType.GetString();
                    int ctxMapped = contextType.GetString() == "playlist" ? 0 : -1;
                    SetParameterSafe(SpotiParameters.ContextType, ctxMapped);
                    LogDebug($"Context type: {spotifyRequestContext.ContextType}");                    
                }
                if (contextElement.TryGetProperty("uri", out JsonElement contextUri))
                {
                    spotifyRequestContext.ContextUri = contextUri.GetString();
                    LogDebug($"Context URI: {spotifyRequestContext.ContextUri}");                    
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
        }




        private void ExtractTrackDetails(JsonElement state)
        {
            if (!state.TryGetProperty("item", out JsonElement item))
                return;

            // --- Track basic info ---
            spotifyRequestContext.TrackName = item.GetProperty("name").GetString();            

            // --- Track Artist ---
            if (item.TryGetProperty("artists", out JsonElement artistsElement))
            {
                spotifyRequestContext.Artists = artistsElement.EnumerateArray()
                    .Select(artist => (artist.GetProperty("name").GetString(), artist.GetProperty("uri").GetString()))
                    .ToList();
                string artistsCombined = string.Join(", ", spotifyRequestContext.Artists);                
                LogDebug($"Artists: {artistsCombined}");
                string mainArtist = spotifyRequestContext.Artists.FirstOrDefault().Item1;                
                LogDebug($"Main Artist: {mainArtist}");
            }

            // --- Track-level properties ---
            if (item.TryGetProperty("duration_ms", out JsonElement duration))
            {
                spotifyRequestContext.TrackDurationMs = duration.GetInt32();
                SetParameterSafe(SpotiParameters.TrackDurationMs, duration.GetInt32());
                LogDebug($"Track duration: {spotifyRequestContext.TrackDurationMs}");
            }
            if (item.TryGetProperty("disc_number", out JsonElement disc))
            {
                spotifyRequestContext.DiscNumber = disc.GetInt32();
                SetParameterSafe(SpotiParameters.DiscNumber, disc.GetInt32());
                LogDebug($"Disc number: {spotifyRequestContext.DiscNumber}");
            }
            if (item.TryGetProperty("explicit", out JsonElement explicitElem))
            {
                spotifyRequestContext.IsExplicit = explicitElem.GetBoolean();
                SetParameterSafe(SpotiParameters.IsExplicit, explicitElem.GetBoolean());
                LogDebug($"Explicit: {spotifyRequestContext.IsExplicit}");
            }
            if (item.TryGetProperty("is_local", out JsonElement isLocal))
            {
                spotifyRequestContext.IsLocal = isLocal.GetBoolean();
                SetParameterSafe(SpotiParameters.IsLocal, isLocal.GetBoolean());
                LogDebug($"Is local: {spotifyRequestContext.IsLocal}");
            }
            if (item.TryGetProperty("popularity", out JsonElement popularity))
            {
                spotifyRequestContext.Popularity = popularity.GetInt32();
                SetParameterSafe(SpotiParameters.SongPopularity, popularity.GetInt32());
                LogDebug($"Popularity: {spotifyRequestContext.Popularity}");
            }
            if (item.TryGetProperty("preview_url", out JsonElement previewUrl))
            {
                spotifyRequestContext.PreviewUrl = previewUrl.GetString();                
                LogDebug($"Preview URL: {spotifyRequestContext.PreviewUrl}");
            }
            if (item.TryGetProperty("track_number", out JsonElement trackNumber))
            {
                spotifyRequestContext.TrackNumber = trackNumber.GetInt32();
                SetParameterSafe(SpotiParameters.TrackNumber, trackNumber.GetInt32());
                LogDebug($"Track number: {spotifyRequestContext.TrackNumber}");
            }
            if (item.TryGetProperty("uri", out JsonElement trackUri))
            {
                spotifyRequestContext.TrackUri = trackUri.GetString();                
                LogDebug($"Track URI: {spotifyRequestContext.TrackUri}");
            }
            if (item.TryGetProperty("currently_playing_type", out JsonElement playingType))
            {
                spotifyRequestContext.CurrentlyPlayingType = playingType.GetString();                
                LogDebug($"Currently playing type: {spotifyRequestContext.CurrentlyPlayingType}");
            }

            // --- Album details ---
            if (item.TryGetProperty("album", out JsonElement album))
            {
                spotifyRequestContext.AlbumName = album.GetProperty("name").GetString();
                LogDebug($"Album name: {spotifyRequestContext.AlbumName}");                
                if (album.TryGetProperty("images", out JsonElement images))
                {
                    var imageUrl = images.EnumerateArray().FirstOrDefault().GetProperty("url").GetString();
                    spotifyRequestContext.AlbumArtworkUrl = imageUrl;
                    LogDebug($"Album artwork URL: {imageUrl}");                    
                }
                if (album.TryGetProperty("album_type", out JsonElement albumType))
                {
                    spotifyRequestContext.AlbumType = albumType.GetString();
                    // Map "single" to 0, "album" to 1, otherwise 2.
                    string typeStr = albumType.GetString();
                    int albumTypeMapped = typeStr == "single" ? 0 : (typeStr == "album" ? 1 : 2);
                    SetParameterSafe(SpotiParameters.AlbumType, albumTypeMapped);
                    LogDebug($"Album type: {spotifyRequestContext.AlbumType}");                    
                }
                if (album.TryGetProperty("release_date", out JsonElement releaseDate))
                {
                    spotifyRequestContext.AlbumReleaseDate = releaseDate.GetString();
                    LogDebug($"Album release date: {spotifyRequestContext.AlbumReleaseDate}");                    
                }
                if (album.TryGetProperty("total_tracks", out JsonElement totalTracks))
                {
                    spotifyRequestContext.AlbumTotalTracks = totalTracks.GetInt32();
                    SetParameterSafe(SpotiParameters.AlbumTotalTracks, totalTracks.GetInt32());
                    LogDebug($"Album total tracks: {spotifyRequestContext.AlbumTotalTracks}");                    
                }
            }
            
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
                // Log context properties before making the call.
                LogDebug("SpotifyRequestContext.HttpClient is " + (spotifyRequestContext.HttpClient != null ? "initialized" : "null"));
                LogDebug("SpotifyRequestContext.AccessToken is " + (spotifyRequestContext.AccessToken != null ? "initialized" : "null"));
                LogDebug("SpotifyRequestContext.ClientToken is " + (spotifyRequestContext.ClientToken != null ? "initialized" : "null"));

                try
                {
                    bool jamCreated = await SpotifyJamRequests.CreateSpotifyJam(spotifyRequestContext, spotifyUtilities);
                    LogDebug("CreateSpotifyJam returned: " + jamCreated);
                }
                catch (Exception ex)
                {
                    Log("Exception while creating Spotify Jam: " + ex.ToString());
                }
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

        // -- Switching logic: choose the state based on several booleans.
        private void setState()
        {
            if (spotifyRequestContext.IsInJam)
            {
                if (spotifyRequestContext.IsPlaying)
                {
                    if (spotifyRequestContext.IsExplicit)
                    {
                        if (spotifyRequestContext.ShuffleState)
                            ChangeState("Playing_Jam_Explicit_Shuffle");
                        else
                            ChangeState("Playing_Jam_Explicit_NoShuffle");
                    }
                    else
                    {
                        if (spotifyRequestContext.ShuffleState)
                            ChangeState("Playing_Jam_Clean_Shuffle");
                        else
                            ChangeState("Playing_Jam_Clean_NoShuffle");
                    }
                }
                else
                {
                    ChangeState("Paused_Jam");
                }
            }
            else // not in a jam
            {
                if (spotifyRequestContext.IsPlaying)
                {
                    if (spotifyRequestContext.IsExplicit)
                    {
                        if (spotifyRequestContext.ShuffleState)
                            ChangeState("Playing_Explicit_Shuffle");
                        else
                            ChangeState("Playing_Explicit_NoShuffle");
                    }
                    else
                    {
                        if (spotifyRequestContext.ShuffleState)
                            ChangeState("Playing_Clean_Shuffle");
                        else
                            ChangeState("Playing_Clean_NoShuffle");
                    }
                }
                else
                {
                    ChangeState("Paused_Normal");
                }
            }
        }

        // -- ChatBoxUpdate: update all variables, then call setState() once.
        [ModuleUpdate(ModuleUpdateMode.ChatBox)]
        private void ChatBoxUpdate()
        {
            // Update device info
            SetVariableValue("DeviceId", spotifyRequestContext.DeviceId);
            SetVariableValue("DeviceName", spotifyRequestContext.DeviceName);
            SetVariableValue("IsActiveDevice", spotifyRequestContext.IsActiveDevice);
            SetVariableValue("VolumePercent", spotifyRequestContext.VolumePercent);

            // Update context info
            SetVariableValue("ContextExternalUrl", spotifyRequestContext.ContextExternalUrl);
            SetVariableValue("ContextHref", spotifyRequestContext.ContextHref);
            SetVariableValue("ContextType", spotifyRequestContext.ContextType);
            SetVariableValue("ContextUri", spotifyRequestContext.ContextUri);

            // Update media info
            SetVariableValue("TrackName", spotifyRequestContext.TrackName);
            SetVariableValue("TrackArtist", spotifyRequestContext.Artists.FirstOrDefault().Name ?? string.Empty);
            SetVariableValue("TrackDurationMs", spotifyRequestContext.TrackDurationMs);
            SetVariableValue("DiscNumber", spotifyRequestContext.DiscNumber);
            SetVariableValue("IsExplicit", spotifyRequestContext.IsExplicit);
            SetVariableValue("Popularity", spotifyRequestContext.Popularity);
            SetVariableValue("TrackNumber", spotifyRequestContext.TrackNumber);
            SetVariableValue("TrackUri", spotifyRequestContext.TrackUri);
            SetVariableValue("CurrentlyPlayingType", spotifyRequestContext.CurrentlyPlayingType);

            SetVariableValue("AlbumName", spotifyRequestContext.AlbumName);
            SetVariableValue("AlbumArtworkUrl", spotifyRequestContext.AlbumArtworkUrl);
            SetVariableValue("AlbumType", spotifyRequestContext.AlbumType);
            SetVariableValue("AlbumReleaseDate", spotifyRequestContext.AlbumReleaseDate);
            SetVariableValue("AlbumTotalTracks", spotifyRequestContext.AlbumTotalTracks);

            SetVariableValue("ShuffleState", spotifyRequestContext.ShuffleState);
            SetVariableValue("SmartShuffle", spotifyRequestContext.SmartShuffle);
            SetVariableValue("RepeatState", spotifyRequestContext.RepeatState);
            SetVariableValue("Timestamp", spotifyRequestContext.Timestamp.ToString());
            SetVariableValue("ProgressMs", spotifyRequestContext.ProgressMs);

            // Update artists info
            SetVariableValue("Artists", string.Join(", ", spotifyRequestContext.Artists.Select(a => a.Name)));

            // Update jam state
            SetVariableValue("InAJam", spotifyRequestContext.IsInJam);

            // Switch state (only one ChangeState call)
            setState();
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
                SendParameter(SpotiParameters.Error, true);                
            }
        }

    }
}