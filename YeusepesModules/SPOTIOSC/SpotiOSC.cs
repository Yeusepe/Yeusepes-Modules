using System.Collections.Concurrent;
using System.Net.Http;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.SDK.VRChat;
using VRCOSC.App.Settings;
using YeusepesModules.Common;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Runtime;
using YeusepesModules.SPOTIOSC.Runtime.Jam;
using YeusepesModules.SPOTIOSC.Runtime.Spotify;
using YeusepesModules.SPOTIOSC.UI;
using YeusepesModules.SPOTIOSC.Utils.Requests;

namespace YeusepesModules.SPOTIOSC;

[ModuleTitle("SpotiOSC")]
[ModuleDescription("A module to control your Spotify Through OSC.")]
[ModuleType(ModuleType.Integrations)]
[ModuleInfo("https://github.com/Yeusepe/Yeusepes-Modules/wiki/SPOTIOSC")]
[ModuleSettingsWindow(typeof(SignInWindow))]
public sealed class SpotiOSC : Module
{
    private const string MelodyServerUrl = "https://melody.yucp.club/";
    private readonly ConcurrentDictionary<Enum, object> _activeParameterUpdates = new();
    private HttpClient? _httpClient;
    private SpotiOscOutput? _output;
    private SpotiOscRuntime? _runtime;
    private EventHandler<UnobservedTaskExceptionEventArgs>? _puppeteerNoiseHandler;

    // Kept public for the existing runtime and settings views.
    public SpotifyRequestContext? spotifyRequestContext;
    public SpotifyUtilities? spotifyUtilities;

    public enum SpotiSettings
    {
        SignInButton,
        PopUpJam,
        MelodyServerUrl
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
        CurrentSong,
        CurrentPlaylist,
        Pause,
        NextTrack,
        PreviousTrack,
        ShuffleMode,
        RepeatMode,
        Timestamp,
        PlaybackPosition,
        IsPlaying,
        DeviceIsActive,
        DeviceIsPrivate,
        DeviceIsRestricted,
        DeviceSupportsVolume,
        DeviceVolumePercent,
        ContextType,
        DiscNumber,
        TrackDurationMs,
        IsExplicit,
        IsLocal,
        SongPopularity,
        TrackNumber,
        AlbumTotalTracks,
        AlbumType,
        DisallowPausing,
        DisallowResuming,
        DisallowSkippingPrev,
        JamParticipantCount,
        SessionMaxMemberCount,
        SessionIsOwner,
        SessionIsListening,
        SessionIsControlling,
        QueueOnlyMode,
        HostIsGroup,
        TrackChangedEvent,
        PlayUri,
        Allegro,
        Cadence,
        Groove,
        Ritmo,
        Metronome,
        Encore,
        Chorus,
        AllegroReceiver,
        CadenceReceiver,
        GrooveReceiver,
        RitmoReceiver,
        MetronomeReceiver,
        EncoreReceiver,
        ChorusReceiver,
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
        AlbumColorR,
        AlbumColorG,
        AlbumColorB,
        WorldJamHosting,
        WorldJamPrompt,
        WorldJamJoin,
        WorldJamDismiss,
        WorldJamTransmit,
        WorldJamReceive,
        WorldJamBits,
        WorldJamTransmitLow,
        WorldJamTransmitHigh,
        WorldJamReceive1,
        WorldJamReceive2,
        WorldJamReceive3,
        WorldJamReceiveLow0,
        WorldJamReceiveLow1,
        WorldJamReceiveLow2,
        WorldJamReceiveLow3,
        WorldJamReceiveHigh0,
        WorldJamReceiveHigh1,
        WorldJamReceiveHigh2,
        WorldJamReceiveHigh3,
        JammrResize,
        JammrGadget
    }

    protected override void OnPreLoad()
    {
        YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("libusb-1.0.dll", Log);
        YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("cvextern.dll", Log);
        YeusepesLowLevelTools.EarlyLoader.InitializeNativeLibraries("JamPake.dll", Log);
        _output = new SpotiOscOutput(
            (parameter, value) => SetParameterSafe(parameter, value),
            (address, value) => SendParameter(address, value),
            TriggerEvent,
            ChangeState,
            LogDebug);
        spotifyUtilities = CreateUtilities();
        CredentialManager.SpotifyUtils = spotifyUtilities;
        RegisterParameters();
        CreateCustomSetting(
            SpotiSettings.SignInButton,
            new CustomModuleSetting(string.Empty, string.Empty, typeof(SignIn), true));
        SetRuntimeView(typeof(NowPlayingRuntimeView));
        base.OnPreLoad();
    }

