using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game;

namespace RailroaderModSettings
{
    /// <summary>Shared settings registry for native mods.</summary>
    public static class SettingsRegistry
    {
        private static readonly Dictionary<string, SettingsRegistration> Entries =
            new Dictionary<string, SettingsRegistration>(StringComparer.OrdinalIgnoreCase);
        private static readonly object SyncRoot = new object();

        // A detached, read-only list. A Hub adapter can enumerate registrations safely.
        public static IReadOnlyList<SettingsRegistration> Registrations => GetAll();

        // Mod list changes only; editing individual settings does not fire this event.
        public static event Action? RegistrationsChanged;

        public static SettingsRegistration Register<T>(
            string modIdentifier,
            string displayName,
            Action<T>? onChanged = null,
            Action<string>? onError = null,
            string? settingsRoot = null)
            where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(modIdentifier) || string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A mod identifier and display name are required.");
            }

            string root = settingsRoot ?? Path.Combine(UserPaths.RailroaderDirectory, "ModSettings");

            Action<object>? changedCallback = null;
            if (onChanged != null)
            {
                Action<T> callback = onChanged;
                changedCallback = settings => callback((T)settings);
            }

            var registration = new SettingsRegistration(
                modIdentifier, displayName, typeof(T),
                changedCallback, onError ?? Loader.LogError, root);

            lock (SyncRoot)
            {
                // A mod can register again after being disabled and re-enabled.
                Entries[modIdentifier] = registration;
            }

            NotifyRegistrationsChanged();
            return registration;
        }

        public static void Unregister(string modIdentifier)
        {
            if (modIdentifier == null)
            {
                return;
            }
            bool removed;
            lock (SyncRoot)
            {
                removed = Entries.Remove(modIdentifier);
            }
            if (removed) NotifyRegistrationsChanged();
        }

        /// <summary>Clears registrations but leaves saved JSON alone.</summary>
        public static void Clear()
        {
            bool hadEntries;
            lock (SyncRoot)
            {
                hadEntries = Entries.Count > 0;
                Entries.Clear();
            }
            if (hadEntries) NotifyRegistrationsChanged();
        }

        public static SettingsRegistration? Get(string modIdentifier)
        {
            lock (SyncRoot)
            {
                return Entries.TryGetValue(modIdentifier, out SettingsRegistration? result) ? result : null;
            }
        }

        public static IReadOnlyList<SettingsRegistration> GetAll()
        {
            lock (SyncRoot)
            {
                return Array.AsReadOnly(Entries.Values
                    .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            }
        }

        private static void NotifyRegistrationsChanged()
        {
            Action? listeners = RegistrationsChanged;
            if (listeners == null) return;

            // An integration callback must not break another mod's load/unload.
            foreach (Action listener in listeners.GetInvocationList())
            {
                try
                {
                    listener();
                }
                catch (Exception ex)
                {
                    Loader.LogError("RegistrationsChanged handler failed: " + ex);
                }
            }
        }
    }
}
