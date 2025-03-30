using NAudio.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.UI.Core;

namespace YeusepesModules.OSCQR.UI
{    
    /// <summary>
    /// Interaction logic for SavedQRCodesWindow.xaml
    /// </summary>
    public partial class SavedQRCodesWindow : IManagedWindow
    {
        private readonly OSCQR _module;
        private object _comparer;
        public SavedQRCodesWindow(OSCQR module)
        {
            InitializeComponent();
            _module = module;
            _comparer = _module;
            SourceInitialized += SavedQRCodesWindow_SourceInitialized;
            Closed += SavedQRCodesWindow_Closed;
        }

        private void SavedQRCodesWindow_SourceInitialized(object sender, EventArgs e)
        {
            var savedQRCodes = _module.GetSetting(OSCQR.OSCQRSettings.SavedQRCodes);
            var savedQRCodesControl = new SavedQRCodesView(_module, savedQRCodes);
            MainGrid.Children.Insert(0, savedQRCodesControl);
            _module.screenUtilities.AttachSelector(ScreenSelector);
        }

        private void SavedQRCodesWindow_Closed(object sender, EventArgs e)
        {
            _comparer = new object();
        }

        public object GetComparer() => _comparer;
    }
}