    protected override void OnPostLoad()
    {
        var trackName = CreateVariable<string>("TrackName", "Track Name")!;
        var trackArtist = CreateVariable<string>("TrackArtist", "Track Artist")!;
        var albumName = CreateVariable<string>("AlbumName", "Album Name")!;
        var deviceName = CreateVariable<string>("DeviceName", "Device Name")!;
        var volume = CreateVariable<int>("VolumePercent", "Volume (%)")!;
        var shuffle = CreateVariable<bool>("ShuffleState", "Shuffle")!;
        var repeat = CreateVariable<string>("RepeatState", "Repeat Mode")!;
        var inJam = CreateVariable<bool>("InAJam", "In a Jam")!;

        CreateRemainingVariables();
        CreateEvent("PlayEvent", "Play Event", "Playback started: {0}", [trackName]);
        CreateEvent("PauseEvent", "Pause Event", "Playback paused: {0}", [trackName]);
        CreateEvent("TrackChangedEvent", "Track Changed Event", "Now playing: {0}", [trackName]);
        CreateEvent("VolumeEvent", "Volume Event", "Volume changed to {0}%.", [volume]);
        CreateEvent("RepeatEvent", "Repeat Event", "Repeat mode set to {0}.", [repeat]);
        CreateEvent("ShuffleEvent", "Shuffle Event", "Shuffle is {0}.", [shuffle]);
        CreateEvent("JamEvent", "Jam Event", "Jam status updated: {0}", [inJam]);

        const string format = "Track: {0} - {1}\nAlbum: {2}\nDevice: {3} ({4}%)\nPlayback: Shuffle: {5}, Repeat: {6}\n";
        var variables = new[] { trackName, trackArtist, albumName, deviceName, volume, shuffle, repeat };
        foreach (var state in StateDefinitions)
            CreateState(state.Name, state.Title, state.Prefix + format, variables);
    }

    protected override async Task<bool> OnModuleStart()
    {
        _puppeteerNoiseHandler = (_, args) =>
        {
            if (args.Exception.InnerExceptions.All(IsHarmlessPuppeteerException)) args.SetObserved();
        };
        TaskScheduler.UnobservedTaskException += _puppeteerNoiseHandler;

        _httpClient = new HttpClient();
        spotifyUtilities = CreateUtilities();
        CredentialManager.SpotifyUtils = spotifyUtilities;
        spotifyRequestContext = await new SpotifySessionBootstrapper(Log, LogDebug)
            .CreateContextAsync(_httpClient);
        if (spotifyRequestContext is null)
        {
            Log("Failed to validate tokens or fetch profile. Exiting.");
            await StopRuntimeAsync();
            return false;
        }

        try
        {
            _runtime = await SpotiOscRuntime.CreateAsync(
                _httpClient,
                spotifyRequestContext,
                spotifyUtilities,
                _output!,
                LogDebug,
                MelodyServerUrl);
            await _runtime.StartAsync();
            return true;
        }
        catch (Exception exception)
        {
            LogDebug($"SpotiOSC startup failed: {exception.Message}");
            await StopRuntimeAsync();
            return false;
        }
    }

    protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
    {
        if (parameter.Lookup is not SpotiParameters lookup) return;
        if (_runtime is not null && _runtime.IsEphemeral(lookup))
            _runtime.HandleEphemeral(lookup, parameter.GetValue<bool>());
        if (_activeParameterUpdates.TryRemove(lookup, out var sentValue) &&
            Equals(sentValue, parameter.GetValue<object>())) return;

        int beaconInput = (int)lookup - (int)SpotiParameters.WorldJamReceive1;
        if (beaconInput >= 0 && beaconInput < 11) {
            int lane = beaconInput < 3 ? beaconInput + 1 : (beaconInput - 3) % 4;
            int part = beaconInput < 3 ? 0 : 1 + (beaconInput - 3) / 4;
            _runtime?.ReceiveWorldJam(lane, part, parameter.GetValue<float>());
            return;
        }
        switch (lookup)
        {
            case SpotiParameters.JammrResize:
                _runtime?.SetJamLocalControl(0, parameter.GetValue<float>());
                break;
            case SpotiParameters.JammrGadget:
                _runtime?.SetJamLocalControl(1, parameter.GetValue<bool>() ? 1 : 0);
                break;
            case SpotiParameters.WorldJamReceive:
                _runtime?.ReceiveWorldJam(parameter.GetValue<float>());
                break;
            case SpotiParameters.WorldJamBits:
                _runtime?.SetWorldJamBandwidth(parameter.GetValue<int>());
                break;
            case SpotiParameters.WorldJamHosting:
                RunBackground(() => _runtime!.SetWorldHostingAsync(parameter.GetValue<bool>()), "World jam hosting");
                break;
            case SpotiParameters.WorldJamJoin when parameter.GetValue<bool>():
                RunBackground(() => _runtime!.JoinWorldJamAsync(), "World jam join");
                break;
            case SpotiParameters.WorldJamDismiss when parameter.GetValue<bool>():
                _runtime?.DismissWorldJam();
                break;
            case SpotiParameters.WantJam:
                RunBackground(() => _runtime!.SetWantJamAsync(parameter.GetValue<bool>()), "Jam request");
                break;
            case SpotiParameters.Touching:
                RunBackground(() => _runtime!.SetTouchingAsync(parameter.GetValue<bool>()), "Touch interaction");
                break;
            default:
                _runtime?.TryHandleCommand(parameter);
                break;
        }
    }

    protected override async Task OnModuleStop()
    {
        LogDebug("Stopping SpotiOSC module...");
        await StopRuntimeAsync();
    }

    protected override void OnAvatarChange(AvatarConfig? avatarConfig)
    {
        base.OnAvatarChange(avatarConfig);
        if (_runtime is not null) _output?.Set(SpotiParameters.Enabled, true);
        _runtime?.RefreshWorldPrompt();
    }

    [ModuleUpdate(ModuleUpdateMode.ChatBox)]
    private void ChatBoxUpdate()
    {
        var context = spotifyRequestContext;
        if (context is null) return;
        SetVariableValue("DeviceId", context.DeviceId ?? string.Empty);
        SetVariableValue("DeviceName", context.DeviceName ?? string.Empty);
        SetVariableValue("IsActiveDevice", context.IsActiveDevice);
        SetVariableValue("VolumePercent", context.VolumePercent);
        SetVariableValue("ContextExternalUrl", context.ContextExternalUrl ?? string.Empty);
        SetVariableValue("ContextHref", context.ContextHref ?? string.Empty);
        SetVariableValue("ContextType", context.ContextType ?? string.Empty);
        SetVariableValue("ContextUri", context.ContextUri ?? string.Empty);
        SetVariableValue("TrackName", context.TrackName ?? string.Empty);
        SetVariableValue("TrackArtist", context.Artists.FirstOrDefault().Name ?? string.Empty);
        SetVariableValue("TrackDurationMs", context.TrackDurationMs);
        SetVariableValue("DiscNumber", context.DiscNumber);
        SetVariableValue("IsExplicit", context.IsExplicit);
        SetVariableValue("Popularity", context.Popularity);
        SetVariableValue("TrackNumber", context.TrackNumber);
        SetVariableValue("TrackUri", context.TrackUri ?? string.Empty);
        SetVariableValue("CurrentlyPlayingType", context.CurrentlyPlayingType ?? string.Empty);
        SetVariableValue("AlbumName", context.AlbumName ?? string.Empty);
        SetVariableValue("AlbumArtworkUrl", context.AlbumArtworkUrl ?? string.Empty);
        SetVariableValue("AlbumType", context.AlbumType ?? string.Empty);
        SetVariableValue("AlbumReleaseDate", context.AlbumReleaseDate ?? string.Empty);
        SetVariableValue("AlbumTotalTracks", context.AlbumTotalTracks);
        SetVariableValue("ShuffleState", context.ShuffleState);
        SetVariableValue("SmartShuffle", context.SmartShuffle);
        SetVariableValue("RepeatState", context.RepeatState ?? string.Empty);
        SetVariableValue("Timestamp", context.Timestamp.ToString());
        SetVariableValue("ProgressMs", context.ProgressMs);
        SetVariableValue("Artists", string.Join(", ", context.Artists.Select(artist => artist.Name)));
        SetVariableValue("InAJam", context.IsInJam);
        SetVariableValue("JamShortCode", context.JamShortCode ?? string.Empty);
        SetVariableValue("JamOwnerName", context.JamOwnerName ?? string.Empty);
        SetVariableValue("JamParticipantCount", _runtime?.JamState.ParticipantCount ?? 0);
        SetVariableValue("JamMaxMemberCount", _runtime?.JamState.MaxMemberCount ?? 0);
        SetVariableValue("SessionIsOwner", context.IsJamOwner);
        SetVariableValue("SessionIsListening", _runtime?.JamState.IsListening ?? false);
        SetVariableValue("SessionIsControlling", _runtime?.JamState.IsControlling ?? false);
        SetVariableValue("Danceability", context.Danceability);
        SetVariableValue("Energy", context.Energy);
        SetVariableValue("Key", context.Key);
        SetVariableValue("Loudness", context.Loudness);
        SetVariableValue("Mode", context.Mode);
        SetVariableValue("Speechiness", context.Speechiness);
        SetVariableValue("Acousticness", context.Acousticness);
        SetVariableValue("Instrumentalness", context.Instrumentalness);
        SetVariableValue("Liveness", context.Liveness);
        SetVariableValue("Valence", context.Valence);
        SetVariableValue("Tempo", context.Tempo);
        SetVariableValue("TimeSignature", context.TimeSignature);
    }

