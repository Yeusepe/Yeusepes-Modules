using DISCORDOSC.RPCTools;
using DISCORDOSC.UI;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Parameters;
using YeusepesModules.Common;
using Microsoft.Extensions.Configuration;
using System.Text.Json;


#pragma warning disable CA1416 // Validate platform compatibility

namespace YeusepesModules.DISCORDOSC
{
    [ModuleTitle("DiscordOSC")]
    [ModuleDescription("A module to control your Discord Through OSC.")]
    [ModuleType(ModuleType.Integrations)]
    [ModuleInfo("https://github.com/Yeusepe/Yeusepes-Modules/wiki/DiscordOSC")]
    public class DISCORDOSC : Module
    {
        private string clientId;
        private string clientSecret;
        BaseDiscordClient client;
        private IConfiguration config;

        private string lastGuildId = string.Empty;
        private string lastChannelId = string.Empty;
        private string defaultGuildId = string.Empty;
        private string defaultChannelId = string.Empty;
        private bool autoUpdateDefaults;

        public enum DISCORDOSCParameter
        {
            Mute,
            Deafen,
            RequestGuildCount,
            GuildCount,
            RequestChannelCount,
            ChannelCount,
            SelectVoiceChannel,
            RequestSelectedVoiceChannel,
            SelectedVoiceChannelId,
            RequestVoiceSettings,
            InputVolume,
            OutputVolume,
            SetInputVolume,
            SetOutputVolume,
            SetVoiceSettings,
            RequestChannelUserCount,
            ChannelUserCount,
            RequestChannelType,
            ChannelType,
            Ready,
            LastErrorCode,
            VoiceConnectionState,
            SubscribeEvent,
            UnsubscribeEvent,
            SelectTextChannel,
            SetUserVolume,
            SetUserMute,
            SetActivity,
            SendActivityJoinInvite,
            CloseActivityRequest,
            SendCertifiedDevices,
            RequestGuildInfo,
            GuildHasIcon,
            LastEventCode
        }

