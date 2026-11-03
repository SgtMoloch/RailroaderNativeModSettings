using System;

namespace RailroaderModSettings
{
    /// <summary>Draws a press-to-bind control for a ModKeyBinding setting.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ModKeybindAttribute : ModSettingAttribute
    {
        public ModKeybindAttribute(string label) : base(label)
        {
            Section = "Keybinds";
        }
    }
}
