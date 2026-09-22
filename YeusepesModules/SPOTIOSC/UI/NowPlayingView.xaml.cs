using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using YeusepesModules.SPOTIOSC.Runtime;
using YeusepesModules.SPOTIOSC.Runtime.Playback;

namespace YeusepesModules.SPOTIOSC.UI;

public partial class NowPlayingRuntimeView : UserControl
{
    public NowPlayingRuntimeView(SpotiOSC module)
    {
        InitializeComponent();
        DataContext = module.spotifyRequestContext;
        if (module.spotifyRequestContext is not null && module.spotifyUtilities is not null)
            _ = SpotifyPlaybackStateLoader.LoadAsync(module.spotifyRequestContext, module.spotifyUtilities);
    }
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool flag) return Visibility.Collapsed;
        var invert = parameter is string text && bool.TryParse(text, out var parsed) && parsed;
        return (invert ? !flag : flag) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ColorBrightnessToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not System.Windows.Media.Color color) return System.Windows.Media.Brushes.White;
        var brightness = color.R * 0.299 + color.G * 0.587 + color.B * 0.114;
        return brightness > 128 ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
