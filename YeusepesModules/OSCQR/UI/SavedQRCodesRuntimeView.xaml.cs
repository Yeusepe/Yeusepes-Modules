using System.Windows.Controls;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace YeusepesModules.OSCQR.UI
{
    public partial class SavedQRCodesRuntimeView : UserControl
    {
        public SavedQRCodesRuntimeView(OSCQR module)
        {
            Uri resourceLocater = new Uri("/YeusepesModules;component/oscqr/ui/savedqrcodesruntimeview.xaml", UriKind.Relative);
            System.Windows.Application.LoadComponent(this, resourceLocater);
            DataContext = new SavedQRCodesRuntimeViewModel(module);
        }

        public class SavedQRCodesRuntimeViewModel
        {
            public ObservableCollection<string> QRCodeLinks { get; }
            public ICommand OpenLinkCommand { get; }

            public SavedQRCodesRuntimeViewModel(OSCQR module)
            {
                QRCodeLinks = new ObservableCollection<string>(module.GetSavedQRCodes());
                OpenLinkCommand = new RelayCommand<string>(OpenLink);
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


