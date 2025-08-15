using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows.Input;
using YeusepesLowLevelTools;

namespace YeusepesModules.ShazamOSC.UI
{
    public class SavedSong
    {
        public string RawJson { get; }
        public string Title { get; }
        public string Artist { get; }
        public string CoverArtUrl { get; }
        public string SpotifyUri { get; }
        public string ShazamUri { get; }

        public ICommand OpenSpotifyCommand { get; }
        public ICommand OpenShazamCommand { get; }

        public SavedSong(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new ArgumentNullException(nameof(rawJson));

            RawJson = rawJson;
            JsonNode root = JsonNode.Parse(rawJson)!;
            JsonNode trackNode = root["track"] ?? root;

            Title = trackNode["title"]?.GetValue<string>() ?? "";
            Artist = trackNode["subtitle"]?.GetValue<string>() ?? "";
            CoverArtUrl = trackNode["images"]?["coverart"]?.GetValue<string>() ?? "";

            // Safely get the JSON array of actions (may be null)
            JsonArray actions = trackNode["hub"]?["actions"] as JsonArray
                                ?? new JsonArray();

            // Look first for a search URI, then fallback to a track URI
            string? searchUri = actions
                .Select(a => a?["uri"]?.GetValue<string>())
                .FirstOrDefault(u => u != null && u.StartsWith("spotify:search:", StringComparison.OrdinalIgnoreCase));

            string? trackUri = actions
                .Select(a => a?["uri"]?.GetValue<string>())
                .FirstOrDefault(u => u != null && u.StartsWith("spotify:track:", StringComparison.OrdinalIgnoreCase));

            SpotifyUri = searchUri
                ?? trackUri
                ?? "";

            Console.WriteLine($"[SavedSong] Extracted SpotifyUri = '{SpotifyUri}'");

            ShazamUri = trackNode["url"]?.GetValue<string>() ?? "";
            Console.WriteLine($"[SavedSong] Extracted ShazamUri  = '{ShazamUri}'");

            OpenSpotifyCommand = new RelayCommand(_ =>
            {
                UriLauncher.LaunchUri(SpotifyUri);
            });

            OpenShazamCommand = new RelayCommand(_ =>
            {
                UriLauncher.LaunchUri(ShazamUri);
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
