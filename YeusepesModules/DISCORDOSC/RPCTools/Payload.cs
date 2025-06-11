using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DISCORDOSC.RPCTools
{
    public class Payload
    {
        // Create a payload to authenticate
        public static object Authenticate(string accessToken)
        {
            return new
            {
                cmd = "AUTHENTICATE",
                args = new { access_token = accessToken },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Create a payload to set voice settings
        public static object SetVoiceSettings(bool mute, string inputDeviceId = "default", int inputVolume = 100, string outputDeviceId = "default", int outputVolume = 100, string modeType = "VOICE_ACTIVITY", bool autoThreshold = true)
        {
            return new
            {
                cmd = "SET_VOICE_SETTINGS",
                args = new
                {
                    input = new { device_id = inputDeviceId, volume = inputVolume },
                    output = new { device_id = outputDeviceId, volume = outputVolume },
                    mode = new { type = modeType, auto_threshold = autoThreshold },
                    automatic_gain_control = false,
                    echo_cancellation = false,
                    noise_suppression = false,
                    qos = false,
                    silence_warning = false,
                    deaf = false,
                    mute = mute
                },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Minimal payload for muting only
        public static object SetMuteOnly(bool mute)
        {
            return new
            {
                cmd = "SET_VOICE_SETTINGS",
                args = new
                {
                    mute = mute
                },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Create a payload to fetch voice settings dynamically
        public static object GetVoiceSettings()
        {
            return new
            {
                cmd = "GET_VOICE_SETTINGS",
                args = new { },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Set user activity
        public static object SetActivity(string state, string details, string largeImageKey, string smallImageKey)
        {
            return new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Environment.ProcessId,
                    activity = new
                    {
                        state = state,
                        details = details,
                        assets = new
                        {
                            large_image = largeImageKey,
                            small_image = smallImageKey
                        }
                    }
                },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Get guilds
        public static object GetGuilds()
        {
            return new
            {
                cmd = "GET_GUILDS",
                args = new { },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Get a specific guild
        public static object GetGuild(string guildId)
        {
            return new
            {
                cmd = "GET_GUILD",
                args = new { guild_id = guildId },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Get channels of a guild
        public static object GetChannels(string guildId)
        {
            return new
            {
                cmd = "GET_CHANNELS",
                args = new { guild_id = guildId },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Select a voice channel
        public static object SelectVoiceChannel(string channelId)
        {
            return new
            {
                cmd = "SELECT_VOICE_CHANNEL",
                args = new { channel_id = channelId },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Subscribe to an event
        public static object Subscribe(string eventName)
        {
            return Subscribe(eventName, null);
        }

        public static object Subscribe(string eventName, object? args)
        {
            return new
            {
                cmd = "SUBSCRIBE",                
                args = args ?? new { },
                evt = eventName,
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Unsubscribe from an event
        public static object Unsubscribe(string eventName)
        {
            return Unsubscribe(eventName, null);
        }

        public static object Unsubscribe(string eventName, object? args)
        {
            return new
            {
                cmd = "UNSUBSCRIBE",                
                args = args ?? new { },
                evt = eventName,
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Minimal payload for deafening only
        public static object SetDeafenOnly(bool deafen)
        {
            return new
            {
                cmd = "SET_VOICE_SETTINGS",
                args = new
                {
                    deaf = deafen
                },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Get information about a channel
        public static object GetChannel(string channelId)
        {
            return new
            {
                cmd = "GET_CHANNEL",
                args = new { channel_id = channelId },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Request currently selected voice channel
        public static object GetSelectedVoiceChannel()
        {
            return new
            {
                cmd = "GET_SELECTED_VOICE_CHANNEL",
                args = new { },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Select a text channel
        public static object SelectTextChannel(string channelId)
        {
            return new
            {
                cmd = "SELECT_TEXT_CHANNEL",
                args = new { channel_id = channelId },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Modify other user's voice settings
        public static object SetUserVoiceSettings(string userId, float? left = null, float? right = null, int? volume = null, bool? mute = null)
        {
            object pan = null;
            if (left.HasValue && right.HasValue)
            {
                pan = new { left = left.Value, right = right.Value };
            }

            return new
            {
                cmd = "SET_USER_VOICE_SETTINGS",
                args = new
                {
                    user_id = userId,
                    pan,
                    volume,
                    mute
                },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Send a join invite approval
        public static object SendActivityJoinInvite(string userId)
        {
            return new
            {
                cmd = "SEND_ACTIVITY_JOIN_INVITE",
                args = new { user_id = userId },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Close a join request
        public static object CloseActivityRequest(string userId)
        {
            return new
            {
                cmd = "CLOSE_ACTIVITY_REQUEST",
                args = new { user_id = userId },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Send certified device information
        public static object SetCertifiedDevices(object[] devices)
        {
            return new
            {
                cmd = "SET_CERTIFIED_DEVICES",
                args = new { devices },
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }

        // Generic payload creator for SET_VOICE_SETTINGS using custom args
        public static object SetVoiceSettings(Dictionary<string, object> args)
        {
            return new
            {
                cmd = "SET_VOICE_SETTINGS",
                args,
                nonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            };
        }
    }
}
