using YeusepesModules.Common.ScreenUtilities;

namespace YeusepesModules.IDC.Encoder
{
    public class EncodingUtilities
    {
        public bool IsDebug { get; set; }
        public Action<string> Log { get; set; }
        public Action<string> LogDebug { get; set; }

        // Add a Func to accept the FindParameter method
        public Func<Enum, Task<object?>> FindParameter { get; set; }

        // Add a Func to accept the FindParameter by string
        public Func<string, Task<object?>> FindParameterByString { get; set; }

        public Func<Enum, string> GetSettingValue;
        public Action<Enum, string> SetSettingValue;
        public ScreenUtilities ScreenUtilities { get; set; }


    }
}
