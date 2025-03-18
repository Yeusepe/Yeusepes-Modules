using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;
using System;
using System.IO;


namespace YeusepesModules.SPOTIOSC.UI
{
    public static class UIHelpers
    {
        public static IEnumerable<T> FindChildren<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    yield return typedChild;

                foreach (var descendant in FindChildren<T>(child))
                    yield return descendant;
            }
        }
    }


    public static class FontHelper
    {
        public static void ApplyFonts(string tempFontDirectory)
        {
            try
            {
                // Define font paths
                string boldFontPath = Path.Combine(tempFontDirectory, "circular-std-4.ttf");
                string blackFontPath = Path.Combine(tempFontDirectory, "circular-std-2.ttf");
                string bookFontPath = Path.Combine(tempFontDirectory, "circular-std-6.ttf");

                // Validate the existence of font files
                if (!File.Exists(boldFontPath) || !File.Exists(blackFontPath) || !File.Exists(bookFontPath))
                {
                    MessageBox.Show("Font files are missing. Please ensure they are downloaded correctly.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Load fonts dynamically
                var boldFontFamily = new FontFamily(new Uri(boldFontPath, UriKind.Absolute), "./#Circular Std Bold");
                var blackFontFamily = new FontFamily(new Uri(blackFontPath, UriKind.Absolute), "./#Circular Std Black");
                var bookFontFamily = new FontFamily(new Uri(bookFontPath, UriKind.Absolute), "./#Circular Std Book");

                // Store fonts in application-level resources
                Application.Current.Resources["CircularStdBold"] = boldFontFamily;
                Application.Current.Resources["CircularStdBlack"] = blackFontFamily;
                Application.Current.Resources["CircularStdBook"] = bookFontFamily;

                Console.WriteLine("Fonts applied successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying fonts: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }



}
