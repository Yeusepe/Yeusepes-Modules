using System.IO;
using System.Net.Http;
using System.Windows.Media;

namespace YeusepesModules.SPOTIOSC.UI;

internal static class FontHelper
{
    private const string Source = "https://db.onlinewebfonts.com/t/";
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "YeusepesFonts") + Path.DirectorySeparatorChar;
    private static readonly string[] Files = ["424dca828dee407629efe273d0a28630.ttf", "91bf51a8358ebe7c72e49ea84fd8db7f.ttf"];
    private static Task<FontFamily?>? _spotifyMix;

    public static Task<FontFamily?> SpotifyMixAsync() => _spotifyMix ??= LoadAsync();

    private static async Task<FontFamily?> LoadAsync()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            using var http = new HttpClient();
            foreach (var file in Files.Where(file => !File.Exists(Folder + file)))
                await File.WriteAllBytesAsync(Folder + file, await http.GetByteArrayAsync(Source + file));
            return new FontFamily(new Uri(Folder), "./#Spotify Mix");
        }
        catch
        {
            return null;
        }
    }
}
