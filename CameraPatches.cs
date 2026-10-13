using System;
using CameraControl;
using CustomUpdate;
using Game.Info;
using Game.UI.Windows.Elements.PlanMissionElements;
using Game.UI.Windows.Windows;
using Game.VisualizationScripts;
using HarmonyLib;
using ScriptsAdamTests;
using UnityEngine;

namespace UpdatedCamera
{
    // While a click handler runs, the game's camera moves are blocked. A double click on the
    // same object then re-centers the camera explicitly.
    internal static class CameraGate
    {
        private static int _suppress;
        private static object _lastClicked;
        private static float _lastClickTime;

        public static bool Suppressed => _suppress > 0;

        public static void Enter() => _suppress++;

        public static void Exit() => _suppress = Math.Max(0, _suppress - 1);

        // Records a click on `target`; true when it completes a double click.
        public static bool RegisterClick(object target)
        {
            if (target == null)
            {
                return false;
            }
            float now = Time.unscaledTime;
            if (ReferenceEquals(target, _lastClicked) && now - _lastClickTime <= Settings.DoubleClickSeconds.Value)
            {
                _lastClicked = null;
                return true;
            }
            _lastClicked = target;
            _lastClickTime = now;
            return false;
        }

        // Same call the game makes when it centers on a clicked object.
        public static void Center(Component target)
        {
            if (target != null && !MonoBehaviourSingleton<MyCameraController>.InstanceIsNull)
            {
                MonoBehaviourSingleton<MyCameraController>.Instance.ChangeTarget(target.transform);
            }
        }
    }

    // --- The camera moves themselves: skipped while a blocked handler is running. ---

    [HarmonyPatch(typeof(MyCameraController), nameof(MyCameraController.ChangeTarget), new[] { typeof(Transform), typeof(bool) })]
    internal static class ChangeTargetTransformPatch
    {
        private static bool Prefix(Transform newTarget)
        {
            if (!CameraGate.Suppressed)
            {
                return true;
            }
            UiClickGate.Record(newTarget);
            return false;
        }
    }

    [HarmonyPatch(typeof(MyCameraController), nameof(MyCameraController.ChangeTarget), new[] { typeof(ObjectInfo), typeof(bool) })]
    internal static class ChangeTargetObjectInfoPatch
    {
        private static bool Prefix(ObjectInfo newTarget)
        {
            if (!CameraGate.Suppressed)
            {
                return true;
            }
            UiClickGate.Record(newTarget);
            return false;
        }
    }

    [HarmonyPatch(typeof(MyCameraController), nameof(MyCameraController.ChangeTargetForPlanMission))]
    internal static class ChangeTargetForPlanMissionPatch
    {
        private static bool Prefix() => !CameraGate.Suppressed;
    }

    // --- Clicks: open the info window as usual, center only on double click. ---

    // Clicking a planet, moon or the Sun in the 3D view.
    [HarmonyPatch(typeof(InfoBase), nameof(InfoBase.MyOnMouseUpAsButton))]
    internal static class PlanetClickPatch
    {
        private static void Prefix(InfoBase __instance, out bool __state)
        {
            CameraGate.Enter();
            __state = CameraGate.RegisterClick(__instance);
        }

        private static void Finalizer(InfoBase __instance, bool __state)
        {
            CameraGate.Exit();
            if (__state)
            {
                CameraGate.Center(__instance);
            }
        }
    }

    // Clicking an object's name label in the 3D view.
    [HarmonyPatch(typeof(InfoBase), nameof(InfoBase.MyOnMouseUpAsButtonFromObjectLabel))]
    internal static class PlanetLabelClickPatch
    {
        private static void Prefix(InfoBase __instance, out bool __state)
        {
            CameraGate.Enter();
            __state = CameraGate.RegisterClick(__instance);
        }

        private static void Finalizer(InfoBase __instance, bool __state)
        {
            CameraGate.Exit();
            if (__state)
            {
                CameraGate.Center(__instance);
            }
        }
    }

    // The planet icons at the bottom center of the screen, moon labels and favourites.
    [HarmonyPatch(typeof(HighlightHoverObject), nameof(HighlightHoverObject.ChangeTarget))]
    internal static class PlanetIconClickPatch
    {
        private static void Prefix(HighlightHoverObject __instance, out bool __state)
        {
            CameraGate.Enter();
            __state = __instance.enabled && CameraGate.RegisterClick(__instance.MyTargetObjectInfo);
        }

