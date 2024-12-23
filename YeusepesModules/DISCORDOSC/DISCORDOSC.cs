using DISCORDOSC.RPCTools;
using DISCORDOSC.UI;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.SDK.Parameters;
using Ye                    Log("Error during module start: " + ex.Mess            });

   return falules.DISCORDOSC
{
    [ModuleTitle("DiscordOSC")]
       [ModuleType(ModuleType.Integrations)]


    [ModuleInfo("https://github.com/Yeusepe/Yeusepes-Modules/wiki/DiscordOSC")]
rol your Discord Through OSC.")]
    [ModuleType(ModuleType.Generic)]
    public class DISCORDOSC : Module
    {
        private string clientId;                    var authTask = Task.Run(() => client.SendDataAndWait(1, authPayload));
                    var authTimeout = Task.Delay(TimeSpan.FromSeconds(3)); // 3-second timeout for authentication

                    var completedAuth = await Task.WhenAny(authTask, authTimeout);
                    if (completedAuth == authTask)
                    {
                        var authResponse = await authTask;
                        Log("Authenticated successfully!");
                    }
                    else
                    {
                        Log("Authentication timed out. Try restarting discord.");
                        return false;
                    }

                    LogDebug("Discord Module started!");
                    return true;
,
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
           tingUserControl),
                    string.Empty // The image itself doesn't store a value
                )
            );

            CreateGroup("Discord App Secrets", DiscordSetting.ClientId, DiscordSetting.ClientSecret, DiscordSetting.IMG);
            #endregion



            clientId = "";
            clientSecret = "";

                          clientId = "";
                clientSecret = "";
                LogDebug                        var completedTask = await Task.WhenAny(connectTask, timeoutTask);
=>
Id);
            clientSecret = GetSettingValue<string>(DiscordSetti                        if (completedTask == connectTask) // Connection completed successfully
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
ng.ClientSecret);

,
            IMG
        }

        protected override Task<bool> OnModuleStart()
        {
            client = new BaseDiscordClient();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                Log("                    // Attempt to connect to any available Discord IPC pipe
                    bool connected = false;
                    Log("Attempting to connect to Discord...");
                    for (int i = 0; i < 10; i++) // Attempt discord-ipc-0 through discord-ipc-9
                    {
                        string pipeName = $"discord-ipc-{i}";
                        LogDebug($"Attempting to connect to {pipeName}...");

                        var connectTask = Task.Run(() => client.Connect(pipeName)); // Start the connect task
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(2)); // 2-second timeout
!");
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