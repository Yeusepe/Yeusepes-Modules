using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using YeusepesModules.VRChatAPI.Credentials;
using YeusepesModules.VRChatAPI.Utils;
using YeusepesModules.VRChatAPI.Utils.Requests;
using System.Text.Json;

namespace YeusepesModules.VRChatAPI.UI
{
    public partial class SignIn : UserControl
    {
        private ModuleSetting _setting;
        private readonly string _tempFontDirectory = Path.GetTempPath();
        private VRChatUtilities vrchatUtilities;

        public SignIn(VRCOSC.App.SDK.Modules.Module module, ModuleSetting setting)
        {
            InitializeComponent();
            _setting = setting;

            vrchatUtilities = new VRChatUtilities
            {
                Log = message => System.Diagnostics.Debug.WriteLine(message),
                LogDebug = message => System.Diagnostics.Debug.WriteLine(message),
                SendParameter = (param, value) => { }
            };

            VRChatCredentialManager.VRChatUtils = vrchatUtilities;

            // Load VRChat fonts
            LoadFontFromUrl("https://assets.vrchat.com/fonts/notosans/noto-sans-v27-latin-300.woff2");
            LoadFontFromUrl("https://assets.vrchat.com/fonts/notosans/noto-sans-v27-latin-regular.woff2");
            LoadFontFromUrl("https://assets.vrchat.com/fonts/notosans/noto-sans-v27-latin-700.woff2");
            
            // Initialize UI asynchronously
            _ = InitializeUIAsync();
            
            // Apply fonts when loaded
            this.Loaded += (s, e) => ApplyFonts();
        }

        private async Task InitializeUIAsync()
        {
            // Show spinner
            await Dispatcher.InvokeAsync(() =>
            {
                SpinnerOverlay.Visibility = Visibility.Visible;
            });

            try
            {
                // 1) If we already have a valid token, just fetch profile
                if (VRChatCredentialManager.IsUserSignedIn())
                {
                    using var httpClient = new HttpClient();
                    string authToken = VRChatCredentialManager.LoadAuthToken();
                    
                    if (!string.IsNullOrEmpty(authToken))
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.vrchat.cloud/api/1/auth/user");
                        request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                        request.Headers.Add("Cookie", $"auth={authToken}");

                        var response = await httpClient.SendAsync(request);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            var json = JsonDocument.Parse(content).RootElement;

                            string username = json.TryGetProperty("username", out var usernameProp) ? usernameProp.GetString() : "Unknown";
                            string displayName = json.TryGetProperty("displayName", out var displayNameProp) ? displayNameProp.GetString() : "Unknown";
                            string status = json.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : "Unknown";
                            string userIcon = json.TryGetProperty("userIcon", out var userIconProp) ? userIconProp.GetString() : null;

                            vrchatUtilities.LogDebug($"User profile fetched: {displayName}, {status}");
                            UpdateUIWithUserProfile(displayName, status, userIcon);
                            await Dispatcher.InvokeAsync(DisplaySignedInState);
                            return;
                        }
                    }
                }
                
                // 2) Not signed in, but cookie exists → run the non-UI login
                else if (VRChatCredentialManager.HasSavedCookie())
                {
                    await VRChatCredentialManager.LoginAndCaptureCookiesAsync();

                    if (VRChatCredentialManager.IsUserSignedIn())
                    {
                        using var httpClient = new HttpClient();
                        string authToken = VRChatCredentialManager.LoadAuthToken();
                        
                        if (!string.IsNullOrEmpty(authToken))
                        {
                            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.vrchat.cloud/api/1/auth/user");
                            request.Headers.Add("User-Agent", "VRChatAPI-Interactor/1.0");
                            request.Headers.Add("Cookie", $"auth={authToken}");

                            var response = await httpClient.SendAsync(request);
                            
                            if (response.IsSuccessStatusCode)
                            {
                                var content = await response.Content.ReadAsStringAsync();
                                var json = JsonDocument.Parse(content).RootElement;

                                string username = json.TryGetProperty("username", out var usernameProp) ? usernameProp.GetString() : "Unknown";
                                string displayName = json.TryGetProperty("displayName", out var displayNameProp) ? displayNameProp.GetString() : "Unknown";
                                string status = json.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : "Unknown";
                                string userIcon = json.TryGetProperty("userIcon", out var userIconProp) ? userIconProp.GetString() : null;

                                vrchatUtilities.LogDebug($"User profile fetched: {displayName}, {status}");
                                UpdateUIWithUserProfile(displayName, status, userIcon);
                                await Dispatcher.InvokeAsync(DisplaySignedInState);
                                return;
                            }
                        }
                    }

                    await Dispatcher.InvokeAsync(DisplaySignedOutState);
                    return;
                }
                
