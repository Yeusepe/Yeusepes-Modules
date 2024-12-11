using DISCORDOSC.RPCTools;
using DISCORDOSC.UI;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Parameters;

#pragma warning disable CA1416 // Validate platform compatibility

namespace VIRAModules.DISCORDOSC
{
    [ModuleTitle("DiscordOSC")]
    [ModuleDescription("A module to control your Discord Through OSC.")]
    [ModuleType(ModuleType.Generic)]
    public class DISCORDOSC : Module
    {
        private string clientId;
        private string clientSecret;
        BaseDiscordClient client;

        public enum DISCORDOSCParameter
        {
            Mute,
            Deafen
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

            #endregion
            #region Settings

            CreateTextBox(DiscordSetting.ClientId, "Client ID", "Discord Client ID", string.Empty);
            CreatePasswordTextBox(DiscordSetting.ClientSecret, "Client Secret", "Discord Client Secret", string.Empty);

            // Add the image setting
            CreateCustomSetting(
                DiscordSetting.IMG,
                new CustomModuleSetting(
                    string.Empty,
                    string.Empty,
                    typeof(ImageSettingUserControl),
                    string.Empty // The image itself doesn't store a value
                )
            );

            CreateGroup("Discord App Secrets", DiscordSetting.ClientId, DiscordSetting.ClientSecret, DiscordSetting.IMG);
            #endregion



            clientId = "";
            clientSecret = "";

            base.OnPreLoad();
        }

        private enum DiscordSetting
        {
            ClientId,
            ClientSecret,
            IMG
        }

        protected override Task<bool> OnModuleStart()
        {
            client = new BaseDiscordClient();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                Log("Please set both Client ID and Client Secret as environment variables or in the settings!");
                return Task.FromResult(false);
            }

            Log("Discord Module started!");

            Task.Run(async () =>
            {
                try
                {
                    var auth = new DiscordAuth(clientId, clientSecret);
                    string accessToken = await auth.FetchAccessTokenAsync();
                    LogDebug("Access token retrieved successfully.");

                    client.Connect("discord-ipc-0"); // Adjust the pipe name as needed

                    var handshakeResponse = client.Handshake(clientId);
                    LogDebug("Handshake completed");

                    var authPayload = Payload.Authenticate(accessToken);
                    var authResponse = client.SendDataAndWait(1, authPayload);
                    LogDebug("Authenticated successfully");
                }
                catch (Exception ex)
                {
                    LogDebug("Error during module start: " + ex.Message);
                }
            }).Wait();

            return Task.FromResult(true);
        }

        protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
        {
            switch (parameter.Lookup)
            {
                case DISCORDOSCParameter.Mute:
                    bool shouldUnmute = parameter.GetValue<bool>();
                    Log("Mute?" + shouldUnmute);
                    Task.Run(async () =>
                    {
                        var payload = shouldUnmute
                            ? Payload.SetMuteOnly(false)
                            : Payload.SetMuteOnly(true);

                        var response = client.SendDataAndWait(1, payload);
                        LogDebug($"Mute/Unmute response: {response}");
                    }).Wait();
                    break;

                case DISCORDOSCParameter.Deafen:
                    bool shouldUndeafen = parameter.GetValue<bool>();
                    Log("Deafen?" + shouldUndeafen);
                    Task.Run(async () =>
                    {
                        var payload = shouldUndeafen
                            ? Payload.SetDeafenOnly(false)
                            : Payload.SetDeafenOnly(true);

                        var response = client.SendDataAndWait(1, payload);
                        LogDebug($"Deafen/Undeafen response: {response}");
                    }).Wait();
                    break;
            }
        }
    }

    public class CustomModuleSetting : ModuleSetting
    {
        public string DefaultValue { get; }

        public CustomModuleSetting(string title, string description, Type controlType, string defaultValue = "")
            : base(title, description, controlType)
        {
            DefaultValue = defaultValue;
        }

        public override void SetDefault()
        {

        }

        public override bool IsDefault()
        {
            return true; // Adjust as required
        }

        public override bool Deserialise(object? ingestValue)
        {
            return true; // Implement deserialization logic if required
        }

        public override object? GetRawValue()
        {
            return DefaultValue;
        }
    }
}