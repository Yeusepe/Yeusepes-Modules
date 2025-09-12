using System;
using VRCOSC.App.SDK.Parameters;

namespace YeusepesModules.VRChatAPI.Utils
{
    public class VRChatUtilities
    {
        public Action<string> Log { get; set; }
        public Action<string> LogDebug { get; set; }
        public Action<Enum, object> SendParameter { get; set; }
    }
}
