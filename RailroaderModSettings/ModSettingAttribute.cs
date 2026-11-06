using System;

namespace RailroaderModSettings
{
    /// <summary>Exposes a field or property in the settings UI.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ModSettingAttribute : Attribute
    {
        public ModSettingAttribute(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("A setting label is required.", nameof(label));
            }

            Label = label;
        }

        public string Label { get; }
        public string Section { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public int Order { get; set; }
        // Hide from clients in the in-game window. Manage Mods is unaffected.
        public bool HostOnly { get; set; } = false;
        public double Min { get; set; } = double.NaN;
        public double Max { get; set; } = double.NaN;
        // -1 = automatic. Otherwise this sets decimals and default slider step.
        public int Precision { get; set; } = -1;
        public double Step { get; set; } = double.NaN;
    }
}