        protected override void OnPreLoad()
        {
            #region Parameters

            RegisterParameter<bool>(
                DISCORDOSCParameter.Mute,
                "VRCOSC/Discord/Mic",
                ParameterMode.ReadWrite,
                "Mute or unmute.",
                "Trigger to mute or unmute the Discord client."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.Deafen,
                "VRCOSC/Discord/Deafen",
                ParameterMode.ReadWrite,
                "Deafen or undeafen.",
                "Trigger to deafen or undeafen the Discord client."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.RequestGuildCount,
                "VRCOSC/Discord/GetGuilds",
                ParameterMode.ReadWrite,
                "Request guild list",
                "Trigger to fetch guilds and update GuildCount."
            );

            RegisterParameter<int>(
                DISCORDOSCParameter.GuildCount,
                "VRCOSC/Discord/GuildCount",
                ParameterMode.Write,
                "Guild count",
                "Number of guilds returned by GET_GUILDS."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.RequestChannelCount,
                "VRCOSC/Discord/GetChannels/*",
                ParameterMode.ReadWrite,
                "Request channel list",
                "Send guild id as wildcard to fetch channels and update ChannelCount."
            );

            RegisterParameter<int>(
                DISCORDOSCParameter.ChannelCount,
                "VRCOSC/Discord/ChannelCount",
                ParameterMode.Write,
                "Channel count",
                "Number of channels returned by GET_CHANNELS."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.SelectVoiceChannel,
                "VRCOSC/Discord/SelectVoiceChannel/*",
                ParameterMode.ReadWrite,
                "Join voice channel",
                "Send channel id as wildcard to join."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.RequestSelectedVoiceChannel,
                "VRCOSC/Discord/GetCurrentVoiceChannel",
                ParameterMode.ReadWrite,
                "Request current voice channel",
                "Fetch the id of the currently joined voice channel."
            );

            RegisterParameter<int>(
                DISCORDOSCParameter.SelectedVoiceChannelId,
                "VRCOSC/Discord/CurrentVoiceChannelId",
                ParameterMode.Write,
                "Current voice channel id",
                "ID of the current voice channel, or 0 if none."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.RequestVoiceSettings,
                "VRCOSC/Discord/GetVoiceSettings",
                ParameterMode.ReadWrite,
                "Request voice settings",
                "Fetch input and output volumes."
            );

            RegisterParameter<float>(
                DISCORDOSCParameter.InputVolume,
                "VRCOSC/Discord/InputVolume",
                ParameterMode.ReadWrite,
                "Input volume",
                "Input volume percentage."
            );

            RegisterParameter<float>(
                DISCORDOSCParameter.OutputVolume,
                "VRCOSC/Discord/OutputVolume",
                ParameterMode.ReadWrite,
                "Output volume",
                "Output volume percentage."
            );

            RegisterParameter<float>(
                DISCORDOSCParameter.SetInputVolume,
                "VRCOSC/Discord/SetInputVolume",
                ParameterMode.ReadWrite,
                "Set input volume",
                "Set the input device volume."
            );

            RegisterParameter<float>(
                DISCORDOSCParameter.SetOutputVolume,
                "VRCOSC/Discord/SetOutputVolume",
                ParameterMode.ReadWrite,
                "Set output volume",
                "Set the output device volume."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.SetVoiceSettings,
                "VRCOSC/Discord/SetVoiceSettings/*/*",
                ParameterMode.ReadWrite,
                "Set voice settings wildcard",
                "Usage: SetVoiceSettings/<field>/<value>."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.RequestChannelUserCount,
                "VRCOSC/Discord/GetChannelUsers/*",
                ParameterMode.ReadWrite,
                "Request channel user count",
                "Send channel id as wildcard to fetch user count."
            );

            RegisterParameter<int>(
                DISCORDOSCParameter.ChannelUserCount,
                "VRCOSC/Discord/ChannelUserCount",
                ParameterMode.Write,
                "Channel user count",
                "Number of users returned by GET_CHANNEL."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.RequestChannelType,
                "VRCOSC/Discord/GetChannelType/*",
                ParameterMode.ReadWrite,
                "Request channel type",
                "Send channel id as wildcard to fetch channel type."
            );

            RegisterParameter<int>(
                DISCORDOSCParameter.ChannelType,
                "VRCOSC/Discord/ChannelType",
                ParameterMode.Write,
                "Channel type",
                "Type of channel returned by GET_CHANNEL."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.Ready,
                "VRCOSC/Discord/Ready",
                ParameterMode.Write,
                "RPC ready",
                "True when the READY event is received."
            );

            RegisterParameter<int>(
                DISCORDOSCParameter.LastErrorCode,
                "VRCOSC/Discord/LastErrorCode",
                ParameterMode.Write,
                "Last error code",
                "The most recent error code from the ERROR event."
            );

            RegisterParameter<int>(
                DISCORDOSCParameter.VoiceConnectionState,
                "VRCOSC/Discord/VoiceConnectionState",
                ParameterMode.Write,
                "Voice connection state",
                "State enum from VOICE_CONNECTION_STATUS."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.SubscribeEvent,
                "VRCOSC/Discord/Subscribe/*/*",
                ParameterMode.ReadWrite,
                "Subscribe to event",
                "Wildcards: event name and optional id argument."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.UnsubscribeEvent,
                "VRCOSC/Discord/Unsubscribe/*/*",
                ParameterMode.ReadWrite,
                "Unsubscribe from event",
                "Wildcards: event name and optional id argument."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.SelectTextChannel,
                "VRCOSC/Discord/SelectTextChannel/*",
                ParameterMode.ReadWrite,
                "Join text channel",
                "Wildcard is channel id to join."
            );

            RegisterParameter<float>(
                DISCORDOSCParameter.SetUserVolume,
                "VRCOSC/Discord/UserVolume/*",
                ParameterMode.ReadWrite,
                "Set user volume",
                "Wildcard: user id; value is volume 0-200."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.SetUserMute,
                "VRCOSC/Discord/UserMute/*",
                ParameterMode.ReadWrite,
                "Mute user",
                "Wildcard: user id to mute or unmute."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.SetActivity,
                "VRCOSC/Discord/SetActivity/*/*",
                ParameterMode.ReadWrite,
                "Set Rich Presence",
                "Wildcards: state and details strings."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.SendActivityJoinInvite,
                "VRCOSC/Discord/SendJoinInvite/*",
                ParameterMode.ReadWrite,
                "Send join invite",
                "Wildcard: user id."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.CloseActivityRequest,
                "VRCOSC/Discord/CloseJoinRequest/*",
                ParameterMode.ReadWrite,
                "Close join request",
                "Wildcard: user id."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.SendCertifiedDevices,
                "VRCOSC/Discord/SendCertifiedDevices",
                ParameterMode.ReadWrite,
                "Send certified devices",
                "Trigger to send example certified device info."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.RequestGuildInfo,
                "VRCOSC/Discord/GetGuild/*",
                ParameterMode.ReadWrite,
                "Request guild info",
                "Wildcard guild id to fetch; updates GuildHasIcon."
            );

            RegisterParameter<bool>(
                DISCORDOSCParameter.GuildHasIcon,
                "VRCOSC/Discord/GuildHasIcon",
                ParameterMode.Write,
                "Guild has icon",
                "True if the requested guild has an icon."
            );

            RegisterParameter<int>(
                DISCORDOSCParameter.LastEventCode,
                "VRCOSC/Discord/LastEvent",
                ParameterMode.Write,
                "Last event code",
                "Numeric code of the most recent RPC event."
            );



            #endregion
            #region Settings
            CreateTextBox(DiscordSetting.DefaultGuildId, "Default Guild ID", "Guild ID used for subscriptions", string.Empty);
            CreateTextBox(DiscordSetting.DefaultChannelId, "Default Channel ID", "Channel ID used for subscriptions", string.Empty);
            CreateToggle(DiscordSetting.AutoUpdateDefaults, "Auto Update Defaults", "Automatically update default guild and channel when joining a voice channel", false);

            CreateTextBox(DiscordSetting.ClientId, "Client ID", "Discord Client ID", string.Empty);
            CreatePasswordTextBox(DiscordSetting.ClientSecret, "Client Secret", "Discord Client Secret", string.Empty);

            CreateGroup("Discord App Secrets", DiscordSetting.ClientId, DiscordSetting.ClientSecret);
            #endregion

            config = new ConfigurationBuilder()
                        .AddUserSecrets<DISCORDOSC>()
                        .Build();

            base.OnPreLoad();
        }

