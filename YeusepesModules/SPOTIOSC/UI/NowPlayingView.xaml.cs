using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using YeusepesModules.SPOTIOSC.Utils.Requests;
using System.Net.Http;
using Microsoft.VisualBasic.Logging;

namespace YeusepesModules.SPOTIOSC.UI
{
    public partial class NowPlayingRuntimeView : UserControl
    {
        private readonly SpotiOSC _module;
        private readonly string _tempFontDirectory = Path.GetTempPath();
        public NowPlayingRuntimeView(SpotiOSC module)
        {
            InitializeComponent();
            _module = module;
            DataContext = module.spotifyRequestContext;
            FontHelper.ApplyFonts(_tempFontDirectory);

            SpotifyRequest.ExtractCurrentlyPlayingState(_module.spotifyRequestContext, _module.spotifyUtilities);
        }


    }
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                bool invert = parameter is string strParam && bool.TryParse(strParam, out bool parsedParam) && parsedParam;
                return (invert ? !boolValue : boolValue) ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public static class ImageColorHelper
    {
        public static async Task<System.Drawing.Color> GetSingleColorAsync(string imageUrl, HttpClient client)
        {
            try
            {
                var imageData = await client.GetByteArrayAsync(imageUrl);
                using (var ms = new MemoryStream(imageData))
                using (var bitmap = new Bitmap(ms))
                {
                    var pixelColor = bitmap.GetPixel(0, 0);
                    return System.Drawing.Color.FromArgb(pixelColor.A, pixelColor.R, pixelColor.G, pixelColor.B);
                }
            }
            catch
            {
                return System.Drawing.Color.Transparent;
            }
        }

        public static System.Drawing.Color GetSingleColor(string imageUrl, HttpClient client)
        {
            try
            {

                // Download the image data
                var imageData = client.GetByteArrayAsync(imageUrl).Result;

                using (var ms = new MemoryStream(imageData))
                using (var bitmap = new Bitmap(ms))
                {
                    // Sample the color from the top-left pixel
                    var pixelColor = bitmap.GetPixel(0, 0);
                    return System.Drawing.Color.FromArgb(pixelColor.A, pixelColor.R, pixelColor.G, pixelColor.B);
                }

            }
            catch
            {
                // Return transparent if any error occurs
                return System.Drawing.Color.Transparent;
            }
        }
    }

    public class ColorBrightnessToForegroundConverter : IValueConverter
    {
        // The Convert method takes a System.Windows.Media.Color and returns a Brush.
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is System.Windows.Media.Color color)
            {
                // Calculate brightness using the luminance formula.
                // This formula weighs red, green, and blue components.
                double brightness = (color.R * 0.299 + color.G * 0.587 + color.B * 0.114);

                // Use a threshold value (128 in this example) to decide the contrasting color.
                return brightness > 128 ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White;
            }
            return System.Windows.Media.Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}


