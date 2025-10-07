using System;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;

namespace YeusepesModules.ShazamOSC.UI
{

    public class SavedSongsViewModel
    {
        private readonly ShazamOSC _module;
        public ObservableCollection<SavedSong> Songs { get; }
        public ICommand DeleteSongCommand { get; }

        public SavedSongsViewModel(ShazamOSC module, IEnumerable<string> rawJsonSongs)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));

            // Reverse so newest-saved items appear at the top
            var reversed = rawJsonSongs.Reverse();
            Songs = new ObservableCollection<SavedSong>(
                reversed.Select(json => new SavedSong(json, _module.LogDebug))
            );

            // Command that both removes from the UI and calls back into your module
            DeleteSongCommand = new RelayCommand(param =>
            {
                if (param is SavedSong song)
                {
                    // 1) Remove from the ObservableCollection
                    Songs.Remove(song);

                    // 2) Tell your module to update the persisted setting
                    _module.DeleteSavedSong(song.RawJson);
                }
            });
        }
    }

    public partial class SavedSongsView : UserControl
    {
        public SavedSongsView(Module module, ModuleSetting setting)
        {
            InitializeComponent();

            if (module is ShazamOSC shazam)
            {
                // Pass the module reference into the VM, plus the raw JSON list
                DataContext = new SavedSongsViewModel(
                    shazam,
                    shazam.GetSavedSongs()
                );
            }
            else
            {
                throw new InvalidOperationException(
                    "SavedSongsView must be constructed with a ShazamOSC module."
                );
            }
        }
    }
}