        protected override void OnPostLoad()
        {
            var guildVar = CreateVariable<int>("GuildCount", "Guild Count");
            var channelVar = CreateVariable<int>("ChannelCount", "Channel Count");
            var voiceIdVar = CreateVariable<int>("SelectedVoiceChannelId", "Voice Channel Id");
            var inputVar = CreateVariable<float>("InputVolume", "Input Volume");
            var outputVar = CreateVariable<float>("OutputVolume", "Output Volume");
            var userCountVar = CreateVariable<int>("ChannelUserCount", "Channel User Count");
            var typeVar = CreateVariable<int>("ChannelType", "Channel Type");
            var readyVar = CreateVariable<bool>("Ready", "Ready Event");
            var errorVar = CreateVariable<int>("LastErrorCode", "Error Code");
            var voiceStateVar = CreateVariable<int>("VoiceConnectionState", "Voice Conn State");
            var eventGuildVar = CreateVariable<int>("EventGuildId", "Event Guild Id");
            var eventChannelVar = CreateVariable<int>("EventChannelId", "Event Channel Id");
            var eventUserVar = CreateVariable<int>("EventUserId", "Event User Id");
            var eventMessageVar = CreateVariable<int>("EventMessageId", "Event Message Id");
            var lastEventVar = CreateVariable<int>("LastEventCode", "Last Event Code");

            CreateState("VoiceState", "Voice Connection State", "State: {0}", new[] { voiceStateVar });
            CreateEvent("ReadyEvent", "Ready Event", "RPC Ready", new[] { readyVar });
            CreateEvent("ErrorEvent", "Error Event", "Error code {0}", new[] { errorVar });
            CreateEvent("GuildStatusEvent", "Guild Status", "Guild {0}", new[] { eventGuildVar });
            CreateEvent("GuildCreateEvent", "Guild Created", "Guild {0}", new[] { eventGuildVar });
            CreateEvent("ChannelCreateEvent", "Channel Created", "Channel {0}", new[] { eventChannelVar });
            CreateEvent("VoiceStateCreateEvent", "Voice Join", "User {0}", new[] { eventUserVar });
            CreateEvent("VoiceStateUpdateEvent", "Voice Update", "User {0}", new[] { eventUserVar });
            CreateEvent("VoiceStateDeleteEvent", "Voice Leave", "User {0}", new[] { eventUserVar });
            CreateEvent("VoiceSettingsEvent", "Voice Settings", "Volumes", new[] { inputVar });
            CreateEvent("SpeakingStartEvent", "Speaking Start", "User {0}", new[] { eventUserVar });
            CreateEvent("SpeakingStopEvent", "Speaking Stop", "User {0}", new[] { eventUserVar });
            CreateEvent("MessageCreateEvent", "Message Created", "Msg {0}", new[] { eventMessageVar });
            CreateEvent("MessageUpdateEvent", "Message Updated", "Msg {0}", new[] { eventMessageVar });
            CreateEvent("MessageDeleteEvent", "Message Deleted", "Msg {0}", new[] { eventMessageVar });
            CreateEvent("NotificationEvent", "Notification", "Chan {0}", new[] { eventChannelVar });
            CreateEvent("ActivityJoinEvent", "Activity Join", "Join", null);
            CreateEvent("ActivitySpectateEvent", "Activity Spectate", "Spectate", null);
            CreateEvent("ActivityJoinRequestEvent", "Join Request", "User {0}", new[] { eventUserVar });
        }



        private enum DiscordSetting
        {
            ClientId,
            ClientSecret,
            DefaultGuildId,
            DefaultChannelId,
            AutoUpdateDefaults
        }

        protected override Task<bool> OnModuleStart()
        {
            client = new BaseDiscordClient();

            // Fallback to hardcoded defaults if settings are empty
            clientId = GetSettingValue<string>(DiscordSetting.ClientId);
            clientSecret = GetSettingValue<string>(DiscordSetting.ClientSecret);
            defaultGuildId = GetSettingValue<string>(DiscordSetting.DefaultGuildId);
            defaultChannelId = GetSettingValue<string>(DiscordSetting.DefaultChannelId);

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                clientId = "1316146450681303071";
                clientSecret = "cTZvtl89suuCa41EaGu8MFrhDfagtt_5";
                LogDebug("ClientId was empty or clientSecret was empty. Using defaults.");
            }

            return Task.Run(async () =>
            {
                try
                {
                    var auth = new DiscordAuth(clientId, clientSecret);
                    string accessToken = await auth.FetchAccessTokenAsync();
                    LogDebug("Access token retrieved successfully.");

                    // Attempt to connect to any available Discord IPC pipe
                    bool connected = false;
                    Log("Attempting to connect to Discord...");
                    for (int i = 0; i < 10; i++) // Attempt discord-ipc-0 through discord-ipc-9
                    {
                        string pipeName = $"discord-ipc-{i}";
                        LogDebug($"Attempting to connect to {pipeName}...");

                        var connectTask = Task.Run(() => client.Connect(pipeName)); // Start the connect task
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2)); // 2-second timeout

                        var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                        if (completedTask == connectTask) // Connection completed successfully
                        {
                            try
                            {
                                await connectTask; // Ensure no exceptions occurred
                                connected = true;
                                Log($"Successfully connected to {pipeName}");
                                break;
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"Error connecting to {pipeName}: {ex.Message}");
                            }
                        }
                        else // Timeout
                        {
                            LogDebug($"Connection attempt to {pipeName} timed out.");
                        }
                    }

