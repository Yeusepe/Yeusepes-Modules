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
using System.Net.Http.Headers;
using static YeusepesModules.SPOTIOSC.SpotiOSC;

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

        /// <summary>
        /// Clones an HttpRequestMessage by copying its method, URI, headers, and content.
        /// </summary>
        private async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);

            // Copy headers
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Copy content (if any)
            if (request.Content != null)
            {
                var contentBytes = await request.Content.ReadAsByteArrayAsync();
                var contentClone = new ByteArrayContent(contentBytes);
                foreach (var header in request.Content.Headers)
                {
                    contentClone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                clone.Content = contentClone;
            }
            return clone;
        }

        public async Task<string> SendAsync(HttpRequestMessage request)
        {
            const int maxRetries = 1;
            int attempt = 0;
            HttpResponseMessage response = null;

            // Store the original request for cloning on retries.
            var originalRequest = request;
            // Create a fresh clone for the first attempt.
            var clonedRequest = await CloneRequestAsync(originalRequest);

            while (attempt <= maxRetries)
            {
                response = await HttpClient.SendAsync(clonedRequest);

                // If the response is not unauthorized, break out.
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                    break;

                // Handle unauthorized response by trying to refresh the token.
                bool tokenRefreshed = await RefreshAccessTokenAsync();
                if (tokenRefreshed)
                {
                    // Recreate the request with the updated token.
                    clonedRequest = await CloneRequestAsync(originalRequest);
                    clonedRequest.Headers.Remove("Authorization");
                    clonedRequest.Headers.Add("Authorization", $"Bearer {CredentialManager.LoadAccessToken()}");
                    attempt++;
                    continue;
                }
                else
                {
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
                    // Use the access token stored in the context.
                    request.Headers.Add("Authorization", $"Bearer {context.ApiToken}");
                    utilities.LogDebug("Fetching current playback state...");

                    HttpResponseMessage response = await context.HttpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        string responseContent = await response.Content.ReadAsStringAsync();
                        utilities.LogDebug("Successfully fetched current playback state:");
                        utilities.LogDebug(responseContent);

                        var playbackState = JsonSerializer.Deserialize<JsonElement>(responseContent);

                        // --- Device Information ---
                        if (playbackState.TryGetProperty("device", out JsonElement device))
                        {
                            context.DeviceId = device.GetProperty("id").GetString();
                            context.DeviceName = device.GetProperty("name").GetString();
                            context.IsActiveDevice = device.GetProperty("is_active").GetBoolean();
                            context.VolumePercent = device.GetProperty("volume_percent").GetInt32();
                        }

                        // --- Playback State ---
                        if (playbackState.TryGetProperty("shuffle_state", out JsonElement shuffle))
                        {
                            context.ShuffleState = shuffle.GetBoolean();
                        }
                        if (playbackState.TryGetProperty("smart_shuffle", out JsonElement smartShuffle))
                        {
                            context.SmartShuffle = smartShuffle.GetBoolean();
                        }
                        if (playbackState.TryGetProperty("repeat_state", out JsonElement repeat))
                        {
                            context.RepeatState = repeat.GetString();
                        }
                        if (playbackState.TryGetProperty("is_playing", out JsonElement isPlaying))
                        {
                            context.IsPlaying = isPlaying.GetBoolean();
                        }
                        if (playbackState.TryGetProperty("timestamp", out JsonElement timestamp))
                        {
                            context.Timestamp = timestamp.GetInt64();
                        }
                        if (playbackState.TryGetProperty("progress_ms", out JsonElement progressMs))
                        {
                            context.ProgressMs = progressMs.GetInt32();
                        }

                        // --- Context (e.g. Playlist) ---
                        if (playbackState.TryGetProperty("context", out JsonElement contextObj))
                        {
                            if (contextObj.TryGetProperty("external_urls", out JsonElement extUrls) &&
                                extUrls.TryGetProperty("spotify", out JsonElement contextSpotifyUrl))
                            {
                                context.ContextExternalUrl = contextSpotifyUrl.GetString();
                            }
                            if (contextObj.TryGetProperty("href", out JsonElement contextHref))
                            {
                                context.ContextHref = contextHref.GetString();
                            }
                            if (contextObj.TryGetProperty("type", out JsonElement contextType))
                            {
                                context.ContextType = contextType.GetString();
                            }
                            if (contextObj.TryGetProperty("uri", out JsonElement contextUri))
                            {
                                context.ContextUri = contextUri.GetString();
                            }
                        }

                        // --- Track Details ---
                        if (playbackState.TryGetProperty("item", out JsonElement item))
                        {
                            // Basic track information
                            context.TrackName = item.GetProperty("name").GetString();

                            // Track duration, disc number, explicit flag, popularity, preview URL, track number, and track URI.
                            if (item.TryGetProperty("duration_ms", out JsonElement duration))
                            {
                                context.TrackDurationMs = duration.GetInt32();
                            }
                            if (item.TryGetProperty("disc_number", out JsonElement discNumber))
                            {
                                context.DiscNumber = discNumber.GetInt32();
                            }
                            if (item.TryGetProperty("explicit", out JsonElement explicitElem))
                            {
                                context.IsExplicit = explicitElem.GetBoolean();
                            }
                            if (item.TryGetProperty("popularity", out JsonElement popularity))
                            {
                                context.Popularity = popularity.GetInt32();
                            }
                            if (item.TryGetProperty("preview_url", out JsonElement previewUrl))
                            {
                                context.PreviewUrl = previewUrl.GetString();
                            }
                            if (item.TryGetProperty("track_number", out JsonElement trackNumber))
                            {
                                context.TrackNumber = trackNumber.GetInt32();
                            }
                            if (item.TryGetProperty("uri", out JsonElement trackUri))
                            {
                                context.TrackUri = trackUri.GetString();
                            }
                            if (item.TryGetProperty("currently_playing_type", out JsonElement playingType))
                            {
                                context.CurrentlyPlayingType = playingType.GetString();
                            }

                            // --- Album Details ---
                            if (item.TryGetProperty("album", out JsonElement album))
                            {
                                context.AlbumName = album.GetProperty("name").GetString();
                                if (album.TryGetProperty("images", out JsonElement images))
                                {
                                    var imageUrl = images.EnumerateArray().FirstOrDefault().GetProperty("url").GetString();
                                    context.AlbumArtworkUrl = imageUrl;
                                    // Update color with utilities to send RGB parameters
                                    context.UpdateSingleColor(utilities);
                                }
                                if (album.TryGetProperty("album_type", out JsonElement albumType))
                                {
                                    context.AlbumType = albumType.GetString();
                                }
                                if (album.TryGetProperty("release_date", out JsonElement releaseDate))
                                {
                                    context.AlbumReleaseDate = releaseDate.GetString();
                                }
                                if (album.TryGetProperty("total_tracks", out JsonElement totalTracks))
                                {
                                    context.AlbumTotalTracks = totalTracks.GetInt32();
                                }
                            }

                            // --- Artists Details ---
                            if (item.TryGetProperty("artists", out JsonElement artists))
                            {
                                var artistList = artists.EnumerateArray()
                                    .Select(artist => (Name: artist.GetProperty("name").GetString(), Uri: artist.GetProperty("uri").GetString()))
                                    .ToList();
                                context.Artists = artistList;
                            }
                        }
                    }
                    else
                    {
                        utilities.Log($"Spotify seems to either be paused or your device marked as \"Not active\". Try pausing/unpausing to see if your device activates!");
                        string errorContent = await response.Content.ReadAsStringAsync();
                        utilities.LogDebug($"Error response: {errorContent}");
                    }
                }
            }
            catch (Exception ex)
            {
                // Check if the error message matches the expected JSON token error.
                if (ex.Message.Contains("The input does not contain any JSON tokens"))
                {
                    // Optionally log that the error was ignored.
                    utilities.LogDebug("Ignored JSON token error while fetching playback state.");
                    return; // Ignore the error.
                }

                // Log any other exceptions.
                utilities.LogDebug($"An error occurred while fetching playback state: {ex.Message}");
            }

        }


        public async Task<bool> RefreshAccessTokenAsync()
        {
            try
            {
                await CredentialManager.LoginAndCaptureCookiesAsync();
                string newAccessToken = CredentialManager.LoadAccessToken();
                return !string.IsNullOrEmpty(newAccessToken);
            }
            catch (Exception)
            {
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

        public string ApiToken { get; set; }
        
        // Store utilities for sending parameters
        public SpotifyUtilities Utilities { get; set; }

        // Device Information
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public bool IsActiveDevice { get; set; }
        public int VolumePercent { get; set; }
        public bool SmartShuffle { get; set; }
        public long Timestamp { get; set; }
        public int ProgressMs { get; set; }
        public string ContextExternalUrl { get; set; }
        public string ContextHref { get; set; }
        public string ContextType { get; set; }
        public string ContextUri { get; set; }
        public int TrackDurationMs { get; set; }
        public int DiscNumber { get; set; }
        public bool IsExplicit { get; set; }
        public int Popularity { get; set; }
        public string PreviewUrl { get; set; }
        public int TrackNumber { get; set; }
        public string TrackUri { get; set; }
        public string CurrentlyPlayingType { get; set; }
        public string AlbumType { get; set; }
        public string AlbumReleaseDate { get; set; }
        public int AlbumTotalTracks { get; set; }

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

        // Playback Restrictions
        private bool _disallowPausing;
        public bool DisallowPausing
        {
            get => _disallowPausing;
            set { _disallowPausing = value; OnPropertyChanged(); }
        }

        private bool _disallowResuming;
        public bool DisallowResuming
        {
            get => _disallowResuming;
            set { _disallowResuming = value; OnPropertyChanged(); }
        }

        private bool _disallowSkippingPrev;
        public bool DisallowSkippingPrev
        {
            get => _disallowSkippingPrev;
            set { _disallowSkippingPrev = value; OnPropertyChanged(); }
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
                OnPropertyChanged(); 
                // Update color with stored utilities if available
                UpdateSingleColor(Utilities);
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

        // Audio Features
        private float _danceability;
        public float Danceability
        {
            get => _danceability;
            set { _danceability = value; OnPropertyChanged(); }
        }

        private float _energy;
        public float Energy
        {
            get => _energy;
            set { _energy = value; OnPropertyChanged(); }
        }

        private int _key;
        public int Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        private float _loudness;
        public float Loudness
        {
            get => _loudness;
            set { _loudness = value; OnPropertyChanged(); }
        }

        private int _mode;
        public int Mode
        {
            get => _mode;
            set { _mode = value; OnPropertyChanged(); }
        }

        private float _speechiness;
        public float Speechiness
        {
            get => _speechiness;
            set { _speechiness = value; OnPropertyChanged(); }
        }

        private float _acousticness;
        public float Acousticness
        {
            get => _acousticness;
            set { _acousticness = value; OnPropertyChanged(); }
        }

        private float _instrumentalness;
        public float Instrumentalness
        {
            get => _instrumentalness;
            set { _instrumentalness = value; OnPropertyChanged(); }
        }

        private float _liveness;
        public float Liveness
        {
            get => _liveness;
            set { _liveness = value; OnPropertyChanged(); }
        }

        private float _valence;
        public float Valence
        {
            get => _valence;
            set { _valence = value; OnPropertyChanged(); }
        }

        private float _tempo;
        public float Tempo
        {
            get => _tempo;
            set { _tempo = value; OnPropertyChanged(); }
        }

        private int _timeSignature;
        public int TimeSignature
        {
            get => _timeSignature;
            set { _timeSignature = value; OnPropertyChanged(); }
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

        private string _jamShortCode;
        public string JamShortCode
        {
            get => _jamShortCode;
            set
            {
                _jamShortCode = value;
                OnPropertyChanged();
            }
        }

        private bool _isJamOwner;
        public bool IsJamOwner
        {
            get => _isJamOwner;
            set
            {
                _isJamOwner = value;
                OnPropertyChanged();
            }
        }

        public bool IsLocal { get; internal set; }        

        public void UpdateSingleColor(SpotifyUtilities utilities = null)
        {
            if (!string.IsNullOrEmpty(AlbumArtworkUrl))
            {
                var drawingColor = ImageColorHelper.GetSingleColor(AlbumArtworkUrl, HttpClient);
                var color = System.Windows.Media.Color.FromArgb(drawingColor.A, drawingColor.R, drawingColor.G, drawingColor.B);
                DominantColor = color;
                
                // Send RGB parameters if utilities are available
                // Cast byte to float since OSC parameters are registered as float type
                if (utilities?.SendParameter != null)
                {
                    utilities.SendParameter(SpotiOSC.SpotiParameters.AlbumColorR, (float)color.R);
                    utilities.SendParameter(SpotiOSC.SpotiParameters.AlbumColorG, (float)color.G);
                    utilities.SendParameter(SpotiOSC.SpotiParameters.AlbumColorB, (float)color.B);
                }
            }
            else
            {
                DominantColor = System.Windows.Media.Colors.Transparent;
                
                // Send zero RGB parameters if utilities are available
                if (utilities?.SendParameter != null)
                {
                    utilities.SendParameter(SpotiOSC.SpotiParameters.AlbumColorR, 0f);
                    utilities.SendParameter(SpotiOSC.SpotiParameters.AlbumColorG, 0f);
                    utilities.SendParameter(SpotiOSC.SpotiParameters.AlbumColorB, 0f);
                }
            }
        }





    }
    public class GenericSpotifyRequest : SpotifyRequest
    {
        public GenericSpotifyRequest(HttpClient httpClient, string accessToken, string clientToken)
            : base(httpClient, accessToken, clientToken) { }

        public GenericSpotifyRequest(HttpClient httpClient, string accessToken)
            : base(httpClient, accessToken, clientToken: null) { }

        // Expose a method to send the request using the base SendAsync logic.
        public async Task<string> SendRequestAsync(HttpRequestMessage request)
        {
            return await SendAsync(request);
        }

        /// <summary>
        /// Overrides the base CreateRequest to drop all headers
        /// except Authorization: Bearer {AccessToken}.
        /// </summary>
        public HttpRequestMessage CreateRequest(
            HttpMethod method,
            string url,
            HttpContent content = null)
        {
            var request = new HttpRequestMessage(method, url);

            // Only the OAuth token header
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

            // Attach body if you explicitly provided one (e.g. for context_uri in play)
            if (content != null)
                request.Content = content;

            return request;
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
