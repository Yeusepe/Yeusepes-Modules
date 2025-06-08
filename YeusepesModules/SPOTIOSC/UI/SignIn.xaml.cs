using System;
using System.Windows;
using System.Windows.Controls;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using YeusepesModules.SPOTIOSC.Credentials;
using VRCOSC.App.SDK.Modules;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VRCOSC.App.Utils;
using System.IO;
using System.Net.Http;
using YeusepesModules.SPOTIOSC;
using System.Text.Json;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using YeusepesLowLevelTools;
using System.Windows.Data;
using Octokit;
using static YeusepesLowLevelTools.Loader;
using System.Reflection;

namespace YeusepesModules.SPOTIOSC.UI
{
    public partial class SignIn : UserControl
    {
        private ModuleSetting _setting;
        private readonly string _tempFontDirectory = Path.GetTempPath();
        public bool IsPremium { get; set; }

        SpotifyUtilities spotifyUtilities;

        public SignIn(VRCOSC.App.SDK.Modules.Module module, ModuleSetting setting)
        {
            Uri resourceLocater = new Uri("/YeusepesModules;component/spotiosc/ui/signin.xaml", UriKind.Relative);
            System.Windows.Application.LoadComponent(this, resourceLocater);            

            spotifyUtilities = ((SpotiOSC)module).spotifyUtilities;

            CursorManager.SetSpinnerCursor();
            LoadFontFromUrl("https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Fonts/circular-std-2.ttf");
            LoadFontFromUrl("https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Fonts/circular-std-3.ttf");
            LoadFontFromUrl("https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Fonts/circular-std-3.ttf");
            LoadFontFromUrl("https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Fonts/circular-std-4.ttf");
            LoadFontFromUrl("https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Fonts/circular-std-5.ttf");
            LoadFontFromUrl("https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Fonts/circular-std-6.ttf");
            LoadFontFromUrl("https://raw.githubusercontent.com/Yeusepe/Yeusepes-Modules/refs/heads/main/Resources/Fonts/circular-std-7.ttf");
            // Thread InitializeUIAsync() on a separate thread
            _setting = setting;
            _ = InitializeUIAsync();
            // Set the font after downloading
            this.Loaded += (s, e) => ApplyFonts();
            CursorManager.RestoreCursor();
        }