                    if (!connected)
                    {
                        Log("Failed to connect to Discord IPC pipes. Make sure Discord is running.");
                        return false;
                    }

                    // Timeout for the handshake process
                    var handshakeTask = Task.Run(() => client.Handshake(clientId));
                    var handshakeTimeout = Task.Delay(TimeSpan.FromSeconds(3)); // 3-second timeout for handshake

                    var completedHandshake = await Task.WhenAny(handshakeTask, handshakeTimeout);
                    if (completedHandshake == handshakeTask)
                    {
                        var handshakeResponse = await handshakeTask;
                        LogDebug("Handshake completed successfully.");
                    }
                    else
                    {
                        Log("Handshake timed out. Try restarting discord.");
                        return false;
                    }

                    // Timeout for the authentication process
                    var authPayload = Payload.Authenticate(accessToken);
                    var authTask = Task.Run(() => client.SendDataAndWait(1, authPayload));
                    var authTimeout = Task.Delay(TimeSpan.FromSeconds(3)); // 3-second timeout for authentication

                    var completedAuth = await Task.WhenAny(authTask, authTimeout);
                    if (completedAuth == authTask)
                    {
                        var authResponse = await authTask;
                        Log("Authenticated successfully!");
                        // Subscribe to common events
                        LogDebug("Subscribing to READY");
                        client.SendDataAndWait(1, Payload.Subscribe("READY"));
                        LogDebug("Subscribing to ERROR");
                        client.SendDataAndWait(1, Payload.Subscribe("ERROR"));
                        LogDebug("Subscribing to VOICE_CHANNEL_SELECT");
                        client.SendDataAndWait(1, Payload.Subscribe("VOICE_CHANNEL_SELECT"));
                        LogDebug("Subscribing to VOICE_SETTINGS_UPDATE");
                        client.SendDataAndWait(1, Payload.Subscribe("VOICE_SETTINGS_UPDATE"));
                        LogDebug("Subscribing to VOICE_CONNECTION_STATUS");
                        client.SendDataAndWait(1, Payload.Subscribe("VOICE_CONNECTION_STATUS"));

                        if (!string.IsNullOrEmpty(defaultGuildId))
                        {
                            LogDebug($"Subscribing to GUILD_STATUS for {defaultGuildId}");
                            client.SendDataAndWait(1, Payload.Subscribe("GUILD_STATUS", new { guild_id = defaultGuildId }));
                        }
                        else
                        {
                            LogDebug("DefaultGuildId empty; skipping GUILD_STATUS subscription");
                        }

                        client.SendDataAndWait(1, Payload.Subscribe("GUILD_CREATE"));
                        client.SendDataAndWait(1, Payload.Subscribe("CHANNEL_CREATE"));

                        if (!string.IsNullOrEmpty(defaultChannelId))
                        {
                            LogDebug($"Subscribing to voice and message events for channel {defaultChannelId}");
                            var chArgs = new { channel_id = defaultChannelId };
                            client.SendDataAndWait(1, Payload.Subscribe("VOICE_STATE_CREATE", chArgs));
                            client.SendDataAndWait(1, Payload.Subscribe("VOICE_STATE_UPDATE", chArgs));
                            client.SendDataAndWait(1, Payload.Subscribe("VOICE_STATE_DELETE", chArgs));
                            client.SendDataAndWait(1, Payload.Subscribe("SPEAKING_START", chArgs));
                            client.SendDataAndWait(1, Payload.Subscribe("SPEAKING_STOP", chArgs));
                            client.SendDataAndWait(1, Payload.Subscribe("MESSAGE_CREATE", chArgs));
                            client.SendDataAndWait(1, Payload.Subscribe("MESSAGE_UPDATE", chArgs));
                            client.SendDataAndWait(1, Payload.Subscribe("MESSAGE_DELETE", chArgs));
                        }
                        else
                        {
                            LogDebug("DefaultChannelId empty; skipping channel-specific subscriptions");
                        }

                        client.SendDataAndWait(1, Payload.Subscribe("NOTIFICATION_CREATE"));
                        client.SendDataAndWait(1, Payload.Subscribe("ACTIVITY_JOIN"));
                        client.SendDataAndWait(1, Payload.Subscribe("ACTIVITY_SPECTATE"));
                        client.SendDataAndWait(1, Payload.Subscribe("ACTIVITY_JOIN_REQUEST"));

                        client.StartListening(HandleRpcEvent);
                    }
                    else
                    {
                        Log("Authentication timed out. Try restarting discord.");
                        return false;
                    }

