using System;

namespace RailroaderModSettings
{
    /// <summary>Groups nested settings in the UI and parent JSON file.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ModSettingsGroupAttribute : Attribute
    {
        public ModSettingsGroupAttribute(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("A group label is required.", nameof(label));
            }

            Label = label;
        }

        public string Label { get; }
        public string Section { get; set; } = "";
        public int Order { get; set; }
        // Only nested groups use this; top-level sections have their own state.
        public bool Collapsible { get; set; } = true;
        public string Warning { get; set; } = "";
    }

    public sealed class ModSettingsGroupInfo
    {
        internal ModSettingsGroupInfo(string path, ModSettingsGroupAttribute metadata, string section)
        {
            Path = path;
            Metadata = metadata;
            Section = section;
        }

        public string Path { get; }
        public string Label => Metadata.Label;
        public string Section { get; }
        public bool Collapsible => Metadata.Collapsible;
        public string Warning => Metadata.Warning;
        internal ModSettingsGroupAttribute Metadata { get; }
    }
}
