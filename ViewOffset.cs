using System.Collections.Generic;
using System.Linq;
using CameraControl;
using Game.UI;
using Game.UI.Windows.Windows;
using HarmonyLib;
using UnityEngine;

namespace UpdatedCamera
{
    // Shifts the 3D view left while windows cover the right side of the screen, so the object
    // the camera centers on sits in the middle of the uncovered area.
    //
    // It skews the camera's projection (an "off-center" projection) instead of moving the
    // camera, so nothing in the scene changes. Unity derives mouse rays from the same
    // projection, so clicking on objects stays accurate.
    internal static class ViewOffset
    {
        // Short easing, only so windows that appear without animating don't jump the view.
        // Sliding and fading windows are already followed frame by frame.
        private const float SmoothSeconds = 0.1f;
        // Windows wider than this share of the screen are full-screen screens, not side panels.
        private const float MaxPanelShare = 0.8f;
        // The game parks windows 77px left of the screen edge, leaving room for its toolbar on
        // the right (RightPanel). The first window may start this far from the edge (at 1080p).
        private const float EdgeMargin = 120f;
        // When the covered width drops by more than this in one frame (at 1080p), a window was
        // removed outright rather than sliding or fading. The game often opens another one a frame
        // later (e.g. closing the spacecraft window reopens the object window), so hold the view
        // for HoldSeconds before following the drop.
        private const float SuddenDrop = 100f;
        private const float HoldSeconds = 0.25f;

        private static readonly AccessTools.FieldRef<UIManager, Dictionary<EWindowType, UIWindow>> Windows =
            AccessTools.FieldRefAccess<UIManager, Dictionary<EWindowType, UIWindow>>("windows");
        private static readonly AccessTools.FieldRef<UIManager, Dictionary<EWindowType, UIWindow>> SecondWindows =
            AccessTools.FieldRefAccess<UIManager, Dictionary<EWindowType, UIWindow>>("windowsSecond");
        private static readonly AccessTools.FieldRef<UIWindow, CanvasGroup> WindowCanvasGroup =
            AccessTools.FieldRefAccess<UIWindow, CanvasGroup>("canvasGroup");
        private static readonly AccessTools.FieldRef<UIWindow, bool> WindowSlides =
            AccessTools.FieldRefAccess<UIWindow, bool>("showLeftRight");

        private static readonly List<Camera> ShiftedCameras = new List<Camera>();
        private static readonly List<Panel> Panels = new List<Panel>();
        private static readonly Dictionary<UIWindow, Rect> LastOpenRect = new Dictionary<UIWindow, Rect>();
        private static readonly Vector3[] Corners = new Vector3[4];

        private static float _current;      // applied shift, as a fraction of screen width
        private static bool _applied;
        private static float _lastMeasured;
        private static float _held;
        private static float _holdUntil;

        // A visible window's horizontal extent on screen, in pixels.
        private struct Panel
        {
            public UIWindow Window;
            public float Left;
            public float Right;
            public bool Counted;
        }

        // For the tuning panel.
        public static float CoveredPixels { get; private set; }

        public static void LateUpdate()
        {
            if (MonoBehaviourSingleton<MyCameraController>.InstanceIsNull || SerializedMonoBehaviourSingleton<UIManager>.InstanceIsNull)
            {
                return;
            }
            MyCameraController controller = MonoBehaviourSingleton<MyCameraController>.Instance;
            Camera main = controller.MyCamera;
            if (main == null)
            {
                return;
            }

            CoveredPixels = HoldSuddenDrops(MeasureCovered(SerializedMonoBehaviourSingleton<UIManager>.Instance));

            float wanted = 0f;
            bool following = controller.Target != null;
            if (Settings.OffsetEnabled.Value && (following || Settings.OffsetWhenNotFollowing.Value))
            {
                wanted = CoveredPixels / Screen.width * Settings.OffsetStrength.Value;
            }
            // Ease toward the wanted shift; about 95% of the way there after SmoothSeconds.
            _current = Mathf.Lerp(_current, wanted, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime / SmoothSeconds));
            if (Mathf.Abs(_current - wanted) < 0.0005f)
            {
                _current = wanted;
            }

            CollectCameras(main);
            if (_current == 0f)
            {
                if (_applied)
                {
                    foreach (Camera cam in ShiftedCameras)
                    {
                        cam.ResetProjectionMatrix();
                    }
                    _applied = false;
                }
                return;
            }

            // In normalized device coordinates the screen spans -1..1. Putting the center at
            // the middle of the uncovered area means moving it left by the covered fraction.
            foreach (Camera cam in ShiftedCameras)
            {
                cam.ResetProjectionMatrix();
                Matrix4x4 m = cam.projectionMatrix;
                m[0, 2] += _current;
                cam.projectionMatrix = m;
            }
            _applied = true;
        }