                    LogDebug("Discord Module started!");
                    return true;
                }
                catch (Exception ex)
                {
                    Log("Error during module start: " + ex.Message);
                    return false;
                }
            });


        }



        protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
        {
            switch (parameter.Lookup)
            {
                case DISCORDOSCParameter.Mute:
                    {
                        bool mute = parameter.GetValue<bool>();
                        client.SendCommand(1, Payload.SetMuteOnly(mute));
                        break;
                    }

                case DISCORDOSCParameter.Deafen:
                    {
                        bool deafen = parameter.GetValue<bool>();
                        client.SendCommand(1, Payload.SetDeafenOnly(deafen));
                        break;
                    }

                case DISCORDOSCParameter.RequestGuildCount:
                    {
                        if (parameter.GetValue<bool>())
                            client.SendCommand(1, Payload.GetGuilds());
                        break;
                    }

                case DISCORDOSCParameter.RequestChannelCount:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0))
                        {
                            string guildId = parameter.GetWildcard<string>(0);
                            client.SendCommand(1, Payload.GetChannels(guildId));
                        }
                        break;
                    }

                case DISCORDOSCParameter.SelectVoiceChannel:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0))
                        {
                            string channelId = parameter.GetWildcard<string>(0);
                            client.SendCommand(1, Payload.SelectVoiceChannel(channelId));
                        }
                        break;
                    }

                case DISCORDOSCParameter.RequestSelectedVoiceChannel:
                    {
                        if (parameter.GetValue<bool>())
                            client.SendCommand(1, Payload.GetSelectedVoiceChannel());
                        break;
                    }

                case DISCORDOSCParameter.RequestVoiceSettings:
                    {
                        if (parameter.GetValue<bool>())
                            client.SendCommand(1, Payload.GetVoiceSettings());
                        break;
                    }

                case DISCORDOSCParameter.InputVolume:
                    {
                        float vol = parameter.GetValue<float>();
                        var args = new Dictionary<string, object> { ["input"] = new { volume = vol } };
                        client.SendCommand(1, Payload.SetVoiceSettings(args));
                        break;
                    }

                case DISCORDOSCParameter.OutputVolume:
                    {
                        float vol = parameter.GetValue<float>();
                        var args = new Dictionary<string, object> { ["output"] = new { volume = vol } };
                        client.SendCommand(1, Payload.SetVoiceSettings(args));
                        break;
                    }

                case DISCORDOSCParameter.SetInputVolume:
                    {
                        float vol = parameter.GetValue<float>();
                        var args = new Dictionary<string, object> { ["input"] = new { volume = vol } };
                        client.SendCommand(1, Payload.SetVoiceSettings(args));
                        break;
                    }

                case DISCORDOSCParameter.SetOutputVolume:
                    {
                        float vol = parameter.GetValue<float>();
                        var args = new Dictionary<string, object> { ["output"] = new { volume = vol } };
                        client.SendCommand(1, Payload.SetVoiceSettings(args));
                        break;
                    }

                case DISCORDOSCParameter.SetVoiceSettings:
                    {
                        if (!parameter.IsWildcardType<string>(0)) break;
                        string field = parameter.GetWildcard<string>(0).ToLower();
                        float? numVal = parameter.IsWildcardType<float>(1) ? parameter.GetWildcard<float>(1)
                                         : parameter.IsWildcardType<int>(1) ? parameter.GetWildcard<int>(1)
                                         : null;
                        bool? boolVal = numVal == null ? (bool?)parameter.GetValue<bool>() : null;

                        var args = new Dictionary<string, object>();
                        switch (field)
                        {
                            case "mute": args["mute"] = boolVal ?? numVal != 0; break;
                            case "deaf": args["deaf"] = boolVal ?? numVal != 0; break;
                            case "input_volume":
                            case "inputvolume":
                                if (numVal.HasValue) args["input"] = new { volume = numVal.Value };
                                break;
                            case "output_volume":
                            case "outputvolume":
                                if (numVal.HasValue) args["output"] = new { volume = numVal.Value };
                                break;
                            case "automatic_gain_control":
                            case "agc":
                                args["automatic_gain_control"] = boolVal ?? numVal != 0; break;
                            case "echo_cancellation":
                                args["echo_cancellation"] = boolVal ?? numVal != 0; break;
                            case "noise_suppression":
                                args["noise_suppression"] = boolVal ?? numVal != 0; break;
                            case "qos":
                                args["qos"] = boolVal ?? numVal != 0; break;
                            case "silence_warning":
                                args["silence_warning"] = boolVal ?? numVal != 0; break;
                        }

                        if (args.Count > 0)
                            client.SendCommand(1, Payload.SetVoiceSettings(args));
                        break;
                    }

                case DISCORDOSCParameter.RequestChannelUserCount:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0))
                        {
                            string channelId = parameter.GetWildcard<string>(0);
                            client.SendCommand(1, Payload.GetChannel(channelId));
                        }
                        break;
                    }

                case DISCORDOSCParameter.RequestChannelType:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0))
                        {
                            string channelId = parameter.GetWildcard<string>(0);
                            client.SendCommand(1, Payload.GetChannel(channelId));
                        }
                        break;
                    }

                case DISCORDOSCParameter.SubscribeEvent:
                    {
                        if (!parameter.GetValue<bool>() || !parameter.IsWildcardType<string>(0)) break;
                        string evt = parameter.GetWildcard<string>(0);
                        object args = null;
                        if (parameter.IsWildcardType<string>(1))
                        {
                            string id = parameter.GetWildcard<string>(1);
                            if (evt == "GUILD_STATUS")
                                args = new { guild_id = id };
                            else if (evt.StartsWith("VOICE_STATE") || evt.StartsWith("MESSAGE_") || evt is "SPEAKING_START" or "SPEAKING_STOP")
                                args = new { channel_id = id };
                        }
                        client.SendCommand(1, Payload.Subscribe(evt, args));
                        break;
                    }

                case DISCORDOSCParameter.UnsubscribeEvent:
                    {
                        if (!parameter.GetValue<bool>() || !parameter.IsWildcardType<string>(0)) break;
                        string evt = parameter.GetWildcard<string>(0);
                        object args = null;
                        if (parameter.IsWildcardType<string>(1))
                        {
                            string id = parameter.GetWildcard<string>(1);
                            if (evt == "GUILD_STATUS")
                                args = new { guild_id = id };
                            else if (evt.StartsWith("VOICE_STATE") || evt.StartsWith("MESSAGE_") || evt is "SPEAKING_START" or "SPEAKING_STOP")
                                args = new { channel_id = id };
                        }
                        client.SendCommand(1, Payload.Unsubscribe(evt, args));
                        break;
                    }

                case DISCORDOSCParameter.SelectTextChannel:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0))
                        {
                            string textChannel = parameter.GetWildcard<string>(0);
                            client.SendCommand(1, Payload.SelectTextChannel(textChannel));
                        }
                        break;
                    }

                case DISCORDOSCParameter.SetUserVolume:
                    {
                        if (!parameter.IsWildcardType<string>(0)) break;
                        string userVolId = parameter.GetWildcard<string>(0);
                        float vol = parameter.GetValue<float>();
                        client.SendCommand(1, Payload.SetUserVoiceSettings(userVolId, volume: (int)vol));
                        break;
                    }

                case DISCORDOSCParameter.SetUserMute:
                    {
                        if (!parameter.IsWildcardType<string>(0)) break;
                        string userMuteId = parameter.GetWildcard<string>(0);
                        bool muteUser = parameter.GetValue<bool>();
                        client.SendCommand(1, Payload.SetUserVoiceSettings(userMuteId, mute: muteUser));
                        break;
                    }

                case DISCORDOSCParameter.SetActivity:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0) && parameter.IsWildcardType<string>(1))
                        {
                            string state = parameter.GetWildcard<string>(0);
                            string details = parameter.GetWildcard<string>(1);
                            client.SendCommand(1, Payload.SetActivity(state, details, string.Empty, string.Empty));
                        }
                        break;
                    }

                case DISCORDOSCParameter.SendActivityJoinInvite:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0))
                        {
                            string invitee = parameter.GetWildcard<string>(0);
                            client.SendCommand(1, Payload.SendActivityJoinInvite(invitee));
                        }
                        break;
                    }

                case DISCORDOSCParameter.CloseActivityRequest:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0))
                        {
                            string requester = parameter.GetWildcard<string>(0);
                            client.SendCommand(1, Payload.CloseActivityRequest(requester));
                        }
                        break;
                    }

                case DISCORDOSCParameter.SendCertifiedDevices:
                    {
                        if (parameter.GetValue<bool>())
                        {
                            var devices = new object[]
                            {
                    new {
                        type = "audioinput",
                        id = Guid.NewGuid().ToString(),
                        vendor = new { name = "Generic", url = "https://localhost" },
                        model  = new { name = "Example", url = "https://localhost" },
                        related = Array.Empty<string>(),
                        echo_cancellation       = true,
                        noise_suppression       = true,
                        automatic_gain_control  = true,
                        hardware_mute           = false
                    }
                            };
                            client.SendCommand(1, Payload.SetCertifiedDevices(devices));
                        }
                        break;
                    }

                case DISCORDOSCParameter.RequestGuildInfo:
                    {
                        if (parameter.GetValue<bool>() && parameter.IsWildcardType<string>(0))
                        {
                            string gid = parameter.GetWildcard<string>(0);
                            client.SendCommand(1, Payload.GetGuild(gid));
                        }
                        break;
                    }

                default:
                    // Unhandled parameter
                    break;
            }
        }


        [ModuleUpdate(ModuleUpdateMode.ChatBox)]
        private void ChatBoxUpdate()
        {
            try
            {
                var resp = client.SendDataAndWait(1, Payload.GetGuilds());
                if (!string.IsNullOrWhiteSpace(resp))
                {
                    var doc = JsonDocument.Parse(resp);
                    var guilds = doc.RootElement.GetProperty("data").GetProperty("guilds");
                    SetVariableValue("GuildCount", guilds.GetArrayLength());
                }
            }
            catch (Exception ex)
            {
                LogDebug($"ChatBox GET_GUILDS failed: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(lastGuildId))
            {
                try
                {
                    var resp = client.SendDataAndWait(1, Payload.GetChannels(lastGuildId));
                    if (!string.IsNullOrWhiteSpace(resp))
                    {
                        var doc = JsonDocument.Parse(resp);
                        var ch = doc.RootElement.GetProperty("data").GetProperty("channels");
                        SetVariableValue("ChannelCount", ch.GetArrayLength());
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"ChatBox GET_CHANNELS failed: {ex.Message}");
                }
            }

            try
            {
                var resp = client.SendDataAndWait(1, Payload.GetSelectedVoiceChannel());
                if (!string.IsNullOrWhiteSpace(resp))
                {
                    var doc = JsonDocument.Parse(resp);
                    if (doc.RootElement.GetProperty("data").ValueKind == JsonValueKind.Null)
                    {
                        SetVariableValue("SelectedVoiceChannelId", 0);
                    }
                    else
                    {
                        string idStr = doc.RootElement.GetProperty("data").GetProperty("id").GetString();
                        lastChannelId = idStr;
                        if (long.TryParse(idStr, out long id))
                            SetVariableValue("SelectedVoiceChannelId", unchecked((int)id));
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"ChatBox GET_SELECTED_VOICE_CHANNEL failed: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(lastChannelId))
            {
                try
                {
                    var resp = client.SendDataAndWait(1, Payload.GetChannel(lastChannelId));
                    if (!string.IsNullOrWhiteSpace(resp))
                    {
                        var doc = JsonDocument.Parse(resp);
                        var data = doc.RootElement.GetProperty("data");
                        SetVariableValue("ChannelUserCount", data.GetProperty("voice_states").GetArrayLength());
                        SetVariableValue("ChannelType", data.GetProperty("type").GetInt32());
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"ChatBox GET_CHANNEL failed: {ex.Message}");
                }
            }

            try
            {
                var resp = client.SendDataAndWait(1, Payload.GetVoiceSettings());
                if (!string.IsNullOrWhiteSpace(resp))
                {
                    var doc = JsonDocument.Parse(resp);
                    var data = doc.RootElement.GetProperty("data");
                    SetVariableValue("InputVolume", data.GetProperty("input").GetProperty("volume").GetSingle());
                    SetVariableValue("OutputVolume", data.GetProperty("output").GetProperty("volume").GetSingle());
                }
            }
            catch (Exception ex)
            {
                LogDebug($"ChatBox GET_VOICE_SETTINGS failed: {ex.Message}");
            }

            ChangeState("VoiceState");
        }

        private static int VoiceStateToInt(string state)
        {
            return state switch
            {
                "DISCONNECTED" => 0,
                "AWAITING_ENDPOINT" => 1,
                "AUTHENTICATING" => 2,
                "CONNECTING" => 3,
                "CONNECTED" => 4,
                "VOICE_DISCONNECTED" => 5,
                "VOICE_CONNECTING" => 6,
                "VOICE_CONNECTED" => 7,
                "NO_ROUTE" => 8,
                "ICE_CHECKING" => 9,
                _ => -1
            };
        }

        private static int EventNameToInt(string evtName)
        {
            return evtName switch
            {
                "READY" => 0,
                "ERROR" => 1,
                "GUILD_STATUS" => 2,
                "GUILD_CREATE" => 3,
                "CHANNEL_CREATE" => 4,
                "VOICE_CHANNEL_SELECT" => 5,
                "VOICE_STATE_CREATE" => 6,
                "VOICE_STATE_UPDATE" => 7,
                "VOICE_STATE_DELETE" => 8,
                "VOICE_SETTINGS_UPDATE" => 9,
                "VOICE_CONNECTION_STATUS" => 10,
                "SPEAKING_START" => 11,
                "SPEAKING_STOP" => 12,
                "MESSAGE_CREATE" => 13,
                "MESSAGE_UPDATE" => 14,
                "MESSAGE_DELETE" => 15,
                "NOTIFICATION_CREATE" => 16,
                "ACTIVITY_JOIN" => 17,
                "ACTIVITY_SPECTATE" => 18,
                "ACTIVITY_JOIN_REQUEST" => 19,
                _ => -1
            };
        }

        private void HandleRpcEvent(JsonElement evt)
        {
            try
            {
                LogDebug($"Event received: {evt}");
                if (!evt.TryGetProperty("evt", out var evtNameEl)) return;
                string evtName = evtNameEl.GetString();
                int evtCode = EventNameToInt(evtName);
                SendParameter(DISCORDOSCParameter.LastEventCode, evtCode);
                SetVariableValue("LastEventCode", evtCode);

                switch (evtName)
                {
                    case "READY":
                        SendParameter(DISCORDOSCParameter.Ready, true);
                        SetVariableValue("Ready", true);
                        TriggerEvent("ReadyEvent");
                        break;
                    case "ERROR":
                        if (evt.TryGetProperty("data", out var err) && err.TryGetProperty("code", out var codeEl))
                        {
                            int code = codeEl.GetInt32();
                            SendParameter(DISCORDOSCParameter.LastErrorCode, code);
                            SetVariableValue("LastErrorCode", code);
                            TriggerEvent("ErrorEvent");
                        }
                        break;
                    case "VOICE_CHANNEL_SELECT":
                        if (evt.TryGetProperty("data", out var vc) && vc.TryGetProperty("channel_id", out var cid))
                        {
                            string idStr = cid.GetString();
                            lastChannelId = idStr;
                            if (long.TryParse(idStr, out long id))
                            {
                                int val = unchecked((int)id);
                                SendParameter(DISCORDOSCParameter.SelectedVoiceChannelId, val);
                                SetVariableValue("SelectedVoiceChannelId", val);
                            }
                            if (autoUpdateDefaults)
                            {
                                SetSettingValue(DiscordSetting.DefaultChannelId, idStr);
                                defaultChannelId = idStr;
                                if (vc.TryGetProperty("guild_id", out var gidEl) && gidEl.ValueKind != JsonValueKind.Null)
                                {
                                    string gidStr = gidEl.GetString();
                                    SetSettingValue(DiscordSetting.DefaultGuildId, gidStr);
                                    defaultGuildId = gidStr;
                                }
                            }
                        }
                        break;
                    case "VOICE_SETTINGS_UPDATE":
                        if (evt.TryGetProperty("data", out var vs))
                        {
                            float inVol = vs.GetProperty("input").GetProperty("volume").GetSingle();
                            float outVol = vs.GetProperty("output").GetProperty("volume").GetSingle();
                            SendParameter(DISCORDOSCParameter.InputVolume, inVol);
                            SendParameter(DISCORDOSCParameter.OutputVolume, outVol);
                            SetVariableValue("InputVolume", inVol);
                            SetVariableValue("OutputVolume", outVol);
                        }
                        break;
                    case "VOICE_CONNECTION_STATUS":
                        if (evt.TryGetProperty("data", out var vcstat) && vcstat.TryGetProperty("state", out var stateEl))
                        {
                            int state = VoiceStateToInt(stateEl.GetString());
                            SendParameter(DISCORDOSCParameter.VoiceConnectionState, state);
                            SetVariableValue("VoiceConnectionState", state);
                        }
                        break;
                    case "GUILD_STATUS":
                        if (evt.TryGetProperty("data", out var gs) && gs.TryGetProperty("guild", out var g) && g.TryGetProperty("id", out var gidElStatus))
                        {
                            if (long.TryParse(gidElStatus.GetString(), out long gid))
                            {
                                SetVariableValue("EventGuildId", unchecked((int)gid));
                                TriggerEvent("GuildStatusEvent");
                            }
                        }
                        break;
                    case "GUILD_CREATE":
                        if (evt.TryGetProperty("data", out var gc) && gc.TryGetProperty("id", out var gidc))
                        {
                            if (long.TryParse(gidc.GetString(), out long gid))
                            {
                                SetVariableValue("EventGuildId", unchecked((int)gid));
                                TriggerEvent("GuildCreateEvent");
                            }
                        }
                        break;
                    case "CHANNEL_CREATE":
                        if (evt.TryGetProperty("data", out var ch) && ch.TryGetProperty("id", out var cidEl))
                        {
                            if (long.TryParse(cidEl.GetString(), out long cidv))
                            {
                                SetVariableValue("EventChannelId", unchecked((int)cidv));
                                TriggerEvent("ChannelCreateEvent");
                            }
                        }
                        break;
                    case "VOICE_STATE_CREATE":
                    case "VOICE_STATE_UPDATE":
                    case "VOICE_STATE_DELETE":
                        if (evt.TryGetProperty("data", out var vsd) && vsd.TryGetProperty("user", out var usr) && usr.TryGetProperty("id", out var uid))
                        {
                            if (long.TryParse(uid.GetString(), out long uidv))
                            {
                                SetVariableValue("EventUserId", unchecked((int)uidv));
                                string evname = evtName switch
                                {
                                    "VOICE_STATE_CREATE" => "VoiceStateCreateEvent",
                                    "VOICE_STATE_UPDATE" => "VoiceStateUpdateEvent",
                                    _ => "VoiceStateDeleteEvent"
                                };
                                TriggerEvent(evname);
                            }
                        }
                        break;
                    case "SPEAKING_START":
                    case "SPEAKING_STOP":
                        if (evt.TryGetProperty("data", out var sp) && sp.TryGetProperty("user_id", out var suid))
                        {
                            if (long.TryParse(suid.GetString(), out long uidv))
                            {
                                SetVariableValue("EventUserId", unchecked((int)uidv));
                                TriggerEvent(evtName == "SPEAKING_START" ? "SpeakingStartEvent" : "SpeakingStopEvent");
                            }
                        }
                        break;
                    case "MESSAGE_CREATE":
                    case "MESSAGE_UPDATE":
                    case "MESSAGE_DELETE":
                        if (evt.TryGetProperty("data", out var msg) && msg.TryGetProperty("message", out var m) && m.TryGetProperty("id", out var mid))
                        {
                            if (long.TryParse(mid.GetString(), out long midv))
                            {
                                SetVariableValue("EventMessageId", unchecked((int)midv));
                                string evname = evtName switch
                                {
                                    "MESSAGE_CREATE" => "MessageCreateEvent",
                                    "MESSAGE_UPDATE" => "MessageUpdateEvent",
                                    _ => "MessageDeleteEvent"
                                };
                                TriggerEvent(evname);
                            }
                        }
                        break;
                    case "NOTIFICATION_CREATE":
                        if (evt.TryGetProperty("data", out var noti) && noti.TryGetProperty("channel_id", out var nch))
                        {
                            if (long.TryParse(nch.GetString(), out long nchv))
                            {
                                SetVariableValue("EventChannelId", unchecked((int)nchv));
                                TriggerEvent("NotificationEvent");
                            }
                        }
                        break;
                    case "ACTIVITY_JOIN":
                        TriggerEvent("ActivityJoinEvent");
                        break;
                    case "ACTIVITY_SPECTATE":
                        TriggerEvent("ActivitySpectateEvent");
                        break;
                    case "ACTIVITY_JOIN_REQUEST":
                        if (evt.TryGetProperty("data", out var aj) && aj.TryGetProperty("user", out var aju) && aju.TryGetProperty("id", out var ajuId))
                        {
                            if (long.TryParse(ajuId.GetString(), out long uidv))
                            {
                                SetVariableValue("EventUserId", unchecked((int)uidv));
                                TriggerEvent("ActivityJoinRequestEvent");
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to handle RPC event: {ex.Message}");
            }
        }

    }
}
