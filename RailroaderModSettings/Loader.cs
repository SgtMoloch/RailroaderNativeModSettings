using System;
using System.Reflection;
using HarmonyLib;
using Railroader.Modding;

namespace RailroaderModSettings
{
    /// <summary>Native mod entry point.</summary>
    public sealed class Loader : IModEntryPoint
    {
        private static IModContext? _context;
        private Harmony? _harmony;

        internal static void LogError(string message)
        {
            _context?.Log.Error(message);
        }

        internal static void LogInfo(string message)
        {
            _context?.Log.Info(message);
        }

        public void OnModLoad(IModContext context)
        {
            _context = context;

            try
            {
                _harmony = new Harmony(context.ModIdentifier);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                InGameSettingsWindow.Start();
                LogInfo("Settings registry initialized.");
            }
            catch (Exception ex)
            {
                LogError("Startup failed: " + ex);
                _harmony?.UnpatchAll();
                _harmony = null;
                _context = null;
            }
        }

        public void OnModUnload()
        {
            InGameSettingsWindow.Stop();
            _harmony?.UnpatchAll();
            _harmony = null;
            ManageModsSettingsPatch.ResetViewState();
            SettingsRegistry.Clear();
            LogInfo("Unloaded.");
            _context = null;
        }
    }
}
