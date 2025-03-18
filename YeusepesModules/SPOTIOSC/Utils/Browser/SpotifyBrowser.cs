using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YeusepesModules.SPOTIOSC.Utils.Browser
{
    public class SpotifyBrowser
    {
        private WebView2 webView;
        private Form browserForm;
        public async Task LaunchBrowserAsync()
        {
            // Create and configure the form
            browserForm = new Form
            {
                Text = "Spotify Login App Mode",
                Width = 800,
                Height = 600
            };

            // Initialize WebView2
            webView = new WebView2
            {
                Dock = DockStyle.Fill
            };
            webView.CoreWebView2InitializationCompleted += WebView2_CoreWebView2InitializationCompleted;
            browserForm.Controls.Add(webView);

            // Initialize WebView2 environment and navigate to the URL
            var env = await CoreWebView2Environment.CreateAsync();
            await webView.EnsureCoreWebView2Async(env);
            webView.CoreWebView2.Navigate("https://accounts.spotify.com/en/login?continue=https%3A%2F%2Fopen.spotify.com%2F");

            // Show the form
            browserForm.ShowDialog();
        }

        private void WebView2_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                // Subscribe to the NavigationCompleted event to capture responses
                webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                // Enable DevTools Protocol to intercept network responses
                webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived")
                    .DevToolsProtocolEventReceived += ResponseReceived;
                webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
            }
            else
            {
                MessageBox.Show("Failed to initialize WebView2: " + e.InitializationException.Message);
                browserForm.Close();
            }
        }

        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                MessageBox.Show("Navigation failed: " + e.WebErrorStatus);
                browserForm.Close();
            }
        }

        private void ResponseReceived(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            // Parse the response details
            var response = JsonSerializer.Deserialize<JsonElement>(e.ParameterObjectAsJson);
            var url = response.GetProperty("response").GetProperty("url").GetString();

            // Check if the response URL matches "https://open.spotify.com/"
            if (url.Contains("https://open.spotify.com/"))
            {
                MessageBox.Show("Captured response: " + url);
                browserForm.Close(); // Close the browser form
            }
        }
    }
}

