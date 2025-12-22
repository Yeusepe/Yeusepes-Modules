using System.Security;
using System.Security.Cryptography;
using System.Net;
using VRCOSC.App.SDK.Modules;
using YeusepesModules.SPOTIOSC.Credentials;
using System.Net.Http;
using VRCOSC.App.SDK.Parameters;
using YeusepesModules.SPOTIOSC.Utils;
using YeusepesModules.SPOTIOSC.UI;
using YeusepesModules.SPOTIOSC.Utils.Requests.Profiles;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using YeusepesModules.Common;
using YeusepesModules.SPOTIOSC.Utils.Events;
using System.Text.Json;
using System.Text;
using VRCOSC.App.Settings;
using SpotifyAPI.Web;
using Google.Protobuf;
using YeusepesModules.SPOTIOSC.Utils.Protobuf;



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
        private DealerWebSocket _dealerWebSocket;
        private SpotifyApiService _apiService;
        private ContentSettingsParser _contentSettingsParser;
        private VolumeUpdateParser _volumeUpdateParser;



        private bool isTouching = false;
        
        // Syncopation Server Communication
        private string _syncopationInstanceId;
        private string _melodyServerUrl = "https://melody.yucp.club/syncopation";        
        private HttpClient _syncopationHttpClient;
        private string _currentEphemeralWord1;
        private string _currentEphemeralWord2;
        private Dictionary<SpotiParameters, bool> _ephemeralWordValues = new Dictionary<SpotiParameters, bool>();
        private Dictionary<SpotiParameters, bool> _ephemeralWordReceiverValues = new Dictionary<SpotiParameters, bool>();
        private bool _isProcessingEphemeralJoin = false;
        private readonly object _ephemeralJoinLock = new object();
        private DateTime _lastEphemeralCheckTime = DateTime.MinValue;
        private readonly TimeSpan _ephemeralCheckThrottle = TimeSpan.FromSeconds(30);
        private System.Net.WebSockets.ClientWebSocket _notificationWebSocket;
        private System.Threading.CancellationTokenSource _notificationCancellationTokenSource;
        private System.Threading.Tasks.Task _notificationTask;

        private HashSet<System.Enum> _activeParameterUpdates = new HashSet<System.Enum>();

        private readonly HashSet<string> _processedEventKeys = new();
        private readonly object _deduplicationLock = new();

        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _getTrackFeaturesEnabled = false;
        
        // Continuous playback position tracking
        private System.Timers.Timer _positionUpdateTimer;
        private DateTime _lastPositionUpdateTime;
        private int _lastKnownPositionMs;
        private bool _isPlaying = false;
        private int _trackDurationMs = 0;

        public enum SpotiSettings
        {
            SignInButton,
            PopUpJam,
            MelodyServerUrl
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
            CurrentSong,
            CurrentPlaylist,
            Pause,
            NextTrack,
            PreviousTrack,


            // Playback state (from root level)
            ShuffleMode,            // mapped from shuffle_state + smart_shuffle: off=0, shuffle=1, smart_shuffle=2
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
            HostIsGroup,             // from session.host_device_info.is_group

            TrackChangedEvent,
            
            // URI Playback
            PlayUri,

            // Syncopation Ephemeral Word Parameters (Sending)
            Allegro,
            Cadence,
            Groove,
            Ritmo,
            Metronome,
            Encore,
            Chorus,

            // Syncopation Ephemeral Word Parameters (Receiving)
            AllegroReceiver,
            CadenceReceiver,
            GrooveReceiver,
            RitmoReceiver,
            MetronomeReceiver,
            EncoreReceiver,
            ChorusReceiver,

            // Audio Features
            GetTrackFeatures,
            Danceability,
            Energy,
            Key,
            Loudness,
            Mode,
            Speechiness,
            Acousticness,
            Instrumentalness,
            Liveness,
            Valence,
            Tempo,
            TimeSignature,

            // Album Color (RGB)
            AlbumColorR,
            AlbumColorG,
            AlbumColorB

        }
        private enum UiState
        {
            Playing_Jam_Explicit_Shuffle,
            Playing_Jam_Explicit_NoShuffle,
            Playing_Jam_Clean_Shuffle,
            Playing_Jam_Clean_NoShuffle,
            Paused_Jam,
            Playing_Explicit_Shuffle,
            Playing_Explicit_NoShuffle,
            Playing_Clean_Shuffle,
            Playing_Clean_NoShuffle,
            Paused_Normal
        }

        private UiState? _lastUiState = null;

        // Event debouncers
        private bool _lastIsPlaying = false;
        private string _lastTrackUri = string.Empty;
        private bool _lastShuffle = false;
        private string _lastRepeat = "off";
        private int _lastVolume = -1;

        // Track playing state for OSC endpoint
        private string _currentPlayingTrackUri = string.Empty;
        private bool _wasPlayingLastUpdate = false;
        
        // Playlist/context playing state for OSC endpoint
        private string _currentPlayingContextUri = string.Empty;
        private bool _wasPlayingFromContextLastUpdate = false;



        protected override void OnPreLoad()
        {
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("libusb-1.0.dll", message => Log(message));
            YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("cvextern.dll", message => Log(message));


            _syncopationHttpClient = new HttpClient();
            LogDebug($"[OnPreLoad] Initialized _syncopationHttpClient={(_syncopationHttpClient == null ? "NULL" : "SUCCESS")}");
            LogDebug($"[OnPreLoad] _melodyServerUrl={_melodyServerUrl}");
            
            // Initialize ephemeral word values dictionary (sending)
            _ephemeralWordValues[SpotiParameters.Allegro] = false;
            _ephemeralWordValues[SpotiParameters.Cadence] = false;
            _ephemeralWordValues[SpotiParameters.Groove] = false;
            _ephemeralWordValues[SpotiParameters.Ritmo] = false;
            _ephemeralWordValues[SpotiParameters.Metronome] = false;
            _ephemeralWordValues[SpotiParameters.Encore] = false;
            _ephemeralWordValues[SpotiParameters.Chorus] = false;

            // Initialize ephemeral word receiver values dictionary (receiving)
            _ephemeralWordReceiverValues[SpotiParameters.AllegroReceiver] = false;
            _ephemeralWordReceiverValues[SpotiParameters.CadenceReceiver] = false;
            _ephemeralWordReceiverValues[SpotiParameters.GrooveReceiver] = false;
            _ephemeralWordReceiverValues[SpotiParameters.RitmoReceiver] = false;
            _ephemeralWordReceiverValues[SpotiParameters.MetronomeReceiver] = false;
            _ephemeralWordReceiverValues[SpotiParameters.EncoreReceiver] = false;
            _ephemeralWordReceiverValues[SpotiParameters.ChorusReceiver] = false;

            spotifyUtilities = new SpotifyUtilities
            {
                Log = message => Log(message),
                LogDebug = message => LogDebug(message),
                SendParameter = (param, value) => SetParameterSafe(param, value),
            };
            
            // Store utilities in context so it can send parameters
            if (spotifyRequestContext != null)
            {
                spotifyRequestContext.Utilities = spotifyUtilities;
            }

            CredentialManager.SpotifyUtils = spotifyUtilities;

            #region Parameters


            RegisterParameter<bool>(SpotiParameters.Enabled, "SpotiOSC/Enabled", ParameterMode.Write, "Enabled", "Set to true if the module is enabled.");
            RegisterParameter<bool>(SpotiParameters.WantJam, "SpotiOSC/WantJam", ParameterMode.ReadWrite, "Want Jam", "Set to true if you want to join a jam.");
            RegisterParameter<bool>(SpotiParameters.InAJam, "SpotiOSC/InAJam", ParameterMode.Write, "In A Jam", "Set to true if you are in a jam.");
            RegisterParameter<bool>(SpotiParameters.IsJamOwner, "SpotiOSC/IsJamOwner", ParameterMode.Write, "Is Jam Owner", "Set to true if you are the owner of the jam.");
            RegisterParameter<bool>(SpotiParameters.Error, "SpotiOSC/Error", ParameterMode.Write, "Error", "Triggered when an error occurs.");
            RegisterParameter<bool>(SpotiParameters.Touching, "SpotiOSC/Touching", ParameterMode.ReadWrite, "Touching", "Set to true when two compatible devices tap eachother.");            
            RegisterParameter<bool>(
              SpotiParameters.Play,
              "SpotiOSC/Play/*",
              ParameterMode.ReadWrite,
              "Play [URI]",
              "Set to true to resume playback, or append /<spotify:uri> to play that URI. " +
              "To play a track within a playlist context, use format: /<playlist:uri>|<track:uri> or /<playlist:uri>|position:N"
            );

            RegisterParameter<bool>(
              SpotiParameters.CurrentSong,
              "SpotiOSC/CurrentSong/*",
              ParameterMode.Write,
              "Current Song [Track URI]",
              "Outputs true when a song with the given track URI is playing, false when it stops."
            );

            RegisterParameter<bool>(
              SpotiParameters.CurrentPlaylist,
              "SpotiOSC/CurrentPlaylist/*",
              ParameterMode.Write,
              "Current Playlist [Context URI]",
              "Outputs true when playing from a playlist/album/context with the given URI, false when it changes or stops."
            );

            RegisterParameter<bool>(SpotiParameters.Pause, "SpotiOSC/Pause", ParameterMode.ReadWrite, "Pause", "Pauses playback.");
            RegisterParameter<bool>(SpotiParameters.NextTrack, "SpotiOSC/NextTrack", ParameterMode.ReadWrite, "Next Track", "Skips to the next track.");
            RegisterParameter<bool>(SpotiParameters.PreviousTrack, "SpotiOSC/PreviousTrack", ParameterMode.ReadWrite, "Previous Track", "Skips to the previous track.");                        
            RegisterParameter<bool>(SpotiParameters.TrackChangedEvent, "SpotiOSC/TrackChangedEvent", ParameterMode.Write, "Track Changed Event", "Triggers when succesfully run a ChangedEvent.");                        
            RegisterParameter<bool>(
                SpotiParameters.PlayUri,
                "SpotiOSC/PlayUri/*",
                ParameterMode.ReadWrite,
                "Play URI (Local)",
                "Set to true with a Spotify URI to launch it locally through the system's URI handler (this is LOCAL and does NOT go thru the API). Supports all Spotify URI types: track, album, artist, playlist, episode, show, collection, genre, charts, search, radio, station, user, concert."
            );

            // Playback state (root)
            RegisterParameter<int>(SpotiParameters.ShuffleMode, "SpotiOSC/ShuffleMode", ParameterMode.ReadWrite, "Shuffle Mode (Mapped)", "Mapped shuffle state: off=0, shuffle=1, smart_shuffle=2.");
            RegisterParameter<int>(SpotiParameters.RepeatMode, "SpotiOSC/RepeatMode", ParameterMode.ReadWrite, "Repeat Mode (Mapped)", "Mapped repeat state: off=0, track=1, context=2.");
            RegisterParameter<float>(SpotiParameters.Timestamp, "SpotiOSC/Timestamp", ParameterMode.ReadWrite, "Timestamp", "Playback timestamp.");
            RegisterParameter<float>(SpotiParameters.PlaybackPosition, "SpotiOSC/PlaybackPosition", ParameterMode.ReadWrite, "Playback Progress (ms)", "Playback progress in ms.");
            RegisterParameter<bool>(SpotiParameters.IsPlaying, "SpotiOSC/IsPlaying", ParameterMode.Write, "Is Playing", "Whether playback is active.");

            // Device details (state.device)
            RegisterParameter<bool>(SpotiParameters.DeviceIsActive, "SpotiOSC/DeviceIsActive", ParameterMode.ReadWrite, "Device Active", "Device is active.");
            RegisterParameter<bool>(SpotiParameters.DeviceIsPrivate, "SpotiOSC/DeviceIsPrivate", ParameterMode.ReadWrite, "Private Session", "Device is in a private session.");
            RegisterParameter<bool>(SpotiParameters.DeviceIsRestricted, "SpotiOSC/DeviceIsRestricted", ParameterMode.ReadWrite, "Restricted Device", "Device is restricted.");
            RegisterParameter<bool>(SpotiParameters.DeviceSupportsVolume, "SpotiOSC/DeviceSupportsVolume", ParameterMode.ReadWrite, "Volume Support", "Device supports volume.");
            RegisterParameter<int>(SpotiParameters.DeviceVolumePercent, "SpotiOSC/Volume", ParameterMode.ReadWrite, "Device Volume (%)", "Set to 0-100 to change the playback volume.");

            // Context details (state.context)
            RegisterParameter<int>(SpotiParameters.ContextType, "SpotiOSC/ContextType", ParameterMode.Write, "Context Type (Mapped)", "Mapped context type (playlist=0, else -1).");

            // Track details (state.item)
            RegisterParameter<int>(SpotiParameters.DiscNumber, "SpotiOSC/DiscNumber", ParameterMode.Write, "Disc Number", "Track disc number.");
            RegisterParameter<float>(SpotiParameters.TrackDurationMs, "SpotiOSC/TrackDurationMs", ParameterMode.Write, "Track Duration (ms)", "Duration of the track in ms.");
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

            // Audio Features
            RegisterParameter<bool>(SpotiParameters.GetTrackFeatures, "SpotiOSC/GetTrackFeatures", ParameterMode.ReadWrite, "Get Track Features", "Enable to fetch audio features for the current track.");
            RegisterParameter<float>(SpotiParameters.Danceability, "SpotiOSC/Danceability", ParameterMode.Write, "Danceability", "How suitable a track is for dancing (0.0-1.0).");
            RegisterParameter<float>(SpotiParameters.Energy, "SpotiOSC/Energy", ParameterMode.Write, "Energy", "Perceptual measure of intensity and activity (0.0-1.0).");
            RegisterParameter<int>(SpotiParameters.Key, "SpotiOSC/Key", ParameterMode.Write, "Key", "Key the track is in (Pitch Class Notation, 0-11).");
            RegisterParameter<float>(SpotiParameters.Loudness, "SpotiOSC/Loudness", ParameterMode.Write, "Loudness", "Overall loudness in decibels (-60 to 0 dB).");
            RegisterParameter<int>(SpotiParameters.Mode, "SpotiOSC/Mode", ParameterMode.Write, "Mode", "Major (1) or minor (0) mode.");
            RegisterParameter<float>(SpotiParameters.Speechiness, "SpotiOSC/Speechiness", ParameterMode.Write, "Speechiness", "Presence of spoken words (0.0-1.0).");
            RegisterParameter<float>(SpotiParameters.Acousticness, "SpotiOSC/Acousticness", ParameterMode.Write, "Acousticness", "Confidence measure of acoustic recording (0.0-1.0).");
            RegisterParameter<float>(SpotiParameters.Instrumentalness, "SpotiOSC/Instrumentalness", ParameterMode.Write, "Instrumentalness", "Predicts whether track contains vocals (0.0-1.0).");
            RegisterParameter<float>(SpotiParameters.Liveness, "SpotiOSC/Liveness", ParameterMode.Write, "Liveness", "Detects presence of audience in recording (0.0-1.0).");
            RegisterParameter<float>(SpotiParameters.Valence, "SpotiOSC/Valence", ParameterMode.Write, "Valence", "Musical positiveness (mood indicator) (0.0-1.0).");
            RegisterParameter<float>(SpotiParameters.Tempo, "SpotiOSC/Tempo", ParameterMode.Write, "Tempo", "Overall estimated tempo in BPM.");
            RegisterParameter<int>(SpotiParameters.TimeSignature, "SpotiOSC/TimeSignature", ParameterMode.Write, "Time Signature", "Estimated time signature (3-7).");

            // Ephemeral word parameters (sending - for creating jam codes)
            RegisterParameter<bool>(SpotiParameters.Allegro, "SpotiOSC/allegro", ParameterMode.ReadWrite, "Allegro", "Ephemeral jam code word (sending).");
            RegisterParameter<bool>(SpotiParameters.Cadence, "SpotiOSC/cadence", ParameterMode.ReadWrite, "Cadence", "Ephemeral jam code word (sending).");
            RegisterParameter<bool>(SpotiParameters.Groove, "SpotiOSC/groove", ParameterMode.ReadWrite, "Groove", "Ephemeral jam code word (sending).");
            RegisterParameter<bool>(SpotiParameters.Ritmo, "SpotiOSC/ritmo", ParameterMode.ReadWrite, "Ritmo", "Ephemeral jam code word (sending).");
            RegisterParameter<bool>(SpotiParameters.Metronome, "SpotiOSC/metronome", ParameterMode.ReadWrite, "Metronome", "Ephemeral jam code word (sending).");
            RegisterParameter<bool>(SpotiParameters.Encore, "SpotiOSC/encore", ParameterMode.ReadWrite, "Encore", "Ephemeral jam code word (sending).");
            RegisterParameter<bool>(SpotiParameters.Chorus, "SpotiOSC/chorus", ParameterMode.ReadWrite, "Chorus", "Ephemeral jam code word (sending).");

            // Ephemeral word receiver parameters (receiving - for joining jams)
            RegisterParameter<bool>(SpotiParameters.AllegroReceiver, "SpotiOSC/allegro_receiver", ParameterMode.ReadWrite, "Allegro Receiver", "Ephemeral jam code word (receiving).");
            RegisterParameter<bool>(SpotiParameters.CadenceReceiver, "SpotiOSC/cadence_receiver", ParameterMode.ReadWrite, "Cadence Receiver", "Ephemeral jam code word (receiving).");
            RegisterParameter<bool>(SpotiParameters.GrooveReceiver, "SpotiOSC/groove_receiver", ParameterMode.ReadWrite, "Groove Receiver", "Ephemeral jam code word (receiving).");
            RegisterParameter<bool>(SpotiParameters.RitmoReceiver, "SpotiOSC/ritmo_receiver", ParameterMode.ReadWrite, "Ritmo Receiver", "Ephemeral jam code word (receiving).");
            RegisterParameter<bool>(SpotiParameters.MetronomeReceiver, "SpotiOSC/metronome_receiver", ParameterMode.ReadWrite, "Metronome Receiver", "Ephemeral jam code word (receiving).");
            RegisterParameter<bool>(SpotiParameters.EncoreReceiver, "SpotiOSC/encore_receiver", ParameterMode.ReadWrite, "Encore Receiver", "Ephemeral jam code word (receiving).");
            RegisterParameter<bool>(SpotiParameters.ChorusReceiver, "SpotiOSC/chorus_receiver", ParameterMode.ReadWrite, "Chorus Receiver", "Ephemeral jam code word (receiving).");

            // Album Color (RGB)
            RegisterParameter<float>(SpotiParameters.AlbumColorR, "SpotiOSC/AlbumColorR", ParameterMode.Write, "Album Color R", "Red component of the dominant album color (0-255).");
            RegisterParameter<float>(SpotiParameters.AlbumColorG, "SpotiOSC/AlbumColorG", ParameterMode.Write, "Album Color G", "Green component of the dominant album color (0-255).");
            RegisterParameter<float>(SpotiParameters.AlbumColorB, "SpotiOSC/AlbumColorB", ParameterMode.Write, "Album Color B", "Blue component of the dominant album color (0-255).");

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
            var jamShortCodeVar = CreateVariable<string>("JamShortCode", "Jam Short Code");
            var jamOwnerNameVar = CreateVariable<string>("JamOwnerName", "Jam Owner Name");
            var jamParticipantCountVar = CreateVariable<int>("JamParticipantCount", "Jam Participant Count");
            var jamMaxMemberCountVar = CreateVariable<int>("JamMaxMemberCount", "Jam Max Member Count");
            var sessionIsOwnerVar = CreateVariable<bool>("SessionIsOwner", "Session Is Owner");
            var sessionIsListeningVar = CreateVariable<bool>("SessionIsListening", "Session Is Listening");
            var sessionIsControllingVar = CreateVariable<bool>("SessionIsControlling", "Session Is Controlling");

            // --- Audio Features variables ---
            var danceabilityVar = CreateVariable<float>("Danceability", "Danceability");
            var energyVar = CreateVariable<float>("Energy", "Energy");
            var keyVar = CreateVariable<int>("Key", "Key");
            var loudnessVar = CreateVariable<float>("Loudness", "Loudness");
            var modeVar = CreateVariable<int>("Mode", "Mode");
            var speechinessVar = CreateVariable<float>("Speechiness", "Speechiness");
            var acousticnessVar = CreateVariable<float>("Acousticness", "Acousticness");
            var instrumentalnessVar = CreateVariable<float>("Instrumentalness", "Instrumentalness");
            var livenessVar = CreateVariable<float>("Liveness", "Liveness");
            var valenceVar = CreateVariable<float>("Valence", "Valence");
            var tempoVar = CreateVariable<float>("Tempo", "Tempo");
            var timeSignatureVar = CreateVariable<int>("TimeSignature", "Time Signature");

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
                SendParameter = (param, value) => SetParameterSafe(param, value)
            };
            CredentialManager.SpotifyUtils = spotifyUtilities;

            LogDebug("Starting Spotify Cookie Manager...");

            // Validate tokens and fetch profile
            bool isProfileFetched = await ValidateAndFetchProfileAsync();
            if (!isProfileFetched)
            {
                Log("Failed to validate tokens or fetch profile. Exiting.");
                return false;
            }

            LogDebug("Spotify Cookie Manager initialized successfully.");

            AccessToken = CredentialManager.AccessToken;
            ClientToken = CredentialManager.ClientToken;

            // Initialize SpotifyRequestContext
            await UseTokensSecurely(async (accessToken, clientToken) =>
            {
                spotifyRequestContext = new SpotifyRequestContext
                {
                    HttpClient = _httpClient,
                    AccessToken = accessToken,
                    ClientToken = clientToken,
                    Utilities = spotifyUtilities
                };

                return true;
            });

            // Initialize protobuf parsers
            _contentSettingsParser = new ContentSettingsParser(
                spotifyRequestContext,
                LogDebug,
                shuffleMode => SetParameterSafe(SpotiParameters.ShuffleMode, shuffleMode),
                shuffleState => { /* ShuffleState is updated in context, no separate parameter */ }
            );
            
            _volumeUpdateParser = new VolumeUpdateParser(
                spotifyRequestContext,
                LogDebug,
                volumePercent => SetParameterSafe(SpotiParameters.DeviceVolumePercent, volumePercent),
                TriggerEvent
            );

            _dealerWebSocket = new DealerWebSocket(spotifyRequestContext);
            _dealerWebSocket.OnMessageReceived += HandlePlayerEvent;

            LogDebug("Starting player event subscription...");
            await _dealerWebSocket.StartAsync();
            SendParameter(SpotiParameters.Enabled, true);            

            _apiService = new SpotifyApiService();

            await _apiService.InitializeAsync();

            SendParameter(SpotiParameters.InAJam, false);
            SendParameter(SpotiParameters.IsJamOwner, false);
            SendParameter(SpotiParameters.Error, false);
            SendParameter(SpotiParameters.GetTrackFeatures, false); // Initialize as disabled

            await RegisterWithSyncopationServerAsync();
            
            // Start WebSocket notification listener
            StartNotificationListener();
            
            // Initialize continuous position update timer
            InitializePositionUpdateTimer();
            
            return true;
        }

        protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
        {
            // Ensure we are processing only relevant parameters
            if (parameter.Lookup is not SpotiParameters param)
            {
                return;
            }
            
            
            if (IsEphemeralWordParameter(param))
            {
                bool value = parameter.GetValue<bool>();
                _ephemeralWordValues[param] = value;
                // Sender parameters don't trigger join detection
            }
            
            if (IsEphemeralWordReceiverParameter(param))
            {
                bool value = parameter.GetValue<bool>();
                _ephemeralWordReceiverValues[param] = value;
                HandleEphemeralWordParameter(param, value);
            }

            // Prevent handling changes that originated from within the code
            if (_activeParameterUpdates.Contains(param))
            {
                _activeParameterUpdates.Remove(param);
                LogDebug($"Ignored internal update for parameter: {param}");
                return;
            }

            async void Do(Func<SpotifyApiService, Task> work)
            {
                try 
                { 
                    await Task.Yield(); 
                    await work(_apiService); 
                }
                catch (Exception ex) 
                { 
                    LogDebug($"Spotify API error: {ex.Message}"); 
                }
            }

            if (parameter.Lookup is SpotiParameters p && p == SpotiParameters.Play && parameter.GetValue<bool>())
            {
                LogDebug($"Play parameter received. DeviceId: {spotifyRequestContext?.DeviceId ?? "null"}");
                
                // Check if we have a valid device ID
                if (string.IsNullOrEmpty(spotifyRequestContext?.DeviceId))
                {
                    LogDebug("Warning: No device ID available. Play command may not work properly.");
                    LogDebug("Make sure Spotify is open and a device is active.");
                }
                else
                {
                    LogDebug($"Using device: {spotifyRequestContext.DeviceName} (ID: {spotifyRequestContext.DeviceId})");
                    
                    // Check if resuming is disallowed on this device
                    if (spotifyRequestContext.DisallowResuming)
                    {
                        LogDebug("WARNING: This device has resuming disallowed. The play command may not work.");
                        LogDebug("Try using a different device or check your Spotify account restrictions.");
                    }
                }
                
                // is there something in that * ?
                if (parameter.IsWildcardType<string>(0)
                    && !string.IsNullOrEmpty(parameter.GetWildcard<string>(0)))
                {
                    var uri = parameter.GetWildcard<string>(0);
                    LogDebug($"Playing URI: {uri}");
                    
                    // Check for combined format: playlist:ID|track:ID or playlist:ID|position:5
                    if (uri.Contains("|"))
                    {
                        var parts = uri.Split('|');
                        if (parts.Length == 2)
                        {
                            var contextUri = parts[0].Trim();
                            var offsetPart = parts[1].Trim();
                            
                            // fire-and-forget
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    // Check if offset is a track URI or position
                                    if (offsetPart.StartsWith("spotify:track:") || offsetPart.StartsWith("track:"))
                                    {
                                        var trackUri = offsetPart.StartsWith("track:") 
                                            ? "spotify:" + offsetPart 
                                            : offsetPart;
                                        
                                        // Find the track's position in the playlist and use that instead of the URI
                                        // This ensures the playlist queues correctly from that position
                                        var playlistId = ExtractPlaylistId(contextUri);
                                        if (!string.IsNullOrEmpty(playlistId))
                                        {
                                            LogDebug($"Finding track position in playlist for {trackUri}...");
                                            var trackPosition = await _apiService.FindTrackPositionInPlaylistAsync(playlistId, trackUri);
                                            if (trackPosition.HasValue)
                                            {
                                                LogDebug($"Found track at position {trackPosition.Value}, playing playlist from that position...");
                                                await _apiService.PlayUriWithOffsetAsync(
                                                    contextUri, 
                                                    offsetPosition: trackPosition.Value, 
                                                    deviceId: spotifyRequestContext.DeviceId
                                                );
                                                LogDebug($"Successfully started playing playlist with track position offset");
                                            }
                                            else
                                            {
                                                LogDebug($"Track not found in playlist, falling back to track URI offset");
                                                await _apiService.PlayUriWithOffsetAsync(
                                                    contextUri, 
                                                    offsetTrackUri: trackUri, 
                                                    deviceId: spotifyRequestContext.DeviceId
                                                );
                                            }
                                        }
                                        else
                                        {
                                            LogDebug($"Could not extract playlist ID, using track URI as offset");
                                            await _apiService.PlayUriWithOffsetAsync(
                                                contextUri, 
                                                offsetTrackUri: trackUri, 
                                                deviceId: spotifyRequestContext.DeviceId
                                            );
                                        }
                                    }
                                    else if (offsetPart.StartsWith("position:") && int.TryParse(offsetPart.Replace("position:", ""), out int position))
                                    {
                                        LogDebug($"Playing playlist {contextUri} starting from position {position}");
                                        await _apiService.PlayUriWithOffsetAsync(
                                            contextUri, 
                                            offsetPosition: position, 
                                            deviceId: spotifyRequestContext.DeviceId
                                        );
                                        LogDebug($"Successfully started playing playlist with position offset");
                                    }
                                    else
                                    {
                                        // Try to find track position in playlist
                                        var playlistId = ExtractPlaylistId(contextUri);
                                        if (!string.IsNullOrEmpty(playlistId) && offsetPart.StartsWith("spotify:track:"))
                                        {
                                            LogDebug($"Finding track position in playlist...");
                                            var trackPosition = await _apiService.FindTrackPositionInPlaylistAsync(playlistId, offsetPart);
                                            if (trackPosition.HasValue)
                                            {
                                                LogDebug($"Found track at position {trackPosition.Value}, playing playlist...");
                                                await _apiService.PlayUriWithOffsetAsync(
                                                    contextUri, 
                                                    offsetPosition: trackPosition.Value, 
                                                    deviceId: spotifyRequestContext.DeviceId
                                                );
                                            }
                                            else
                                            {
                                                LogDebug($"Track not found in playlist, using track URI as offset");
                                                await _apiService.PlayUriWithOffsetAsync(
                                                    contextUri, 
                                                    offsetTrackUri: offsetPart, 
                                                    deviceId: spotifyRequestContext.DeviceId
                                                );
                                            }
                                        }
                                        else
                                        {
                                            throw new Exception($"Invalid offset format. Use 'spotify:track:ID', 'track:ID', or 'position:N'");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogDebug($"Error playing URI with offset {uri}: {ex.Message}");
                                    SendParameter(SpotiParameters.Error, true);
                                    await Task.Delay(100);
                                    SendParameter(SpotiParameters.Error, false);
                                }
                            });
                        }
                        else
                        {
                            LogDebug($"Invalid combined URI format. Expected: 'playlist:ID|track:ID' or 'playlist:ID|position:N'");
                            SendParameter(SpotiParameters.Error, true);
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(100);
                                SendParameter(SpotiParameters.Error, false);
                            });
                        }
                    }
                    else
                    {
                        // Original behavior: play single URI
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _apiService.PlayUriAsync(uri, spotifyRequestContext.DeviceId);
                                LogDebug($"Successfully started playing URI: {uri}");
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"Error playing URI {uri}: {ex.Message}");
                                SendParameter(SpotiParameters.Error, true);
                                await Task.Delay(100);
                                SendParameter(SpotiParameters.Error, false);
                            }
                        });
                    }
                }
                else
                {
                    LogDebug("Resuming current playback");
                    // no URI ⇒ just resume current playback
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _apiService.PlayAsync(spotifyRequestContext.DeviceId);
                            LogDebug("Successfully resumed playback");
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Error resuming playback: {ex.Message}");
                            if (ex.Message.Contains("Device not found"))
                            {
                                LogDebug("Device not found - this usually means the device went offline. Try opening Spotify on your computer or another device.");
                            }
                            else if (ex.Message.Contains("Melody API"))
                            {
                                LogDebug("Tried melody API fallback but it also failed. This might be a device restriction issue.");
                            }
                            SendParameter(SpotiParameters.Error, true);
                            // Reset error after delay
                            await Task.Delay(100);
                            SendParameter(SpotiParameters.Error, false);
                        }
                    });
                }
            }

            if (parameter.Lookup is SpotiParameters p2 && p2 == SpotiParameters.PlayUri && parameter.GetValue<bool>())
            {
                // Check if there's a URI in the wildcard
                if (parameter.IsWildcardType<string>(0) && !string.IsNullOrEmpty(parameter.GetWildcard<string>(0)))
                {
                    var uri = parameter.GetWildcard<string>(0);
                    
                    // Validate that it's a Spotify URI
                    if (uri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
                    {
                        LogDebug($"Playing Spotify URI locally: {uri}");
                        try
                        {
                            // Use Process.Start to launch the Spotify URI through the system's URI handler
                            // This is equivalent to running "start spotify:playlist:..." in the terminal
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = uri,
                                UseShellExecute = true
                            });
                            LogDebug($"Successfully launched Spotify URI: {uri}");
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"Error launching Spotify URI: {ex.Message}");
                            SendParameter(SpotiParameters.Error, true);
                            
                            // Reset the Error trigger after a short delay to allow for one-shot behavior
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(100); // Small delay to ensure the trigger is processed
                                SendParameter(SpotiParameters.Error, false);
                            });
                        }
                    }
                    else
                    {
                        LogDebug($"Invalid Spotify URI format: {uri}. URI must start with 'spotify:'");
                        SendParameter(SpotiParameters.Error, true);
                        
                        // Reset the Error trigger after a short delay to allow for one-shot behavior
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(100); // Small delay to ensure the trigger is processed
                            SendParameter(SpotiParameters.Error, false);
                        });
                    }
                }
                else
                {
                    LogDebug("PlayUri parameter received but no URI provided in wildcard");
                    SendParameter(SpotiParameters.Error, true);
                    
                    // Reset the Error trigger after a short delay to allow for one-shot behavior
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100); // Small delay to ensure the trigger is processed
                        SendParameter(SpotiParameters.Error, false);
                    });
                }
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

                case SpotiParameters.Pause when parameter.GetValue<bool>():
                    Do(async svc => await svc.PauseAsync(spotifyRequestContext.DeviceId));
                    break;

                case SpotiParameters.NextTrack when parameter.GetValue<bool>():
                    Do(async svc => await svc.NextTrackAsync());
                    break;

                case SpotiParameters.PreviousTrack when parameter.GetValue<bool>():
                    Do(async svc => await svc.PreviousTrackAsync());
                    break;
                case SpotiParameters.RepeatMode
                  when parameter.GetValue<int>() is var mode:
                    {
                        LogDebug($"Setting repeat mode to: {mode}");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await SetRepeatModeAsync(mode);
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"Error setting repeat mode: {ex.Message}");
                            }
                        });
                    }
                    break;

                case SpotiParameters.ShuffleMode
                  when parameter.GetValue<int>() is var shuffleMode:
                    {
                        LogDebug($"Setting shuffle mode to: {shuffleMode}");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await SetShuffleModeAsync(shuffleMode);
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"Error setting shuffle mode: {ex.Message}");
                            }
                        });
                    }
                    break;

                case SpotiParameters.DeviceVolumePercent
                    when parameter.GetValue<int>() is var vol && vol is >= 0 and <= 100:
                    Do(svc => svc.SetVolumeAsync(vol, spotifyRequestContext.DeviceId));
                    break;

                case SpotiParameters.GetTrackFeatures:
                    bool isEnabled = parameter.GetValue<bool>();
                    _getTrackFeaturesEnabled = isEnabled;
                    LogDebug($"GetTrackFeatures set to: {isEnabled}");
                    
                    if (isEnabled)
                    {
                        LogDebug("GetTrackFeatures enabled - fetching audio features for current track");
                        // Trigger audio features fetch for current track if there's one playing
                        if (spotifyRequestContext != null && !string.IsNullOrEmpty(spotifyRequestContext.TrackUri))
                        {
                            // Extract track ID from URI (format: spotify:track:ID)
                            if (spotifyRequestContext.TrackUri.StartsWith("spotify:track:"))
                            {
                                string trackId = spotifyRequestContext.TrackUri.Replace("spotify:track:", "");
                                _ = FetchAudioFeaturesForTrackId(trackId);
                            }
                        }
                    }
                    break;

            }
        }

        private async Task RegisterWithSyncopationServerAsync()
        {
            LogDebug($"[RegisterWithSyncopationServerAsync] Called - _melodyServerUrl={_melodyServerUrl}");
            try
            {
                if (string.IsNullOrEmpty(_melodyServerUrl) || _melodyServerUrl.Contains("your-melody-server"))
                {
                    LogDebug("Melody Server URL not configured. Syncopation features will be disabled.");
                    return;
                }

                LogDebug($"[RegisterWithSyncopationServerAsync] Sending registration request to {_melodyServerUrl}/register");
                LogDebug($"[RegisterWithSyncopationServerAsync] _syncopationHttpClient={(_syncopationHttpClient == null ? "NULL" : "INITIALIZED")}");
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_melodyServerUrl}/register");
                var response = await _syncopationHttpClient.SendAsync(request);
                
                LogDebug($"[RegisterWithSyncopationServerAsync] Response status: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    LogDebug($"[RegisterWithSyncopationServerAsync] Response content: {content}");
                    
                    var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
                    if (result.TryGetProperty("instance_id", out var instanceIdElement))
                    {
                        _syncopationInstanceId = instanceIdElement.GetString();
                        LogDebug($"[RegisterWithSyncopationServerAsync] Registered with syncopation server. Instance ID: {_syncopationInstanceId}");
                    }
                    else
                    {
                        LogDebug("[RegisterWithSyncopationServerAsync] Response does not contain instance_id property");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogDebug($"[RegisterWithSyncopationServerAsync] Failed to register with syncopation server: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"[RegisterWithSyncopationServerAsync] Exception: {ex.GetType().Name} - {ex.Message}");
                LogDebug($"[RegisterWithSyncopationServerAsync] Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    LogDebug($"[RegisterWithSyncopationServerAsync] Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                }
            }
        }

        private async Task DeregisterFromSyncopationServerAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_syncopationInstanceId) || string.IsNullOrEmpty(_melodyServerUrl) || _melodyServerUrl.Contains("your-melody-server"))
                {
                    return;
                }

                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_melodyServerUrl}/register/{_syncopationInstanceId}");
                var response = await _syncopationHttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    LogDebug($"Deregistered from syncopation server. Instance ID: {_syncopationInstanceId}");
                }
                else
                {
                    LogDebug($"Failed to deregister from syncopation server: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error deregistering from syncopation server: {ex.Message}");
            }
        }

        private async Task<(string word1, string word2)?> CreateSyncopationJamAsync(string sessionId)
        {
            LogDebug($"[CreateSyncopationJamAsync] Called with sessionId={(string.IsNullOrEmpty(sessionId) ? "NULL/EMPTY" : sessionId)}");
            
            try
            {
                LogDebug($"[CreateSyncopationJamAsync] Checking _syncopationInstanceId={(string.IsNullOrEmpty(_syncopationInstanceId) ? "NULL/EMPTY" : _syncopationInstanceId)}");
                LogDebug($"[CreateSyncopationJamAsync] Checking _syncopationHttpClient={(_syncopationHttpClient == null ? "NULL" : "INITIALIZED")}");
                LogDebug($"[CreateSyncopationJamAsync] Checking _melodyServerUrl={(string.IsNullOrEmpty(_melodyServerUrl) ? "NULL/EMPTY" : _melodyServerUrl)}");
                
                if (string.IsNullOrEmpty(_syncopationInstanceId))
                {
                    LogDebug("[CreateSyncopationJamAsync] Not registered with syncopation server. Cannot create jam code.");
                    return null;
                }

                if (_syncopationHttpClient == null)
                {
                    LogDebug("[CreateSyncopationJamAsync] _syncopationHttpClient is NULL - this should not happen!");
                    return null;
                }

                if (string.IsNullOrEmpty(_melodyServerUrl))
                {
                    LogDebug("[CreateSyncopationJamAsync] _melodyServerUrl is NULL or EMPTY!");
                    return null;
                }

                var payload = new
                {
                    instance_id = _syncopationInstanceId,
                    session_id = sessionId
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                LogDebug($"[CreateSyncopationJamAsync] Payload JSON: {json}");
                
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                var requestUrl = $"{_melodyServerUrl}/jam/create";
                LogDebug($"[CreateSyncopationJamAsync] Request URL: {requestUrl}");
                
                var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
                {
                    Content = content
                };

                LogDebug("[CreateSyncopationJamAsync] Sending HTTP request...");
                var response = await _syncopationHttpClient.SendAsync(request);
                LogDebug($"[CreateSyncopationJamAsync] HTTP response status: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    LogDebug($"[CreateSyncopationJamAsync] Response content: {responseContent}");
                    
                    var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseContent);
                    
                    if (result.TryGetProperty("word1", out var word1Element) && 
                        result.TryGetProperty("word2", out var word2Element))
                    {
                        string word1 = word1Element.GetString();
                        string word2 = word2Element.GetString();
                        LogDebug($"[CreateSyncopationJamAsync] Created ephemeral code: {word1} {word2}");
                        return (word1, word2);
                    }
                    else
                    {
                        LogDebug("[CreateSyncopationJamAsync] Response does not contain word1 and/or word2 properties");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    LogDebug($"[CreateSyncopationJamAsync] Failed to create syncopation jam: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"[CreateSyncopationJamAsync] Exception: {ex.GetType().Name} - {ex.Message}");
                LogDebug($"[CreateSyncopationJamAsync] Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    LogDebug($"[CreateSyncopationJamAsync] Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                }
            }
            return null;
        }

        private async Task<string> JoinSyncopationJamAsync(string key)
        {
            try
            {
                if (_syncopationHttpClient == null)
                {
                    return null;
                }

                var url = $"{_melodyServerUrl}/jam/join?key={Uri.EscapeDataString(key)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                var response = await _syncopationHttpClient.SendAsync(request);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content);
                    
                    if (result.TryGetProperty("session_id", out var sessionIdElement))
                    {
                        return sessionIdElement.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error joining syncopation jam: {ex.Message}");
            }
            return null;
        }

        private void StartNotificationListener()
        {
            if (string.IsNullOrEmpty(_syncopationInstanceId) || string.IsNullOrEmpty(_melodyServerUrl) || _melodyServerUrl.Contains("your-melody-server"))
            {
                return;
            }

            _notificationCancellationTokenSource = new System.Threading.CancellationTokenSource();
            _notificationTask = System.Threading.Tasks.Task.Run(async () =>
            {
                await ListenForNotifications(_notificationCancellationTokenSource.Token);
            });
        }

        private void StopNotificationListener()
        {
            if (_notificationCancellationTokenSource != null)
            {
                _notificationCancellationTokenSource.Cancel();
                _notificationCancellationTokenSource.Dispose();
                _notificationCancellationTokenSource = null;
            }

            if (_notificationWebSocket != null)
            {
                try
                {
                    if (_notificationWebSocket.State == System.Net.WebSockets.WebSocketState.Open)
                    {
                        _notificationWebSocket.CloseAsync(
                            System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                            "Module stopping",
                            System.Threading.CancellationToken.None).Wait(TimeSpan.FromSeconds(2));
                    }
                }
                catch { }
                _notificationWebSocket?.Dispose();
                _notificationWebSocket = null;
            }
            
            if (_notificationTask != null)
            {
                try
                {
                    _notificationTask.Wait(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    // Task may have already completed or been cancelled
                }
                _notificationTask = null;
            }
        }

        private async System.Threading.Tasks.Task ListenForNotifications(System.Threading.CancellationToken cancellationToken)
        {
            int reconnectAttempts = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (string.IsNullOrEmpty(_syncopationInstanceId))
                    {
                        await System.Threading.Tasks.Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    var wsUrl = _melodyServerUrl.Replace("https://", "wss://").Replace("http://", "ws://");
                    var uri = new System.Uri($"{wsUrl}/notify/{_syncopationInstanceId}");

                    _notificationWebSocket = new System.Net.WebSockets.ClientWebSocket();
                    await _notificationWebSocket.ConnectAsync(uri, cancellationToken);

                    LogDebug("Connected to syncopation notification WebSocket");
                    reconnectAttempts = 0; // Reset on successful connection

                    var buffer = new byte[1024 * 4];
                    while (_notificationWebSocket.State == System.Net.WebSockets.WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            // Use cancellation token with timeout to prevent hanging
                            using (var timeoutCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            {
                                timeoutCts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout
                                
                                var result = await _notificationWebSocket.ReceiveAsync(
                                    new System.ArraySegment<byte>(buffer),
                                    timeoutCts.Token);

                                if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
                                {
                                    var message = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                                    var jsonDoc = System.Text.Json.JsonDocument.Parse(message);
                                    
                                    if (jsonDoc.RootElement.TryGetProperty("type", out var typeElement))
                                    {
                                        string type = typeElement.GetString();
                                        
                                        if (type == "jam_joined")
                                        {
                                            LogDebug("Received notification: Someone joined the jam. Stopping parameter broadcast.");
                                            
                                            // Stop sending word parameters
                                            if (!string.IsNullOrEmpty(_currentEphemeralWord1) && !string.IsNullOrEmpty(_currentEphemeralWord2))
                                            {
                                                _activeParameterUpdates.Add(GetParameterFromWordName(_currentEphemeralWord1));
                                                _activeParameterUpdates.Add(GetParameterFromWordName(_currentEphemeralWord2));
                                                
                                                SetParameterSafe(GetParameterFromWordName(_currentEphemeralWord1), false);
                                                SetParameterSafe(GetParameterFromWordName(_currentEphemeralWord2), false);
                                                
                                                _currentEphemeralWord1 = null;
                                                _currentEphemeralWord2 = null;
                                            }
                                        }
                                        else if (type == "ping")
                                        {
                                            // Server ping, respond with pong
                                            var pongMessage = System.Text.Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                                            await _notificationWebSocket.SendAsync(
                                                new System.ArraySegment<byte>(pongMessage),
                                                System.Net.WebSockets.WebSocketMessageType.Text,
                                                true,
                                                cancellationToken);
                                        }
                                        else if (type == "pong")
                                        {
                                            // Keep-alive response
                                        }
                                    }
                                }
                                else if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                                {
                                    break;
                                }
                            }
                        }
                        catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            // Timeout occurred, but connection is still alive - continue waiting
                            continue;
                        }
                    }
                }
                catch (System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogDebug($"WebSocket error: {ex.Message}");
                    
                    // Clean up the broken connection
                    if (_notificationWebSocket != null)
                    {
                        try
                        {
                            if (_notificationWebSocket.State == System.Net.WebSockets.WebSocketState.Open)
                            {
                                await _notificationWebSocket.CloseAsync(
                                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                                    "Reconnecting",
                                    System.Threading.CancellationToken.None);
                            }
                            _notificationWebSocket.Dispose();
                        }
                        catch { }
                        _notificationWebSocket = null;
                    }
                    
                    // Exponential backoff for reconnection (max 30 seconds)
                    reconnectAttempts++;
                    int delaySeconds = Math.Min(30, (int)Math.Pow(2, Math.Min(reconnectAttempts - 1, 4))); // 1s, 2s, 4s, 8s, 16s, then 30s max
                    if (delaySeconds < 1) delaySeconds = 1;
                    LogDebug($"WebSocket reconnecting in {delaySeconds} seconds (attempt {reconnectAttempts})");
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
            }
        }

        private bool IsEphemeralWordParameter(SpotiParameters param)
        {
            return param == SpotiParameters.Allegro || param == SpotiParameters.Cadence ||
                   param == SpotiParameters.Groove || param == SpotiParameters.Ritmo ||
                   param == SpotiParameters.Metronome || param == SpotiParameters.Encore ||
                   param == SpotiParameters.Chorus;
        }

        private bool IsEphemeralWordReceiverParameter(SpotiParameters param)
        {
            return param == SpotiParameters.AllegroReceiver || param == SpotiParameters.CadenceReceiver ||
                   param == SpotiParameters.GrooveReceiver || param == SpotiParameters.RitmoReceiver ||
                   param == SpotiParameters.MetronomeReceiver || param == SpotiParameters.EncoreReceiver ||
                   param == SpotiParameters.ChorusReceiver;
        }

        private string GetWordNameFromParameter(SpotiParameters param)
        {
            return param switch
            {
                SpotiParameters.Allegro => "allegro",
                SpotiParameters.Cadence => "cadence",
                SpotiParameters.Groove => "groove",
                SpotiParameters.Ritmo => "ritmo",
                SpotiParameters.Metronome => "metronome",
                SpotiParameters.Encore => "encore",
                SpotiParameters.Chorus => "chorus",
                SpotiParameters.AllegroReceiver => "allegro",
                SpotiParameters.CadenceReceiver => "cadence",
                SpotiParameters.GrooveReceiver => "groove",
                SpotiParameters.RitmoReceiver => "ritmo",
                SpotiParameters.MetronomeReceiver => "metronome",
                SpotiParameters.EncoreReceiver => "encore",
                SpotiParameters.ChorusReceiver => "chorus",
                _ => null
            };
        }

        private SpotiParameters GetParameterFromWordName(string wordName)
        {
            return wordName.ToLower() switch
            {
                "allegro" => SpotiParameters.Allegro,
                "cadence" => SpotiParameters.Cadence,
                "groove" => SpotiParameters.Groove,
                "ritmo" => SpotiParameters.Ritmo,
                "metronome" => SpotiParameters.Metronome,
                "encore" => SpotiParameters.Encore,
                "chorus" => SpotiParameters.Chorus,
                _ => throw new ArgumentException($"Unknown word name: {wordName}")
            };
        }

        private SpotiParameters GetReceiverParameterFromWordName(string wordName)
        {
            return wordName.ToLower() switch
            {
                "allegro" => SpotiParameters.AllegroReceiver,
                "cadence" => SpotiParameters.CadenceReceiver,
                "groove" => SpotiParameters.GrooveReceiver,
                "ritmo" => SpotiParameters.RitmoReceiver,
                "metronome" => SpotiParameters.MetronomeReceiver,
                "encore" => SpotiParameters.EncoreReceiver,
                "chorus" => SpotiParameters.ChorusReceiver,
                _ => throw new ArgumentException($"Unknown word name: {wordName}")
            };
        }

        private async void HandleEphemeralWordParameter(SpotiParameters param, bool value)
        {
            LogDebug($"[HandleEphemeralWordParameter] Called with param={param}, value={value}");
            if (spotifyRequestContext?.IsInJam == true)
            {
                LogDebug("[HandleEphemeralWordParameter] Already in jam, skipping");
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // Throttle checks to once every 30 seconds
                    lock (_ephemeralJoinLock)
                    {
                        if (DateTime.Now - _lastEphemeralCheckTime < _ephemeralCheckThrottle)
                        {
                            return;
                        }
                        if (_isProcessingEphemeralJoin)
                        {
                            return;
                        }
                        _lastEphemeralCheckTime = DateTime.Now;
                    }
                    
                    await Task.Delay(50);
                    
                    lock (_ephemeralJoinLock)
                    {
                        if (_isProcessingEphemeralJoin)
                        {
                            return;
                        }
                    }
                    
                    // Check receiver parameters for joining jams
                    var wordReceiverParams = new[]
                    {
                        (SpotiParameters.AllegroReceiver, "allegro"),
                        (SpotiParameters.CadenceReceiver, "cadence"),
                        (SpotiParameters.GrooveReceiver, "groove"),
                        (SpotiParameters.RitmoReceiver, "ritmo"),
                        (SpotiParameters.MetronomeReceiver, "metronome"),
                        (SpotiParameters.EncoreReceiver, "encore"),
                        (SpotiParameters.ChorusReceiver, "chorus")
                    };

                    var activeWords = new List<string>();
                    foreach (var (paramEnum, wordName) in wordReceiverParams)
                    {
                        try
                        {
                            bool isActive = _ephemeralWordReceiverValues.TryGetValue(paramEnum, out bool val) && val;
                            if (isActive)
                            {
                                activeWords.Add(wordName);
                                LogDebug($"[HandleEphemeralWordParameter] Active receiver word detected: {wordName}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogDebug($"[HandleEphemeralWordParameter] Error checking {wordName}: {ex.Message}");
                        }
                    }

                    LogDebug($"[HandleEphemeralWordParameter] Active words count: {activeWords.Count}, words: [{string.Join(", ", activeWords)}]");

                    if (activeWords.Count == 2 && spotifyRequestContext?.IsInJam != true)
                    {
                        lock (_ephemeralJoinLock)
                        {
                            if (_isProcessingEphemeralJoin)
                            {
                                return;
                            }
                            _isProcessingEphemeralJoin = true;
                        }
                        
                        try
                        {
                            var sortedWords = activeWords.OrderBy(w => w).ToList();
                            string key = $"{sortedWords[0]}_{sortedWords[1]}";
                            LogDebug($"[HandleEphemeralWordParameter] Attempting to join jam with key: {key}");
                            
                            string sessionId = await JoinSyncopationJamAsync(key);
                            LogDebug($"[HandleEphemeralWordParameter] JoinSyncopationJamAsync returned sessionId: {(string.IsNullOrEmpty(sessionId) ? "NULL/EMPTY" : sessionId)}");
                        if (!string.IsNullOrEmpty(sessionId))
                        {
                            // Reset all receiver word parameters
                            _activeParameterUpdates.Add(SpotiParameters.AllegroReceiver);
                            _activeParameterUpdates.Add(SpotiParameters.CadenceReceiver);
                            _activeParameterUpdates.Add(SpotiParameters.GrooveReceiver);
                            _activeParameterUpdates.Add(SpotiParameters.RitmoReceiver);
                            _activeParameterUpdates.Add(SpotiParameters.MetronomeReceiver);
                            _activeParameterUpdates.Add(SpotiParameters.EncoreReceiver);
                            _activeParameterUpdates.Add(SpotiParameters.ChorusReceiver);
                            
                            SetParameterSafe(SpotiParameters.AllegroReceiver, false);
                            SetParameterSafe(SpotiParameters.CadenceReceiver, false);
                            SetParameterSafe(SpotiParameters.GrooveReceiver, false);
                            SetParameterSafe(SpotiParameters.RitmoReceiver, false);
                            SetParameterSafe(SpotiParameters.MetronomeReceiver, false);
                            SetParameterSafe(SpotiParameters.EncoreReceiver, false);
                            SetParameterSafe(SpotiParameters.ChorusReceiver, false);
                            
                            bool joinResult = await SpotifyJamRequests.JoinSpotifyJam(sessionId, spotifyRequestContext, spotifyUtilities);
                            if (!joinResult)
                            {
                                SendParameter(SpotiParameters.Error, true);
                                await Task.Delay(100);
                                SendParameter(SpotiParameters.Error, false);
                            }
                        }
                        else
                        {
                            SendParameter(SpotiParameters.Error, true);
                            await Task.Delay(100);
                            SendParameter(SpotiParameters.Error, false);
                        }
                        }
                        finally
                        {
                            lock (_ephemeralJoinLock)
                            {
                                _isProcessingEphemeralJoin = false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error handling ephemeral word parameter: {ex.Message}");
                    lock (_ephemeralJoinLock)
                    {
                        _isProcessingEphemeralJoin = false;
                    }
                }
            });
        }

        protected override async Task OnModuleStop()
        {
            LogDebug("Stopping SpotiOSC module...");
            _cts.Cancel(); // Cancel all ongoing operations

            try
            {
                // Stop dealer WebSocket subscription if active
                if (_dealerWebSocket != null)
                {
                    LogDebug("Stopping Dealer WebSocket subscription...");
                    await _dealerWebSocket.StopAsync();
                    _dealerWebSocket.OnMessageReceived -= HandlePlayerEvent;
                    _dealerWebSocket = null;
                }

                // Stop continuous position update timer
                LogDebug("Stopping position update timer...");
                StopPositionUpdateTimer();

                StopNotificationListener();
                
                await DeregisterFromSyncopationServerAsync();

                if (_syncopationHttpClient != null)
                {
                    _syncopationHttpClient.Dispose();
                    _syncopationHttpClient = null;
                }

                // Clear active parameter updates
                _activeParameterUpdates.Clear();

                // Clean up processed event keys
                lock (_deduplicationLock)
                {
                    _processedEventKeys.Clear();
                }

                // Send false to current playing track on module stop
                if (!string.IsNullOrEmpty(_currentPlayingTrackUri) && _wasPlayingLastUpdate)
                {
                    SendCurrentSongParameter(_currentPlayingTrackUri, false);
                    _currentPlayingTrackUri = string.Empty;
                    _wasPlayingLastUpdate = false;
                }

                // Send false to current playing context on module stop
                if (!string.IsNullOrEmpty(_currentPlayingContextUri) && _wasPlayingFromContextLastUpdate)
                {
                    SendCurrentPlaylistParameter(_currentPlayingContextUri, false);
                    _currentPlayingContextUri = string.Empty;
                    _wasPlayingFromContextLastUpdate = false;
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

                LogDebug("SpotiOSC module stopped successfully.");
                SendParameter(SpotiParameters.Enabled, false);
            }
            catch (Exception ex)
            {
                LogDebug($"Error during module stop: {ex.Message}");
            }
            finally
            {
                _cts.Dispose();
            }
        }


        private async Task<bool> ValidateAndFetchProfileAsync()
        {
            try
            {
                LogDebug("Validating tokens and fetching profile data...");

                string accessToken = CredentialManager.LoadAccessToken();
                string clientToken = CredentialManager.LoadClientToken();

                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientToken))
                {
                    LogDebug("Tokens are missing. Attempting to fetch new tokens...");
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

                    LogDebug($"Profile fetch result: isAuthorized={isAuthorized}");

                    if (!isAuthorized)
                    {
                        LogDebug("Treating failure as unauthorized - deleting tokens");
                        LogDebug("Unauthorized response received. Deleting invalid tokens...");
                        CredentialManager.ClearAllTokensAndCookies(); // Remove invalid tokens

                        LogDebug("Attempting to refresh tokens...");
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
                catch (RateLimitException ex)
                {
                    Log($"Rate limit exceeded. {ex.Message}");
                    LogDebug("Rate limit error - not deleting tokens as they are still valid");
                    return false; // Return false but don't delete tokens
                }
            }
            catch (OperationCanceledException)
            {
                LogDebug("Token validation was cancelled");
                return false;
            }
            catch (TimeoutException ex)
            {
                LogDebug($"Token validation timed out: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LogDebug($"Error during token validation and profile fetch: {ex.Message}");
                LogDebug($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }


        private async Task<bool> RefreshTokensAsync()
        {
            try
            {
                LogDebug("Attempting to refresh tokens...");
                
                // Add timeout to prevent hanging
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                
                var loginTask = CredentialManager.LoginAndCaptureCookiesAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(10), cts.Token);
                
                var completedTask = await Task.WhenAny(loginTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    LogDebug("Token refresh timed out after 10 minutes");
                    return false;
                }
                
                await loginTask; // Ensure we await the actual task to catch any exceptions

                var newAccessToken = CredentialManager.LoadAccessToken();                
                var newClientToken = CredentialManager.LoadClientToken();

                if (string.IsNullOrEmpty(newAccessToken) || string.IsNullOrEmpty(newClientToken))
                {
                    Log("Token refresh failed: Tokens are null or empty.");
                    return false;
                }

                LogDebug("Tokens refreshed successfully.");
                return true;
            }
            catch (OperationCanceledException)
            {
                LogDebug("Token refresh was cancelled");
                return false;
            }
            catch (TimeoutException ex)
            {
                LogDebug($"Token refresh timed out: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LogDebug($"Error refreshing tokens: {ex.Message}");
                LogDebug($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }


        private void HandlePlayerEvent(JsonElement playerEvent)
        {
            try
            {
                LogDebug($"Processing player event: {playerEvent}");

                // Check the URI to determine message type
                if (playerEvent.TryGetProperty("uri", out var uriElement))
                {
                    string uri = uriElement.GetString();
                    LogDebug($"Processing message with URI: {uri}");

                    // Handle playback-settings/content-settings-update separately (contains smart shuffle info)
                    if (uri == "playback-settings/content-settings-update")
                    {
                        LogDebug("Received playback-settings/content-settings-update message");
                        _contentSettingsParser.HandleContentSettingsUpdate(playerEvent);
                        return;
                    }

                    // Handle connect-state volume updates (ProtoBuf SetVolumeCommand -> JSON -> volume%)
                    if (uri.StartsWith("hm://connect-state/v1/connect/volume", StringComparison.OrdinalIgnoreCase))
                    {
                        LogDebug("Received connect-state volume update message");
                        _volumeUpdateParser.HandleConnectVolumeUpdate(playerEvent);
                        return;
                    }

                    // Cluster updates (hm://connect-state/v1/cluster) are parsed separately if needed.
                    // Currently we rely primarily on wss://event for main playback JSON state, but
                    // we still log cluster updates for future use.
                    if (uri.StartsWith("hm://connect-state/v1/cluster", StringComparison.OrdinalIgnoreCase))
                    {
                        LogDebug("Received connect-state cluster update message");
                        // A full migration to connect-state would parse ClusterUpdate here using
                        // Google.Protobuf and map it into spotifyRequestContext.
                        // For now we only log the presence of these messages.
                    }
                }

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
                setState();
            }

            catch (Exception ex)
            {
                spotifyUtilities.LogDebug($"Error processing player event: {ex.Message}");
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
                spotifyRequestContext.IsJamOwner = isCurrentUserOwner;

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
                    string shortCode = SpotifyJamRequests.GenerateShareableUrlAsync(joinSessionUri, spotifyRequestContext, spotifyUtilities).Result;
                    SpotifyJamRequests._shareableUrl = $"https://spotify.link/{shortCode}";
                    spotifyRequestContext.JamShortCode = shortCode;
                    LogDebug($"Generated short code: {shortCode}");
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
                LogDebug($"Error updating session details: {ex.Message}");
            }
        }

        private async void HandleJoinJam(string sessionId)
        {
            await ExecuteWithErrorHandlingAsync(async () =>
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    spotifyUtilities.LogDebug("No session ID provided for joining jam.");
                    SendParameter(SpotiParameters.Error, true);
                    await Task.Delay(100);
                    SendParameter(SpotiParameters.Error, false);
                    return;
                }

                spotifyUtilities.LogDebug($"Joining jam with session ID: {sessionId}");

                bool joinResult = await SpotifyJamRequests.JoinSpotifyJam(sessionId, spotifyRequestContext, spotifyUtilities);
                if (joinResult)
                {
                    spotifyUtilities.LogDebug("Successfully joined the jam session.");
                }
                else
                {
                    spotifyUtilities.LogDebug("Failed to join the jam session.");
                    SendParameter(SpotiParameters.Error, true);
                    
                    // Reset the Error trigger after a short delay to allow for one-shot behavior
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(100); // Small delay to ensure the trigger is processed
                        SendParameter(SpotiParameters.Error, false);
                    });
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
                    UpdateCurrentSongParameter();
                }
            }
        }

        private void ExtractPlaybackState(JsonElement state)
        {
            // --- Device info ---
            if (state.TryGetProperty("device", out JsonElement device))
            {
                string newDeviceId = device.GetProperty("id").GetString();
                string newDeviceName = device.GetProperty("name").GetString();
                bool isActive = device.GetProperty("is_active").GetBoolean();
                
                // Update device info
                spotifyRequestContext.DeviceId = newDeviceId;
                spotifyRequestContext.DeviceName = newDeviceName;
                spotifyRequestContext.IsActiveDevice = isActive;
                spotifyRequestContext.VolumePercent = device.GetProperty("volume_percent").GetInt32();
                
                SetParameterSafe(SpotiParameters.DeviceIsActive, isActive);
                SetParameterSafe(SpotiParameters.DeviceIsPrivate, device.GetProperty("is_private_session").GetBoolean());
                SetParameterSafe(SpotiParameters.DeviceIsRestricted, device.GetProperty("is_restricted").GetBoolean());
                SetParameterSafe(SpotiParameters.DeviceSupportsVolume, device.GetProperty("supports_volume").GetBoolean());
                SetParameterSafe(SpotiParameters.DeviceVolumePercent, device.GetProperty("volume_percent").GetInt32());
                
                LogDebug($"Device updated: {newDeviceName} (ID: {newDeviceId}, Active: {isActive})");
            }

            // --- Shuffle and Smart Shuffle ---
            bool shuffleEnabled = false;
            // Use existing SmartShuffle value if not present in this message (it comes from content-settings-update or REST API)
            bool smartShuffle = spotifyRequestContext.SmartShuffle;
            
            if (state.TryGetProperty("shuffle_state", out JsonElement shuffle))
            {
                spotifyRequestContext.ShuffleState = shuffle.GetBoolean();
                shuffleEnabled = shuffle.GetBoolean();
                LogDebug($"Shuffle state: {spotifyRequestContext.ShuffleState}");
            }
            if (state.TryGetProperty("smart_shuffle", out JsonElement smartShuffleElem))
            {
                spotifyRequestContext.SmartShuffle = smartShuffleElem.GetBoolean();
                smartShuffle = smartShuffleElem.GetBoolean();
                LogDebug($"Smart Shuffle state from event: {spotifyRequestContext.SmartShuffle}");                
            }
            
            // Map shuffle states to integer mode: 0 = off, 1 = shuffle, 2 = smart shuffle
            int shuffleMode = !shuffleEnabled ? 0 : (smartShuffle ? 2 : 1);
            LogDebug($"Mapped shuffle mode: {shuffleMode} (shuffle={shuffleEnabled}, smart={smartShuffle})");
            SetParameterSafe(SpotiParameters.ShuffleMode, shuffleMode);

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
                
                // Get playback state
                bool isPlaying = state.GetProperty("is_playing").GetBoolean();
                int progressMs = state.GetProperty("progress_ms").GetInt32();
                SetParameterSafe(SpotiParameters.IsPlaying, isPlaying);
                
                // Get track duration for continuous updates
                int trackDurationMs = 0;
                if (state.TryGetProperty("item", out JsonElement item) && 
                    item.TryGetProperty("duration_ms", out JsonElement duration))
                {
                    trackDurationMs = duration.GetInt32();
                }
                
                // Update position tracking for continuous updates
                UpdatePositionTracking(progressMs, isPlaying, trackDurationMs);
                
                LogDebug($"Timestamp: {spotifyRequestContext.Timestamp}, Progress: {progressMs}ms, Playing: {isPlaying}");                
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

            // --- Check for playback restrictions ---
            if (state.TryGetProperty("actions", out JsonElement actions) && 
                actions.TryGetProperty("disallows", out JsonElement disallows))
            {
                if (disallows.TryGetProperty("resuming", out JsonElement resumingDisallowed))
                {
                    spotifyRequestContext.DisallowResuming = resumingDisallowed.GetBoolean();
                    SetParameterSafe(SpotiParameters.DisallowResuming, resumingDisallowed.GetBoolean());
                    if (resumingDisallowed.GetBoolean())
                    {
                        LogDebug("WARNING: Resuming playback is disallowed on this device. This may prevent the Play command from working.");
                    }
                }
                if (disallows.TryGetProperty("pausing", out JsonElement pausingDisallowed))
                {
                    spotifyRequestContext.DisallowPausing = pausingDisallowed.GetBoolean();
                    SetParameterSafe(SpotiParameters.DisallowPausing, pausingDisallowed.GetBoolean());
                    if (pausingDisallowed.GetBoolean())
                    {
                        LogDebug("WARNING: Pausing playback is disallowed on this device.");
                    }
                }
                if (disallows.TryGetProperty("skipping_prev", out JsonElement skippingPrevDisallowed))
                {
                    spotifyRequestContext.DisallowSkippingPrev = skippingPrevDisallowed.GetBoolean();
                    SetParameterSafe(SpotiParameters.DisallowSkippingPrev, skippingPrevDisallowed.GetBoolean());
                }
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
                SetParameterSafe(SpotiParameters.TrackDurationMs, (float)duration.GetInt32());
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
                    // Update color with utilities to send RGB parameters
                    spotifyRequestContext.UpdateSingleColor(spotifyUtilities);
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

            // --- Fetch Audio Features if enabled ---
            if (_getTrackFeaturesEnabled)
            {
                _ = FetchAudioFeaturesIfEnabled(item);
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

        private async Task FetchAudioFeaturesIfEnabled(JsonElement item)
        {
            try
            {
                LogDebug("FetchAudioFeaturesIfEnabled called");
                
                // Get track ID from the item
                if (!item.TryGetProperty("id", out var trackIdElement))
                {
                    LogDebug("No track ID found, cannot fetch audio features");
                    return;
                }

                string trackId = trackIdElement.GetString();
                if (string.IsNullOrEmpty(trackId))
                {
                    LogDebug("Track ID is null or empty, cannot fetch audio features");
                    return;
                }

                LogDebug($"Fetching audio features for track ID: {trackId}");

                // Check if _apiService is initialized
                if (_apiService == null)
                {
                    LogDebug("API service is null, cannot fetch audio features");
                    return;
                }

                // Fetch audio features using the API service
                var audioFeatures = await _apiService.GetTrackFeaturesAsync(trackId);
                if (audioFeatures != null)
                {
                    LogDebug($"Successfully fetched audio features for track: {spotifyRequestContext.TrackName}");
                    
                    // Update context with audio features
                    spotifyRequestContext.Danceability = audioFeatures.Danceability;
                    spotifyRequestContext.Energy = audioFeatures.Energy;
                    spotifyRequestContext.Key = audioFeatures.Key;
                    spotifyRequestContext.Loudness = audioFeatures.Loudness;
                    spotifyRequestContext.Mode = audioFeatures.Mode;
                    spotifyRequestContext.Speechiness = audioFeatures.Speechiness;
                    spotifyRequestContext.Acousticness = audioFeatures.Acousticness;
                    spotifyRequestContext.Instrumentalness = audioFeatures.Instrumentalness;
                    spotifyRequestContext.Liveness = audioFeatures.Liveness;
                    spotifyRequestContext.Valence = audioFeatures.Valence;
                    spotifyRequestContext.Tempo = audioFeatures.Tempo;
                    spotifyRequestContext.TimeSignature = audioFeatures.TimeSignature;

                    // Send parameters
                    SetParameterSafe(SpotiParameters.Danceability, audioFeatures.Danceability);
                    SetParameterSafe(SpotiParameters.Energy, audioFeatures.Energy);
                    SetParameterSafe(SpotiParameters.Key, audioFeatures.Key);
                    SetParameterSafe(SpotiParameters.Loudness, audioFeatures.Loudness);
                    SetParameterSafe(SpotiParameters.Mode, audioFeatures.Mode);
                    SetParameterSafe(SpotiParameters.Speechiness, audioFeatures.Speechiness);
                    SetParameterSafe(SpotiParameters.Acousticness, audioFeatures.Acousticness);
                    SetParameterSafe(SpotiParameters.Instrumentalness, audioFeatures.Instrumentalness);
                    SetParameterSafe(SpotiParameters.Liveness, audioFeatures.Liveness);
                    SetParameterSafe(SpotiParameters.Valence, audioFeatures.Valence);
                    SetParameterSafe(SpotiParameters.Tempo, audioFeatures.Tempo);
                    SetParameterSafe(SpotiParameters.TimeSignature, audioFeatures.TimeSignature);

                    LogDebug($"Audio features updated for track: {spotifyRequestContext.TrackName}");
                    LogDebug($"Danceability: {audioFeatures.Danceability}, Energy: {audioFeatures.Energy}, Valence: {audioFeatures.Valence}");
                }
                else
                {
                    LogDebug("Failed to fetch audio features - received null response");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error fetching audio features: {ex.Message}");
                LogDebug($"Stack trace: {ex.StackTrace}");
                
                // Log more details about the error
                if (ex.InnerException != null)
                {
                    LogDebug($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        private async Task FetchAudioFeaturesForTrackId(string trackId)
        {
            try
            {
                LogDebug($"Fetching audio features for track ID: {trackId}");

                // Check if _apiService is initialized
                if (_apiService == null)
                {
                    LogDebug("API service is null, cannot fetch audio features");
                    return;
                }

                // Fetch audio features using the API service
                var audioFeatures = await _apiService.GetTrackFeaturesAsync(trackId);
                if (audioFeatures != null)
                {
                    LogDebug($"Successfully fetched audio features for track ID: {trackId}");
                    
                    // Update context with audio features
                    spotifyRequestContext.Danceability = audioFeatures.Danceability;
                    spotifyRequestContext.Energy = audioFeatures.Energy;
                    spotifyRequestContext.Key = audioFeatures.Key;
                    spotifyRequestContext.Loudness = audioFeatures.Loudness;
                    spotifyRequestContext.Mode = audioFeatures.Mode;
                    spotifyRequestContext.Speechiness = audioFeatures.Speechiness;
                    spotifyRequestContext.Acousticness = audioFeatures.Acousticness;
                    spotifyRequestContext.Instrumentalness = audioFeatures.Instrumentalness;
                    spotifyRequestContext.Liveness = audioFeatures.Liveness;
                    spotifyRequestContext.Valence = audioFeatures.Valence;
                    spotifyRequestContext.Tempo = audioFeatures.Tempo;
                    spotifyRequestContext.TimeSignature = audioFeatures.TimeSignature;

                    // Send parameters
                    SetParameterSafe(SpotiParameters.Danceability, audioFeatures.Danceability);
                    SetParameterSafe(SpotiParameters.Energy, audioFeatures.Energy);
                    SetParameterSafe(SpotiParameters.Key, audioFeatures.Key);
                    SetParameterSafe(SpotiParameters.Loudness, audioFeatures.Loudness);
                    SetParameterSafe(SpotiParameters.Mode, audioFeatures.Mode);
                    SetParameterSafe(SpotiParameters.Speechiness, audioFeatures.Speechiness);
                    SetParameterSafe(SpotiParameters.Acousticness, audioFeatures.Acousticness);
                    SetParameterSafe(SpotiParameters.Instrumentalness, audioFeatures.Instrumentalness);
                    SetParameterSafe(SpotiParameters.Liveness, audioFeatures.Liveness);
                    SetParameterSafe(SpotiParameters.Valence, audioFeatures.Valence);
                    SetParameterSafe(SpotiParameters.Tempo, audioFeatures.Tempo);
                    SetParameterSafe(SpotiParameters.TimeSignature, audioFeatures.TimeSignature);

                    LogDebug($"Audio features updated for track: {spotifyRequestContext.TrackName}");
                    LogDebug($"Danceability: {audioFeatures.Danceability}, Energy: {audioFeatures.Energy}, Valence: {audioFeatures.Valence}");
                }
                else
                {
                    LogDebug("Failed to fetch audio features - received null response");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error fetching audio features: {ex.Message}");
                LogDebug($"Stack trace: {ex.StackTrace}");
                
                // Log more details about the error
                if (ex.InnerException != null)
                {
                    LogDebug($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        private async void HandleTouching(bool touching)
        {
            LogDebug($"[HandleTouching] Called with touching={touching}");
            isTouching = touching;
            
            LogDebug($"[HandleTouching] Checking conditions - touching={touching}, spotifyRequestContext?.IsInJam={spotifyRequestContext?.IsInJam}, _joinSessionToken={(string.IsNullOrEmpty(SpotifyJamRequests._joinSessionToken) ? "NULL/EMPTY" : SpotifyJamRequests._joinSessionToken)}");
            
            if (touching && spotifyRequestContext?.IsInJam == true && !string.IsNullOrEmpty(SpotifyJamRequests._joinSessionToken))
            {
                LogDebug($"[HandleTouching] Conditions met. Checking ephemeral words - _currentEphemeralWord1={(string.IsNullOrEmpty(_currentEphemeralWord1) ? "NULL/EMPTY" : _currentEphemeralWord1)}, _currentEphemeralWord2={(string.IsNullOrEmpty(_currentEphemeralWord2) ? "NULL/EMPTY" : _currentEphemeralWord2)}");
                
                if (string.IsNullOrEmpty(_currentEphemeralWord1) || string.IsNullOrEmpty(_currentEphemeralWord2))
                {
                    LogDebug($"[HandleTouching] Ephemeral words missing, calling CreateSyncopationJamAsync with sessionId={SpotifyJamRequests._joinSessionToken}");
                    var ephemeralCode = await CreateSyncopationJamAsync(SpotifyJamRequests._joinSessionToken);
                    if (ephemeralCode.HasValue)
                    {
                        _currentEphemeralWord1 = ephemeralCode.Value.word1;
                        _currentEphemeralWord2 = ephemeralCode.Value.word2;
                        
                        LogDebug($"[HandleTouching] Ephemeral code received - word1={_currentEphemeralWord1}, word2={_currentEphemeralWord2}");
                        
                        var param1 = GetParameterFromWordName(ephemeralCode.Value.word1);
                        var param2 = GetParameterFromWordName(ephemeralCode.Value.word2);
                        LogDebug($"[HandleTouching] About to set parameters - word1 param={param1}, word2 param={param2}");
                        
                        _activeParameterUpdates.Add(param1);
                        _activeParameterUpdates.Add(param2);
                        
                        SetParameterSafe(param1, true);
                        SetParameterSafe(param2, true);
                        
                        LogDebug($"[HandleTouching] Ephemeral code created and parameters set: {ephemeralCode.Value.word1} {ephemeralCode.Value.word2}");
                    }
                    else
                    {
                        LogDebug("[HandleTouching] Failed to create ephemeral code on syncopation server.");
                    }
                }
                else
                {
                    LogDebug($"[HandleTouching] Ephemeral words already exist, skipping creation");
                }
            }
            else
            {
                LogDebug($"[HandleTouching] Conditions not met - not creating ephemeral code");
            }
            
            if (!touching)
            {
                if (!string.IsNullOrEmpty(_currentEphemeralWord1) && !string.IsNullOrEmpty(_currentEphemeralWord2))
                {
                    LogDebug($"[HandleTouching] Touching=false, clearing ephemeral words");
                    _activeParameterUpdates.Add(GetParameterFromWordName(_currentEphemeralWord1));
                    _activeParameterUpdates.Add(GetParameterFromWordName(_currentEphemeralWord2));
                    
                    SetParameterSafe(GetParameterFromWordName(_currentEphemeralWord1), false);
                    SetParameterSafe(GetParameterFromWordName(_currentEphemeralWord2), false);
                    
                    _currentEphemeralWord1 = null;
                    _currentEphemeralWord2 = null;
                }
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
                    LogDebug("Exception while creating Spotify Jam: " + ex.ToString());
                }
            }
            else if (!wantJam && _isInJam)
            {
                _isInJam = false;
                LogDebug("Ending jam request...");
                string sessionId = SpotifyJamRequests._currentSessionId;
                await SpotifyJamRequests.LeaveSpotifyJam(sessionId, spotifyRequestContext, spotifyUtilities);
                
                // Stop sending ephemeral code parameters when leaving jam
                if (!string.IsNullOrEmpty(_currentEphemeralWord1) && !string.IsNullOrEmpty(_currentEphemeralWord2))
                {
                    _activeParameterUpdates.Add(GetParameterFromWordName(_currentEphemeralWord1));
                    _activeParameterUpdates.Add(GetParameterFromWordName(_currentEphemeralWord2));
                    
                    SetParameterSafe(GetParameterFromWordName(_currentEphemeralWord1), false);
                    SetParameterSafe(GetParameterFromWordName(_currentEphemeralWord2), false);
                    
                    _currentEphemeralWord1 = null;
                    _currentEphemeralWord2 = null;
                }
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

        private async Task<string> HandleResponseAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            throw new System.Exception($"API call failed with status code {response.StatusCode}: {errorContent}");
        }

        private void SetParameterSafe(System.Enum parameter, object value)
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
                
                // Reset the Error trigger after a short delay to allow for one-shot behavior
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100); // Small delay to ensure the trigger is processed
                    SendParameter(SpotiParameters.Error, false);
                });
            }
        }

        // Helper method to send CurrentSong parameter with dynamic track URI
        private void SendCurrentSongParameter(string trackUri, bool value)
        {
            try
            {
                SendParameter($"SpotiOSC/CurrentSong/{trackUri}", value);
                LogDebug($"Sent CurrentSong/{trackUri} = {value}");
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to send CurrentSong parameter: {ex.Message}");
            }
        }

        // Helper method to send CurrentPlaylist parameter with dynamic context URI
        private void SendCurrentPlaylistParameter(string contextUri, bool value)
        {
            try
            {
                SendParameter($"SpotiOSC/CurrentPlaylist/{contextUri}", value);
                LogDebug($"Sent CurrentPlaylist/{contextUri} = {value}");
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to send CurrentPlaylist parameter: {ex.Message}");
            }
        }

        // Updates the CurrentSong parameter based on current track URI and playing state
        private void UpdateCurrentSongParameter()
        {
            string currentTrackUri = spotifyRequestContext.TrackUri ?? string.Empty;
            bool currentlyPlaying = spotifyRequestContext.IsPlaying;

            // Case 1: Track changed while playing
            if (currentlyPlaying && !string.IsNullOrEmpty(currentTrackUri) && currentTrackUri != _currentPlayingTrackUri)
            {
                // Send false to old track
                if (!string.IsNullOrEmpty(_currentPlayingTrackUri))
                {
                    SendCurrentSongParameter(_currentPlayingTrackUri, false);
                }
                
                // Send true to new track
                SendCurrentSongParameter(currentTrackUri, true);
                _currentPlayingTrackUri = currentTrackUri;
                _wasPlayingLastUpdate = true;
            }
            // Case 2: Same track, but play state changed from paused to playing
            else if (currentlyPlaying && !string.IsNullOrEmpty(currentTrackUri) && currentTrackUri == _currentPlayingTrackUri && !_wasPlayingLastUpdate)
            {
                SendCurrentSongParameter(currentTrackUri, true);
                _wasPlayingLastUpdate = true;
            }
            // Case 3: Playback paused or stopped
            else if (!currentlyPlaying && _wasPlayingLastUpdate && !string.IsNullOrEmpty(_currentPlayingTrackUri))
            {
                SendCurrentSongParameter(_currentPlayingTrackUri, false);
                _wasPlayingLastUpdate = false;
            }
            // Case 4: Track changed while paused (edge case)
            else if (!currentlyPlaying && !string.IsNullOrEmpty(currentTrackUri) && currentTrackUri != _currentPlayingTrackUri && !string.IsNullOrEmpty(_currentPlayingTrackUri))
            {
                // Send false to old track if we were tracking it
                if (_wasPlayingLastUpdate)
                {
                    SendCurrentSongParameter(_currentPlayingTrackUri, false);
                    _wasPlayingLastUpdate = false;
                }
                _currentPlayingTrackUri = currentTrackUri;
            }
            
            // Update playlist/context parameter
            UpdateCurrentPlaylistParameter();
        }
        
        // Updates the CurrentPlaylist parameter based on current context URI and playing state
        private void UpdateCurrentPlaylistParameter()
        {
            string currentContextUri = spotifyRequestContext.ContextUri ?? string.Empty;
            bool currentlyPlaying = spotifyRequestContext.IsPlaying;

            // Case 1: Context changed while playing
            if (currentlyPlaying && !string.IsNullOrEmpty(currentContextUri) && currentContextUri != _currentPlayingContextUri)
            {
                // Send false to old context
                if (!string.IsNullOrEmpty(_currentPlayingContextUri))
                {
                    SendCurrentPlaylistParameter(_currentPlayingContextUri, false);
                }
                
                // Send true to new context
                SendCurrentPlaylistParameter(currentContextUri, true);
                _currentPlayingContextUri = currentContextUri;
                _wasPlayingFromContextLastUpdate = true;
            }
            // Case 2: Same context, but play state changed from paused to playing
            else if (currentlyPlaying && !string.IsNullOrEmpty(currentContextUri) && currentContextUri == _currentPlayingContextUri && !_wasPlayingFromContextLastUpdate)
            {
                SendCurrentPlaylistParameter(currentContextUri, true);
                _wasPlayingFromContextLastUpdate = true;
            }
            // Case 3: Playback paused or stopped
            else if (!currentlyPlaying && _wasPlayingFromContextLastUpdate && !string.IsNullOrEmpty(_currentPlayingContextUri))
            {
                SendCurrentPlaylistParameter(_currentPlayingContextUri, false);
                _wasPlayingFromContextLastUpdate = false;
            }
            // Case 4: Context changed while paused (edge case)
            else if (!currentlyPlaying && !string.IsNullOrEmpty(currentContextUri) && currentContextUri != _currentPlayingContextUri && !string.IsNullOrEmpty(_currentPlayingContextUri))
            {
                // Send false to old context if we were tracking it
                if (_wasPlayingFromContextLastUpdate)
                {
                    SendCurrentPlaylistParameter(_currentPlayingContextUri, false);
                    _wasPlayingFromContextLastUpdate = false;
                }
                _currentPlayingContextUri = currentContextUri;
            }
            // Case 5: Context was removed (playing single track/queue with no context)
            else if (string.IsNullOrEmpty(currentContextUri) && !string.IsNullOrEmpty(_currentPlayingContextUri))
            {
                // Send false to old context
                if (_wasPlayingFromContextLastUpdate)
                {
                    SendCurrentPlaylistParameter(_currentPlayingContextUri, false);
                    _wasPlayingFromContextLastUpdate = false;
                }
                _currentPlayingContextUri = string.Empty;
            }
        }

        // -- Switching logic: choose the state based on several booleans.
        private void setState()
        {
            // Compute the target UI state from the current context
            UiState newState;

            if (spotifyRequestContext.IsInJam)
            {
                if (spotifyRequestContext.IsPlaying)
                {
                    if (spotifyRequestContext.IsExplicit)
                        newState = spotifyRequestContext.ShuffleState
                            ? UiState.Playing_Jam_Explicit_Shuffle
                            : UiState.Playing_Jam_Explicit_NoShuffle;
                    else
                        newState = spotifyRequestContext.ShuffleState
                            ? UiState.Playing_Jam_Clean_Shuffle
                            : UiState.Playing_Jam_Clean_NoShuffle;
                }
                else
                {
                    newState = UiState.Paused_Jam;
                }
            }
            else
            {
                if (spotifyRequestContext.IsPlaying)
                {
                    if (spotifyRequestContext.IsExplicit)
                        newState = spotifyRequestContext.ShuffleState
                            ? UiState.Playing_Explicit_Shuffle
                            : UiState.Playing_Explicit_NoShuffle;
                    else
                        newState = spotifyRequestContext.ShuffleState
                            ? UiState.Playing_Clean_Shuffle
                            : UiState.Playing_Clean_NoShuffle;
                }
                else
                {
                    newState = UiState.Paused_Normal;
                }
            }

            // Only switch if different from last state
            if (_lastUiState == null || _lastUiState.Value != newState)
            {
                _lastUiState = newState;

                switch (newState)
                {
                    case UiState.Playing_Jam_Explicit_Shuffle: ChangeState("Playing_Jam_Explicit_Shuffle"); break;
                    case UiState.Playing_Jam_Explicit_NoShuffle: ChangeState("Playing_Jam_Explicit_NoShuffle"); break;
                    case UiState.Playing_Jam_Clean_Shuffle: ChangeState("Playing_Jam_Clean_Shuffle"); break;
                    case UiState.Playing_Jam_Clean_NoShuffle: ChangeState("Playing_Jam_Clean_NoShuffle"); break;
                    case UiState.Paused_Jam: ChangeState("Paused_Jam"); break;
                    case UiState.Playing_Explicit_Shuffle: ChangeState("Playing_Explicit_Shuffle"); break;
                    case UiState.Playing_Explicit_NoShuffle: ChangeState("Playing_Explicit_NoShuffle"); break;
                    case UiState.Playing_Clean_Shuffle: ChangeState("Playing_Clean_Shuffle"); break;
                    case UiState.Playing_Clean_NoShuffle: ChangeState("Playing_Clean_NoShuffle"); break;
                    case UiState.Paused_Normal: ChangeState("Paused_Normal"); break;
                }
            }
        }


        [ModuleUpdate(ModuleUpdateMode.ChatBox)]
        private void ChatBoxUpdate()
        {
            // Device
            SetVariableValue("DeviceId", spotifyRequestContext.DeviceId);
            SetVariableValue("DeviceName", spotifyRequestContext.DeviceName);
            SetVariableValue("IsActiveDevice", spotifyRequestContext.IsActiveDevice);
            SetVariableValue("VolumePercent", spotifyRequestContext.VolumePercent);

            // Context
            SetVariableValue("ContextExternalUrl", spotifyRequestContext.ContextExternalUrl);
            SetVariableValue("ContextHref", spotifyRequestContext.ContextHref);
            SetVariableValue("ContextType", spotifyRequestContext.ContextType);
            SetVariableValue("ContextUri", spotifyRequestContext.ContextUri);

            // Media
            SetVariableValue("TrackName", spotifyRequestContext.TrackName);

            // First artist (works for named or unnamed (string,string) tuples)
            string firstArtist = string.Empty;
            if (spotifyRequestContext.Artists != null && spotifyRequestContext.Artists.Count > 0)
            {
                var (name, _) = spotifyRequestContext.Artists[0];
                firstArtist = name ?? string.Empty;
            }
            SetVariableValue("TrackArtist", firstArtist);

            SetVariableValue("TrackDurationMs", spotifyRequestContext.TrackDurationMs);
            SetVariableValue("DiscNumber", spotifyRequestContext.DiscNumber);
            SetVariableValue("IsExplicit", spotifyRequestContext.IsExplicit);
            SetVariableValue("Popularity", spotifyRequestContext.Popularity);
            SetVariableValue("TrackNumber", spotifyRequestContext.TrackNumber);
            SetVariableValue("TrackUri", spotifyRequestContext.TrackUri);
            SetVariableValue("CurrentlyPlayingType", spotifyRequestContext.CurrentlyPlayingType);

            // Album
            SetVariableValue("AlbumName", spotifyRequestContext.AlbumName);
            SetVariableValue("AlbumArtworkUrl", spotifyRequestContext.AlbumArtworkUrl);
            SetVariableValue("AlbumType", spotifyRequestContext.AlbumType);
            SetVariableValue("AlbumReleaseDate", spotifyRequestContext.AlbumReleaseDate);
            SetVariableValue("AlbumTotalTracks", spotifyRequestContext.AlbumTotalTracks);

            // Playback flags
            SetVariableValue("ShuffleState", spotifyRequestContext.ShuffleState);
            SetVariableValue("SmartShuffle", spotifyRequestContext.SmartShuffle);
            SetVariableValue("RepeatState", spotifyRequestContext.RepeatState);
            SetVariableValue("Timestamp", spotifyRequestContext.Timestamp.ToString());
            SetVariableValue("ProgressMs", spotifyRequestContext.ProgressMs);

            // All artists (use Item1 so it works for named/unnamed tuples)
            string artistsJoined = (spotifyRequestContext.Artists != null && spotifyRequestContext.Artists.Count > 0)
                ? string.Join(", ", spotifyRequestContext.Artists.Select(a => a.Item1 ?? string.Empty))
                : string.Empty;
            SetVariableValue("Artists", artistsJoined);

            // Jam flag as variable
            SetVariableValue("InAJam", spotifyRequestContext.IsInJam);

            // Jam-specific variables
            SetVariableValue("JamShortCode", spotifyRequestContext.JamShortCode ?? string.Empty);
            SetVariableValue("JamOwnerName", spotifyRequestContext.JamOwnerName ?? string.Empty);
            SetVariableValue("JamParticipantCount", SpotifyJamRequests._participantCount);
            SetVariableValue("JamMaxMemberCount", SpotifyJamRequests._maxMemberCount);
            SetVariableValue("SessionIsOwner", spotifyRequestContext.IsJamOwner);
            SetVariableValue("SessionIsListening", SpotifyJamRequests._isListening);
            SetVariableValue("SessionIsControlling", SpotifyJamRequests._isControlling);

            // Audio Features
            SetVariableValue("Danceability", spotifyRequestContext.Danceability);
            SetVariableValue("Energy", spotifyRequestContext.Energy);
            SetVariableValue("Key", spotifyRequestContext.Key);
            SetVariableValue("Loudness", spotifyRequestContext.Loudness);
            SetVariableValue("Mode", spotifyRequestContext.Mode);
            SetVariableValue("Speechiness", spotifyRequestContext.Speechiness);
            SetVariableValue("Acousticness", spotifyRequestContext.Acousticness);
            SetVariableValue("Instrumentalness", spotifyRequestContext.Instrumentalness);
            SetVariableValue("Liveness", spotifyRequestContext.Liveness);
            SetVariableValue("Valence", spotifyRequestContext.Valence);
            SetVariableValue("Tempo", spotifyRequestContext.Tempo);
            SetVariableValue("TimeSignature", spotifyRequestContext.TimeSignature);

            // IMPORTANT: do NOT call setState() here anymore.
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
                
                // Reset the Error trigger after a short delay to allow for one-shot behavior
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100); // Small delay to ensure the trigger is processed
                    SendParameter(SpotiParameters.Error, false);
                });
            }
        }

        private async Task SetShuffleModeAsync(int mode)
        {
            try
            {
                if (string.IsNullOrEmpty(spotifyRequestContext?.DeviceId))
                {
                    LogDebug("Cannot set shuffle mode: No active device");
                    return;
                }

                string accessToken = CredentialManager.LoadAccessToken();
                string clientToken = CredentialManager.LoadClientToken();
                
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientToken))
                {
                    LogDebug("Cannot set shuffle mode: Missing tokens");
                    return;
                }

                // Build the command URL using device IDs
                // Device IDs are 40 characters long, so we need to generate one
                string fromDeviceId;
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    var bytes = new byte[20];
                    rng.GetBytes(bytes);
                    fromDeviceId = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                }
                string url = $"https://gue1-spclient.spotify.com/connect-state/v1/player/command/from/{fromDeviceId}/to/{spotifyRequestContext.DeviceId}";
                
                // Determine shuffle settings based on mode
                bool shufflingContext = mode > 0;
                string contextEnhancement = mode == 2 ? "RECOMMENDATION" : "NONE";

                // Create the command payload
                var commandPayload = new
                {
                    command = new
                    {
                        shuffling_context = shufflingContext,
                        modes = new
                        {
                            context_enhancement = contextEnhancement
                        },
                        logging_params = new
                        {
                            page_instance_ids = new[] { Guid.NewGuid().ToString() },
                            interaction_ids = new[] { Guid.NewGuid().ToString() },
                            command_id = Guid.NewGuid().ToString("N")
                        },
                        endpoint = "set_options"
                    }
                };

                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(commandPayload);

                using var handler = new System.Net.Http.HttpClientHandler();
                using var client = new System.Net.Http.HttpClient(handler);
                
                client.DefaultRequestHeaders.Add("accept", "*/*");
                client.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9");
                client.DefaultRequestHeaders.Add("authorization", $"Bearer {accessToken}");
                client.DefaultRequestHeaders.Add("client-token", clientToken);
                client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"141\", \"Not?A_Brand\";v=\"8\", \"Chromium\";v=\"141\"");
                client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
                client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
                client.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
                client.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
                client.DefaultRequestHeaders.Add("sec-fetch-site", "same-site");

                var content = new System.Net.Http.StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    LogDebug($"Successfully set shuffle mode to {mode}");
                }
                else
                {
                    LogDebug($"Failed to set shuffle mode: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error setting shuffle mode: {ex.Message}");
            }
        }

        private async Task SetRepeatModeAsync(int mode)
        {
            try
            {
                if (string.IsNullOrEmpty(spotifyRequestContext?.DeviceId))
                {
                    LogDebug("Cannot set repeat mode: No active device");
                    return;
                }

                string accessToken = CredentialManager.LoadAccessToken();
                string clientToken = CredentialManager.LoadClientToken();
                
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(clientToken))
                {
                    LogDebug("Cannot set repeat mode: Missing tokens");
                    return;
                }

                // Build the command URL using device IDs
                string fromDeviceId;
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    var bytes = new byte[20];
                    rng.GetBytes(bytes);
                    fromDeviceId = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
                }
                string url = $"https://gue1-spclient.spotify.com/connect-state/v1/player/command/from/{fromDeviceId}/to/{spotifyRequestContext.DeviceId}";
                
                // Determine repeat settings based on mode
                // Mode 0 (off): repeating_context=false, repeating_track=false
                // Mode 1 (track): repeating_context=true, repeating_track=true
                // Mode 2 (context): repeating_context=true, repeating_track=false
                bool repeatingContext = mode > 0;
                bool repeatingTrack = mode == 1;

                // Create the command payload
                var commandPayload = new
                {
                    command = new
                    {
                        repeating_context = repeatingContext,
                        repeating_track = repeatingTrack,
                        endpoint = "set_options",
                        logging_params = new
                        {
                            command_id = Guid.NewGuid().ToString("N")
                        }
                    }
                };

                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(commandPayload);

                using var handler = new System.Net.Http.HttpClientHandler();
                using var client = new System.Net.Http.HttpClient(handler);
                
                client.DefaultRequestHeaders.Add("accept", "*/*");
                client.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9");
                client.DefaultRequestHeaders.Add("authorization", $"Bearer {accessToken}");
                client.DefaultRequestHeaders.Add("client-token", clientToken);
                client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"141\", \"Not?A_Brand\";v=\"8\", \"Chromium\";v=\"141\"");
                client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
                client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
                client.DefaultRequestHeaders.Add("sec-fetch-dest", "empty");
                client.DefaultRequestHeaders.Add("sec-fetch-mode", "cors");
                client.DefaultRequestHeaders.Add("sec-fetch-site", "same-site");

                var content = new System.Net.Http.StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    LogDebug($"Successfully set repeat mode to {mode}");
                }
                else
                {
                    LogDebug($"Failed to set repeat mode: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error setting repeat mode: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract playlist ID from a Spotify playlist URI.
        /// </summary>
        private string ExtractPlaylistId(string playlistUri)
        {
            if (string.IsNullOrEmpty(playlistUri)) return null;
            
            if (playlistUri.StartsWith("spotify:playlist:"))
            {
                return playlistUri.Replace("spotify:playlist:", "");
            }
            if (playlistUri.Contains("/playlist/"))
            {
                var parts = playlistUri.Split(new[] { "/playlist/" }, StringSplitOptions.None);
                if (parts.Length > 1)
                {
                    var id = parts[1].Split('?')[0].Split('&')[0];
                    return id;
                }
            }
            return null;
        }

        #region Continuous Position Updates

        /// <summary>
        /// Initializes the timer for continuous playback position updates
        /// </summary>
        private void InitializePositionUpdateTimer()
        {
            _positionUpdateTimer = new System.Timers.Timer(1); // 1 millisecond interval
            _positionUpdateTimer.Elapsed += OnPositionUpdateTimerElapsed;
            _positionUpdateTimer.AutoReset = true;
            _positionUpdateTimer.Start();
            
            LogDebug("Continuous position update timer initialized");
        }

        /// <summary>
        /// Stops and disposes the position update timer
        /// </summary>
        private void StopPositionUpdateTimer()
        {
            if (_positionUpdateTimer != null)
            {
                _positionUpdateTimer.Stop();
                _positionUpdateTimer.Elapsed -= OnPositionUpdateTimerElapsed;
                _positionUpdateTimer.Dispose();
                _positionUpdateTimer = null;
                
                LogDebug("Position update timer stopped");
            }
        }

        /// <summary>
        /// Timer elapsed event handler for continuous position updates
        /// </summary>
        private void OnPositionUpdateTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                // Only update if we're playing and have a valid track duration
                if (_isPlaying && _trackDurationMs > 0 && _lastKnownPositionMs >= 0)
                {
                    var now = DateTime.UtcNow;
                    var timeSinceLastUpdate = now - _lastPositionUpdateTime;
                    
                    // Only update if enough time has passed (avoid excessive updates)
                    if (timeSinceLastUpdate.TotalMilliseconds >= 1)
                    {
                        // Calculate new position based on elapsed time
                        var elapsedMs = (int)timeSinceLastUpdate.TotalMilliseconds;
                        var newPosition = _lastKnownPositionMs + elapsedMs;
                        
                        // Don't exceed track duration
                        if (newPosition > _trackDurationMs)
                        {
                            newPosition = _trackDurationMs;
                        }
                        
                        // Only update if position actually changed
                        if (newPosition != _lastKnownPositionMs)
                        {
                            // Update the position parameter
                            SetParameterSafe(SpotiParameters.PlaybackPosition, (float)newPosition);
                            
                            // Update tracking variables
                            _lastKnownPositionMs = newPosition;
                            _lastPositionUpdateTime = now;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error in position update timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the position tracking variables when receiving new playback state
        /// </summary>
        private void UpdatePositionTracking(int progressMs, bool isPlaying, int durationMs)
        {
            _lastKnownPositionMs = progressMs;
            _lastPositionUpdateTime = DateTime.UtcNow;
            _isPlaying = isPlaying;
            _trackDurationMs = durationMs;
            
            // Send the current position immediately
            SetParameterSafe(SpotiParameters.PlaybackPosition, (float)progressMs);
            
            LogDebug($"Position tracking updated - Progress: {progressMs}ms, Playing: {isPlaying}, Duration: {durationMs}ms");
        }

        #endregion

    }
}
