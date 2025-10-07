using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace YeusepesModules.ShazamOSC.UI
{
    public class SavedSong
    {
        private readonly Action<string> _logDebug;
        
        public string RawJson { get; }
        public string Title { get; }
        public string Artist { get; }
        public string CoverArtUrl { get; }
        public string SpotifyUri { get; }
        public string ShazamUri { get; }

        public ICommand OpenSpotifyCommand { get; }
        public ICommand OpenShazamCommand { get; }

        public SavedSong(string rawJson, Action<string> logDebug = null)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new ArgumentNullException(nameof(rawJson));

            _logDebug = logDebug ?? (_ => { });
            RawJson = rawJson;
            JsonNode root = JsonNode.Parse(rawJson)!;
            JsonNode trackNode = root["track"] ?? root;

            Title = trackNode["title"]?.GetValue<string>() ?? "";
            Artist = trackNode["subtitle"]?.GetValue<string>() ?? "";
            CoverArtUrl = trackNode["images"]?["coverart"]?.GetValue<string>() ?? "";

            // Extract Spotify URI from hub.providers array
            string? spotifyUri = null;
            JsonArray providers = trackNode["hub"]?["providers"] as JsonArray ?? new JsonArray();
            
            // Find the Spotify provider
            foreach (var provider in providers)
            {
                if (provider?["type"]?.GetValue<string>() == "SPOTIFY")
                {
                    JsonArray actions = provider["actions"] as JsonArray ?? new JsonArray();
                    spotifyUri = actions
                        .Select(a => a?["uri"]?.GetValue<string>())
                        .FirstOrDefault(u => !string.IsNullOrEmpty(u));
                    break;
                }
            }

            SpotifyUri = spotifyUri ?? "";

            _logDebug($"[SavedSong] Extracted SpotifyUri = '{SpotifyUri}'");

            ShazamUri = trackNode["url"]?.GetValue<string>() ?? "";
            _logDebug($"[SavedSong] Extracted ShazamUri  = '{ShazamUri}'");

            OpenSpotifyCommand = new RelayCommand(_ =>
            {
                _logDebug($"[SavedSong] OpenSpotifyCommand clicked! SpotifyUri = '{SpotifyUri}'");
                
                if (string.IsNullOrEmpty(SpotifyUri))
                {
                    _logDebug("[SavedSong] SpotifyUri is null or empty - cannot launch");
                    return;
                }
                
                if (!SpotifyUri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
                {
                    _logDebug($"[SavedSong] SpotifyUri doesn't start with 'spotify:' - URI: '{SpotifyUri}'");
                    return;
                }
                
                try
                {
                    _logDebug($"[SavedSong] Launching Spotify URI: {SpotifyUri}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = SpotifyUri,
                        UseShellExecute = true
                    });
                    _logDebug($"[SavedSong] Successfully launched Spotify URI");
                }
                catch (Exception ex)
                {
                    _logDebug($"[SavedSong] Error launching Spotify URI: {ex.Message}");
                    _logDebug($"[SavedSong] Stack trace: {ex.StackTrace}");
                }
            });

            OpenShazamCommand = new RelayCommand(_ =>
            {
                if (!string.IsNullOrEmpty(ShazamUri))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = ShazamUri,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SavedSong] Error launching Shazam URI: {ex.Message}");
                    }
                }
            });
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool> _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (_ => true);
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);
    }
}
