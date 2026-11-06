using System;
using System.Collections.Generic;
using System.Linq;
using UI.Builder;
using UI.Windows;
using UnityEngine;
using UnityEngine.UI;

namespace RailroaderModSettings
{
    /// <summary>In-game settings window and hotkey.</summary>
    internal static class InGameSettingsWindow
    {
        private const string WindowId = "sgtmoloch.mod-settings.window";
        private const string FrameworkModId = "sgtmoloch/mod-settings";
        private static FrameworkPreferences? _preferences;

        internal static ModKeyBinding? ToggleKeybinding => _preferences?.ToggleWindow;
        private static readonly UIState<string> Selected = new UIState<string>(null);
        private static GameObject? _hotkeyHost;
        // Use pixels from the top. Normalized scroll positions shift as sections grow.
        private static float? _pendingScrollOffset;
        private static string? _displayedMod;

        internal static bool IsOpen
        {
            get
            {
                WindowManager? windows = WindowManager.Shared;
                return windows != null && windows.IsWindowOpen(WindowId);
            }
        }

        internal static void Start()
        {
            Stop();
            _preferences = SettingsRegistry.Register<FrameworkPreferences>(
                FrameworkModId, "Railroader Mod Settings", onError: Loader.LogError)
                .GetValues<FrameworkPreferences>();
            _hotkeyHost = new GameObject("RailroaderModSettings.Hotkey");
            UnityEngine.Object.DontDestroyOnLoad(_hotkeyHost);
            _hotkeyHost.AddComponent<InGameSettingsHotkey>();
        }

        internal static void Stop()
        {
            try
            {
                WindowManager? windows = WindowManager.Shared;
                if (windows != null && windows.IsWindowOpen(WindowId))
                {
                    windows.CloseWindow(WindowId);
                }
            }
            catch (Exception ex)
            {
                Loader.LogError("Failed to close Mod settings window: " + ex);
            }

            ModKeybindCapture.Cancel(rebuild: false);
            if (_hotkeyHost != null)
            {
                UnityEngine.Object.Destroy(_hotkeyHost);
                _hotkeyHost = null;
            }
            _pendingScrollOffset = null;
            _displayedMod = null;
            Selected.Value = null;
            _preferences = null;
            SettingsRegistry.Unregister(FrameworkModId);
        }

        internal static void Toggle()
        {
            try
            {
                WindowManager? windows = WindowManager.Shared;
                if (windows == null) // Only available after entering a world.
                {
                    return;
                }

                if (windows.IsWindowOpen(WindowId))
                {
                    ModKeybindCapture.Cancel(rebuild: false);
                    windows.CloseWindow(WindowId);
                    return;
                }

                if (!windows.HasDefinition(WindowId))
                {
                    windows.RegisterCustomWindow(new CustomWindowDefinition
                    {
                        Identifier = WindowId,
                        Title = "Mod Settings",
                        Config = WindowConfig.Resizable(
                            new Vector2Int(850, 600),  // Default
                            new Vector2Int(750, 400),  // Minimum
                            new Vector2Int(1000, 900)), // Maximum
                        BuilderAction = BuildWindow
                    });
                }
                windows.ShowWindow(WindowId);
            }
            catch (Exception ex)
            {
                Loader.LogError("Failed to open Mod settings window: " + ex);
            }
        }

        private static void Refresh()
        {
            try
            {
                WindowManager? windows = WindowManager.Shared;
                if (windows == null || !windows.IsWindowOpen(WindowId)) return;
                windows.GetCustomWindow(WindowId)?.Rebuild();
            }
            catch (Exception ex)
            {
                Loader.LogError("Failed to rebuild Mod settings window: " + ex);
            }
        }

        private static void RefreshPreservingScroll(UIPanelBuilder builder)
        {
            RectTransform? container = ManageModsSettingsPatch.GetBuilderContainer(builder);
            ScrollRect? scroll = container != null ? container.GetComponentInParent<ScrollRect>() : null;
            if (scroll != null && scroll.content != null)
            {
                Canvas.ForceUpdateCanvases();
                float viewportHeight = scroll.viewport != null
                    ? scroll.viewport.rect.height
                    : ((RectTransform)scroll.transform).rect.height;
                float scrollRange = Mathf.Max(0f, scroll.content.rect.height - viewportHeight);
                _pendingScrollOffset = (1f - Mathf.Clamp01(scroll.verticalNormalizedPosition)) * scrollRange;
            }
            else
            {
                _pendingScrollOffset = null;
            }
            Refresh();
        }

