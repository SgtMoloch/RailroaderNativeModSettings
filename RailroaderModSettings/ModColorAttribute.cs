using System;

namespace RailroaderModSettings
{
    // Color values are hex strings so JSON persistence works like any other string setting.
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ModColorAttribute : ModSettingAttribute
    {
        public ModColorAttribute(string label) : base(label)
        {
        }
    }
}