    private SpotifyUtilities CreateUtilities() => new()
    {
        Log = Log,
        LogDebug = LogDebug
    };

    private void SetParameterSafe(Enum parameter, object value)
    {
        try
        {
            _activeParameterUpdates[parameter] = value;
            SendParameter(parameter, value);
        }
        catch (Exception exception)
        {
            LogDebug($"Failed to set parameter {parameter}: {exception.Message}");
        }
    }

    private void RunBackground(Func<Task> action, string operation) => _ = RunBackgroundAsync(action, operation);

    private async Task RunBackgroundAsync(Func<Task> action, string operation)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            LogDebug($"{operation} failed: {exception.Message}");
            if (_output is not null) await _output.PulseErrorAsync();
        }
    }

    private static bool IsHarmlessPuppeteerException(Exception exception) =>
        exception is PuppeteerSharp.PuppeteerException &&
        (exception.Message.Contains("redirect", StringComparison.OrdinalIgnoreCase) ||
         exception.Message.Contains("Execution Context", StringComparison.OrdinalIgnoreCase));

    private async Task StopRuntimeAsync()
    {
        if (_puppeteerNoiseHandler is not null)
        {
            TaskScheduler.UnobservedTaskException -= _puppeteerNoiseHandler;
            _puppeteerNoiseHandler = null;
        }
        try
        {
            if (_runtime is not null) await _runtime.DisposeAsync();
        }
        catch (Exception exception)
        {
            LogDebug($"Module shutdown failed: {exception.Message}");
        }
        finally
        {
            _runtime = null;
            _activeParameterUpdates.Clear();
            spotifyRequestContext = null;
            spotifyUtilities = null;
            _httpClient?.Dispose();
            _httpClient = null;
        }
    }

    private void RegisterParameters()
    {
        RegisterParameter<int>(SpotiParameters.WorldJamTransmit, "SpotiOSC/WorldJam/Transmit", ParameterMode.Write, "World Jam Beacon", "Local framed invitation transport.");
        RegisterParameter<float>(SpotiParameters.JammrResize, "SpotiOSC/JAMMR/Resize", ParameterMode.Read, "JAMMR Resize", "Prioritize local resize changes over background discovery.");
        RegisterParameter<bool>(SpotiParameters.JammrGadget, "SpotiOSC/Gadget/On", ParameterMode.Read, "JAMMR Gadget", "Prioritize gadget changes over background discovery.");
        RegisterParameter<int>(SpotiParameters.WorldJamTransmitLow, "SpotiOSC/WorldJam/TransmitLow", ParameterMode.Write, "World Jam Low", "Local borrowed song bits.");
        RegisterParameter<int>(SpotiParameters.WorldJamTransmitHigh, "SpotiOSC/WorldJam/TransmitHigh", ParameterMode.Write, "World Jam High", "Local borrowed song bits.");
        for (int index = 0; index < 11; index++) {
            int lane = index < 3 ? index + 1 : (index - 3) % 4;
            string part = index < 3 ? "" : index < 7 ? "Low" : "High";
            RegisterParameter<float>((SpotiParameters)((int)SpotiParameters.WorldJamReceive1 + index),
                "SpotiOSC/WorldJam/Receive" + part + lane, ParameterMode.Read, "World Jam Contact", "Local cooperative contact input.");
        }
        RegisterParameter<float>(SpotiParameters.WorldJamReceive, "SpotiOSC/WorldJam/Receive", ParameterMode.Read, "World Jam Receiver", "Local contact proximity input.");
        RegisterParameter<int>(SpotiParameters.WorldJamBits, "SpotiOSC/WorldJam/Bits", ParameterMode.Read, "World Jam Transport Capacity", "Local avatar transport capability.");
        RegisterParameter<bool>(SpotiParameters.WorldJamHosting, "SpotiOSC/WorldJam/Menu", ParameterMode.ReadWrite, "Share World Jam", "Advertise your jam to this VRChat instance.");
        RegisterParameter<int>(SpotiParameters.WorldJamPrompt, "SpotiOSC/WorldJam/Prompt", ParameterMode.Write, "World Jam Prompt", "Local card: 0 hidden, 1 invitation, 2 joining, 3 retry, 4 joined.");
        RegisterParameter<bool>(SpotiParameters.WorldJamJoin, "SpotiOSC/UI/Pressed/Join", ParameterMode.Read, "Join World Jam", "Accept the world jam invitation.");
        RegisterParameter<bool>(SpotiParameters.WorldJamDismiss, "SpotiOSC/UI/Pressed/Dismiss", ParameterMode.Read, "Dismiss World Jam", "Dismiss this jam without repeated prompts.");
        RegisterParameter<bool>(SpotiParameters.Enabled, "SpotiOSC/Enabled", ParameterMode.Write, "Enabled", "Set to true if the module is enabled.");
        RegisterParameter<bool>(SpotiParameters.WantJam, "SpotiOSC/WantJam", ParameterMode.ReadWrite, "Want Jam", "Set to true if you want to join a jam.");
        RegisterParameter<bool>(SpotiParameters.InAJam, "SpotiOSC/InAJam", ParameterMode.Write, "In A Jam", "Set to true if you are in a jam.");
        RegisterParameter<bool>(SpotiParameters.IsJamOwner, "SpotiOSC/IsJamOwner", ParameterMode.Write, "Is Jam Owner", "Set to true if you are the owner of the jam.");
        RegisterParameter<bool>(SpotiParameters.Error, "SpotiOSC/Error", ParameterMode.Write, "Error", "Triggered when an error occurs.");
        RegisterParameter<bool>(SpotiParameters.Touching, "SpotiOSC/Touching", ParameterMode.ReadWrite, "Touching", "Set to true when two compatible devices tap each other.");
        RegisterParameter<bool>(SpotiParameters.Play, "SpotiOSC/Play/*", ParameterMode.ReadWrite, "Play [URI]", "Resume playback or play the wildcard Spotify URI. Use context|track or context|position:N for offsets.");
        RegisterParameter<bool>(SpotiParameters.CurrentSong, "SpotiOSC/CurrentSong/*", ParameterMode.Write, "Current Song [Track URI]", "True while the wildcard track URI is playing.");
        RegisterParameter<bool>(SpotiParameters.CurrentPlaylist, "SpotiOSC/CurrentPlaylist/*", ParameterMode.Write, "Current Playlist [Context URI]", "True while the wildcard context URI is playing.");
        RegisterParameter<bool>(SpotiParameters.Pause, "SpotiOSC/Pause", ParameterMode.ReadWrite, "Pause", "Pauses playback.");
        RegisterParameter<bool>(SpotiParameters.NextTrack, "SpotiOSC/NextTrack", ParameterMode.ReadWrite, "Next Track", "Skips to the next track.");
        RegisterParameter<bool>(SpotiParameters.PreviousTrack, "SpotiOSC/PreviousTrack", ParameterMode.ReadWrite, "Previous Track", "Skips to the previous track.");
        RegisterParameter<bool>(SpotiParameters.TrackChangedEvent, "SpotiOSC/TrackChangedEvent", ParameterMode.Write, "Track Changed Event", "Triggers after a track change.");
        RegisterParameter<bool>(SpotiParameters.PlayUri, "SpotiOSC/PlayUri/*", ParameterMode.ReadWrite, "Play URI (Local)", "Launches a wildcard spotify: URI through the local system handler.");
        RegisterParameter<int>(SpotiParameters.ShuffleMode, "SpotiOSC/ShuffleMode", ParameterMode.ReadWrite, "Shuffle Mode (Mapped)", "Off=0, shuffle=1, smart shuffle=2.");
        RegisterParameter<int>(SpotiParameters.RepeatMode, "SpotiOSC/RepeatMode", ParameterMode.ReadWrite, "Repeat Mode (Mapped)", "Off=0, track=1, context=2.");
        RegisterParameter<float>(SpotiParameters.Timestamp, "SpotiOSC/Timestamp", ParameterMode.Write, "Timestamp", "Playback timestamp reported by Spotify.");
        RegisterParameter<float>(SpotiParameters.PlaybackPosition, "SpotiOSC/PlaybackPosition", ParameterMode.ReadWrite, "Playback Progress (ms)", "Playback progress in ms. Set to a nonnegative position to seek.");
        RegisterParameter<bool>(SpotiParameters.IsPlaying, "SpotiOSC/IsPlaying", ParameterMode.Write, "Is Playing", "Whether playback is active.");
        RegisterParameter<bool>(SpotiParameters.DeviceIsActive, "SpotiOSC/DeviceIsActive", ParameterMode.Write, "Device Active", "Device is active.");
        RegisterParameter<bool>(SpotiParameters.DeviceIsPrivate, "SpotiOSC/DeviceIsPrivate", ParameterMode.Write, "Private Session", "Device is in a private session.");
        RegisterParameter<bool>(SpotiParameters.DeviceIsRestricted, "SpotiOSC/DeviceIsRestricted", ParameterMode.Write, "Restricted Device", "Device is restricted.");
        RegisterParameter<bool>(SpotiParameters.DeviceSupportsVolume, "SpotiOSC/DeviceSupportsVolume", ParameterMode.Write, "Volume Support", "Device supports volume.");
        RegisterParameter<int>(SpotiParameters.DeviceVolumePercent, "SpotiOSC/Volume", ParameterMode.ReadWrite, "Device Volume (%)", "Set to 0-100 to change playback volume.");
        RegisterParameter<int>(SpotiParameters.ContextType, "SpotiOSC/ContextType", ParameterMode.Write, "Context Type (Mapped)", "Playlist=0, otherwise -1.");
        RegisterParameter<int>(SpotiParameters.DiscNumber, "SpotiOSC/DiscNumber", ParameterMode.Write, "Disc Number", "Track disc number.");
        RegisterParameter<float>(SpotiParameters.TrackDurationMs, "SpotiOSC/TrackDurationMs", ParameterMode.Write, "Track Duration (ms)", "Track duration in ms.");
        RegisterParameter<bool>(SpotiParameters.IsExplicit, "SpotiOSC/IsExplicit", ParameterMode.Write, "Explicit", "Whether the track is explicit.");
        RegisterParameter<bool>(SpotiParameters.IsLocal, "SpotiOSC/IsLocal", ParameterMode.Write, "Is Local", "Whether the track is local.");
        RegisterParameter<int>(SpotiParameters.SongPopularity, "SpotiOSC/SongPopularity", ParameterMode.Write, "Song Popularity", "Current track popularity.");
        RegisterParameter<int>(SpotiParameters.TrackNumber, "SpotiOSC/TrackNumber", ParameterMode.Write, "Track Number", "Track number.");
        RegisterParameter<int>(SpotiParameters.AlbumTotalTracks, "SpotiOSC/AlbumTotalTracks", ParameterMode.Write, "Album Total Tracks", "Album track count.");
        RegisterParameter<int>(SpotiParameters.AlbumType, "SpotiOSC/AlbumType", ParameterMode.Write, "Album Type (Mapped)", "Single=0, album=1, other=2.");
        RegisterParameter<bool>(SpotiParameters.DisallowPausing, "SpotiOSC/DisallowPausing", ParameterMode.Write, "Disallow Pausing", "Whether pausing is disallowed.");
        RegisterParameter<bool>(SpotiParameters.DisallowResuming, "SpotiOSC/DisallowResuming", ParameterMode.Write, "Disallow Resuming", "Whether resuming is disallowed.");
        RegisterParameter<bool>(SpotiParameters.DisallowSkippingPrev, "SpotiOSC/DisallowSkippingPrev", ParameterMode.Write, "Disallow Skipping Prev", "Whether previous-track is disallowed.");
        RegisterParameter<int>(SpotiParameters.JamParticipantCount, "SpotiOSC/JamParticipantCount", ParameterMode.Write, "Jam Participant Count", "Number of Jam participants.");
        RegisterParameter<int>(SpotiParameters.SessionMaxMemberCount, "SpotiOSC/SessionMaxMemberCount", ParameterMode.Write, "Session Max Member Count", "Maximum Jam participants.");
        RegisterParameter<bool>(SpotiParameters.SessionIsOwner, "SpotiOSC/SessionIsOwner", ParameterMode.Write, "Session Is Owner", "Whether the user owns the session.");
        RegisterParameter<bool>(SpotiParameters.SessionIsListening, "SpotiOSC/SessionIsListening", ParameterMode.Write, "Session Is Listening", "Whether the session is listening.");
        RegisterParameter<bool>(SpotiParameters.SessionIsControlling, "SpotiOSC/SessionIsControlling", ParameterMode.Write, "Session Is Controlling", "Whether the session is controlling.");
        RegisterParameter<bool>(SpotiParameters.QueueOnlyMode, "SpotiOSC/QueueOnlyMode", ParameterMode.Write, "Queue Only Mode", "Whether queue-only mode is active.");
        RegisterParameter<bool>(SpotiParameters.HostIsGroup, "SpotiOSC/HostIsGroup", ParameterMode.Write, "Host Is Group", "Whether the host device is a group.");
        RegisterParameter<bool>(SpotiParameters.GetTrackFeatures, "SpotiOSC/GetTrackFeatures", ParameterMode.ReadWrite, "Get Track Features", "Fetch audio features for the current track.");
        RegisterAudioFeatureParameters();
        foreach (var binding in SyncopationCoordinator.WordBindings)
        {
            var title = char.ToUpperInvariant(binding.Word[0]) + binding.Word[1..];
            RegisterParameter<bool>(binding.Sender, $"SpotiOSC/{binding.Word}", ParameterMode.ReadWrite, title, "Ephemeral Jam code word (sending).");
            RegisterParameter<bool>(binding.Receiver, $"SpotiOSC/{binding.Word}_receiver", ParameterMode.ReadWrite, $"{title} Receiver", "Ephemeral Jam code word (receiving).");
        }
        RegisterParameter<float>(SpotiParameters.AlbumColorR, "SpotiOSC/AlbumColorR", ParameterMode.Write, "Album Color R", "Dominant album red component (0-255).");
        RegisterParameter<float>(SpotiParameters.AlbumColorG, "SpotiOSC/AlbumColorG", ParameterMode.Write, "Album Color G", "Dominant album green component (0-255).");
        RegisterParameter<float>(SpotiParameters.AlbumColorB, "SpotiOSC/AlbumColorB", ParameterMode.Write, "Album Color B", "Dominant album blue component (0-255).");
    }

    private void RegisterAudioFeatureParameters()
    {
        RegisterParameter<float>(SpotiParameters.Danceability, "SpotiOSC/Danceability", ParameterMode.Write, "Danceability", "Danceability (0.0-1.0).");
        RegisterParameter<float>(SpotiParameters.Energy, "SpotiOSC/Energy", ParameterMode.Write, "Energy", "Energy (0.0-1.0).");
        RegisterParameter<int>(SpotiParameters.Key, "SpotiOSC/Key", ParameterMode.Write, "Key", "Pitch class (0-11).");
        RegisterParameter<float>(SpotiParameters.Loudness, "SpotiOSC/Loudness", ParameterMode.Write, "Loudness", "Loudness in dB.");
        RegisterParameter<int>(SpotiParameters.Mode, "SpotiOSC/Mode", ParameterMode.Write, "Mode", "Major=1, minor=0.");
        RegisterParameter<float>(SpotiParameters.Speechiness, "SpotiOSC/Speechiness", ParameterMode.Write, "Speechiness", "Speechiness (0.0-1.0).");
        RegisterParameter<float>(SpotiParameters.Acousticness, "SpotiOSC/Acousticness", ParameterMode.Write, "Acousticness", "Acousticness (0.0-1.0).");
        RegisterParameter<float>(SpotiParameters.Instrumentalness, "SpotiOSC/Instrumentalness", ParameterMode.Write, "Instrumentalness", "Instrumentalness (0.0-1.0).");
        RegisterParameter<float>(SpotiParameters.Liveness, "SpotiOSC/Liveness", ParameterMode.Write, "Liveness", "Liveness (0.0-1.0).");
        RegisterParameter<float>(SpotiParameters.Valence, "SpotiOSC/Valence", ParameterMode.Write, "Valence", "Valence (0.0-1.0).");
        RegisterParameter<float>(SpotiParameters.Tempo, "SpotiOSC/Tempo", ParameterMode.Write, "Tempo", "Tempo in BPM.");
        RegisterParameter<int>(SpotiParameters.TimeSignature, "SpotiOSC/TimeSignature", ParameterMode.Write, "Time Signature", "Estimated time signature.");
    }

    private void CreateRemainingVariables()
    {
        CreateVariable<int>("TrackDurationMs", "Track Duration (ms)");
        CreateVariable<int>("DiscNumber", "Disc Number");
        CreateVariable<bool>("IsExplicit", "Explicit");
        CreateVariable<int>("Popularity", "Popularity");
        CreateVariable<int>("TrackNumber", "Track Number");
        CreateVariable<string>("TrackUri", "Track URI");
        CreateVariable<string>("CurrentlyPlayingType", "Playing Type");
        CreateVariable<string>("AlbumArtworkUrl", "Album Artwork URL");
        CreateVariable<string>("AlbumType", "Album Type");
        CreateVariable<string>("AlbumReleaseDate", "Album Release Date");
        CreateVariable<int>("AlbumTotalTracks", "Album Total Tracks");
        CreateVariable<bool>("SmartShuffle", "Smart Shuffle");
        CreateVariable<string>("Timestamp", "Timestamp");
        CreateVariable<int>("ProgressMs", "Progress (ms)");
        CreateVariable<string>("Artists", "Artists");
        CreateVariable<string>("DeviceId", "Device ID");
        CreateVariable<bool>("IsActiveDevice", "Active Device");
        CreateVariable<string>("ContextExternalUrl", "Context URL");
        CreateVariable<string>("ContextHref", "Context Href");
        CreateVariable<string>("ContextType", "Context Type");
        CreateVariable<string>("ContextUri", "Context URI");
        CreateVariable<string>("JamShortCode", "Jam Short Code");
        CreateVariable<string>("JamOwnerName", "Jam Owner Name");
        CreateVariable<int>("JamParticipantCount", "Jam Participant Count");
        CreateVariable<int>("JamMaxMemberCount", "Jam Max Member Count");
        CreateVariable<bool>("SessionIsOwner", "Session Is Owner");
        CreateVariable<bool>("SessionIsListening", "Session Is Listening");
        CreateVariable<bool>("SessionIsControlling", "Session Is Controlling");
        CreateVariable<float>("Danceability", "Danceability");
        CreateVariable<float>("Energy", "Energy");
        CreateVariable<int>("Key", "Key");
        CreateVariable<float>("Loudness", "Loudness");
        CreateVariable<int>("Mode", "Mode");
        CreateVariable<float>("Speechiness", "Speechiness");
        CreateVariable<float>("Acousticness", "Acousticness");
        CreateVariable<float>("Instrumentalness", "Instrumentalness");
        CreateVariable<float>("Liveness", "Liveness");
        CreateVariable<float>("Valence", "Valence");
        CreateVariable<float>("Tempo", "Tempo");
        CreateVariable<int>("TimeSignature", "Time Signature");
    }

    private static readonly (string Name, string Title, string Prefix)[] StateDefinitions =
    [
        ("Playing_Jam_Explicit_Shuffle", "In a Jam!: Explicit Song + Shuffle", "State: In a Jam! (Explicit, Shuffle)\n"),
        ("Playing_Jam_Explicit_NoShuffle", "In a Jam!: Explicit Song, No Shuffle", "State: In a Jam! (Explicit, No Shuffle)\n"),
        ("Playing_Jam_Clean_Shuffle", "In a Jam!: Clean + Shuffle", "State: In a Jam! (Clean, Shuffle)\n"),
        ("Playing_Jam_Clean_NoShuffle", "In a Jam!: Clean, No Shuffle", "State: In a Jam! (Clean, No Shuffle)\n"),
        ("Paused_Jam", "In a Jam!: Paused Music", "State: In a Jam!: Paused Music\n"),
        ("Playing_Explicit_Shuffle", "Playing: Explicit Song + Shuffle", "State: Playing (Explicit, Shuffle)\n"),
        ("Playing_Explicit_NoShuffle", "Playing: Explicit Song, No Shuffle", "State: Playing (Explicit, No Shuffle)\n"),
        ("Playing_Clean_Shuffle", "Playing: Clean + Shuffle", "State: Playing (Clean, Shuffle)\n"),
        ("Playing_Clean_NoShuffle", "Playing: Clean, No Shuffle", "State: Playing (Clean, No Shuffle)\n"),
        ("Paused_Normal", "Paused Music", "State: Paused Music\n")
    ];
}
