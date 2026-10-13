using CameraControl;
using Data;
using Game.Info;
using HarmonyLib;
using UnityEngine;

namespace UpdatedCamera
{
    internal enum ZoomCategory
    {
        Planets,
        Moons,
        SmallBodies,
        Spacecraft
    }

    // Sets the distance when the game centers on something. The game uses a hand-picked distance
    // per object (ObjectInfo.ZoomToSet), so bodies of different sizes end up framed differently.
    // For planets, moons, asteroids and comets this replaces it with the distance at which the
    // body fills a set share of the screen height, so every body of a kind looks the same size.
    // Spacecraft have no meaningful size, so their distance is scaled by a multiplier instead.
    // The game sets the distance (FinalZoom) inside ChangeTarget and
    // SetTargetAndZoomBasedOnTimeScaleAndObjectType, which call each other, so only the
    // outermost call applies it.
    internal static class ZoomScaler
    {
        private static int _depth;
        private static float _zoomBefore;

        public static void Begin(MyCameraController camera)
        {
            if (_depth++ == 0)
            {
                _zoomBefore = camera.CurrentFinalZoom;
            }
        }

        public static void End(MyCameraController camera)
        {
            if (--_depth > 0)
            {
                return;
            }
            _depth = 0;
            float zoom = camera.CurrentFinalZoom;
            if (zoom == _zoomBefore)
            {
                return;  // didn't re-zoom (e.g. a blocked move, or same target without reset)
            }
            Transform target = camera.Target;
            ZoomCategory category = CategoryOf(target);
            float share = ScreenShare(category);
            float radius = Radius(target);
            if (share > 0f && radius > 0f && camera.MyCamera != null)
            {
                // Zoom is negative: the camera sits behind the target along its view direction.
                // The setter clamps to the game's zoom limits and restarts the zoom animation.
                camera.FinalZoom = -DistanceForShare(share / 100f, radius, camera.MyCamera.fieldOfView);
                return;
            }
            float multiplier = category == ZoomCategory.Spacecraft ? Settings.ZoomSpacecraft.Value : 1f;
            if (!Mathf.Approximately(multiplier, 1f))
            {
                camera.FinalZoom = zoom * multiplier;
            }
        }

        // The body's radius in world units, from the same collider the game uses for its
        // closest-zoom limit. 0 if it has none.
        public static float Radius(Transform target)
        {
            if (target == null)
            {
                return 0f;
            }
            if (target.TryGetComponent(out SphereCollider sphere))
            {
                return sphere.radius * Mathf.Abs(sphere.transform.lossyScale.x);
            }
            if (target.TryGetComponent(out Collider collider))
            {
                Vector3 e = collider.bounds.extents;
                return Mathf.Max(e.x, e.y, e.z);
            }
            return 0f;
        }

        // A sphere of this radius at this distance spans an angle of 2*asin(r/d). On screen its
        // height relative to the screen height is tan(that half-angle) / tan(half the field of view).
        public static float DistanceForShare(float share, float radius, float fieldOfView)
        {
            float halfFov = fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfAngle = Mathf.Atan(share * Mathf.Tan(halfFov));
            return radius / Mathf.Sin(halfAngle);
        }

        public static float ShareAtDistance(float distance, float radius, float fieldOfView)
        {
            if (distance <= radius)
            {
                return 1f;
            }
            float halfFov = fieldOfView * 0.5f * Mathf.Deg2Rad;
            return Mathf.Tan(Mathf.Asin(radius / distance)) / Mathf.Tan(halfFov);
        }

        public static ZoomCategory CategoryOf(Transform target)
        {
            ObjectInfo info = target != null ? target.GetComponent<ObjectInfo>() : null;
            if (info == null)
            {
                return ZoomCategory.Spacecraft;  // ships, stations and other markers
            }
            switch (info.objectTypes)
            {
                case EObjectTypes.Moons:
                    return ZoomCategory.Moons;
                case EObjectTypes.Asteroid:
                case EObjectTypes.Comet:
                case EObjectTypes.Belt:
                    return ZoomCategory.SmallBodies;
                case EObjectTypes.Orbit:
                case EObjectTypes.Spacecraft:
                    return ZoomCategory.Spacecraft;
                default:
                    return ZoomCategory.Planets;
            }
        }

        // Target share of the screen height in %, or 0 to keep the game's distance.
        public static float ScreenShare(ZoomCategory category)
        {
            switch (category)
            {
                case ZoomCategory.Planets: return Settings.SizePlanets.Value;
                case ZoomCategory.Moons: return Settings.SizeMoons.Value;
                case ZoomCategory.SmallBodies: return Settings.SizeSmallBodies.Value;
                default: return 0f;
            }
        }
    }

    [HarmonyPatch(typeof(MyCameraController), nameof(MyCameraController.ChangeTarget), new[] { typeof(Transform), typeof(bool) })]
    internal static class ZoomChangeTargetTransformPatch
    {
        private static void Prefix(MyCameraController __instance) => ZoomScaler.Begin(__instance);

        private static void Finalizer(MyCameraController __instance) => ZoomScaler.End(__instance);
    }

    [HarmonyPatch(typeof(MyCameraController), nameof(MyCameraController.ChangeTarget), new[] { typeof(ObjectInfo), typeof(bool) })]
    internal static class ZoomChangeTargetObjectInfoPatch
    {
        private static void Prefix(MyCameraController __instance) => ZoomScaler.Begin(__instance);

        private static void Finalizer(MyCameraController __instance) => ZoomScaler.End(__instance);
    }

    // Also runs on its own when the time speed changes and the camera swaps between a moon or
    // ship and its parent planet.
    [HarmonyPatch(typeof(MyCameraController), "SetTargetAndZoomBasedOnTimeScaleAndObjectType")]
    internal static class ZoomTimeScalePatch
    {
        private static void Prefix(MyCameraController __instance) => ZoomScaler.Begin(__instance);

        private static void Finalizer(MyCameraController __instance) => ZoomScaler.End(__instance);
    }
}
