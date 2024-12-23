using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRCOSC.App.SDK.Modules.Attributes.Settings;
using VRCOSC.App.Utils;

namespace YeusepesModules.Common
{ /*
    public class CustomModuleSetting : ModuleSetting
    {
        public string DefaultValue { get; }

        public CustomModuleSetting(string title, string description, Type controlType, string defaultValue = "")
            : base(title, description, controlType)
        {
            DefaultValue = defaultValue;
        }

        public override void SetDefault()
        {

        }

        public override bool IsDefault()
        {
            return true; // Adjust as required
        }

        public override bool Deserialise(object? ingestValue)
        {
            return true; // Implement deserialization logic if required
        }

        public override object? GetRawValue()
        {
            return DefaultValue;
        }
    }
    */
    public class CustomModuleSetting : BoolModuleSetting
    {
        public CustomModuleSetting(string title, string description, Type controlType, bool defaultValue = false)
            : base(title, description, controlType, defaultValue)
        {
        }
    }
}
