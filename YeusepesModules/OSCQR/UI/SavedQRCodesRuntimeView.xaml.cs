using System.Windows.Controls;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows;

namespace YeusepesModules.OSCQR.UI
{
    public partial class SavedQRCodesRuntimeView : UserControl
    {
        private SavedQRCodesRuntimeViewModel _viewModel;

        public SavedQRCodesRuntimeView(OSCQR module)
        {
            Uri resourceLocater = new Uri("/YeusepesModules;component/oscqr/ui/savedqrcodesruntimeview.xaml", UriKind.Relative);
            System.Windows.Application.LoadComponent(this, resourceLocater);
            
            _viewModel = new SavedQRCodesRuntimeViewModel(module);
            DataContext = _viewModel;

            // Subscribe to the module's QR codes updated event
            module.QRCodesUpdated += _viewModel.RefreshQRCodes;
        }

        public class SavedQRCodesRuntimeViewModel : INotifyPropertyChanged
        {
            private readonly OSCQR _module;
            
            public ObservableCollection<DetectedCodeInfo> DetectedCodes { get; }
            public ICommand OpenLinkCommand { get; }

            public SavedQRCodesRuntimeViewModel(OSCQR module)
            {
                _module = module;
                DetectedCodes = new ObservableCollection<DetectedCodeInfo>();
                OpenLinkCommand = new RelayCommand<string>(OpenLink);
                
                // Initialize with existing codes
                RefreshDetectedCodes();
            }

            public void RefreshQRCodes()
            {
                RefreshDetectedCodes();
            }
            
            private void RefreshDetectedCodes()
            {
                // Ensure UI updates happen on the UI thread
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    var currentCodes = _module.GetSavedQRCodes();
                    var currentSpotifyInfo = _module.GetLastSpotifyTrackInfo();
                    var currentSpotifyCode = _module.GetLastDetectedSpotifyCode();
                    
                    Debug.WriteLine($"[OSCQR Runtime View] Refreshing: {currentCodes.Count()} QR codes, Spotify: {(currentSpotifyInfo != null ? currentSpotifyInfo.Name : "none")}");
                    
                    // Clear existing codes
                    DetectedCodes.Clear();
                    
                    // Add QR codes
                    foreach (var code in currentCodes)
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
                    if (currentSpotifyInfo != null && currentSpotifyCode.HasValue)
                    {
                        var spotifyInfo = new DetectedCodeInfo
                        {
                            DisplayText = currentSpotifyInfo.Name,
                            TypeInfo = $"Spotify {currentSpotifyInfo.Type}",
                            Url = currentSpotifyInfo.Url,
                            HasTypeInfo = true
                        };
                        
                        // Add additional info based on content type
                        if (currentSpotifyInfo.Artists != null && currentSpotifyInfo.Artists.Count > 0)
                        {
                            spotifyInfo.TypeInfo += $" by {string.Join(", ", currentSpotifyInfo.Artists)}";
                        }
                        
                        if (!string.IsNullOrEmpty(currentSpotifyInfo.Album))
                        {
                            spotifyInfo.TypeInfo += $" from {currentSpotifyInfo.Album}";
                        }
                        
                        DetectedCodes.Add(spotifyInfo);
                        Debug.WriteLine($"[OSCQR Runtime View] Added Spotify code: {spotifyInfo.DisplayText}");
                    }
                    
                    OnPropertyChanged(nameof(DetectedCodes));
                });
            }

            public event PropertyChangedEventHandler PropertyChanged;
            protected virtual void OnPropertyChanged(string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DetectedCodeInfo
    {
        public string DisplayText { get; set; }
        public string TypeInfo { get; set; }
        public string Url { get; set; }
        public bool HasTypeInfo { get; set; }
    }
}


