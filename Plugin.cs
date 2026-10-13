using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace UpdatedCamera
{
    // Mod identity. BepInEx uses the GUID to tell mods apart, so never change it after release.
    public static class PluginInfo
    {
        public const string Guid = "laserbees.solarexpanse.updatedcamera";
        public const string Name = "Updated Camera";
        public const string Version = "1.0.0";
    }

    // BaseUnityPlugin is a Unity MonoBehaviour: BepInEx creates it once when the game starts,
    // and Unity calls methods like Awake/Update on it.
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        // Static so any class in the mod can write to the BepInEx log via Plugin.Log.
        internal static ManualLogSource Log;

        private Harmony _harmony;
        private readonly TuningPanel _tuningPanel = new TuningPanel();

        // Called once when the mod is loaded.
        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);

            // Applies every [HarmonyPatch] class in this mod, one at a time, so a patch that no
            // longer matches the game (e.g. after a game update) is logged instead of stopping the rest.
            _harmony = new Harmony(PluginInfo.Guid);
            int applied = 0;
            foreach (Type type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    applied++;
                }
                catch (Exception e)
                {
                    Log.LogError("Patch " + type.Name + " failed: " + e);
                }
            }
            Log.LogInfo("Loaded, applied " + applied + " patches.");
        }

        private void Update()
        {
            _tuningPanel.Update();
        }

        // After the game has moved the camera for this frame (MyCameraController.Update).
        private void LateUpdate()
        {
            Guard(ViewOffset.LateUpdate, "view offset");
            Guard(HudTweaks.LateUpdate, "HUD tweaks");
        }

        private void OnGUI()
        {
            _tuningPanel.OnGUI();
        }

        private void OnDestroy()
        {
            Settings.Save();
            _harmony?.UnpatchSelf();
        }

        // Runs a per-frame feature; if it throws, logs once and turns it off rather than
        // spamming the log every frame.
        private static readonly System.Collections.Generic.HashSet<string> Failed = new System.Collections.Generic.HashSet<string>();

        private static void Guard(Action action, string name)
        {
            if (Failed.Contains(name))
            {
                return;
            }
            try
            {
                action();
            }
            catch (Exception e)
            {
                Failed.Add(name);
                Log.LogError(name + " failed and was turned off: " + e);
            }
        }
    }

    // Everything you can tune. Saved in BepInEx\config\laserbees.solarexpanse.updatedcamera.cfg.
    internal static class Settings
    {
        private static ConfigFile _config;

        public static ConfigEntry<KeyboardShortcut> TuningPanelKey;
        public static ConfigEntry<float> DoubleClickSeconds;

        public static ConfigEntry<float> SizePlanets;
        public static ConfigEntry<float> SizeMoons;
        public static ConfigEntry<float> SizeSmallBodies;
        public static ConfigEntry<float> ZoomSpacecraft;

        public static ConfigEntry<bool> OffsetEnabled;
        public static ConfigEntry<bool> OffsetWhenNotFollowing;
        public static ConfigEntry<float> OffsetStrength;
        public static ConfigEntry<float> OffsetGapPixels;

        public static ConfigEntry<bool> HideLogo;
        public static ConfigEntry<int> LogoLevel;

        public static ConfigEntry<int> ContractsLevel;
        public static ConfigEntry<float> ContractsX;
        public static ConfigEntry<float> ContractsY;
        public static ConfigEntry<float> ContractsScale;

        public static void Bind(ConfigFile config)
        {
            _config = config;
            // Sliders change values every frame while dragged; save on a timer instead of each change.
            config.SaveOnConfigSet = false;

            TuningPanelKey = config.Bind("General", "TuningPanelKey", new KeyboardShortcut(KeyCode.F9), "Opens and closes the settings panel. (Not F8: that's the game's bug-report key.)");
            if (TuningPanelKey.Value.MainKey == KeyCode.F8)
            {
                TuningPanelKey.Value = new KeyboardShortcut(KeyCode.F9);  // 0.2.0 defaulted to F8
            }
            DoubleClickSeconds = config.Bind("General", "DoubleClickSeconds", 0.4f, "Two clicks on the same object within this many seconds count as a double click (centers the camera).");

            SizePlanets = config.Bind("Zoom", "PlanetsScreenHeight", 10f, "When centering on a planet, dwarf planet or the Sun, zoom so it fills this % of the screen height (0 = game default distance).");
            SizeMoons = config.Bind("Zoom", "MoonsScreenHeight", 20f, "When centering on a moon, zoom so it fills this % of the screen height (0 = game default distance).");
            SizeSmallBodies = config.Bind("Zoom", "AsteroidsAndCometsScreenHeight", 20f, "When centering on an asteroid or comet, zoom so it fills this % of the screen height (0 = game default distance).");
            ZoomSpacecraft = config.Bind("Zoom", "SpacecraftAndStations", 1f, "Distance multiplier when centering on spacecraft, stations and orbits.");

            OffsetEnabled = config.Bind("Offset", "Enabled", true, "Shift the view so the centered object stays visible beside panels on the right.");
            OffsetWhenNotFollowing = config.Bind("Offset", "WhenNotFollowing", true, "Also shift the view when the camera isn't following anything.");
            OffsetStrength = config.Bind("Offset", "Strength", 0.83f, "How much of the panel width to shift by (1 = center of the uncovered area).");
            OffsetGapPixels = config.Bind("Offset", "StackGap", 20f, "Windows stacked side by side count as one panel if the gap between them is at most this many pixels (at 1080p).");

            HideLogo = config.Bind("HUD", "HideCompanyLogo", true, "Hide the company logo in the top-left corner.");
            LogoLevel = config.Bind("HUD", "CompanyLogoLevel", 1, "How much to hide: 0 = the logo image only, 1 = its parent (logo and frame), 2 = grandparent...");

            ContractsLevel = config.Bind("HUD", "ContractsBoxLevel", 0, "Which object to move: 0 = the contract list, 1 = its parent, 2 = grandparent...");
            ContractsX = config.Bind("HUD", "ContractsBoxX", 0f, "Horizontal move of the contracts box, in screen units at 1080p.");
            ContractsY = config.Bind("HUD", "ContractsBoxY", 232f, "Vertical move of the contracts box (positive = up), in screen units at 1080p.");
            ContractsScale = config.Bind("HUD", "ContractsBoxScale", 0.85f, "Size of the contracts box.");
        }

        public static void Save() => _config?.Save();
    }
}
