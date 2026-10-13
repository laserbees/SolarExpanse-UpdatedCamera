using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UpdatedCamera
{
    // The game uses Unity's Post-Processing Stack v2. With anti-aliasing on, its PostProcessLayer
    // resets the camera's projection just before drawing (OnPreCull) and again just after
    // (OnPostRender), which would undo the view offset. Put the shift back after each reset.
    // Looked up by name so the mod doesn't need to reference the post-processing library.
    internal static class PostProcessLayerTarget
    {
        public static readonly Type Type = AccessTools.TypeByName("UnityEngine.Rendering.PostProcessing.PostProcessLayer");
    }

    [HarmonyPatch]
    internal static class PostProcessPreCullPatch
    {
        private static bool Prepare() => PostProcessLayerTarget.Type != null;

        private static MethodBase TargetMethod() => AccessTools.Method(PostProcessLayerTarget.Type, "OnPreCull");

        private static void Postfix(Component __instance) => ViewOffset.Reapply(__instance.GetComponent<Camera>());
    }

    [HarmonyPatch]
    internal static class PostProcessPostRenderPatch
    {
        private static bool Prepare() => PostProcessLayerTarget.Type != null;

        private static MethodBase TargetMethod() => AccessTools.Method(PostProcessLayerTarget.Type, "OnPostRender");

        private static void Postfix(Component __instance) => ViewOffset.Reapply(__instance.GetComponent<Camera>());
    }
}