        private static void BuildWindow(UIPanelBuilder builder)
        {
            List<SettingsRegistration> registrations = SettingsRegistry.Registrations
                .Where(registration => registration.Fields.Count > 0)
                .ToList();
            if (registrations.Count == 0)
            {
                builder.AddLabel("No mod settings are available for this session.");
                return;
            }

            List<UIPanelBuilder.ListItem<SettingsRegistration>> items = registrations
                .Select(registration => new UIPanelBuilder.ListItem<SettingsRegistration>(
                    registration.ModIdentifier, registration, "Mods", registration.DisplayName))
                .ToList();
            if (Selected.Value == null ||
                !registrations.Any(registration =>
                    string.Equals(registration.ModIdentifier, Selected.Value, StringComparison.OrdinalIgnoreCase)))
            {
                Selected.Value = registrations[0].ModIdentifier;
            }

            builder.AddListDetail<SettingsRegistration>(items, Selected, BuildDetail, 205f);
        }

        private static void BuildDetail(UIPanelBuilder builder, SettingsRegistration registration)
        {
            if (registration == null) return;
            if (!string.Equals(_displayedMod, registration.ModIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                // Don't carry the previous mod's scroll position into the new page.
                _pendingScrollOffset = null;
                _displayedMod = registration.ModIdentifier;
                ModKeybindCapture.Cancel(rebuild: false);
            }

            builder.VScrollView(page =>
            {
                ManageModsSettingsPatch.RenderSettingsInGame(page, registration,
                    () => RefreshPreservingScroll(page));

                RectTransform? container = ManageModsSettingsPatch.GetBuilderContainer(page);
                ScrollRect? scroll = container != null ? container.GetComponentInParent<ScrollRect>() : null;
                if (scroll != null)
                {
                    ModSettingsWindowScrollRestore restore =
                        scroll.GetComponent<ModSettingsWindowScrollRestore>();
                    if (restore == null)
                    {
                        restore = scroll.gameObject.AddComponent<ModSettingsWindowScrollRestore>();
                    }
                    // New scroll views can initialize at the bottom before layout.
                    restore.Request(_pendingScrollOffset ?? 0f);
                    _pendingScrollOffset = null;
                }
            });
        }
    }

    // Both menus call the same settings renderer.
    public static partial class ManageModsSettingsPatch
    {
        internal static RectTransform? GetBuilderContainer(UIPanelBuilder builder)
        {
            return BuilderContainerField?.GetValue(builder) as RectTransform;
        }

        // Hosts and clients share the same renderer; host-only filtering happens there.
        public static void RenderSettingsInGame(
            UIPanelBuilder builder, SettingsRegistration registration, Action rebuild)
        {
            if (registration == null) throw new ArgumentNullException(nameof(registration));
            if (rebuild == null) throw new ArgumentNullException(nameof(rebuild));
            RenderSettings(builder, registration, rebuild, inGame: true);
        }
    }

    public sealed class ModSettingsWindowScrollRestore : MonoBehaviour
    {
        private ScrollRect? _scroll;
        private float _offsetFromTop;
        private int _framesRemaining;

        public void Request(float offsetFromTop)
        {
            _scroll = GetComponent<ScrollRect>();
            _offsetFromTop = Mathf.Max(0f, offsetFromTop);
            // Layout can take a few frames after a section opens.
            _framesRemaining = 3;
        }

        private void LateUpdate()
        {
            if (_framesRemaining <= 0 || _scroll == null || _scroll.content == null) return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);
            Canvas.ForceUpdateCanvases();

            float viewportHeight = _scroll.viewport != null
                ? _scroll.viewport.rect.height
                : ((RectTransform)_scroll.transform).rect.height;
            float scrollRange = Mathf.Max(0f, _scroll.content.rect.height - viewportHeight);
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = scrollRange > 0f
                ? 1f - Mathf.Clamp01(_offsetFromTop / scrollRange)
                : 1f;

            if (--_framesRemaining == 0)
            {
                Destroy(this);
            }
        }
    }

    /// <summary>Checks the saved shortcut each frame.</summary>
    public sealed class InGameSettingsHotkey : MonoBehaviour
    {
        private bool _waitForRelease;

        private void Update()
        {
            ModKeyBinding? hotkey = InGameSettingsWindow.ToggleKeybinding;
            if (hotkey == null) return;

            // Don't immediately close the window with the key we just bound.
            if (ModKeybindCapture.IsAnyCapturing ||
                ModKeybindCapture.LastCaptureFrame == Time.frameCount)
            {
                _waitForRelease = true;
                return;
            }

            if (_waitForRelease)
            {
                if (!hotkey.Pressed()) _waitForRelease = false;
                return;
            }

            if (hotkey.Down())
            {
                InGameSettingsWindow.Toggle();
                _waitForRelease = true;
            }
        }
    }
}
