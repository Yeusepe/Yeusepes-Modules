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
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.SPOTIOSC.UI
{
    public partial class AdvancedCredentials : UserControl
    {
        private readonly string _accessToken = CredentialManager.LoadAccessToken();
        private readonly string _clientToken = CredentialManager.LoadClientToken();

        public AdvancedCredentials()
        {
            InitializeComponent();                        
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (Panel.Visibility == Visibility.Visible)
            {
                Panel.Visibility = Visibility.Collapsed;
                ArrowIcon.Text = "▼";
            }
            else
            {
                Panel.Visibility = Visibility.Visible;
                ArrowIcon.Text = "▲";
            }
        }

        private void AccessTokenBorder_MouseEnter(object s, MouseEventArgs _) =>
            AccessTokenText.Text = _accessToken;
        private void ClientTokenBorder_MouseEnter(object s, MouseEventArgs _) =>
            ClientTokenText.Text = _clientToken;
        private void TokenBorder_MouseLeave(object s, MouseEventArgs _)
        {
            AccessTokenText.Text = new string('•', _accessToken.Length);
            ClientTokenText.Text = new string('•', _clientToken.Length);
        }

        private void CopyAccessToken_Click(object s, RoutedEventArgs _) =>
            Clipboard.SetText(_accessToken);
        private void CopyClientToken_Click(object s, RoutedEventArgs _) =>
            Clipboard.SetText(_clientToken);
    }

}
