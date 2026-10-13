using System;
using CameraControl;
using HarmonyLib;
using UnityEngine;

namespace UpdatedCamera
{
    // Settings panel (F9). Everything is saved to the config file, so it can also be edited
    // there with the game closed.
    internal class TuningPanel
    {
        private const float Width = 440f;

        private bool _open;
        private Rect _window = new Rect(40f, 120f, Width, 10f);
        private bool _dirty;
        private float _nextSave;

        public static bool MouseOver { get; private set; }

        public void Update()
        {
            if (Settings.TuningPanelKey.Value.IsDown())
            {
                _open = !_open;
                if (!_open)
                {
                    SaveNow();
                }
            }
            if (_dirty && Time.unscaledTime >= _nextSave)
            {
                SaveNow();
            }
        }

        public void OnGUI()
        {
            if (!_open)
            {
                MouseOver = false;
                return;
            }

            // Scale the panel with resolution so it's readable on large screens.
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            _window = GUILayout.Window(0x5EC0DE, _window, DrawWindow, "Updated Camera settings  (" + Settings.TuningPanelKey.Value + " to close)", GUILayout.Width(Width));

            Vector2 mouse = Event.current.mousePosition;  // already in scaled GUI space
            MouseOver = _window.Contains(mouse);
            GUI.matrix = Matrix4x4.identity;
        }

        private void DrawWindow(int id)
        {
            GUI.changed = false;

            Header("Size on screen when centering (% of screen height, 0 = game default)");
            ShareSlider("Planets & Sun", Settings.SizePlanets);
            ShareSlider("Moons", Settings.SizeMoons);
            ShareSlider("Asteroids & comets", Settings.SizeSmallBodies);
            ZoomSlider("Spacecraft & stations", Settings.ZoomSpacecraft);
            DrawCameraReadout();

            Header("View offset for panels on the right");
            Settings.OffsetEnabled.Value = GUILayout.Toggle(Settings.OffsetEnabled.Value, " Shift the view beside right-side panels");
            Settings.OffsetWhenNotFollowing.Value = GUILayout.Toggle(Settings.OffsetWhenNotFollowing.Value, " Also when the camera isn't following anything");
            Settings.OffsetStrength.Value = Slider("Strength", Settings.OffsetStrength.Value, 0f, 1.5f, "P0");
            Settings.OffsetGapPixels.Value = Slider("Stacked window gap", Settings.OffsetGapPixels.Value, 0f, 100f, "N0");

            Header("Top-left HUD");
            Settings.HideLogo.Value = GUILayout.Toggle(Settings.HideLogo.Value, " Hide company logo");
            DrawContracts();

            if (GUI.changed)
            {
                _dirty = true;
                _nextSave = Time.unscaledTime + 2f;
            }
            GUI.DragWindow();
        }

        private static void DrawCameraReadout()
        {
            if (MonoBehaviourSingleton<MyCameraController>.InstanceIsNull)
            {
                return;
            }
            MyCameraController camera = MonoBehaviourSingleton<MyCameraController>.Instance;
            Transform target = camera.Target;
            GUILayout.BeginHorizontal();
            string fills = "";
            float radius = ZoomScaler.Radius(target);
            if (radius > 0f && camera.MyCamera != null)
            {
                float distance = Vector3.Distance(camera.MyCamera.transform.position, target.position);
                fills = ", fills " + ZoomScaler.ShareAtDistance(distance, radius, camera.MyCamera.fieldOfView).ToString("P0");
            }
            GUILayout.Label(target != null
                ? "Following: " + target.name + " (" + ZoomScaler.CategoryOf(target) + ")" + fills
                : "Not following anything");
            GUI.enabled = target != null;
            if (GUILayout.Button("Re-center", GUILayout.Width(90f)))
            {
                camera.ChangeTarget(target);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private static void DrawContracts()
        {
            Transform list = HudTweaks.ContractsList;
            if (list == null)
            {
                GUILayout.Label("Contracts box not found yet (load a game).");
                return;
            }

            Settings.ContractsX.Value = Slider("Contracts box X", Settings.ContractsX.Value, -800f, 800f, "N0");
            Settings.ContractsY.Value = Slider("Contracts box Y", Settings.ContractsY.Value, -800f, 800f, "N0");
            Settings.ContractsScale.Value = Slider("Contracts box size", Settings.ContractsScale.Value, 0.4f, 1.6f, "N2");
            if (GUILayout.Button("Reset contracts box"))
            {
                Settings.ContractsX.Value = (float)Settings.ContractsX.DefaultValue;
                Settings.ContractsY.Value = (float)Settings.ContractsY.DefaultValue;
                Settings.ContractsScale.Value = (float)Settings.ContractsScale.DefaultValue;
            }
        }

        // Whole percent steps, with buttons for fine adjustment.
        private static void ShareSlider(string label, BepInEx.Configuration.ConfigEntry<float> entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            float value = Mathf.Round(GUILayout.HorizontalSlider(entry.Value, 0f, 60f));
            if (GUILayout.Button("-", GUILayout.Width(24f)))
            {
                value = Mathf.Max(0f, value - 1f);
            }
            if (GUILayout.Button("+", GUILayout.Width(24f)))
            {
                value = Mathf.Min(60f, value + 1f);
            }
            entry.Value = value;
            GUILayout.Label(value > 0f ? value.ToString("0") + "%" : "game", GUILayout.Width(40f));
            GUILayout.EndHorizontal();
        }

        // Log-scale slider, so 0.5× and 2× are equally far from 1×.
        private static void ZoomSlider(string label, BepInEx.Configuration.ConfigEntry<float> entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            float log = Mathf.Log(Mathf.Max(0.01f, entry.Value), 2f);
            log = GUILayout.HorizontalSlider(log, -2f, 3.33f);  // 0.25× .. 10×
            entry.Value = (float)Math.Round(Mathf.Pow(2f, log), 2);
            GUILayout.Label(entry.Value.ToString("0.00") + "×", GUILayout.Width(50f));
            if (GUILayout.Button("1×", GUILayout.Width(32f)))
            {
                entry.Value = 1f;
            }
            GUILayout.EndHorizontal();
        }

        private static float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString(format), GUILayout.Width(50f));
            GUILayout.EndHorizontal();
            return value;
        }

        private static void Header(string text)
        {
            GUILayout.Space(6f);
            GUILayout.Label("<b>" + text + "</b>");
        }

        private void SaveNow()
        {
            Settings.Save();
            _dirty = false;
        }
    }

    // Clicks on the tuning panel shouldn't also click planets behind it.
    [HarmonyPatch(typeof(Manager.InputManager), "Update")]
    internal static class InputBehindPanelPatch
    {
        private static bool Prefix() => !(TuningPanel.MouseOver && (Input.GetMouseButtonUp(0) || Input.GetMouseButton(0)));
    }
}