        private static void Finalizer(HighlightHoverObject __instance, bool __state)
        {
            CameraGate.Exit();
            if (__state)
            {
                CameraGate.Center(__instance.MyTargetObjectInfo);
            }
        }
    }

    // Older planet buttons that some screens may still use.
    [HarmonyPatch(typeof(ChangeTargetButtons), nameof(ChangeTargetButtons.ChangeTarget))]
    internal static class PlanetButtonClickPatch
    {
        private static void Prefix(ChangeTargetButtons __instance, out bool __state)
        {
            CameraGate.Enter();
            __state = CameraGate.RegisterClick(__instance.idMyTargetObjectInfo?.Object);
        }

        private static void Finalizer(ChangeTargetButtons __instance, bool __state)
        {
            CameraGate.Exit();
            if (__state)
            {
                CameraGate.Center(__instance.idMyTargetObjectInfo?.Object);
            }
        }
    }

    // Spacecraft and facility markers in the 3D view.
    [HarmonyPatch(typeof(Bilboard), nameof(Bilboard.MyOnMouseUpAsButton))]
    internal static class MarkerClickPatch
    {
        private static readonly AccessTools.FieldRef<Bilboard, ObjectOnOrbit> TargetCamera =
            AccessTools.FieldRefAccess<Bilboard, ObjectOnOrbit>("targetCamera");

        private static void Prefix(Bilboard __instance, out bool __state)
        {
            CameraGate.Enter();
            __state = CameraGate.RegisterClick(__instance);
        }

        private static void Finalizer(Bilboard __instance, bool __state)
        {
            CameraGate.Exit();
            if (__state)
            {
                // The marker's own target if it has one, else the marker itself (as the game does).
                ObjectOnOrbit target = TargetCamera(__instance);
                CameraGate.Center(target != null ? (Component)target : __instance);
            }
        }
    }

    // The labels next to spacecraft in flight.
    [HarmonyPatch(typeof(MissionsLabelsMainUI), nameof(MissionsLabelsMainUI.Click))]
    internal static class FlightLabelClickPatch
    {
        private static void Prefix(MissionsLabelsMainUI __instance, out bool __state)
        {
            CameraGate.Enter();
            __state = CameraGate.RegisterClick(__instance.MissionInfo);
        }

        private static void Finalizer(MissionsLabelsMainUI __instance, bool __state)
        {
            CameraGate.Exit();
            if (__state && __instance.MissionInfo?.spacecraftInfo2 is Spacecraft ship && ship != null)
            {
                CameraGate.Center(ship);
                MonoBehaviourSingleton<MyCameraController>.Instance.TargetIsSCOnFly = true;
            }
        }
    }

    // --- Trip planner: no camera moves at all. ---

    // Opening the planner (centered on the origin).
    [HarmonyPatch(typeof(PMTabDestination), "ActiveTab")]
    internal static class PlannerOpenPatch
    {
        private static void Prefix() => CameraGate.Enter();

        private static void Finalizer() => CameraGate.Exit();
    }

    // Picking a destination (centered on it).
    [HarmonyPatch(typeof(PMTabDestination), "DestinationInputOnObjectSelect")]
    internal static class PlannerDestinationPatch
    {
        private static void Prefix() => CameraGate.Enter();

        private static void Finalizer() => CameraGate.Exit();
    }

    // Zooming out to show the trajectory after each porkchop pick. This method only moves the
    // camera, so skip it entirely.
    [HarmonyPatch(typeof(PlanMissionWindow), nameof(PlanMissionWindow.ChangeTargetForCamera))]
    internal static class PlannerTrajectoryZoomPatch
    {
        private static bool Prefix() => false;
    }

    // Scheduling a mission to an orbit (jumps to the origin as the planner closes).
    [HarmonyPatch(typeof(PMTabSchedule), "CreateFly")]
    internal static class PlannerScheduledPatch
    {
        private static void Prefix() => CameraGate.Enter();

        private static void Finalizer() => CameraGate.Exit();
    }
}
