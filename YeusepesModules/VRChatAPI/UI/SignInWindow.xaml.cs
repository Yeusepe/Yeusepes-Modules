using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.UI.Core;
using YeusepesModules.VRChatAPI.Utils.Requests;
using YeusepesModules.VRChatAPI.Utils;

namespace YeusepesModules.VRChatAPI.UI
{
    public partial class SignInWindow : IManagedWindow
    {
        private readonly VRChatAPI _module;
        private object _comparer;  // Holds the comparer value
        private VRChatApiService _apiService;
        private VRChatRequestContext _context;

        public SignInWindow(VRChatAPI module)
        {
            InitializeComponent();
            _module = module;
            _comparer = new object(); // Initialize comparer

            // Set up the SignIn control with the module and setting
            var setting = _module.GetSetting(VRChatAPI.VRChatSettings.SignInButton);

            var signInControl = new SignIn(_module, setting);
            SignInControl = signInControl;

            // Initialize API service
            InitializeApiService();

            // Handle window events
            SourceInitialized += SignInWindow_SourceInitialized;
            Closed += SignInWindow_Closed;
        }

        private void InitializeApiService()
        {
            try
            {
                _context = new VRChatRequestContext
                {
                    HttpClient = new System.Net.Http.HttpClient()
                };

                var utilities = new VRChatUtilities
                {
                    Log = message => System.Diagnostics.Debug.WriteLine(message),
                    LogDebug = message => System.Diagnostics.Debug.WriteLine(message),
                    SendParameter = (param, value) => { }
                };

                _apiService = new VRChatApiService(_context, utilities);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing API service: {ex.Message}");
            }
        }

        private async void SignInWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // Load initial data when window is shown
            await LoadUserData();
        }

        private void SignInWindow_Closed(object sender, EventArgs e)
        {
            // Cleanup logic if needed
            _context?.HttpClient?.Dispose();
        }

        private async Task LoadUserData()
        {
            if (_apiService == null) return;

            try
            {
                // Load user information
                await _apiService.GetUserInfoAsync();
                UpdateUserInfo();

                // Load friends
                await _apiService.GetFriendsAsync();
                UpdateFriendsList();

                // Load calendar events
                await _apiService.GetCalendarEventsAsync();
                UpdateCalendarList();

                // Load notifications
                await _apiService.GetNotificationsAsync();
                UpdateNotificationsList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading user data: {ex.Message}");
            }
        }

        private void UpdateUserInfo()
        {
            if (_context == null) return;

            UserDisplayName.Text = _context.DisplayName ?? "Not available";
            Username.Text = _context.Username ?? "Not available";
            UserStatus.Text = _context.UserStatus ?? "Not available";
            UserLocation.Text = _context.Location ?? "Not available";
        }

        private void UpdateFriendsList()
        {
            if (_context?.Friends == null) return;

            FriendsListBox.ItemsSource = _context.Friends;
        }

        private void UpdateCalendarList()
        {
            if (_context?.CalendarEvents == null) return;

            CalendarListBox.ItemsSource = _context.CalendarEvents;
        }

        private void UpdateNotificationsList()
        {
            if (_context?.Notifications == null) return;

            NotificationsListBox.ItemsSource = _context.Notifications;
        }

        private async void RefreshFriendsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiService == null) return;

            try
            {
                await _apiService.GetFriendsAsync();
                UpdateFriendsList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing friends: {ex.Message}");
            }
        }

        private async void RefreshCalendarButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiService == null) return;

            try
            {
                await _apiService.GetCalendarEventsAsync();
                UpdateCalendarList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing calendar: {ex.Message}");
            }
        }

        private async void RefreshNotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_apiService == null) return;

            try
            {
                await _apiService.GetNotificationsAsync();
                UpdateNotificationsList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing notifications: {ex.Message}");
            }
        }

        private async void RefreshAllButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadUserData();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public object GetComparer() => _comparer;
    }
}
