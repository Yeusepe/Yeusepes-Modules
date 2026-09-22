using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace YeusepesModules.SPOTIOSC.Utils.Requests;

public sealed class SpotifyRequestContext : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public HttpClient HttpClient { get; init; } = null!;
    public string AccessToken { get; init; } = string.Empty;
    public string ClientToken { get; init; } = string.Empty;
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public bool IsActiveDevice { get; set; }
    public int VolumePercent { get; set; }
    public bool SmartShuffle { get; set; }
    public long Timestamp { get; set; }
    public int ProgressMs { get; set; }
    public string? ContextExternalUrl { get; set; }
    public string? ContextHref { get; set; }
    public string? ContextType { get; set; }
    public string? ContextUri { get; set; }
    public int TrackDurationMs { get; set; }
    public int DiscNumber { get; set; }
    public bool IsExplicit { get; set; }
    public bool IsLocal { get; set; }
    public int Popularity { get; set; }
    public string? PreviewUrl { get; set; }
    public int TrackNumber { get; set; }
    public string? TrackUri { get; set; }
    public string? CurrentlyPlayingType { get; set; }
    public string? AlbumType { get; set; }
    public string? AlbumReleaseDate { get; set; }
    public int AlbumTotalTracks { get; set; }

    private bool _shuffleState;
    public bool ShuffleState { get => _shuffleState; set => Set(ref _shuffleState, value); }

    private string? _repeatState;
    public string? RepeatState { get => _repeatState; set => Set(ref _repeatState, value); }

    private bool _isPlaying;
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }

    private bool _disallowPausing;
    public bool DisallowPausing { get => _disallowPausing; set => Set(ref _disallowPausing, value); }

    private bool _disallowResuming;
    public bool DisallowResuming { get => _disallowResuming; set => Set(ref _disallowResuming, value); }

    private bool _disallowSkippingPrev;
    public bool DisallowSkippingPrev { get => _disallowSkippingPrev; set => Set(ref _disallowSkippingPrev, value); }

    private string? _trackName;
    public string? TrackName { get => _trackName; set => Set(ref _trackName, value); }

    private string? _albumName;
    public string? AlbumName { get => _albumName; set => Set(ref _albumName, value); }

    private string? _albumArtworkUrl;
    public string? AlbumArtworkUrl { get => _albumArtworkUrl; set => Set(ref _albumArtworkUrl, value); }

    private List<(string Name, string Uri)> _artists = [];
    public List<(string Name, string Uri)> Artists
    {
        get => _artists;
        set
        {
            _artists = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArtistNames));
        }
    }

    public string ArtistNames => string.Join(", ", Artists.Select(artist => artist.Name));

    private bool _isInJam;
    public bool IsInJam { get => _isInJam; set => Set(ref _isInJam, value); }

    private string? _jamOwnerName;
    public string? JamOwnerName { get => _jamOwnerName; set => Set(ref _jamOwnerName, value); }

    private float _danceability;
    public float Danceability { get => _danceability; set => Set(ref _danceability, value); }

    private float _energy;
    public float Energy { get => _energy; set => Set(ref _energy, value); }

    private int _key;
    public int Key { get => _key; set => Set(ref _key, value); }

    private float _loudness;
    public float Loudness { get => _loudness; set => Set(ref _loudness, value); }

    private int _mode;
    public int Mode { get => _mode; set => Set(ref _mode, value); }

    private float _speechiness;
    public float Speechiness { get => _speechiness; set => Set(ref _speechiness, value); }

    private float _acousticness;
    public float Acousticness { get => _acousticness; set => Set(ref _acousticness, value); }

    private float _instrumentalness;
    public float Instrumentalness { get => _instrumentalness; set => Set(ref _instrumentalness, value); }

    private float _liveness;
    public float Liveness { get => _liveness; set => Set(ref _liveness, value); }

    private float _valence;
    public float Valence { get => _valence; set => Set(ref _valence, value); }

    private float _tempo;
    public float Tempo { get => _tempo; set => Set(ref _tempo, value); }

    private int _timeSignature;
    public int TimeSignature { get => _timeSignature; set => Set(ref _timeSignature, value); }

    private System.Windows.Media.Color _dominantColor = System.Windows.Media.Colors.Transparent;
    public System.Windows.Media.Color DominantColor { get => _dominantColor; set => Set(ref _dominantColor, value); }

    private List<string> _jamParticipantImages = [];
    public List<string> JamParticipantImages { get => _jamParticipantImages; set => Set(ref _jamParticipantImages, value); }

    private string? _jamShortCode;
    public string? JamShortCode { get => _jamShortCode; set => Set(ref _jamShortCode, value); }

    private bool _isJamOwner;
    public bool IsJamOwner { get => _isJamOwner; set => Set(ref _isJamOwner, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class SpotifyUtilities
{
    public Action<string> Log { get; init; } = _ => { };
    public Action<string> LogDebug { get; init; } = _ => { };
}