        private async Task InitializeUIAsync()
        {
            // show spinner
            await Dispatcher.InvokeAsync(() =>
            {
                SpinnerOverlay.Visibility = Visibility.Visible;
                CursorManager.SetSpinnerCursor();
            });

            try
            {
                // 1) If we already have a valid token, just fetch profile
                if (CredentialManager.IsUserSignedIn())
                {
                    using var httpClient = new HttpClient();
                    spotifyUtilities.Log($"Tokens: {CredentialManager.LoadAccessToken()}, {CredentialManager.LoadClientToken()}");
                    var profileRequest = new SpotifyProfileRequest(
                        httpClient,
                        CredentialManager.LoadAccessToken(),
                        CredentialManager.LoadClientToken()
                    );
                    var userProfile = await profileRequest.GetUserProfileAsync();

                    if (userProfile != null)
                    {
                        spotifyUtilities.Log($"User profile fetched: {userProfile.DisplayName}, {userProfile.Product}");
                        UpdateUIWithUserProfile(
                            userProfile.DisplayName,
                            userProfile.Product,
                            userProfile.Images?.FirstOrDefault()?.Url
                        );
                        await Dispatcher.InvokeAsync(DisplaySignedInState);
                        return;
                    }
                }
                // 2) Not signed in, but cookie exists → run the non-UI login (no Puppeteer)
                else if (CredentialManager.HasSavedCookie())
                {
                    await CredentialManager.LoginAndCaptureCookiesAsync();

                    if (CredentialManager.IsUserSignedIn())
                    {
                        using var httpClient = new HttpClient();
                        var profileRequest = new SpotifyProfileRequest(
                            httpClient,
                            CredentialManager.LoadAccessToken(),
                            CredentialManager.LoadClientToken()
                        );
                        var userProfile = await profileRequest.GetUserProfileAsync();

                        if (userProfile != null)
                        {
                            spotifyUtilities.Log($"User profile fetched: {userProfile.DisplayName}, {userProfile.Product}");
                            UpdateUIWithUserProfile(
                                userProfile.DisplayName,
                                userProfile.Product,
                                userProfile.Images?.FirstOrDefault()?.Url
                            );
                            await Dispatcher.InvokeAsync(DisplaySignedInState);
                            return;
                        }
                    }

                    await Dispatcher.InvokeAsync(DisplaySignedOutState);
                    return;
                }
                // 3) No token & no cookie → signed out, no browser ever opened
                else
                {
                    await Dispatcher.InvokeAsync(DisplaySignedOutState);
                    return;
                }
            }
            catch (Exception ex)
            {
                spotifyUtilities.Log($"Error initializing UI: {ex.Message}");
            }
            finally
            {
                // hide spinner
                await Dispatcher.InvokeAsync(() =>
                {
                    SpinnerOverlay.Visibility = Visibility.Collapsed;
                    CursorManager.RestoreCursor();
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


        private void UpdateUIWithUserProfile(string name, string plan, string imageUrl)
        {
            spotifyUtilities.Log($"Updating UI with profile: {name}, {plan}, {imageUrl}");

            UserName.Text = name;
            PlanText.Text = NativeMethods.CapitalizeFirstLetter(plan);
            IsPremium = string.Equals(plan, "premium", StringComparison.OrdinalIgnoreCase);
            if (IsPremium)
            {
                PlanText.Foreground = new SolidColorBrush(Color.FromRgb(212, 175, 55)); // Green color
            }
            else
            {
                PlanText.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); // White color
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
                    spotifyUtilities.Log($"Error loading profile image: {ex.Message}");
                }
            }
        }



        private void ApplyFonts()
        {
            try
            {
                // Define font paths
                string boldFontPath = Path.Combine(_tempFontDirectory, "circular-std-4.ttf");
                string blackFontPath = Path.Combine(_tempFontDirectory, "circular-std-2.ttf");
                string bookFontPath = Path.Combine(_tempFontDirectory, "circular-std-6.ttf");

                // Validate the existence of font files
                if (!File.Exists(boldFontPath) || !File.Exists(blackFontPath) || !File.Exists(bookFontPath))
                {
                    MessageBox.Show("Font files are missing. Please ensure they are downloaded correctly.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Load fonts dynamically
                var boldFontFamily = new FontFamily(new Uri(boldFontPath, UriKind.Absolute), "./#Circular Std Bold");
                var blackFontFamily = new FontFamily(new Uri(blackFontPath, UriKind.Absolute), "./#Circular Std Black");
                var regularFontFamily = new FontFamily(new Uri(bookFontPath, UriKind.Absolute), "./#Circular Std Book");

                // Apply fonts to specific elements
                HeyText.FontFamily = regularFontFamily;
                UserName.FontFamily = boldFontFamily;
                ExclamationText.FontFamily = boldFontFamily;
                YourAccountText.FontFamily = regularFontFamily;
                SignOutText.FontFamily = boldFontFamily;
                SpinnerText.FontFamily = boldFontFamily;

                ////Logger.Log("Fonts successfully applied.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying fonts: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadFontFromUrl(string fontUrl)
        {
            try
            {
                using HttpClient client = new();
                byte[] fontData = await client.GetByteArrayAsync(fontUrl);

                // Extract a unique filename from the URL
                string fileName = Path.GetFileNameWithoutExtension(fontUrl) + ".ttf";
                string tempFontPath = Path.Combine(_tempFontDirectory, fileName);

                // Check if the file already exists
                if (File.Exists(tempFontPath))
                {
                    ////Logger.Log($"Font already exists: {tempFontPath}");
                    return; // Skip downloading if the file already exists
                }

                // Save the font data
                await File.WriteAllBytesAsync(tempFontPath, fontData);

                ////Logger.Log($"Font downloaded and saved to: {tempFontPath}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading font: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnSignInClick(object sender, RoutedEventArgs e)
        {
            // Check if the parent window is still alive.
            var parentWindow = Window.GetWindow(this);

            // Show spinner overlay
            SpinnerOverlay.Visibility = Visibility.Visible;
            CursorManager.SetSpinnerCursor();

            try
            {
                await CredentialManager.LoginAsync();
                if (CredentialManager.IsUserSignedIn())
                {
                    // Update UI on main thread
                    Dispatcher.Invoke(() =>
                    {
                        DisplaySignedInState();
                    });
                    // Reload profile details asynchronously
                    await InitializeUIAsync();
                }
                else
                {
                    // Handle login failure if necessary
                }
            }
            catch (Exception ex)
            {
                // Optionally log or handle the exception
            }
            finally
            {
                // Hide spinner overlay and restore cursor
                SpinnerOverlay.Visibility = Visibility.Collapsed;
                CursorManager.RestoreCursor();
            }
        }



        private void OnYourAccountClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open Spotify account overview page
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://www.spotify.com/account/overview/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open the account page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSignOutClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Sign out logic
                CredentialManager.SignOut(); // Assuming this method clears stored tokens or cookies
                DisplaySignedOutState(); // Update the UI to show the signed-out state
                ////Logger.Log("User signed out successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to sign out: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


    }
}
