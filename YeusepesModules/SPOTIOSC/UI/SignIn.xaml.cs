using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.Utils;
using YeusepesModules.SPOTIOSC.Credentials;
using YeusepesModules.SPOTIOSC.Runtime.Spotify;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using YeusepesLowLevelTools;
using static YeusepesLowLevelTools.Loader;

namespace YeusepesModules.SPOTIOSC.UI;

public partial class SignIn : UserControl
{
    private readonly SpotifyUtilities? _utilities;

    public SignIn(Module module, ModuleSetting setting)
    {
        Application.LoadComponent(
            this,
            new Uri("/YeusepesModules;component/spotiosc/ui/signin.xaml", UriKind.Relative));
        _utilities = ((SpotiOSC)module).spotifyUtilities;
        _ = setting;
        ApplyFonts();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        SetBusy(true);
        try
        {
            if (!CredentialManager.IsUserSignedIn() && CredentialManager.HasSavedCookie())
                await CredentialManager.LoginAndCaptureCookiesAsync();
            await RefreshAccountAsync();
        }
        catch (Exception exception)
        {
            _utilities?.LogDebug($"Spotify account view initialization failed: {exception.Message}");
            ShowSignedIn(false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshAccountAsync()
    {
        if (!CredentialManager.IsUserSignedIn())
        {
            ShowSignedIn(false);
            return;
        }

        using var httpClient = new HttpClient();
        var profile = await new SpotifyProfileClient(
            httpClient,
            CredentialManager.LoadAccessToken()).GetAsync();

        ApplyProfile(profile);
        ShowSignedIn(true);
    }

    private void ApplyProfile(SpotifyProfile profile)
    {
        UserName.Text = profile.DisplayName ?? string.Empty;
        PlanText.Text = NativeMethods.CapitalizeFirstLetter(profile.Product ?? string.Empty);
        var isPremium = string.Equals(profile.Product, "premium", StringComparison.OrdinalIgnoreCase);
        PlanText.Foreground = new SolidColorBrush(isPremium
            ? Color.FromRgb(212, 175, 55)
            : Colors.White);

        var imageUrl = profile.Images?.FirstOrDefault()?.Url;
        if (string.IsNullOrEmpty(imageUrl)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(imageUrl, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            ProfileImageBrush.ImageSource = image;
        }
        catch (Exception exception)
        {
            _utilities?.LogDebug($"Profile image failed to load: {exception.Message}");
        }
    }

    private void ShowSignedIn(bool signedIn)
    {
        SignedInState.Visibility = signedIn ? Visibility.Visible : Visibility.Hidden;
        SignedOutState.Visibility = signedIn ? Visibility.Hidden : Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        SpinnerOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) CursorManager.SetSpinnerCursor();
        else CursorManager.RestoreCursor();
    }

    private void ApplyFonts()
    {
        HeyText.FontFamily = FontHelper.Book;
        UserName.FontFamily = FontHelper.Bold;
        ExclamationText.FontFamily = FontHelper.Bold;
        YourAccountText.FontFamily = FontHelper.Book;
        SignOutText.FontFamily = FontHelper.Bold;
        SpinnerText.FontFamily = FontHelper.Bold;
    }

    private async void OnSignInClick(object sender, RoutedEventArgs args)
    {
        SetBusy(true);
        try
        {
            await CredentialManager.LoginAsync();
            await RefreshAccountAsync();
        }
        catch (Exception exception)
        {
            _utilities?.LogDebug($"Spotify sign-in failed: {exception}");
            _utilities?.Log($"Spotify sign-in failed: {exception.Message}");
            ShowSignedIn(false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnYourAccountClick(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.spotify.com/account/overview/")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Failed to open the account page: {exception.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnSignOutClick(object sender, RoutedEventArgs args)
    {
        try
        {
            CredentialManager.SignOut();
            ShowSignedIn(false);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Failed to sign out: {exception.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
