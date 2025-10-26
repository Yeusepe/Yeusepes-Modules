using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YeusepesModules.SPOTIOSC.Credentials;

namespace YeusepesModules.SPOTIOSC.UI
{
    public partial class AdvancedCredentials : UserControl
    {
        private string _accessToken;
        private string _clientToken;
        private string _apiAccessToken;
        private string _spTToken;

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
                _accessToken = CredentialManager.LoadAccessToken() ?? string.Empty;
                _clientToken = CredentialManager.LoadClientToken() ?? string.Empty;
                _apiAccessToken = CredentialManager.LoadApiAccessToken() ?? string.Empty;
                _spTToken = CredentialManager.LoadSpTToken() ?? string.Empty;
                
                AccessTokenText.Text = new string('•', _accessToken.Length);
                ClientTokenText.Text = new string('•', _clientToken.Length);
                ApiAccessTokenText.Text = new string('•', _apiAccessToken.Length);
                SpTTokenText.Text = new string('•', _spTToken.Length);
                
                Panel.Visibility = Visibility.Visible;
                ArrowIcon.Text = "▲";
            }
        }

        private void AccessTokenBorder_MouseEnter(object s, MouseEventArgs _) =>
            AccessTokenText.Text = _accessToken;

        private void ClientTokenBorder_MouseEnter(object s, MouseEventArgs _) =>
            ClientTokenText.Text = _clientToken;

        private void ApiAccessTokenBorder_MouseEnter(object s, MouseEventArgs _) => 
            ApiAccessTokenText.Text = _apiAccessToken;

        private void SpTTokenBorder_MouseEnter(object s, MouseEventArgs _) =>
            SpTTokenText.Text = _spTToken;

        private void TokenBorder_MouseLeave(object s, MouseEventArgs _)
        {
            AccessTokenText.Text = new string('•', _accessToken.Length);
            ClientTokenText.Text = new string('•', _clientToken.Length);
            ApiAccessTokenText.Text = new string('•', _apiAccessToken.Length);
            SpTTokenText.Text = new string('•', _spTToken.Length);
        }

        private void CopyAccessToken_Click(object s, RoutedEventArgs _)
        {
            CopyToClipboard(_accessToken);
        }

        private void CopyClientToken_Click(object s, RoutedEventArgs _)
        {
            CopyToClipboard(_clientToken);
        }

        private void CopyApiAccessToken_Click(object s, RoutedEventArgs _) => CopyToClipboard(_apiAccessToken);

        private void CopySpTToken_Click(object s, RoutedEventArgs _) => CopyToClipboard(_spTToken);


        private void CopyToClipboard(string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // copy data and keep it alive after app exits; retry 5× with 50 ms delay
                    Clipboard.SetDataObject(text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Unable to copy to clipboard:\n{ex.Message}",
                        "Copy Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }
            });
        }

    }
}
