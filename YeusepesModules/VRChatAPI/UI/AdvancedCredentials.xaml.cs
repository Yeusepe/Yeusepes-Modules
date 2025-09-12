using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YeusepesModules.VRChatAPI.Credentials;

namespace YeusepesModules.VRChatAPI.UI
{
    public partial class AdvancedCredentials : UserControl
    {
        private string _authToken;
        private bool _isConnected = false;

        public AdvancedCredentials()
        {            
            InitializeComponent();
            UpdateConnectionStatus();
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
                _authToken = VRChatCredentialManager.LoadAuthToken() ?? string.Empty;
                AuthTokenText.Text = new string('•', Math.Max(_authToken.Length, 16));
                Panel.Visibility = Visibility.Visible;
                ArrowIcon.Text = "▲";
                UpdateConnectionStatus();
            }
        }

        private void AuthTokenBorder_MouseEnter(object s, MouseEventArgs _)
        {
            if (!string.IsNullOrEmpty(_authToken))
            {
                AuthTokenText.Text = _authToken;
            }
        }

        private void TokenBorder_MouseLeave(object s, MouseEventArgs _)
        {
            if (!string.IsNullOrEmpty(_authToken))
            {
                AuthTokenText.Text = new string('•', Math.Max(_authToken.Length, 16));
            }
        }

        private void CopyAuthToken_Click(object s, RoutedEventArgs _)
        {
            if (!string.IsNullOrEmpty(_authToken))
            {
                CopyToClipboard(_authToken);
            }
        }

        private void CopyToClipboard(string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
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

        private void UpdateConnectionStatus()
        {
            _isConnected = VRChatCredentialManager.IsUserSignedIn();
            
            if (_isConnected)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Green
                StatusText.Text = "Connected";
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red
                StatusText.Text = "Not Connected";
            }
        }

        public void RefreshStatus()
        {
            UpdateConnectionStatus();
            if (Panel.Visibility == Visibility.Visible)
            {
                _authToken = VRChatCredentialManager.LoadAuthToken() ?? string.Empty;
                AuthTokenText.Text = new string('•', Math.Max(_authToken.Length, 16));
            }
        }
    }
}