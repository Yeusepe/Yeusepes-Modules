using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using YeusepesModules.IDC.Encoder;
using YeusepesModules.SPOTIOSC.Credentials;
using VRCOSC.App.Utils;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using YeusepesModules.SPOTIOSC.UI;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Windows;

namespace YeusepesModules.SPOTIOSC.Utils.Requests
{
    public abstract class SpotifyRequest
    {
        protected HttpClient HttpClient { get; }
        protected string AccessToken { get; }
        protected string ClientToken { get; }

        public SpotifyRequest(HttpClient httpClient, string accessToken, string clientToken)
        {
            HttpClient = httpClient;
            AccessToken = accessToken;
            ClientToken = clientToken;
        }

        public HttpRequestMessage CreateRequest(HttpMethod method, string url, HttpContent content = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Authorization", $"Bearer {AccessToken}");
            request.Headers.Add("Client-Token", ClientToken);
            request.Headers.Add("App-Platform", "Win32_x86_64");
            request.Headers.Add("User-Agent", "Spotify/1.0");

            if (content != null)
            {
                request.Content = content;
            }

            return request;
        }

        public async Task<string> SendAsync(HttpRequestMessage request)
        {
            const int maxRetries = 1;
            int attempt = 0;
            HttpResponseMessage response = null;

            while (attempt <= maxRetries)
            {
                response = await HttpClient.SendAsync(request);

                // If response is not unauthorized, break out.
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                    break;

                // Handle unauthorized response by trying to refresh the token.
                bool tokenRefreshed = await RefreshAccessTokenAsync();
                if (tokenRefreshed)
                {
                    // Update the request's Authorization header with the new token.
                    request.Headers.Remove("Authorization");
                    request.Headers.Add("Authorization", $"Bearer {CredentialManager.LoadAccessToken()}");

                    attempt++;
                    // Optionally, you might want to recreate the request if it's non-reusable.
                    continue;
                }
                else
                {
                    // If token refresh fails, prompt the user to sign in.
                    // You can throw an exception or return an error message here.
                    throw new UnauthorizedAccessException("Token refresh failed. Please sign in again.");
                }
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }



        public static async Task ExtractCurrentlyPlayingState(SpotifyRequestContext context, SpotifyUtilities utilities)
        {
            const string endpoint = "https://api.spotify.com/v1/me/player";

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, endpoint))
                {
                    request.Headers.Add("Authorization", $"Bearer {context.AccessToken}");
                    utilities.Log("Fetching current playback state...");

                    HttpResponseMessage response = await context.HttpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();
                        utilities.LogDebug("Successfully fetched current playback state:");
                        utilities.LogDebug(responseContent);

                        var playbackState = JsonSerializer.Deserialize<JsonElement>(responseContent);

                        if (playbackState.TryGetProperty("device", out JsonElement device))
                        {
                            context.DeviceId = device.GetProperty("id").GetString();
                            context.DeviceName = device.GetProperty("name").GetString();
                            context.IsActiveDevice = device.GetProperty("is_active").GetBoolean();
                            context.VolumePercent = device.GetProperty("volume_percent").GetInt32();
                        }

                        // Update observables using safe extensions.
                        context.ShuffleState = playbackState.GetProperty("shuffle_state").GetBoolean();
                        context.RepeatState = playbackState.GetProperty("repeat_state").GetString();
                        context.IsPlaying = playbackState.GetProperty("is_playing").GetBoolean();

                        if (playbackState.TryGetProperty("item", out JsonElement item))
                        {
                            context.TrackName = item.GetProperty("name").GetString();

                            if (item.TryGetProperty("album", out JsonElement album))
                            {
                                context.AlbumName = album.GetProperty("name").GetString();
                                if (album.TryGetProperty("images", out JsonElement images))
                                {
                                    var imageUrl = images.EnumerateArray().FirstOrDefault().GetProperty("url").GetString();
                                    context.AlbumArtworkUrl = imageUrl;
                                }
                            }

                            if (item.TryGetProperty("artists", out var artists))
                            {
                                var artistList = artists.EnumerateArray()
                                    .Select(artist => (artist.GetProperty("name").GetString(), artist.GetProperty("uri").GetString()))
                                    .ToList();

                                context.Artists = artists.EnumerateArray()
                                .Select(artist => (artist.GetProperty("name").GetString(), artist.GetProperty("uri").GetString()))
                                .ToList();
                            }
                        }
                    }
                    else
                    {
                        utilities.Log($"Failed to fetch playback state. Status: {response.StatusCode}");
                        string errorContent = await response.Content.ReadAsStringAsync();
                        utilities.Log($"Error response: {errorContent}");
                    }
                }
            }
            catch (Exception ex)
            {
                utilities.Log($"An error occurred while fetching playback state: {ex.Message}");
            }
        }


        private async Task<bool> RefreshAccessTokenAsync()
        {
            try
            {
                //Logger.Log("Starting full token flow to refresh access token...");
                await CredentialManager.AuthenticateAsync();

                // Check if a new access token was loaded
                string newAccessToken = CredentialManager.LoadAccessToken();
                if (!string.IsNullOrEmpty(newAccessToken))
                {
                    //Logger.Log("Access token successfully refreshed and loaded.");
                    return true;
                }
                else
                {
                    //Logger.Log("Failed to load refreshed access token.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                //Logger.Log($"Error while refreshing access token: {ex.Message}");
                return false;
            }
        }
    }

    public class SpotifyRequestContext : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public HttpClient HttpClient { get; set; }
        public string AccessToken { get; set; }
        public string ClientToken { get; set; }

        // Device Information
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public bool IsActiveDevice { get; set; }
        public int VolumePercent { get; set; }

        // Playback State
        private bool _shuffleState;
        public bool ShuffleState
        {
            get => _shuffleState;
            set { _shuffleState = value; OnPropertyChanged(); }
        }

        private string _repeatState;
        public string RepeatState
        {
            get => _repeatState;
            set { _repeatState = value; OnPropertyChanged(); }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(); }
        }

        // Track Details
        private string _trackName;
        public string TrackName
        {
            get => _trackName;
            set { _trackName = value; OnPropertyChanged(); }
        }

        private string _albumName;
        public string AlbumName
        {
            get => _albumName;
            set { _albumName = value; OnPropertyChanged(); }
        }

        private string _albumArtworkUrl;
        public string AlbumArtworkUrl
        {
            get => _albumArtworkUrl;
            set
            {
                _albumArtworkUrl = value;

                Console.WriteLine("URL: " + _albumArtworkUrl);
                OnPropertyChanged(); UpdateSingleColor();
            }
        }

        private List<(string Name, string Uri)> _artists = new();
        public List<(string Name, string Uri)> Artists
        {
            get => _artists;
            set
            {
                _artists = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ArtistNames)); // Notify UI to update ArtistNames
            }
        }


        private string _artistNames;
        public string ArtistNames
        {
            get => string.Join(", ", Artists.Select(a => a.Name));
            set
            {
                _artistNames = value;
                OnPropertyChanged();
            }
        }


        private string _albumUri;
        public string AlbumUri
        {
            get => _albumUri;
            set
            {
                _albumUri = value;
                OnPropertyChanged();
            }
        }

        private bool _isInJam;
        public bool IsInJam
        {
            get => _isInJam;
            set
            {
                _isInJam = value;
                OnPropertyChanged();
            }
        }

        private string _jamOwnerName;
        public string JamOwnerName
        {
            get => _jamOwnerName;
            set
            {
                _jamOwnerName = value;
                OnPropertyChanged();
            }
        }


        // Helper to notify UI of property changes
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private System.Windows.Media.Color _dominantColor = System.Windows.Media.Colors.Transparent;
        public System.Windows.Media.Color DominantColor
        {
            get => _dominantColor;
            set
            {
                _dominantColor = value;
                OnPropertyChanged();
            }
        }

        private List<string> _jamParticipantImages = new();
        public List<string> JamParticipantImages
        {
            get => _jamParticipantImages;
            set
            {
                _jamParticipantImages = value;
                OnPropertyChanged();
            }
        }


        public void UpdateSingleColor()
        {
            if (!string.IsNullOrEmpty(AlbumArtworkUrl))
            {
                var drawingColor = ImageColorHelper.GetSingleColor(AlbumArtworkUrl, HttpClient);
                DominantColor = System.Windows.Media.Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B);
            }
            else
            {
                DominantColor = System.Windows.Media.Colors.Transparent;
            }
        }





    }
    public class GenericSpotifyRequest : SpotifyRequest
    {
        public GenericSpotifyRequest(HttpClient httpClient, string accessToken, string clientToken)
            : base(httpClient, accessToken, clientToken) { }

        // Expose a method to send the request using the base SendAsync logic.
        public async Task<string> SendRequestAsync(HttpRequestMessage request)
        {
            return await SendAsync(request);
        }
    }


    public class SpotifyUtilities
    {
        public Action<string> Log { get; set; }
        public Action<string> LogDebug { get; set; }
        public Action<Enum, object> SendParameter { get; set; }
        public StringEncoder Encoder { get; set; }        
    }

}
