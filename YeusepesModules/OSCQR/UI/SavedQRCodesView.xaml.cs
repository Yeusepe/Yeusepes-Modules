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

        public ObservableCollection<string> QRCodeLinks { get; }

        public ICommand OpenLinkCommand { get; }
        public ICommand EraseCommand { get; }

        public SavedQRCodesViewModel(IEnumerable<string> qrCodes)
        {
            // Ensure we only take the latest 20 QR codes
            QRCodeLinks = new ObservableCollection<string>(qrCodes.Take(MaxQRCodeCount));

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
            QRCodeLinks.Clear();
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
            DataContext = new SavedQRCodesViewModel(module.GetSavedQRCodes());
        }
    }
}
