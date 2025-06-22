using System.Windows.Controls;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.ComponentModel;
using System.Linq;

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
            public ObservableCollection<string> QRCodeLinks { get; }
            public ICommand OpenLinkCommand { get; }

            public SavedQRCodesRuntimeViewModel(OSCQR module)
            {
                _module = module;
                QRCodeLinks = new ObservableCollection<string>(module.GetSavedQRCodes());
                OpenLinkCommand = new RelayCommand<string>(OpenLink);
            }

            public void RefreshQRCodes()
            {
                // Ensure UI updates happen on the UI thread
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    var currentCodes = _module.GetSavedQRCodes();
                    
                    // Clear and repopulate the collection if the contents have changed
                    if (QRCodeLinks.Count != currentCodes.Count || !QRCodeLinks.SequenceEqual(currentCodes))
                    {
                        QRCodeLinks.Clear();
                        foreach (var code in currentCodes)
                        {
                            QRCodeLinks.Add(code);
                        }
                        OnPropertyChanged(nameof(QRCodeLinks));
                    }
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
}


