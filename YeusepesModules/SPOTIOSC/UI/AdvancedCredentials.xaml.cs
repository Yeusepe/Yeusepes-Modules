using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
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
            RedirectUriText.Text = CredentialManager.CustomRedirectUri;
            ShowApiClient();
        }

        private void ShowApiClient()
        {
            var custom = CredentialManager.UsesCustomApiClient;
            AppModeText.Text = custom ? "Using your app" : "Using shared app";
            UseDefaultButton.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            ClientIdBox.Text = custom ? CredentialManager.ApiClientId : string.Empty;
        }

        private void Link_RequestNavigate(object s, RequestNavigateEventArgs e) =>
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });

        private void CopyRedirectUri_Click(object s, RoutedEventArgs _) => CopyToClipboard(s, CredentialManager.CustomRedirectUri);

        private async void UseCustomApp_Click(object s, RoutedEventArgs _) => await UseApiClientAsync(ClientIdBox.Text.Trim());

        private async void UseDefaultApp_Click(object s, RoutedEventArgs _) => await UseApiClientAsync(string.Empty);

        private async Task UseApiClientAsync(string clientId)
        {
            if (clientId.Length > 0 && !Regex.IsMatch(clientId, "^[0-9a-f]{32}$"))
            {
                ApiClientStatus.Text = "That isn't a Client ID. Copy the 32 character ID from your app's page.";
                return;
            }

            ApiClientStatus.Text = clientId.Length > 0
                ? "Approve SpotiOSC in your browser to finish connecting."
                : "Switching back to the shared app.";
            try
            {
                await CredentialManager.UseApiClientAsync(clientId);
                ApiClientStatus.Text = "Connected. Restart the module to start using it.";
            }
            catch (Exception e)
            {
                ApiClientStatus.Text = $"Couldn't connect: {e.Message}";
            }
            ShowApiClient();
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (Panel.Visibility == Visibility.Visible)
            {
                Panel.Visibility = Visibility.Collapsed;
                ArrowIcon.Text = "";
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
                ArrowIcon.Text = "";
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

        private void CopyAccessToken_Click(object s, RoutedEventArgs _) => CopyToClipboard(s, _accessToken);

        private void CopyClientToken_Click(object s, RoutedEventArgs _) => CopyToClipboard(s, _clientToken);

        private void CopyApiAccessToken_Click(object s, RoutedEventArgs _) => CopyToClipboard(s, _apiAccessToken);

        private void CopySpTToken_Click(object s, RoutedEventArgs _) => CopyToClipboard(s, _spTToken);

        private async void CopyToClipboard(object sender, string text)
        {
            CopyToClipboard(text);
            var button = (Button)sender;
            button.Content = "Copied";
            await Task.Delay(1500);
            button.Content = "Copy";
        }

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
