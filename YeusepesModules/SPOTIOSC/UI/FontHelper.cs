using System.Windows.Media;

namespace YeusepesModules.SPOTIOSC.UI;

internal static class FontHelper
{
    private const string Root = "/YeusepesModules;component/SPOTIOSC/Resources/Fonts/";

    public static FontFamily Bold { get; } =
        new($"{Root}circular-std-4.ttf#Circular Std Bold");

    public static FontFamily Book { get; } =
        new($"{Root}circular-std-6.ttf#Circular Std Book");
}
