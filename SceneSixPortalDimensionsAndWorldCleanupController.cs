using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OWML.Common;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Applies the larger Return portal presentation without modifying the
    /// locked launch/transport controller.
    /// </summary>
    internal sealed class ReturnPortalDimensionOverride : MonoBehaviour
    {
        private const float TargetVisualRadius = 1.5f;
        private const float BlackTransportRadius = 1f;
        private const float MinimumRenderBoundsDiameter = 6f;
        private const float GiantsDeepCoreRadius = 400f;

        private ReturnPortalType _portalType;
        private bool _coreRenderingApplied;

        public void Initialize(ReturnPortalType portalType)
        {
            _portalType = portalType;
            ApplyBlackTransportRadius();
            StartCoroutine(ResizeVisualWhenReady());
        }

        private void ApplyBlackTransportRadius()
        {
            if (_portalType != ReturnPortalType.Black)
            {
                return;
            }

            Transform transport = transform.Find(
                "Return_BlackPortalTransportVolume"
            );
            SphereCollider trigger = transport == null
                ? null
                : transport.GetComponent<SphereCollider>();
            if (trigger != null)
            {
                trigger.radius = BlackTransportRadius;
            }
        }

        private IEnumerator ResizeVisualWhenReady()
        {
            string expectedName = "Return_" + _portalType +
                "PortalSingularityVisual";
            Transform visual = null;
            float timeout = Time.realtimeSinceStartup + 3f;
            while (visual == null && Time.realtimeSinceStartup < timeout)
            {
                visual = transform.Find(expectedName);
                if (visual == null)
                {
                    yield return null;
                }
            }

            if (visual == null)
            {
                yield break;
            }

            // Let the copied SingularityController finish Create() and apply
            // its material-property radius before measuring it.
            yield return new WaitForSecondsRealtime(1f);

            RemoveInheritedSingularityLod(visual);
            ExpandRenderBoundsOnce(visual);
            if (IsInsideGiantsDeepWater(visual.position))
            {
                ConfigureCorePortalRendering(visual);
                _coreRenderingApplied = true;
            }
            SetSingularityRadius(visual, TargetVisualRadius);
            ApplyBlackTransportRadius();
            StartCoroutine(WatchForGiantsDeepWater(visual));
        }

        internal static bool IsInsideGiantsDeepWater(Vector3 position)
        {
            AstroObject giantsDeep = Locator.GetAstroObject(
                AstroObject.Name.GiantsDeep
            );
            OWRigidbody giantsDeepBody = giantsDeep == null
                ? null
                : giantsDeep.GetOWRigidbody();
            if (giantsDeepBody == null)
            {
                return false;
            }

            // Fast path for the core region; the ocean itself extends far
            // beyond the core, so also accept any Giant's Deep collider
            // (the ocean fluid volume above all) that contains the portal.
            if (Vector3.Distance(
                    position,
                    giantsDeepBody.GetPosition()
                ) < GiantsDeepCoreRadius)
            {
                return true;
            }

            foreach (Collider collider in
                giantsDeepBody.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null &&
                    collider.bounds.Contains(position))
                {
                    return true;
                }
            }
            return false;
        }

        private IEnumerator WatchForGiantsDeepWater(Transform visual)
        {
            while (visual != null && !_coreRenderingApplied)
            {
                if (IsInsideGiantsDeepWater(visual.position))
                {
                    _coreRenderingApplied = true;
                    ConfigureCorePortalRendering(visual);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        internal static void ConfigureCorePortalRendering(
            Transform visual
        )
        {
            bool alreadyApplied = true;
            foreach (Renderer renderer in
                visual.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null ||
                        material.name.IndexOf(
                            "_ReturnGiantsDeepCorePortal",
                            StringComparison.Ordinal
                        ) < 0)
                    {
                        alreadyApplied = false;
                        break;
                    }
                }
                if (!alreadyApplied)
                {
                    break;
                }
            }
            if (alreadyApplied)
            {
                return;
            }

            foreach (Renderer renderer in
                visual.GetComponentsInChildren<Renderer>(true))
            {
                Material[] sourceMaterials = renderer.sharedMaterials;
                Material[] coreMaterials =
                    new Material[sourceMaterials.Length];
                for (int index = 0;
                    index < sourceMaterials.Length;
                    index++)
                {
                    Material source = sourceMaterials[index];
                    if (source == null)
                    {
                        continue;
                    }
                    Material coreMaterial = new Material(source);
                    coreMaterial.name = source.name +
                        "_ReturnGiantsDeepCorePortal";
                    coreMaterial.renderQueue = Mathf.Max(
                        3100,
                        source.renderQueue
                    );
                    coreMaterials[index] = coreMaterial;
                }
                renderer.sharedMaterials = coreMaterials;
                renderer.sortingOrder = 100;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.enabled = true;
            }
        }

        private static void RemoveInheritedSingularityLod(Transform visual)
        {
            FieldInfo defaultMaterialField = AccessTools.Field(
                typeof(SingularityLOD),
                "_defaultMaterial"
            );
            foreach (SingularityLOD lod in
                visual.GetComponentsInChildren<SingularityLOD>(true))
            {
                Renderer renderer = lod.GetComponent<Renderer>();
                Material defaultMaterial = defaultMaterialField == null
                    ? null
                    : defaultMaterialField.GetValue(lod) as Material;
                if (renderer != null && defaultMaterial != null)
                {
                    renderer.sharedMaterial = defaultMaterial;
                }
                lod.enabled = false;
                UnityEngine.Object.Destroy(lod);
            }
        }

        private static void SetSingularityRadius(
            Transform visual,
            float radius
        )
        {
            int radiusProperty = Shader.PropertyToID("_Radius");
            foreach (OWRenderer owRenderer in
                visual.GetComponentsInChildren<OWRenderer>(true))
            {
                owRenderer.SetLODActivation(true);
                owRenderer.SetActivation(true);
                owRenderer.SetMaterialProperty(radiusProperty, radius);
            }

            FieldInfo targetRadiusField = AccessTools.Field(
                typeof(SingularityController),
                "_targetRadius"
            );
            FieldInfo baseRadiusField = AccessTools.Field(
                typeof(SingularityController),
                "_baseRadius"
            );
            FieldInfo currentRadiusField = AccessTools.Field(
                typeof(SingularityController),
                "_currentRadius"
            );
            foreach (SingularityController singularity in
                visual.GetComponentsInChildren<SingularityController>(true))
            {
                targetRadiusField?.SetValue(singularity, radius);
                baseRadiusField?.SetValue(singularity, radius);
                currentRadiusField?.SetValue(singularity, radius);
            }
        }

        private static void ExpandRenderBoundsOnce(Transform visual)
        {
            Renderer[] renderers =
                visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            float currentDiameter = Mathf.Max(
                bounds.size.x,
                bounds.size.y,
                bounds.size.z
            );
            if (currentDiameter > 0.001f &&
                currentDiameter < MinimumRenderBoundsDiameter)
            {
                visual.localScale *=
                    MinimumRenderBoundsDiameter / currentDiameter;
            }
        }
    }

    internal static class SceneSixWorldCleanupController
    {
        private const string SunkenModuleName = "Sector_Module_Sunken";

        public static IEnumerator Prepare(ReturnMod mod)
        {
            yield return null;

            if (!SceneSixController.IsActive ||
                LoadManager.GetCurrentScene() != OWScene.SolarSystem)
            {
                yield break;
            }

            int sunkenModules = DisableMatchingRoots(
                SunkenModuleName,
                IsGiantsDeepCoreModule
            );
            List<Transform> cannonRoots =
                FindOrbitalProbeCannonRoots();

            // MapMarker creates its CanvasMapMarker during the stock map-mode
            // construction pass. Disabling the cannon before that pass leaves
            // a null canvas marker, which breaks closing the map after a
            // title-screen round trip. Keep the root active until every live
            // marker has completed that initialization.
            yield return WaitForMapMarkers(cannonRoots);

            // Distance proxies may finish spawning during map construction.
            MergeUnique(
                cannonRoots,
                FindOrbitalProbeCannonRoots()
            );

            int disabledCannonRoots = 0;
            int hiddenCannonRoots = 0;
            foreach (Transform cannonRoot in cannonRoots)
            {
                if (cannonRoot == null)
                {
                    continue;
                }
                if (DisableMapMarkersSafely(cannonRoot))
                {
                    cannonRoot.gameObject.SetActive(false);
                    disabledCannonRoots++;
                }
                else
                {
                    HideGeometryAndCollision(cannonRoot);
                    hiddenCannonRoots++;
                }
            }

            ReturnDebugLog.Write(
                "[RETURN WORLD CLEANUP] sunkenProbeModules=" +
                sunkenModules + "; orbitalProbeCannonRoots=" +
                disabledCannonRoots + "; hiddenFallbacks=" +
                hiddenCannonRoots + ".",
                sunkenModules > 0 &&
                    disabledCannonRoots + hiddenCannonRoots > 0
                    ? MessageType.Success
                    : MessageType.Warning
            );
        }

        private static List<Transform> FindOrbitalProbeCannonRoots()
        {
            List<Transform> roots = new List<Transform>();
            foreach (AstroObject astroObject in
                Resources.FindObjectsOfTypeAll<AstroObject>())
            {
                if (astroObject == null ||
                    !astroObject.gameObject.scene.IsValid() ||
                    astroObject.GetAstroObjectName() !=
                        AstroObject.Name.ProbeCannon)
                {
                    continue;
                }

                Transform root = astroObject.transform;
                if (root.parent != null &&
                    root.parent.name.IndexOf(
                        "OrbitalProbeCannon",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0)
                {
                    root = root.parent;
                }
                AddUnique(roots, root);
            }

            foreach (Transform candidate in
                Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate == null ||
                    !candidate.gameObject.scene.IsValid() ||
                    !HasAncestor(candidate, "GiantsDeep_DistantProxy") ||
                    (candidate.name != "OrbitalProbeCannon_Pivot" &&
                        candidate.name != "OrbitalProbeCannon_Body"))
                {
                    continue;
                }

                Transform root = candidate;
                if (candidate.name == "OrbitalProbeCannon_Body" &&
                    candidate.parent != null &&
                    candidate.parent.name == "OrbitalProbeCannon_Pivot")
                {
                    root = candidate.parent;
                }
                AddUnique(roots, root);
            }
            return roots;
        }

        private static void AddUnique(
            List<Transform> roots,
            Transform candidate
        )
        {
            if (candidate != null && !roots.Contains(candidate))
            {
                roots.Add(candidate);
            }
        }

        private static void MergeUnique(
            List<Transform> destination,
            List<Transform> source
        )
        {
            foreach (Transform candidate in source)
            {
                AddUnique(destination, candidate);
            }
        }

        private static IEnumerator WaitForMapMarkers(
            List<Transform> roots
        )
        {
            FieldInfo canvasMarkerField = AccessTools.Field(
                typeof(MapMarker),
                "_canvasMarker"
            );
            if (canvasMarkerField == null)
            {
                yield return new WaitForSecondsRealtime(2f);
                yield break;
            }

            float timeout = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < timeout)
            {
                bool ready = true;
                foreach (Transform root in roots)
                {
                    if (root == null)
                    {
                        continue;
                    }
                    foreach (MapMarker marker in
                        root.GetComponentsInChildren<MapMarker>(true))
                    {
                        if (marker == null ||
                            !marker.gameObject.activeInHierarchy)
                        {
                            continue;
                        }
                        if (canvasMarkerField.GetValue(marker) == null)
                        {
                            ready = false;
                            break;
                        }
                    }
                    if (!ready)
                    {
                        break;
                    }
                }

                if (ready)
                {
                    // Give the manager one additional frame to finish its
                    // registration list before OnDisable hides the marker.
                    yield return null;
                    yield break;
                }
                yield return null;
            }
        }

        private static bool DisableMapMarkersSafely(Transform root)
        {
            FieldInfo canvasMarkerField = AccessTools.Field(
                typeof(MapMarker),
                "_canvasMarker"
            );
            MapMarker[] markers =
                root.GetComponentsInChildren<MapMarker>(true);
            if (markers.Length > 0 && canvasMarkerField == null)
            {
                return false;
            }

            foreach (MapMarker marker in markers)
            {
                if (marker != null && marker.enabled &&
                    marker.gameObject.activeInHierarchy &&
                    canvasMarkerField.GetValue(marker) == null)
                {
                    return false;
                }
            }
            foreach (MapMarker marker in markers)
            {
                if (marker != null && marker.enabled)
                {
                    marker.enabled = false;
                }
            }
            return true;
        }

        private static void HideGeometryAndCollision(Transform root)
        {
            foreach (OWRenderer owRenderer in
                root.GetComponentsInChildren<OWRenderer>(true))
            {
                owRenderer.SetActivation(false);
                owRenderer.SetLODActivation(false);
            }
            foreach (Renderer renderer in
                root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }
            foreach (Collider collider in
                root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            foreach (Shape shape in
                root.GetComponentsInChildren<Shape>(true))
            {
                shape.SetActivation(false);
            }
        }

        private static int DisableMatchingRoots(
            string exactName,
            Func<Transform, bool> predicate
        )
        {
            List<Transform> matches = FindMatchingRoots(
                exactName,
                predicate
            );
            int disabled = 0;
            foreach (Transform candidate in matches)
            {
                if (candidate == null)
                {
                    continue;
                }
                candidate.gameObject.SetActive(false);
                disabled++;
            }
            return disabled;
        }

        private static List<Transform> FindMatchingRoots(
            string exactName,
            Func<Transform, bool> predicate
        )
        {
            List<Transform> matches = new List<Transform>();
            foreach (Transform candidate in
                Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate == null || candidate.name != exactName ||
                    !candidate.gameObject.scene.IsValid() ||
                    !predicate(candidate))
                {
                    continue;
                }

                matches.Add(candidate);
            }
            return matches;
        }

        private static bool IsGiantsDeepCoreModule(Transform candidate)
        {
            return HasAncestor(candidate, "Sector_GDCore") &&
                HasAncestor(candidate, "GiantsDeep_Body");
        }

        private static bool HasAncestor(
            Transform candidate,
            string ancestorName
        )
        {
            for (Transform current = candidate.parent;
                current != null;
                current = current.parent)
            {
                if (current.name == ancestorName)
                {
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(
        typeof(ReturnWarpCoreToolBehaviour),
        "InstallPortalVisualAndVolume"
    )]
    internal static class ReturnPortalDimensionOverridePatch
    {
        private static void Postfix(
            GameObject carrier,
            ReturnPortalType portalType
        )
        {
            if (!SceneSixController.IsActive || carrier == null)
            {
                return;
            }

            ReturnPortalDimensionOverride dimensions =
                carrier.GetComponent<ReturnPortalDimensionOverride>();
            if (dimensions == null)
            {
                dimensions = carrier.AddComponent<
                    ReturnPortalDimensionOverride>();
            }
            dimensions.Initialize(portalType);
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class SceneSixWorldCleanupPatch
    {
        private static void Postfix(
            ReturnMod __instance,
            OWScene newScene
        )
        {
            if (newScene == OWScene.SolarSystem &&
                SceneSixController.IsActive)
            {
                __instance.StartCoroutine(
                    SceneSixWorldCleanupController.Prepare(__instance)
                );
            }
        }
    }
}
