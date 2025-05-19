using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.UI.Core;
using YeusepesModules.Common.ScreenUtilities;
using YeusepesModules.IDC; // For IManagedWindow

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
            // When the window closes, invalidate the comparer.
            Closed += SignInWindow_Closed;
        }            

        private void SignInWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // The module's OnPreLoad should have registered settings by now.
            var customSetting = _module.GetSetting(SpotiOSC.SpotiSettings.SignInButton);
            var signInControl = new SignIn(_module, customSetting);
            MainGrid.Children.Insert(0, signInControl);
            _module.screenUtilities.AttachSelector(ScreenSelector);
            // In SignInWindow.SourceInitialized or similar:
            var decoderControl = new DecoderToleranceControl();
            decoderControl.AttachDependencies(_module.encodingUtilities);
            MainGrid.Children.Add(decoderControl);

            // inside SignInWindow_SourceInitialized(...)
            var advancedCreds = new AdvancedCredentials();
            // if you need to pass dependencies you can do it here…
            MainGrid.Children.Add(advancedCreds);
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
