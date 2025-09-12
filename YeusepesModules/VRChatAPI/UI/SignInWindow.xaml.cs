using System;
using System.Windows;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.UI.Core;

namespace YeusepesModules.VRChatAPI.UI
{
    public partial class SignInWindow : IManagedWindow
    {
        private readonly VRChatAPI _module;
        private object _comparer;  // Holds the comparer value

        public SignInWindow(VRChatAPI module)
        {
            InitializeComponent();
            _module = module;
            _comparer = new object(); // Initialize comparer

            // Set up the SignIn control with the module and setting
            var setting = _module.GetSetting(VRChatAPI.VRChatSettings.SignInButton);

            var signInControl = new SignIn(_module, setting);
            MainGrid.Children.Add(signInControl);

            // Handle window events
            SourceInitialized += SignInWindow_SourceInitialized;
            Closed += SignInWindow_Closed;
        }

        private void SignInWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Window initialization logic if needed
        }

        private void SignInWindow_Closed(object sender, EventArgs e)
        {
            // Cleanup logic if needed
        }

        public object GetComparer() => _comparer;
    }
}
