using System;
using UI.Common;
using UI.ModSelection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RailroaderModSettings
{
    /// <summary>Handles one keybind capture from either settings menu.</summary>
    internal static class ModKeybindCapture
    {
        private static ModKeybindCaptureDriver? _driver;
        private static SettingsRegistration? _registration;
        private static SettingField? _field;
        private static Action? _rebuild;
        private static ManageModsMenu? _menu;
        private static bool _inGameWindow;
        private static Key _modifierCandidate = Key.None;
        private static byte _modifierFlags;

        internal static bool IsAnyCapturing => _registration != null;
        internal static int LastCaptureFrame { get; private set; } = -1;

        internal static bool IsCapturing(SettingsRegistration registration, SettingField field)
        {
            return ReferenceEquals(_registration, registration) && ReferenceEquals(_field, field);
        }

        internal static void Begin(SettingsRegistration registration, SettingField field, Action rebuild)
        {
            Cancel(rebuild: false);
            bool inGameWindow = InGameSettingsWindow.IsOpen;
            ManageModsMenu? menu = inGameWindow ? null :
                UnityEngine.Object.FindFirstObjectByType<ManageModsMenu>();
            if (!inGameWindow && (menu == null || !menu.isActiveAndEnabled))
            {
                return;
            }

            if (_driver == null)
            {
                GameObject host = new GameObject("RailroaderModSettings.KeybindCapture");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _driver = host.AddComponent<ModKeybindCaptureDriver>();
            }

            _menu = menu;
            _inGameWindow = inGameWindow;
            _modifierCandidate = Key.None;
            _modifierFlags = 0;
            _registration = registration;
            _field = field;
            _rebuild = rebuild;
            rebuild();
        }

        internal static void Cancel(bool rebuild = true)
        {
            Action? rebuildCallback = _rebuild;
            _registration = null;
            _field = null;
            _rebuild = null;
            _menu = null;
            _inGameWindow = false;
            _modifierCandidate = Key.None;
            _modifierFlags = 0;
            if (rebuild)
            {
                rebuildCallback?.Invoke();
            }
        }

        internal static void Shutdown()
        {
            // Don't call a stale UI callback during mod unload.
            LastCaptureFrame = -1;
            _registration = null;
            _field = null;
            _rebuild = null;
            _menu = null;
            _inGameWindow = false;
            _modifierCandidate = Key.None;
            _modifierFlags = 0;
            if (_driver != null)
            {
                UnityEngine.Object.Destroy(_driver.gameObject);
                _driver = null;
            }
        }

        internal static void Update()
        {
            SettingsRegistration? registration = _registration;
            SettingField? field = _field;
            if (registration == null || field == null)
            {
                return;
            }

            // Stop capture when its window closes or its mod unregisters.
            if ((!_inGameWindow && (_menu == null || !_menu.isActiveAndEnabled)) ||
                (_inGameWindow && !InGameSettingsWindow.IsOpen) ||
                !ReferenceEquals(SettingsRegistry.Get(registration.ModIdentifier), registration))
            {
                Cancel(rebuild: false);
                return;
            }

            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Cancel();
                return;
            }

            if (keyboard.backspaceKey.wasPressedThisFrame || keyboard.deleteKey.wasPressedThisFrame)
            {
                Finish(new ModKeyBinding());
                return;
            }

            // Check regular keys first so Ctrl+K wins over standalone Ctrl.
            foreach (KeyControl control in keyboard.allKeys)
            {
                if (!control.wasPressedThisFrame || IsModifier(control.keyCode))
                {
                    continue;
                }

                Finish(new ModKeyBinding(control.keyCode,
                    keyboard.ctrlKey.isPressed, keyboard.shiftKey.isPressed,
                    keyboard.altKey.isPressed));
                return;
            }

            foreach (KeyControl control in keyboard.allKeys)
            {
                if (!control.wasPressedThisFrame || !IsModifier(control.keyCode) ||
                    control.keyCode == Key.LeftMeta || control.keyCode == Key.RightMeta)
                {
                    continue;
                }

                _modifierCandidate = control.keyCode;
                _modifierFlags = GetModifierFlags(keyboard);
                // Don't store Ctrl+Ctrl (or equivalent) as the binding.
                _modifierFlags &= (byte)~ModifierBit(control.keyCode);
            }

            // Save modifier-only binds on release, after giving chords a chance.
            if (_modifierCandidate != Key.None &&
                keyboard[_modifierCandidate].wasReleasedThisFrame)
            {
                var binding = new ModKeyBinding();
                binding.Change(_modifierCandidate, _modifierFlags);
                Finish(binding);
            }
        }

        private static byte GetModifierFlags(Keyboard keyboard)
        {
            return (byte)((keyboard.ctrlKey.isPressed ? 1 : 0) |
                          (keyboard.shiftKey.isPressed ? 2 : 0) |
                          (keyboard.altKey.isPressed ? 4 : 0));
        }

        private static byte ModifierBit(Key key)
        {
            if (key == Key.LeftCtrl || key == Key.RightCtrl) return 1;
            if (key == Key.LeftShift || key == Key.RightShift) return 2;
            if (key == Key.LeftAlt || key == Key.RightAlt) return 4;
            return 0;
        }

        private static bool IsModifier(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftMeta || key == Key.RightMeta;
        }

        private static void Finish(ModKeyBinding key)
        {
            // The hotkey listener ignores a binding captured in this frame.
            LastCaptureFrame = Time.frameCount;
            SettingsRegistration registration = _registration!;
            SettingField field = _field!;
            Action? rebuild = _rebuild;
            _registration = null;
            _field = null;
            _rebuild = null;
            _menu = null;
            _inGameWindow = false;
            _modifierCandidate = Key.None;
            _modifierFlags = 0;

            if (!registration.TrySet(field.Name, key, out string? error))
            {
                ModalAlertController.PresentOkay("Mod Settings", error ?? "Unable to save the keybinding.");
            }
            rebuild?.Invoke();
        }
    }

    public sealed class ModKeybindCaptureDriver : MonoBehaviour
    {
        private void Update()
        {
            ModKeybindCapture.Update();
        }
    }
}
