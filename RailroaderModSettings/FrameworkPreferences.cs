using UnityEngine.InputSystem;

namespace RailroaderModSettings
{
    /// <summary>Settings for the framework itself.</summary>
    public sealed class FrameworkPreferences
    {
        [ModKeybind("Open Mod Settings", Section = "Keybindings",
            Tooltip = "Opens or closes the in-game Mod Settings window.")]
        public ModKeyBinding ToggleWindow { get; set; } =
            new ModKeyBinding(Key.F10, ctrl: true);
    }
}
