using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.Utils;

namespace YeusepesModules.Common
{ 
    public class CustomModuleSetting : BoolModuleSetting
    {
        public CustomModuleSetting(string title, string description, Type controlType, bool defaultValue = false)
            : base(title, description, controlType, defaultValue)
        {
        }
    }
}
