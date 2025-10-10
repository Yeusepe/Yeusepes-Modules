using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using System.Windows.Controls;

namespace YeusepesModules.OSCQR.UI
{
    public class SavedQRCodesViewModel
    {
        private const int MaxQRCodeCount = 20;

        public ObservableCollection<DetectedCodeInfo> DetectedCodes { get; }

        public ICommand OpenLinkCommand { get; }
        public ICommand EraseCommand { get; }

        public SavedQRCodesViewModel(IEnumerable<string> qrCodes, SpotifyTrackInfo spotifyInfo, long? spotifyCode)
        {
            DetectedCodes = new ObservableCollection<DetectedCodeInfo>();
            
            // Add QR codes
            foreach (var code in qrCodes.Take(MaxQRCodeCount))
            {
                DetectedCodes.Add(new DetectedCodeInfo
                {
                    DisplayText = code,
                    TypeInfo = "QR Code",
                    Url = code,
                    HasTypeInfo = true
                });
            }
            
            // Add Spotify code if available
            if (spotifyInfo != null && spotifyCode.HasValue)
            {
                var spotifyCodeInfo = new DetectedCodeInfo
                {
                    DisplayText = spotifyInfo.Name,
                    TypeInfo = $"Spotify {spotifyInfo.Type}",
                    Url = spotifyInfo.Url,
                    HasTypeInfo = true
                };
                
                // Add additional info based on content type
                if (spotifyInfo.Artists != null && spotifyInfo.Artists.Count > 0)
                {
                    spotifyCodeInfo.TypeInfo += $" by {string.Join(", ", spotifyInfo.Artists)}";
                }
                
                if (!string.IsNullOrEmpty(spotifyInfo.Album))
                {
                    spotifyCodeInfo.TypeInfo += $" from {spotifyInfo.Album}";
                }
                
                DetectedCodes.Add(spotifyCodeInfo);
            }

            OpenLinkCommand = new RelayCommand<string>(OpenLink);
            EraseCommand = new RelayCommand(EraseAll);
        }

        private void OpenLink(string link)
        {
            try
            {
                if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening link: {ex.Message}");
            }
        }

        private void EraseAll()
        {
            DetectedCodes.Clear();
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> execute;
        private readonly Func<T, bool>? canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool>? canExecute = null)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute == null || parameter is T t && canExecute(t);
        }

        public void Execute(object? parameter)
        {
            if (parameter is T t)
            {
                execute(t);
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action execute;
        private readonly Func<bool>? canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute == null || canExecute();
        }

        public void Execute(object? parameter)
        {
            execute();
        }
    }

    public partial class SavedQRCodesView : UserControl
    {
        public SavedQRCodesView(OSCQR module, ModuleSetting setting)
        {
            InitializeComponent();
            // NOTE: This is a static snapshot view - it shows codes at the time settings are opened
            // For live updates, use the Runtime View tab instead
            
            var qrCodes = module.GetSavedQRCodes();
            var spotifyInfo = module.GetLastSpotifyTrackInfo();
            var spotifyCode = module.GetLastDetectedSpotifyCode();
            
            Debug.WriteLine($"[SavedQRCodesView] Creating settings view:");
            Debug.WriteLine($"  - QR Codes: {qrCodes.Count()}");
            Debug.WriteLine($"  - Spotify Info: {(spotifyInfo != null ? spotifyInfo.Name : "null")}");
            Debug.WriteLine($"  - Spotify Code: {(spotifyCode.HasValue ? spotifyCode.Value.ToString() : "null")}");
            
            DataContext = new SavedQRCodesViewModel(qrCodes, spotifyInfo, spotifyCode);
        }
    }
}
