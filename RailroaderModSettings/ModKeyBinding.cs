using System;
using System.Runtime.Serialization;
using UnityEngine.InputSystem;

namespace RailroaderModSettings
{
    /// <summary>Keyboard binding with optional Ctrl, Shift, and Alt modifiers.</summary>
    [DataContract]
    public sealed class ModKeyBinding : IEquatable<ModKeyBinding>
    {
        [DataMember(Order = 1)]
        public Key keyCode = Key.None;

        // Ctrl = 1, Shift = 2, Alt = 4.
        [DataMember(Order = 2)]
        public byte modifiers;

        public ModKeyBinding()
        {
        }

        public ModKeyBinding(Key keyCode, bool ctrl = false, bool shift = false, bool alt = false)
        {
            Change(keyCode, ctrl, shift, alt);
        }

        public void Change(Key key, bool ctrl, bool shift, bool alt)
        {
            Change(key, (byte)((ctrl ? 1 : 0) | (shift ? 2 : 0) | (alt ? 4 : 0)));
        }

        public void Change(Key key, byte modifier = 0)
        {
            if ((modifier & ~7) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(modifier), "Only Ctrl, Shift, and Alt are supported.");
            }

            if (!Enum.IsDefined(typeof(Key), key))
            {
                throw new ArgumentOutOfRangeException(nameof(key));
            }

            // A standalone Ctrl binding shouldn't become Ctrl+Ctrl.
            if (key == Key.LeftCtrl || key == Key.RightCtrl) modifier &= 6;
            if (key == Key.LeftShift || key == Key.RightShift) modifier &= 5;
            if (key == Key.LeftAlt || key == Key.RightAlt) modifier &= 3;
            keyCode = key;
            modifiers = key == Key.None ? (byte)0 : modifier;
        }

        public ModKeyBinding Clone()
        {
            return new ModKeyBinding { keyCode = keyCode, modifiers = modifiers };
        }

        private bool ModifiersMatch(Keyboard keyboard)
        {
            // WaypointQueue uses Ctrl/Alt/Shift alone as held shortcuts.
            // Don't check the key's modifier state against itself.
            bool isCtrl = keyCode == Key.LeftCtrl || keyCode == Key.RightCtrl;
            bool isShift = keyCode == Key.LeftShift || keyCode == Key.RightShift;
            bool isAlt = keyCode == Key.LeftAlt || keyCode == Key.RightAlt;
            return (isCtrl || keyboard.ctrlKey.isPressed == ((modifiers & 1) != 0)) &&
                   (isShift || keyboard.shiftKey.isPressed == ((modifiers & 2) != 0)) &&
                   (isAlt || keyboard.altKey.isPressed == ((modifiers & 4) != 0));
        }

        private bool Matches(out KeyControlState state)
        {
            state = default;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || keyCode == Key.None || !ModifiersMatch(keyboard))
            {
                return false;
            }

            // Unity's Key enum starts at 1; verify the index before using it.
            int index = (int)keyCode - 1;
            if (index < 0 || index >= keyboard.allKeys.Count)
            {
                return false;
            }

            var control = keyboard[keyCode];
            if (control == null)
            {
                return false;
            }

            state = new KeyControlState(control.isPressed, control.wasPressedThisFrame, control.wasReleasedThisFrame);
            return true;
        }

        private readonly struct KeyControlState
        {
            public readonly bool Pressed;
            public readonly bool Down;
            public readonly bool Up;

            public KeyControlState(bool pressed, bool down, bool up)
            {
                Pressed = pressed;
                Down = down;
                Up = up;
            }
        }

        public bool Pressed()
        {
            return Matches(out KeyControlState state) && state.Pressed;
        }

        public bool Down()
        {
            return Matches(out KeyControlState state) && state.Down;
        }

        public bool Up()
        {
            // The modifier combination must still match on release.
            return Matches(out KeyControlState state) && state.Up;
        }

        public bool Equals(ModKeyBinding? other)
        {
            return other != null && keyCode == other.keyCode && modifiers == other.modifiers;
        }

        public override bool Equals(object? obj)
        {
            return obj is ModKeyBinding other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)keyCode * 397) ^ modifiers;
        }

        public override string ToString()
        {
            if (keyCode == Key.None)
            {
                return "None";
            }

            string keyName = keyCode.ToString();
            if (keyName.StartsWith("Digit", StringComparison.Ordinal))
            {
                keyName = keyName.Substring(5);
            }
            else if (keyName.StartsWith("Numpad", StringComparison.Ordinal))
            {
                keyName = "Num " + keyName.Substring(6);
            }
            else if (keyCode == Key.Enter)
            {
                keyName = "Enter";
            }

            bool isCtrl = keyCode == Key.LeftCtrl || keyCode == Key.RightCtrl;
            bool isShift = keyCode == Key.LeftShift || keyCode == Key.RightShift;
            bool isAlt = keyCode == Key.LeftAlt || keyCode == Key.RightAlt;
            return ((modifiers & 1) != 0 && !isCtrl ? "Ctrl + " : "") +
                   ((modifiers & 2) != 0 && !isShift ? "Shift + " : "") +
                   ((modifiers & 4) != 0 && !isAlt ? "Alt + " : "") + keyName;
        }
    }
}
