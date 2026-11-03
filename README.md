# Railroader Mod Settings — single-project framework

**One C# project. One framework DLL.**

This is the reusable settings framework with a Harmony postfix for Railroader 0.6.0's Manage Mods rows. Registered mods get a **Settings** button below their row, expanding their annotated settings directly in the menu. Changes save immediately; the game's mod enable/Apply/Revert controls remain separate.

## Files

- `RailroaderModSettings/RailroaderModSettings.csproj` — the only C# project
- `RailroaderModSettings/Loader.cs` — `IModEntryPoint` native registration, Harmony lifecycle
- `RailroaderModSettings/ManageModsSettingsPatch.cs` — Harmony postfix and automatic settings controls in Manage Mods
- `RailroaderModSettings/ModSettingAttribute.cs` — annotations for settings fields/properties
- `RailroaderModSettings/SettingsRegistry.cs` — process-wide registry for other mods
- `RailroaderModSettings/SettingsRegistration.cs` — automatic JSON load/save, defaults, change callbacks, type/range checks
- `Paths.user` / `Paths.user.example` — game and ModDev paths
- `Info.json` — native DLL manifest template
- `tools/Package-Mod.ps1` — Debug ModDev deployment and Release ZIP

## Build

1. Open `RailroaderModSettings/RailroaderModSettings.csproj` in Visual Studio 2022. **There is only one project.**
2. Update `Paths.user` to point at your installation of Railroader, especially if not installed under `C:\Program Files (x86)\Steam`.
3. Restore NuGet packages; requires `Lib.Harmony` **2.4.3**.
4. Build **Debug**. The MSBuild target generates `Info.json`, stages the output under `artifacts/Debug/RailroaderModSettings/`, and copies it to `RailroaderModDevDir`.
5. Build **Release**. The result is `artifacts/Release/RailroaderModSettings_0.1.0.zip` containing a mod folder with `Info.json`, `RailroaderModSettings.dll` and `0Harmony.dll`.

The manifest uses `rr/game >=0.6.0` (no whitespace after the comparison operator).

## Using it from another native mod (later)

Your mod must depend on the framework in its `Info.json`:

```json
"dependencies": {
  "required": ["rr/game >=0.6.0", "sgtmoloch/mod-settings >=0.2.0"]
}
```

Your mod adds an assembly reference to `RailroaderModSettings.dll` with `Private=false` (do not copy this framework DLL into every mod folder).

Then register your own settings in **your** `IModEntryPoint.OnModLoad`:

```csharp
using RailroaderModSettings;

public sealed class MySettings
{
    [ModSetting("Verbose Logging", Section = "Logging")]
    public bool VerboseLogging = false;

    [ModSetting("Passenger Limit", Section = "Gameplay", Min = 1, Max = 100)]
    public int PassengerLimit = 25;
}

// Fields on your mod's Loader:
private SettingsRegistration _settingsRegistration;
private MySettings _settings;

// In OnModLoad(IModContext context):
_settingsRegistration = SettingsRegistry.Register<MySettings>(
    context.ModIdentifier,
    "My Mod",
    onChanged: values => context.Log.Info("Settings changed"),
    onError: message => context.Log.Error(message));
_settings = _settingsRegistration.GetValues<MySettings>();

// In OnModUnload():
SettingsRegistry.Unregister(_settingsRegistration.ModIdentifier);
```

Settings save to `Documents/Railroader/ModSettings/` as JSON. `SettingsRegistration.TrySet(name, value, out error)` validates and saves; `ResetToDefaults()` restores defaults; `Save()` saves changes made directly to the settings instance.

**Supported types:** bool, string, int, float, double, enums. Settings classes require a public parameterless constructor and writable annotated public fields or properties. Settings are local per user; multiplayer synchronization is the consumer mod's responsibility.

## Manage Mods UI (Railroader 0.6.x)

The patch targets the game's private static `UI.ModSelection.ModSelectionContentBuilder.RenderRow(UIPanelBuilder, ModSelectionController, Action, ModRow)` method. Its postfix adds a Settings/Hide Settings button to registered mod rows and an inline editor (checkboxes, enum dropdowns, text/numeric inputs and bounded numeric sliders). Settings are saved by `SettingsRegistration.TrySet()` and can be reset to defaults. It does **not** patch the game's Apply/Revert logic or add a separate window.

Only mods that have already registered settings at runtime have a Settings button. This uses the 0.6.0 decompiled game code; if the method signature changes in a future game release, the Harmony hook will need updating.


## Keybindings (new Input System)

`[ModKeybind]` creates a press-to-bind button in Manage Mods. The value type
`ModKeyBinding` uses `UnityEngine.InputSystem.Key`.
It stores a key and modifier bits (Ctrl=1, Shift=2, Alt=4).

```csharp
using RailroaderModSettings;
using UnityEngine.InputSystem;

public sealed class MySettings
{
    [ModKeybind("Toggle Overlay", Section = "Keybinds", Order = 10)]
    public ModKeyBinding ToggleOverlay { get; set; } =
        new ModKeyBinding(Key.F7, ctrl: true);
}

// In your mod's update loop:
if (Loader.Settings.ToggleOverlay.Down())
{
    ToggleOverlay();
}
```

Click a binding, press the desired key with any Ctrl/Shift/Alt modifiers,
Escape to cancel, or Backspace/Delete to clear. Changes persist immediately.
Reset to Defaults also resets keybindings. Only the binding is managed here:
mods still decide how and when to respond to their actions. Unlike UMM's
legacy input polling, `Down()/Pressed()/Up()` use `Keyboard.current`, so these
bindings are keyboard-only and require Railroader's newer Unity Input System.
Changing/clearing a binding retains the `ModKeyBinding` instance where possible,
so code caching the object still sees the new key.

## Settings coverage

- `[ModKeybind]` + `ModKeyBinding` accepts single modifier keys (LeftCtrl,
  LeftAlt, LeftShift), ordinary keys, Ctrl/Shift/Alt combinations, and `Key.None`.
  Press and release a modifier alone to bind it; press a nonmodifier key while
  modifiers are held to bind a chord. `Pressed()`, `Down()`, `Up()` use the new
  Input System. Keybinding tooltips use the same `Tooltip` property as other settings.
- `[ModSetting]` handles booleans, enums (native dropdowns instead of UMM toggle
  groups), strings, integers, floats, doubles, min/max validation, and tooltips
  including multiline descriptions.
- `Precision = 0` formats whole-number sliders, and `Precision = 2` displays
  two decimal places. Unless `Step` is specified explicitly, a bounded slider
  uses `10^-Precision` (or `1` if Precision is not set). Unbounded settings
  remain input fields with min/max validation where supplied.
- `[ModSettingsGroup("Advanced settings", Section = "Custom Defaults",
  Collapsible = true, Warning = "...")]` declares a nested settings object.
  Mark the nested public fields/properties with `[ModSetting]`; they appear
  in a collapsible group, persist within the parent JSON, and are included
  when resetting defaults. `TrySet` accepts dotted paths such as
  `AdvancedSettings.ShowPostCouplingCutByDefault`.

This is **not** a drop-in UMM compatibility layer. Existing mods need to replace
`UnityModManager.ModSettings`, UMM's `KeyBinding`, and `[Draw]` annotations with
our native API.