                // 3) No token & no cookie → signed out
                else
                {
                    await Dispatcher.InvokeAsync(DisplaySignedOutState);
                    return;
                }
            }
            catch (Exception ex)
            {
                vrchatUtilities.LogDebug($"Error initializing UI: {ex.Message}");
                await Dispatcher.InvokeAsync(DisplaySignedOutState);
            }
            finally
            {
                // Hide spinner
                await Dispatcher.InvokeAsync(() =>
                {
                    SpinnerOverlay.Visibility = Visibility.Collapsed;
                });
            }
        }

        private void DisplaySignedInState()
        {
            if (SignedOutState.Visibility != Visibility.Hidden)
            {
                SignedOutState.Visibility = Visibility.Hidden;
            }

            if (SignedInState.Visibility != Visibility.Visible)
            {
                SignedInState.Visibility = Visibility.Visible;
            }
        }

        private void DisplaySignedOutState()
        {
            if (SignedInState.Visibility != Visibility.Hidden)
            {
                SignedInState.Visibility = Visibility.Hidden;
            }

            if (SignedOutState.Visibility != Visibility.Visible)
            {
                SignedOutState.Visibility = Visibility.Visible;
            }
        }

        private void UpdateUIWithUserProfile(string displayName, string status, string imageUrl)
        {
            vrchatUtilities.LogDebug($"Updating UI with profile: {displayName}, {status}, {imageUrl}");

            UserName.Text = displayName;
            StatusText.Text = status;

            // Set status color based on status
            if (status?.ToLower() == "online")
            {
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Green
            }
            else if (status?.ToLower() == "busy")
            {
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red
            }
            else if (status?.ToLower() == "away")
            {
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(241, 196, 15)); // Yellow
            }
            else
            {
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); // White
            }

            if (!string.IsNullOrEmpty(imageUrl))
            {
                try
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(imageUrl, UriKind.Absolute);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();

                    ProfileImageBrush.ImageSource = image;
                }
                catch (Exception ex)
                {
                    vrchatUtilities.LogDebug($"Error loading profile image: {ex.Message}");
                }
            }
        }

        private void ApplyFonts()
        {
            try
            {
                // Define font paths
                string lightFontPath = Path.Combine(_tempFontDirectory, "noto-sans-300.woff2");
                string regularFontPath = Path.Combine(_tempFontDirectory, "noto-sans-regular.woff2");
                string boldFontPath = Path.Combine(_tempFontDirectory, "noto-sans-700.woff2");

                // Validate the existence of font files
                if (!File.Exists(lightFontPath) || !File.Exists(regularFontPath) || !File.Exists(boldFontPath))
                {
                    vrchatUtilities.LogDebug("VRChat font files are missing. Using system fonts.");
                    return;
                }

                // Load fonts dynamically
                var lightFontFamily = new FontFamily(new Uri(lightFontPath, UriKind.Absolute), "./#Noto Sans");
                var regularFontFamily = new FontFamily(new Uri(regularFontPath, UriKind.Absolute), "./#Noto Sans");
                var boldFontFamily = new FontFamily(new Uri(boldFontPath, UriKind.Absolute), "./#Noto Sans");

                // Apply fonts to specific elements
                HeyText.FontFamily = lightFontFamily;
                UserName.FontFamily = boldFontFamily;
                ExclamationText.FontFamily = boldFontFamily;
                StatusLabelText.FontFamily = regularFontFamily;
                StatusText.FontFamily = boldFontFamily;
                YourAccountText.FontFamily = regularFontFamily;
                SignOutText.FontFamily = boldFontFamily;
                SpinnerText.FontFamily = boldFontFamily;
            }
            catch (Exception ex)
            {
                vrchatUtilities.LogDebug($"Error applying VRChat fonts: {ex.Message}");
            }
        }

        private async void LoadFontFromUrl(string fontUrl)
        {
            try
            {
                using HttpClient client = new();
                byte[] fontData = await client.GetByteArrayAsync(fontUrl);

                // Extract a unique filename from the URL
                string fileName = Path.GetFileNameWithoutExtension(fontUrl) + ".woff2";
                string tempFontPath = Path.Combine(_tempFontDirectory, fileName);

                // Check if the file already exists
                if (File.Exists(tempFontPath))
                {
                    return; // Skip downloading if the file already exists
                }

                // Save the font data
                await File.WriteAllBytesAsync(tempFontPath, fontData);
            }
            catch (Exception ex)
            {
                vrchatUtilities.LogDebug($"Error loading VRChat font: {ex.Message}");
            }
        }

        private async void OnSignInClick(object sender, RoutedEventArgs e)
        {
            // Show spinner overlay
            SpinnerOverlay.Visibility = Visibility.Visible;

            try
            {
                await VRChatCredentialManager.LoginAndCaptureCookiesAsync();
                if (VRChatCredentialManager.IsUserSignedIn())
                {
                    // Update UI on main thread
                    Dispatcher.Invoke(() =>
                    {
                        DisplaySignedInState();
                        // Refresh the advanced credentials status
                        if (AdvancedCredentialsSection != null)
                        {
                            AdvancedCredentialsSection.RefreshStatus();
                        }
                    });
                    // Reload profile details asynchronously
                    await InitializeUIAsync();
                }
                else
                {
                    // Handle login failure
                    DisplaySignedOutState();
                }
            }
            catch (Exception ex)
            {
                vrchatUtilities.LogDebug($"Error during sign in: {ex.Message}");
                DisplaySignedOutState();
            }
            finally
            {
                // Hide spinner overlay
                SpinnerOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void OnYourAccountClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open VRChat website
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://vrchat.com/home",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open VRChat website: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnSignOutClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Sign out logic
                VRChatCredentialManager.SignOut();
                DisplaySignedOutState();
                vrchatUtilities.LogDebug("User signed out successfully.");
                
                // Refresh the advanced credentials status
                if (AdvancedCredentialsSection != null)
                {
                    AdvancedCredentialsSection.RefreshStatus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to sign out: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}