        private static float HoldSuddenDrops(float measured)
        {
            float now = Time.unscaledTime;
            if (measured < _lastMeasured - SuddenDrop * Screen.height / 1080f)
            {
                _held = Mathf.Max(_lastMeasured, now < _holdUntil ? _held : 0f);
                _holdUntil = now + HoldSeconds;
            }
            _lastMeasured = measured;
            if (now < _holdUntil && measured < _held)
            {
                return _held;
            }
            _holdUntil = 0f;
            return measured;
        }

        // How many pixels from the right edge are covered by windows, read live every frame.
        // Starts at the right edge and takes every window that reaches the covered area (within
        // a small gap), so a window stacked to the left of another one counts too. Windows that
        // slide in move their own rect, so the result follows the animation exactly; windows that
        // fade out count by their opacity.
        private static float MeasureCovered(UIManager ui)
        {
            Panels.Clear();
            AddPanels(Windows(ui));
            AddPanels(SecondWindows(ui));

            float scale = Screen.height / 1080f;
            float edge = Screen.width;
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < Panels.Count; i++)
                {
                    Panel p = Panels[i];
                    float gap = (edge == Screen.width ? EdgeMargin : Settings.OffsetGapPixels.Value) * scale;
                    if (p.Counted || p.Right < edge - gap || p.Left >= edge)
                    {
                        continue;
                    }
                    p.Counted = true;
                    Panels[i] = p;
                    edge = Mathf.Min(edge, p.Left);
                    grew = true;
                }
            }
            return Mathf.Max(0f, Screen.width - edge);
        }

        private static void AddPanels(Dictionary<EWindowType, UIWindow> windows)
        {
            if (windows == null)
            {
                return;
            }
            foreach (UIWindow window in windows.Values)
            {
                // Active includes windows still sliding or fading closed.
                if (window == null || !window.gameObject.activeInHierarchy || Panels.Any(p => p.Window == window))
                {
                    continue;
                }
                Rect rect = ScreenRect(window.transform as RectTransform);
                if (window.Open && !window.ShowingOrHiding)
                {
                    LastOpenRect[window] = rect;
                }
                // The spacecraft window reopens the window it came from once it has finished
                // closing (plus a frame). Count it where it sat while open until it's gone; the
                // sudden-drop hold then bridges the gap until the other window appears.
                bool reopensAnother = !window.Open && window is SpaceCraftInfoWindow ship && ship.OpenedFrom != EWindowType.None;
                if (reopensAnother && LastOpenRect.TryGetValue(window, out Rect openRect))
                {
                    rect = openRect;
                }
                if (rect.width <= 0f || rect.width > Screen.width * MaxPanelShare)
                {
                    continue;
                }
                float width = rect.width;
                CanvasGroup group = WindowCanvasGroup(window);
                // Fading windows only count by opacity while closing. When the game switches windows
                // it hides the old one instantly and fades the new one in, so counting the fade-in
                // would make the view dip toward the center and come back.
                if (!WindowSlides(window) && !window.Open && !reopensAnother && group != null)
                {
                    width *= Mathf.Clamp01(group.alpha);
                }
                if (width <= 0f)
                {
                    continue;
                }
                float right = Mathf.Min(rect.xMax, Screen.width);
                Panels.Add(new Panel { Window = window, Left = right - width, Right = right });
            }
        }

        // Re-applies the shift after the post-processing stack resets the projection (it does so
        // before and after drawing when anti-aliasing is on). Adds on top of whatever it set, so
        // its anti-aliasing jitter is kept.
        public static void Reapply(Camera cam)
        {
            if (_current == 0f || cam == null || !ShiftedCameras.Contains(cam))
            {
                return;
            }
            Matrix4x4 m = cam.projectionMatrix;
            // Only if it was reset: the value is then near 0 (plus a tiny jitter), not near our shift.
            if (Mathf.Abs(m[0, 2]) >= Mathf.Abs(m[0, 2] - _current))
            {
                return;
            }
            m[0, 2] += _current;
            cam.projectionMatrix = m;
            Matrix4x4 n = cam.nonJitteredProjectionMatrix;
            if (Mathf.Abs(n[0, 2]) < Mathf.Abs(n[0, 2] - _current))
            {
                n[0, 2] += _current;
                cam.nonJitteredProjectionMatrix = n;
            }
        }

        // The main camera plus any cameras attached under it (e.g. a separate layer for orbits),
        // so everything 3D shifts together.
        private static void CollectCameras(Camera main)
        {
            ShiftedCameras.Clear();
            ShiftedCameras.Add(main);
            foreach (Camera cam in main.GetComponentsInChildren<Camera>())
            {
                if (cam != main && cam.enabled && !cam.orthographic)
                {
                    ShiftedCameras.Add(cam);
                }
            }
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            if (rt == null)
            {
                return default;
            }
            Canvas canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            rt.GetWorldCorners(Corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
