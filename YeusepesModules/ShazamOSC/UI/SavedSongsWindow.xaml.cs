using System;
using System.Windows;
using VRCOSC.App.UI.Core;

namespace YeusepesModules.ShazamOSC.UI
{
    public partial class SavedSongsWindow : IManagedWindow
    {
        private readonly ShazamOSC _module;
        private object _comparer;

        public SavedSongsWindow(ShazamOSC module)
        {
            InitializeComponent();
            _module = module;
            _comparer = _module;
            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var setting = _module.GetSetting(ShazamOSC.ShazamSettings.SavedSongs);
            var view = new SavedSongsView(_module, setting);
            MainGrid.Children.Insert(0, view);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _comparer = new object();
        }

        public object GetComparer() => _comparer;
    }
}