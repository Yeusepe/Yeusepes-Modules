using System;
using System.ComponentModel;
using System.Windows;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.UI.Core; // For IManagedWindow

namespace YeusepesModules.SPOTIOSC.UI
{
    public partial class SignInWindow : IManagedWindow
    {
        private readonly SpotiOSC _module;
        private object _comparer;  // Holds the comparer value

        public SignInWindow(SpotiOSC module)
        {
            InitializeComponent();
            _module = module;
            // Initially, use the module as the comparer.
            _comparer = _module;

            // Defer UI initialization until SourceInitialized.
            SourceInitialized += SignInWindow_SourceInitialized;
            ScreenSelector.ScreenUtilities = _module.screenUtilities;
            ScreenSelector.LiveCaptureProvider = _module.screenUtilities.CaptureImageForDisplay;
            // When the window closes, invalidate the comparer.
            Closed += SignInWindow_Closed;
        }

        private void SignInWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // The module's OnPreLoad should have registered settings by now.
            var customSetting = _module.GetSetting(SpotiOSC.SpotiSettings.SignInButton);
            var signInControl = new SignIn(_module, customSetting);
            MainGrid.Children.Insert(0, signInControl);
        }

        private void SignInWindow_Closed(object sender, EventArgs e)
        {
            // Invalidate the comparer so the WindowManager won't reuse this closed window.
            _comparer = new object();
        }

        // IManagedWindow implementation.
        public object GetComparer() => _comparer;

        private void ScreenSelector_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}
