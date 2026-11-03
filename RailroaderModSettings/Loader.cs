using System;
using System.Reflection;
using HarmonyLib;
using Railroader.Modding;

namespace RailroaderModSettings
{
    /// <summary>Native mod entry point.</summary>
    public sealed class Loader : IModEntryPoint
    {
        private IModContext? _context;
        private Harmony? _harmony;

        public void OnModLoad(IModContext context)
        {
            _context = context;

            try
            {
                _harmony = new Harmony(context.ModIdentifier);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                InGameSettingsWindow.Start(message => context.Log.Warning(message));
                context.Log.Info("Railroader Mod Settings registry initialized.");
            }
            catch (Exception ex)
            {
                context.Log.Error("Railroader Mod Settings startup error: " + ex);
                _harmony?.UnpatchAll();
                _harmony = null;
            }
        }

        public void OnModUnload()
        {
            InGameSettingsWindow.Stop();
            _harmony?.UnpatchAll();
            _harmony = null;
            ManageModsSettingsPatch.ResetViewState();
            SettingsRegistry.Clear();
            _context?.Log.Info("Railroader Mod Settings unloaded.");
            _context = null;
        }
    }
}
