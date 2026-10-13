using System.Collections.Generic;
using System.Reflection;
using CameraControl;
using Game.Info;
using HarmonyLib;
using UnityEngine;

namespace UpdatedCamera
{
    // Buttons and list rows in the game's windows that also move the camera. While one runs, the
    // camera move is blocked and its intended target is remembered (the ChangeTarget patches call Record),
    // so these patches don't need to know what each click would center on.
    internal static class UiClickGate
    {
        private static int _depth;
        private static object _blocked;
        private static bool _onFlyBefore;

        public static void Begin()
        {
            if (_depth++ == 0)
            {
                _blocked = null;
                _onFlyBefore = !MonoBehaviourSingleton<MyCameraController>.InstanceIsNull
                    && MonoBehaviourSingleton<MyCameraController>.Instance.TargetIsSCOnFly;
            }
            CameraGate.Enter();
        }

        public static void Record(object target)
        {
            if (_depth > 0)
            {
                _blocked = target;
            }
        }

        // allowDoubleClick: a second click on the same target within the double-click time centers
        // the camera, as in the 3D view. Off for action buttons, which would run twice.
        public static void End(bool allowDoubleClick)
        {
            CameraGate.Exit();
            if (--_depth > 0)
            {
                return;
            }
            _depth = 0;
            object target = _blocked;
            _blocked = null;
            if (target == null || MonoBehaviourSingleton<MyCameraController>.InstanceIsNull)
            {
                return;
            }

            // Some clicks also mark the camera as following a ship in flight; undo that with the move.
            MyCameraController camera = MonoBehaviourSingleton<MyCameraController>.Instance;
            bool onFlyWanted = camera.TargetIsSCOnFly;
            camera.TargetIsSCOnFly = _onFlyBefore;

            Transform key = target is Component c ? c.transform : target as Transform;
            if (!allowDoubleClick || key == null || !CameraGate.RegisterClick(key))
            {
                return;
            }
            if (target is ObjectInfo info)
            {
                camera.ChangeTarget(info);
            }
            else
            {
                camera.ChangeTarget(key);
            }
            camera.TargetIsSCOnFly = onFlyWanted;
        }

        // "Type:Method", or "Type:Method(object)" to pick the overload taking one object.
        public static IEnumerable<MethodBase> Methods(string[] names)
        {
            foreach (string name in names)
            {
                MethodInfo method = null;
                try
                {
                    method = name.EndsWith("(object)")
                        ? AccessTools.Method(name.Substring(0, name.Length - "(object)".Length), new[] { typeof(object) })
                        : AccessTools.Method(name);
                }
                catch (AmbiguousMatchException)
                {
                    Plugin.Log.LogWarning("Click patch: " + name + " has several overloads, skipped.");
                }
                if (method == null)
                {
                    Plugin.Log.LogWarning("Click patch: " + name + " not found (game updated?), skipped.");
                    continue;
                }
                yield return method;
            }
        }
    }

    // Rows and links: single click does what it did minus the camera move, double click centers.
    [HarmonyPatch]
    internal static class UiRowClickPatch
    {
        private static readonly string[] Names =
        {
            // Find-object dialog: picking a result, in search or when choosing a destination.
            "Game.UI.Windows.Elements.SearchObjectElements.SearchRow:OnClickSelectButton",
            // A facility in an object's facility list (jumps to it in orbit).
            "Game.UI.Windows.Elements.ObjectInfoElements.UIFacilityList:ShowFacilityOnOrbitAndOpenFacilityInfoWindow",
            "Game.UI.Windows.Elements.ObjectInfoElements.UIFacilityList:OpenFacilityInfoWindow",
            // A spacecraft row in the rockets list (jumps to the ship in orbit).
            "Game.UI.Windows.Elements.ObjectInfoElements.UIRowRocket:ShowOnOrbitAndOpenFacilityInfoWindow",
            // A mission row in an object's info window (follows the ship in flight).
            "Game.UI.Windows.Elements.ObjectInfoElements.UIRowMission:RowClick",
            // An item in the production queue.
            "Game.ObjectInfoDataScripts.ProductionItem:OnClick",
            // The object's name at the top of its info window.
            "Game.UI.Windows.Elements.ObjectInfoElements.UIObjectName:ClickName",
        };

        private static IEnumerable<MethodBase> TargetMethods() => UiClickGate.Methods(Names);

        private static void Prefix() => UiClickGate.Begin();

        private static void Finalizer() => UiClickGate.End(allowDoubleClick: true);
    }

    // Action buttons and effects: never move the camera.
    [HarmonyPatch]
    internal static class UiActionClickPatch
    {
        private static readonly string[] Names =
        {
            // Trash button in the info window (jumps to the home planet).
            "Game.UI.Windows.Elements.ObjectInfoElements.UIObjectName:OnClickTrashObjectInfo",
            // Accepting Examine / Discover in the exploration window.
            "Game.UI.Windows.Windows.ExplorationWindow:OnAcceptExamineButtonClick",
            "Game.UI.Windows.Windows.ExplorationWindow:OnAcceptDiscoverButtonClick",
            // Opening a space elevator's facility info (switches to the elevator view).
            "Game.UI.Windows.Windows.FacilityInfoWindow:SetData(object)",
            // The destroy-asteroid button, and the explosion it causes.
            "Game.UI.Windows.Windows.FacilityInfoWindow:OnClickDestroyAsteroid",
            "Game.Info.ObjectInfo:VirtualDestroyFromNuclearBomb",
        };

        private static IEnumerable<MethodBase> TargetMethods() => UiClickGate.Methods(Names);

        private static void Prefix() => UiClickGate.Begin();

        private static void Finalizer() => UiClickGate.End(allowDoubleClick: false);
    }
}
