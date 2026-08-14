using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Build111's deliberately simple prison. There is no playable Bramble
    /// dimension: a door-shaped trigger at Giant's Deep's centre absorbs
    /// external rigidbodies, leaves their map marker at the core, and
    /// transfers their physical mass to Giant's Deep.
    /// </summary>
    internal sealed class Build111SimpleCorePrisonController : MonoBehaviour
    {
        internal static readonly Vector3 CoreFallbackHalfExtents =
            new Vector3(30f, 30f, 30f);
        // The door visual measures about 733.6 x 750.0 x 240.5 m, far larger
        // than the 60 m fallback core box. Portal carriers must be absorbed as
        // soon as their centre enters the whole door region, otherwise a white
        // hole launched through the entrance flies away and lags the game.
        private static readonly Vector3 PortalCarrierContainmentHalfExtents =
            new Vector3(370f, 380f, 130f);
        private const float CoreFallbackPadding = 5f;
        private const float VisualMaximumDiameter = 220f;
        private const float MinimumBodyMass = 0.000001f;
        private const string VisualName =
            "Return111_CorePrisonVisual";
        private const string BrambleSphereSourcePath =
            "DB_VesselDimension_Body/Sector_VesselDimension/" +
            "Geometry_VesselDimension/OtherComponentsGroup/" +
            "Terrain_DB_BrambleSphere_Outer_v2";
        private const string EntranceName =
            "Return111_BramblePrisonEntrance";
        private const string PrisonBodyName =
            "Return111BramblePrison_Body";

        private static readonly Vector3 RevivalLocalPosition =
            new Vector3(-224.8589f, 0.8171f, 92.6140f);
        private static readonly Quaternion RevivalLocalRotation =
            new Quaternion(
                -0.177269f,
                -0.096406f,
                -0.683298f,
                -0.701703f
            );
        private static readonly string[] CoreTerrainNames =
        {
            "BakedTerrain_CoreGroup1",
            "BakedTerrain_CoreGroup2",
            "BakedTerrain_CoreGroup3"
        };

        private static Build111SimpleCorePrisonController _instance;

        private readonly Collider[] _overlapBuffer = new Collider[2048];
        private readonly HashSet<int> _initialOccupants =
            new HashSet<int>();
        private readonly HashSet<int> _absorbedIds =
            new HashSet<int>();
        private readonly List<AbsorbedBody> _absorbedBodies =
            new List<AbsorbedBody>();
        private readonly List<AbsorbedUnityBody> _absorbedUnityBodies =
            new List<AbsorbedUnityBody>();

        private ReturnMod _mod;
        private OWRigidbody _giantsDeep;
        private OWRigidbody _comet;
        private Transform _coreSector;
        private Build111AbsorptionTriggerRelay _areaTrigger;
        private AbsorbedPlayer _absorbedPlayer;
        private bool _armed;
        private bool _victoryTriggered;
        private bool _prisonBodyHidden;
        private float _prisonHideDeadline;
        private bool _revivalInProgress;
        private int _generation;
        private float _nextCometCheckTime;
        private float _nextCometLookupTime;
        private float _nextPortalCarrierCheckTime;
        private float _nextPrisonLookupTime;

        internal void Initialize(ReturnMod mod)
        {
            _instance = this;
            _mod = mod;
            _generation++;
            _armed = false;
            _victoryTriggered = false;
            _prisonBodyHidden = false;
            _prisonHideDeadline = Time.realtimeSinceStartup + 30f;
            _revivalInProgress = false;
            _giantsDeep = null;
            _comet = null;
            _coreSector = null;
            _areaTrigger = null;
            _absorbedPlayer = null;
            _initialOccupants.Clear();
            _absorbedIds.Clear();
            _absorbedBodies.Clear();
            _absorbedUnityBodies.Clear();
            _nextCometCheckTime = 0f;
            _nextCometLookupTime = 0f;
            _nextPortalCarrierCheckTime = 0f;
            _nextPrisonLookupTime = 0f;
            StartCoroutine(Prepare(_generation));
        }

        internal static bool IsBodyAbsorbed(OWRigidbody body)
        {
            return _instance != null && body != null &&
                _instance._absorbedIds.Contains(body.GetInstanceID());
        }

        internal static void TryAbsorbTransportedBody(OWRigidbody body)
        {
            _instance?.TryAbsorbOWBody(body, requireColliderOverlap: false);
        }

        private IEnumerator Prepare(int generation)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (generation == _generation &&
                Time.realtimeSinceStartup < deadline)
            {
                _giantsDeep =
                    InterloperTrajectoryController.FindBody(
                        "GiantsDeep_Body"
                    );
                _coreSector = FindDescendant(
                    _giantsDeep == null
                        ? null
                        : _giantsDeep.transform,
                    "Sector_GDCore"
                );
                if (_giantsDeep != null && _coreSector != null)
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(0.1f);
            }

            if (generation != _generation ||
                _giantsDeep == null || _coreSector == null)
            {
                _mod?.ModHelper.Console.WriteLine(
                    "[RETURN BUILD111 SIMPLE PRISON] Giant's Deep core " +
                    "was unavailable; no prison logic was armed.",
                    MessageType.Error
                );
                yield break;
            }

            int terrainRemoved = RemoveCoreTerrain();
            _comet = InterloperTrajectoryController.FindBody("Comet_Body");
            CreateAbsorptionTrigger();
            yield return CreateEntranceVisual(generation);

            while (generation == _generation &&
                LoadManager.GetCurrentScene() == OWScene.SolarSystem &&
                !SceneSixController.IsActive)
            {
                yield return new WaitForSecondsRealtime(0.1f);
            }

            if (generation != _generation ||
                LoadManager.GetCurrentScene() != OWScene.SolarSystem)
            {
                yield break;
            }

            CaptureInitialOccupants();
            yield return new WaitForFixedUpdate();
            _armed = true;

            _mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD111 SIMPLE PRISON] armed=True; " +
                "centre=GiantsDeep_Body; coreFallbackHalfExtents=" +
                FormatVector(CoreFallbackHalfExtents) +
                "m; playableDimension=False; terrainRemoved=" +
                terrainRemoved + "/3.",
                MessageType.Success
            );
        }

        private IEnumerator CreateEntranceVisual(int generation)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            Transform entrance = null;
            while (generation == _generation &&
                entrance == null &&
                Time.realtimeSinceStartup < deadline)
            {
                entrance = FindLiveTransform(EntranceName);
                if (entrance == null)
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                }
            }
            Transform prisonBody = FindLiveTransform(PrisonBodyName);
            if (prisonBody == null && generation == _generation)
            {
                float bodyDeadline = Time.realtimeSinceStartup + 20f;
                while (generation == _generation &&
                    prisonBody == null &&
                    Time.realtimeSinceStartup < bodyDeadline)
                {
                    prisonBody = FindLiveTransform(PrisonBodyName);
                    if (prisonBody == null)
                    {
                        yield return new WaitForSecondsRealtime(0.1f);
                    }
                }
            }

            int triggerColliderCount = -1;
            if (entrance != null)
            {
                entrance.SetParent(_coreSector, false);
                entrance.localPosition = Vector3.zero;
                entrance.localRotation = Quaternion.identity;
                triggerColliderCount =
                    StripToVisualAndTriggerComponents(entrance.gameObject);
                foreach (Collider collider in
                    entrance.GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null)
                    {
                        continue;
                    }
                    Build111AbsorptionTriggerRelay relay =
                        collider.GetComponent<
                            Build111AbsorptionTriggerRelay>();
                    if (relay == null)
                    {
                        relay = collider.gameObject.AddComponent<
                            Build111AbsorptionTriggerRelay>();
                    }
                    relay.Initialize(this);
                }
                entrance.gameObject.SetActive(true);
            }

            if (prisonBody != null)
            {
                prisonBody.gameObject.SetActive(false);
                _prisonBodyHidden = true;
            }

            if (generation != _generation)
            {
                if (entrance != null)
                {
                    Destroy(entrance.gameObject);
                }
                yield break;
            }

            Vector3 visualSize = Vector3.zero;
            if (entrance != null)
            {
                yield return null;
                if (entrance != null &&
                    TryCalculateVisualBounds(entrance, out Bounds bounds))
                {
                    visualSize = bounds.size;
                }
            }

            _mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD111 SIMPLE PRISON] doorVisual=" +
                (entrance != null) +
                "; triggerColliderCount=" +
                triggerColliderCount +
                "; bounds=" + FormatVector(visualSize) +
                "m; coreFallbackHalfExtents=" +
                FormatVector(CoreFallbackHalfExtents) +
                "m; playableDimensionBodyHidden=" +
                _prisonBodyHidden + ".",
                entrance != null
                    ? MessageType.Success
                    : MessageType.Warning
            );
            yield return null;
        }
        private IEnumerator CreatePrimitiveSphereVisual(int generation)
        {
            Transform existingVisual = FindLiveTransform(VisualName);
            if (existingVisual == null)
            {
                GameObject visual = GameObject.CreatePrimitive(
                    PrimitiveType.Sphere
                );
                visual.name = VisualName;
                visual.transform.SetParent(_coreSector, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale =
                    Vector3.one * VisualMaximumDiameter;

                Collider sphereCollider = visual.GetComponent<Collider>();
                if (sphereCollider != null)
                {
                    Destroy(sphereCollider);
                }

                MeshRenderer renderer =
                    visual.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null)
                    {
                        shader = Shader.Find("Standard");
                    }
                    if (shader != null)
                    {
                        Material material = new Material(shader);
                        material.color = new Color(
                            0.02f,
                            0.03f,
                            0.03f,
                            0.85f
                        );
                        material.renderQueue =
                            (int)UnityEngine.Rendering.RenderQueue.Transparent;
                        renderer.sharedMaterial = material;
                    }
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }

                existingVisual = visual.transform;
            }

            if (generation != _generation)
            {
                if (existingVisual != null)
                {
                    Destroy(existingVisual.gameObject);
                }
                yield break;
            }

            if (existingVisual != null)
            {
                existingVisual.SetParent(_coreSector, false);
                existingVisual.localPosition = Vector3.zero;
                existingVisual.localRotation = Quaternion.identity;
                existingVisual.localScale =
                    Vector3.one * VisualMaximumDiameter;
                existingVisual.gameObject.SetActive(true);
            }

            _mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD111 SIMPLE PRISON] primitiveSphereVisual=" +
                (existingVisual != null) +
                "; diameter=" +
                VisualMaximumDiameter.ToString("F1") +
                "m; material=Sprites/Default; " +
                "colliderRemoved=True; playableDimension=False.",
                existingVisual != null
                    ? MessageType.Success
                    : MessageType.Warning
            );
            yield return null;
        }

        private IEnumerator CreateSphericalBrambleVisual(int generation)
        {
            Transform existingVisual = FindLiveTransform(VisualName);
            if (existingVisual == null && _mod.NewHorizons != null)
            {
                Sector coreSectorComponent =
                    _coreSector.GetComponent<Sector>();
                if (coreSectorComponent != null)
                {
                    GameObject visual = _mod.NewHorizons.SpawnObject(
                        _mod,
                        _giantsDeep.gameObject,
                        coreSectorComponent,
                        BrambleSphereSourcePath,
                        Vector3.zero,
                        Vector3.zero,
                        1f,
                        false
                    );
                    if (visual != null)
                    {
                        visual.name = VisualName;
                        visual.SetActive(false);
                        visual.transform.SetParent(_coreSector, false);
                        visual.transform.localPosition = Vector3.zero;
                        visual.transform.localRotation =
                            Quaternion.identity;
                        existingVisual = visual.transform;
                    }
                }
            }

            if (generation != _generation)
            {
                if (existingVisual != null)
                {
                    Destroy(existingVisual.gameObject);
                }
                yield break;
            }

            int remainingFunctionalComponents = -1;
            if (existingVisual != null)
            {
                existingVisual.SetParent(_coreSector, false);
                existingVisual.localPosition = Vector3.zero;
                existingVisual.localRotation = Quaternion.identity;
                remainingFunctionalComponents =
                    StripToVisualComponents(existingVisual.gameObject);
                existingVisual.gameObject.SetActive(true);
                yield return null;
            }

            Vector3 visualSizeBefore = Vector3.zero;
            Vector3 visualSizeAfter = Vector3.zero;
            float visualScaleMultiplier = 1f;
            bool visualFitted = existingVisual != null &&
                FitVisualToArea(
                    existingVisual,
                    out visualSizeBefore,
                    out visualSizeAfter,
                    out visualScaleMultiplier
                );

            _mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD111 SIMPLE PRISON] sphericalVisual=" +
                (existingVisual != null) +
                "; remainingFunctionalComponents=" +
                remainingFunctionalComponents +
                "; visualFitted=" + visualFitted +
                "; targetMaximumDiameter=" +
                VisualMaximumDiameter.ToString("F1") +
                "m; boundsBefore=" + FormatVector(visualSizeBefore) +
                "m; scaleMultiplier=" +
                visualScaleMultiplier.ToString("F4") +
                "; boundsAfter=" + FormatVector(visualSizeAfter) +
                "m; source=Terrain_DB_BrambleSphere_Outer_v2; " +
                "playableDimension=False.",
                existingVisual != null
                    ? MessageType.Success
                    : MessageType.Warning
            );
        }

        private static bool FitVisualToArea(
            Transform visual,
            out Vector3 sizeBefore,
            out Vector3 sizeAfter,
            out float scaleMultiplier
        )
        {
            sizeBefore = Vector3.zero;
            sizeAfter = Vector3.zero;
            scaleMultiplier = 1f;
            if (visual == null ||
                !TryCalculateVisualBounds(visual, out Bounds bounds))
            {
                return false;
            }

            sizeBefore = bounds.size;
            float maximumDiameter = Mathf.Max(
                sizeBefore.x,
                Mathf.Max(sizeBefore.y, sizeBefore.z)
            );
            if (maximumDiameter <= 0.01f)
            {
                return false;
            }

            scaleMultiplier = VisualMaximumDiameter / maximumDiameter;
            visual.localScale *= scaleMultiplier;

            if (!TryCalculateVisualBounds(visual, out bounds))
            {
                return false;
            }
            sizeAfter = bounds.size;
            return true;
        }

        private static bool TryCalculateVisualBounds(
            Transform visual,
            out Bounds bounds
        )
        {
            bounds = default;
            bool found = false;
            foreach (Renderer renderer in
                visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" + value.x.ToString("F1") + "," +
                value.y.ToString("F1") + "," +
                value.z.ToString("F1") + ")";
        }

        private static int StripToVisualComponents(GameObject root)
        {
            Component[] components =
                root.GetComponentsInChildren<Component>(true);
            // Required components are normally stored before the scripts
            // which depend on them. Removing in reverse order prevents Unity
            // from rejecting removal of InnerFogWarpVolume while its HUD and
            // debug helpers still exist.
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null ||
                    component is Transform ||
                    component is MeshFilter ||
                    component is Renderer ||
                    component is LODGroup)
                {
                    continue;
                }

                if (component is Behaviour behaviour)
                {
                    behaviour.enabled = false;
                }
                if (component is Collider collider)
                {
                    collider.enabled = false;
                }
                DestroyImmediate(component);
            }

            int remaining = 0;
            foreach (Component component in
                root.GetComponentsInChildren<Component>(true))
            {
                if (component != null &&
                    !(component is Transform) &&
                    !(component is MeshFilter) &&
                    !(component is Renderer) &&
                    !(component is LODGroup))
                {
                    remaining++;
                }
            }
            return remaining;
        }

        private static int StripToVisualAndTriggerComponents(
            GameObject root
        )
        {
            Component[] components =
                root.GetComponentsInChildren<Component>(true);
            int triggerCount = 0;
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];
                if (component == null ||
                    component is Transform ||
                    component is MeshFilter ||
                    component is Renderer ||
                    component is LODGroup)
                {
                    continue;
                }

                if (component is Collider collider)
                {
                    collider.isTrigger = true;
                    collider.enabled = true;
                    triggerCount++;
                    continue;
                }

                if (component is Behaviour behaviour)
                {
                    behaviour.enabled = false;
                }
                DestroyImmediate(component);
            }
            return triggerCount;
        }

        private void CreateAbsorptionTrigger()
        {
            Transform existing = FindDescendant(
                _giantsDeep.transform,
                "Return111_AreaA_AbsorptionTrigger"
            );
            GameObject triggerObject;
            if (existing != null)
            {
                triggerObject = existing.gameObject;
            }
            else
            {
                triggerObject = new GameObject(
                    "Return111_AreaA_AbsorptionTrigger"
                );
                int effectVolumeLayer =
                    LayerMask.NameToLayer("BasicEffectVolume");
                if (effectVolumeLayer >= 0)
                {
                    triggerObject.layer = effectVolumeLayer;
                }
                triggerObject.transform.SetParent(_coreSector, false);
                triggerObject.transform.localPosition = Vector3.zero;
                triggerObject.transform.localRotation =
                    Quaternion.identity;
                triggerObject.transform.localScale = Vector3.one;
            }

            BoxCollider trigger =
                triggerObject.GetComponent<BoxCollider>();
            if (trigger == null)
            {
                trigger = triggerObject.AddComponent<BoxCollider>();
            }
            trigger.isTrigger = true;
            trigger.size = CoreFallbackHalfExtents * 2f;
            trigger.enabled = true;

            OWCollider owCollider =
                triggerObject.GetComponent<OWCollider>();
            if (owCollider == null)
            {
                owCollider = triggerObject.AddComponent<OWCollider>();
            }
            owCollider.IgnorePhysicsSwapDelay();
            owCollider.ListenForParentBodySuspension();

            _areaTrigger = triggerObject.GetComponent<
                Build111AbsorptionTriggerRelay>();
            if (_areaTrigger == null)
            {
                _areaTrigger = triggerObject.AddComponent<
                    Build111AbsorptionTriggerRelay>();
            }
            _areaTrigger.Initialize(this);
        }

        internal void OnAreaTriggerEnter(Collider hit)
        {
            if (!_armed || hit == null || _giantsDeep == null ||
                !SceneSixController.IsActive ||
                SceneSixEndingController.IsEndingActive)
            {
                return;
            }

            Rigidbody unityBody = hit.attachedRigidbody;
            OWRigidbody owBody = unityBody == null
                ? hit.GetComponentInParent<OWRigidbody>()
                : unityBody.GetComponent<OWRigidbody>();
            if (owBody == null && unityBody != null)
            {
                owBody = unityBody.GetComponentInParent<OWRigidbody>();
            }

            if (owBody != null)
            {
                TryAbsorbOWBody(owBody, requireColliderOverlap: true);
            }
            else if (unityBody != null)
            {
                TryAbsorbUnityBody(unityBody);
            }
        }

        private void CheckCometFallback()
        {
            if (!_armed || _giantsDeep == null ||
                !SceneSixController.IsActive ||
                SceneSixEndingController.IsEndingActive ||
                Time.unscaledTime < _nextCometCheckTime)
            {
                return;
            }
            _nextCometCheckTime = Time.unscaledTime + 0.05f;

            if (_comet == null &&
                Time.unscaledTime >= _nextCometLookupTime)
            {
                _nextCometLookupTime = Time.unscaledTime + 1f;
                _comet = InterloperTrajectoryController.FindBody(
                    "Comet_Body"
                );
            }
            if (_comet != null)
            {
                if (_absorbedIds.Contains(_comet.GetInstanceID()))
                {
                    _comet = null;
                }
                else if (Vector3.Distance(
                        _comet.GetPosition(),
                        _giantsDeep.GetPosition()
                    ) <= 500f &&
                    (IsBodyOverlappingCore(_comet) ||
                     IsPointInsideCore(_comet.GetPosition(), 0f)))
                {
                    TryAbsorbOWBody(
                        _comet,
                        requireColliderOverlap: false
                    );
                }
            }
        }


        private void CheckPortalCarrierContainment()
        {
            if (!_armed || _giantsDeep == null || _areaTrigger == null ||
                !SceneSixController.IsActive ||
                SceneSixEndingController.IsEndingActive ||
                Time.unscaledTime < _nextPortalCarrierCheckTime)
            {
                return;
            }
            _nextPortalCarrierCheckTime = Time.unscaledTime + 0.1f;

            List<ReturnPortalEndpoint> activeEndpoints =
                ReturnPortalEndpoint.ActiveEndpoints;
            if (activeEndpoints.Count == 0)
            {
                return;
            }
            ReturnPortalEndpoint[] snapshot =
                activeEndpoints.ToArray();
            foreach (ReturnPortalEndpoint endpoint in snapshot)
            {
                if (endpoint == null || endpoint.Body == null)
                {
                    continue;
                }
                if (!endpoint.Launched)
                {
                    continue;
                }
                OWRigidbody body = endpoint.Body;
                int id = body.GetInstanceID();
                if (_absorbedIds.Contains(id) ||
                    _initialOccupants.Contains(id))
                {
                    continue;
                }

                Vector3 position = body.GetPosition();
                bool insideDoor = IsPointInsidePortalContainment(position);
                bool insideCore = IsPointInsideCore(position, 0f) ||
                    IsBodyOverlappingCore(body);
                if (!insideDoor && !insideCore)
                {
                    continue;
                }

                _mod?.ModHelper.Console.WriteLine(
                    "[RETURN BUILD111 CARRIER CONTAINMENT] type=" +
                    endpoint.PortalType + "; body=" + body.name +
                    "; position=" + position +
                    "; insideDoor=" + insideDoor +
                    "; insideCore=" + insideCore + ".",
                    MessageType.Success
                );
                TryAbsorbOWBody(
                    body,
                    requireColliderOverlap: false,
                    forceContainment: true
                );
            }
        }

        private bool IsPointInsidePortalContainment(Vector3 worldPoint)
        {
            if (_areaTrigger == null)
            {
                return false;
            }
            Vector3 local = _areaTrigger.transform.InverseTransformPoint(
                worldPoint
            );
            Vector3 half = PortalCarrierContainmentHalfExtents;
            return Mathf.Abs(local.x) <= half.x &&
                Mathf.Abs(local.y) <= half.y &&
                Mathf.Abs(local.z) <= half.z;
        }

        private bool IsPointInsideCore(Vector3 worldPoint, float padding)
        {
            if (_areaTrigger == null)
            {
                return false;
            }
            Vector3 local = _areaTrigger.transform.InverseTransformPoint(
                worldPoint
            );
            Vector3 half = CoreFallbackHalfExtents + Vector3.one * padding;
            return Mathf.Abs(local.x) <= half.x &&
                Mathf.Abs(local.y) <= half.y &&
                Mathf.Abs(local.z) <= half.z;
        }

        private bool IsBodyOverlappingCore(OWRigidbody body)
        {
            if (body == null || _areaTrigger == null)
            {
                return false;
            }
            int hitCount = Physics.OverlapBoxNonAlloc(
                _areaTrigger.transform.position,
                CoreFallbackHalfExtents,
                _overlapBuffer,
                _areaTrigger.transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide
            );
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapBuffer[i];
                _overlapBuffer[i] = null;
                if (hit == null)
                {
                    continue;
                }
                Rigidbody unityBody = hit.attachedRigidbody;
                OWRigidbody owBody = unityBody == null
                    ? hit.GetComponentInParent<OWRigidbody>()
                    : unityBody.GetComponentInParent<OWRigidbody>();
                if (owBody == body)
                {
                    return true;
                }
            }
            return false;
        }
        private void Update()
        {
            if (!_prisonBodyHidden)
            {
                if (Time.realtimeSinceStartup >= _prisonHideDeadline)
                {
                    _prisonBodyHidden = true;
                }
                else if (Time.realtimeSinceStartup >= _nextPrisonLookupTime)
                {
                    _nextPrisonLookupTime =
                        Time.realtimeSinceStartup + 0.25f;
                    Transform prisonBody = FindLiveTransform(PrisonBodyName);
                    if (prisonBody != null)
                    {
                        prisonBody.gameObject.SetActive(false);
                        _prisonBodyHidden = true;
                        _mod?.ModHelper.Console.WriteLine(
                            "[RETURN BUILD111 SIMPLE PRISON] Playable Bramble " +
                            "dimension body was hidden late.",
                            MessageType.Warning
                        );
                    }
                }
            }

            CheckCometFallback();
            CheckPortalCarrierContainment();

            if (_absorbedPlayer == null)
            {
                return;
            }

            if (!IsPointInsideCore(
                    _absorbedPlayer.Body.GetPosition(),
                    CoreFallbackPadding
                ) && !IsBodyOverlappingCore(_absorbedPlayer.Body))
            {
                RestorePlayerAfterExternalRevival();
                return;
            }

            if (_revivalInProgress ||
                !SceneSixWarpCoreToolController.IsReturnWarpCoreHeld() ||
                !OWInput.IsNewlyPressed(
                    InputLibrary.toolOptionDown,
                    InputMode.All
                ))
            {
                return;
            }

            InputLibrary.toolOptionDown.ConsumeInput();
            StartCoroutine(ReviveAbsorbedPlayer());
        }

        private void LateUpdate()
        {
            if (_giantsDeep == null)
            {
                return;
            }

            for (int i = _absorbedBodies.Count - 1; i >= 0; i--)
            {
                AbsorbedBody prisoner = _absorbedBodies[i];
                if (prisoner.Body == null)
                {
                    _absorbedBodies.RemoveAt(i);
                    continue;
                }
                prisoner.Body.transform.localPosition = Vector3.zero;
                prisoner.Body.transform.localRotation = Quaternion.identity;
            }

            for (int i = _absorbedUnityBodies.Count - 1; i >= 0; i--)
            {
                AbsorbedUnityBody prisoner = _absorbedUnityBodies[i];
                if (prisoner.Body == null)
                {
                    _absorbedUnityBodies.RemoveAt(i);
                    continue;
                }
                prisoner.Body.transform.localPosition = Vector3.zero;
                prisoner.Body.transform.localRotation = Quaternion.identity;
            }

            if (_absorbedPlayer != null &&
                (IsPointInsideCore(
                    _absorbedPlayer.Body.GetPosition(),
                    CoreFallbackPadding
                ) || IsBodyOverlappingCore(_absorbedPlayer.Body)))
            {
                _absorbedPlayer.Body.WarpToPositionRotation(
                    _giantsDeep.GetPosition(),
                    _giantsDeep.GetRotation()
                );
                _absorbedPlayer.Body.SetVelocity(
                    _giantsDeep.GetVelocity()
                );
                _absorbedPlayer.Body.SetAngularVelocity(
                    _giantsDeep.GetAngularVelocity()
                );
            }
        }

        private void CaptureInitialOccupants()
        {
            int hitCount = Physics.OverlapBoxNonAlloc(
                _areaTrigger.transform.position,
                CoreFallbackHalfExtents,
                _overlapBuffer,
                _areaTrigger.transform.rotation,
                ~0,
                QueryTriggerInteraction.Collide
            );
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = _overlapBuffer[i];
                _overlapBuffer[i] = null;
                if (hit == null)
                {
                    continue;
                }
                Rigidbody unityBody = hit.attachedRigidbody;
                OWRigidbody owBody = unityBody == null
                    ? hit.GetComponentInParent<OWRigidbody>()
                    : unityBody.GetComponentInParent<OWRigidbody>();
                if (owBody != null)
                {
                    _initialOccupants.Add(owBody.GetInstanceID());
                }
                else if (unityBody != null)
                {
                    _initialOccupants.Add(unityBody.GetInstanceID());
                }
            }
        }

        private void TryAbsorbOWBody(
            OWRigidbody body,
            bool requireColliderOverlap,
            bool forceContainment = false
        )
        {
            if (!_armed || body == null || _giantsDeep == null ||
                body == _giantsDeep)
            {
                return;
            }

            int id = body.GetInstanceID();
            if (_absorbedIds.Contains(id) ||
                _initialOccupants.Contains(id) ||
                IsNativeGiantDeepObject(body.transform))
            {
                return;
            }

            if (!forceContainment &&
                !requireColliderOverlap &&
                !IsBodyOverlappingCore(body) &&
                !IsPointInsideCore(body.GetPosition(), 0f))
            {
                return;
            }

            OWRigidbody playerBody = Locator.GetPlayerBody();
            if (body == playerBody || body.CompareTag("Player"))
            {
                AbsorbPlayer(body);
                return;
            }

            _absorbedIds.Add(id);
            float transferredMass = Mathf.Max(body.GetMass(), 0f);
            AddMassToGiantDeep(transferredMass);
            body.SetMass(MinimumBodyMass);
            body.DisableCollisionDetection();
            HidePhysicalObject(body.gameObject);

            body.WarpToPositionRotation(
                _giantsDeep.GetPosition(),
                _giantsDeep.GetRotation()
            );
            body.SetVelocity(_giantsDeep.GetVelocity());
            body.SetAngularVelocity(_giantsDeep.GetAngularVelocity());
            if (body.IsSuspended())
            {
                body.ChangeSuspensionBody(_giantsDeep);
            }
            else
            {
                body.Suspend(_giantsDeep);
            }
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            _absorbedBodies.Add(
                new AbsorbedBody(body, transferredMass)
            );

            AstroObject astroObject = body.GetComponent<AstroObject>();
            bool isComet = astroObject != null &&
                astroObject.GetAstroObjectName() == AstroObject.Name.Comet;
            _mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD111 ABSORBED] body=" + body.name +
                "; massAddedToGiantsDeep=" +
                transferredMass.ToString("G9") +
                "; markerAtCore=True; rendered=False; comet=" +
                isComet + ".",
                MessageType.Success
            );

            if (isComet && !_victoryTriggered)
            {
                _victoryTriggered = true;
                SceneSixEndingController.BeginVictoryEnding();
            }
        }

        private void TryAbsorbUnityBody(Rigidbody body)
        {
            if (!_armed || body == null || _giantsDeep == null)
            {
                return;
            }
            int id = body.GetInstanceID();
            if (_absorbedIds.Contains(id) ||
                _initialOccupants.Contains(id) ||
                IsNativeGiantDeepObject(body.transform))
            {
                return;
            }

            _absorbedIds.Add(id);
            float transferredMass = Mathf.Max(body.mass, 0f);
            AddMassToGiantDeep(transferredMass);
            body.mass = MinimumBodyMass;
            body.detectCollisions = false;
            body.isKinematic = true;
            HidePhysicalObject(body.gameObject);
            body.transform.SetParent(_giantsDeep.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            _absorbedUnityBodies.Add(
                new AbsorbedUnityBody(body, transferredMass)
            );

            _mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD111 ABSORBED] rigidbody=" + body.name +
                "; massAddedToGiantsDeep=" +
                transferredMass.ToString("G9") +
                "; markerAtCore=True; rendered=False.",
                MessageType.Success
            );
        }

        private void AbsorbPlayer(OWRigidbody body)
        {
            if (_absorbedPlayer != null)
            {
                return;
            }

            float transferredMass = Mathf.Max(body.GetMass(), 0f);
            _absorbedIds.Add(body.GetInstanceID());
            AddMassToGiantDeep(transferredMass);
            _absorbedPlayer = new AbsorbedPlayer(body, transferredMass);
            _absorbedPlayer.Hide();

            body.SetMass(MinimumBodyMass);
            body.DisableCollisionDetection();
            body.MakeKinematic();
            body.WarpToPositionRotation(
                _giantsDeep.GetPosition(),
                _giantsDeep.GetRotation()
            );
            body.SetVelocity(_giantsDeep.GetVelocity());
            body.SetAngularVelocity(_giantsDeep.GetAngularVelocity());
            SceneSixEndingController.MarkPlayerPortalTransit();

            _mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD111 ABSORBED] player=True; " +
                "revivalStillAvailable=True; massAddedToGiantsDeep=" +
                transferredMass.ToString("G9") + ".",
                MessageType.Success
            );
        }

        private IEnumerator ReviveAbsorbedPlayer()
        {
            _revivalInProgress = true;
            OWRigidbody brittleHollow = null;
            AbsorbedPlayer player = null;
            bool revived = false;
            try
            {
                brittleHollow =
                    InterloperTrajectoryController.FindBody(
                        "BrittleHollow_Body"
                    );
                if (brittleHollow == null || _absorbedPlayer == null)
                {
                    throw new InvalidOperationException(
                        "Brittle Hollow or absorbed player was unavailable."
                    );
                }

                player = _absorbedPlayer;
                Vector3 position =
                    brittleHollow.transform.TransformPoint(
                        RevivalLocalPosition
                    );
                Quaternion rotation =
                    brittleHollow.GetRotation() * RevivalLocalRotation;
                ReturnPortalPlayerDetachment.DetachFromPlayerBeforeRevive();
                player.Body.WarpToPositionRotation(position, rotation);
                RestorePlayerState(player);
                player.Body.SetVelocity(
                    brittleHollow.GetPointVelocity(position)
                );
                player.Body.SetAngularVelocity(
                    brittleHollow.GetAngularVelocity()
                );

                PlayerLockOnTargeting lockOn =
                    Locator.GetPlayerTransform()
                        .GetComponent<PlayerLockOnTargeting>();
                lockOn?.BreakLock();
                SceneSixEndingController.ClearPlayerPortalTransit();
                Physics.SyncTransforms();
                if (!OWTime.IsPaused() && !PlayerState.InConversation())
                {
                    OWInput.ChangeInputMode(InputMode.Character);
                }
                PostRevivalNotification();
                revived = true;

                _mod.ModHelper.Console.WriteLine(
                    "[RETURN BUILD111 REVIVE] Player returned from " +
                    "Area A to the Brittle Hollow checkpoint without " +
                    "resetting loop time.",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN BUILD111 REVIVE] Failed: " + exception,
                    MessageType.Error
                );
            }

            if (revived && player != null && brittleHollow != null)
            {
                yield return new WaitForFixedUpdate();
                if (player.Body != null)
                {
                    Vector3 checkpointPosition =
                        player.Body.GetPosition();
                    player.Body.SetVelocity(
                        brittleHollow.GetPointVelocity(checkpointPosition)
                    );
                    player.Body.SetAngularVelocity(
                        brittleHollow.GetAngularVelocity()
                    );
                }
            }

            yield return null;
            _revivalInProgress = false;
        }

        private void RestorePlayerAfterExternalRevival()
        {
            if (_absorbedPlayer == null)
            {
                return;
            }
            RestorePlayerState(_absorbedPlayer);
            SceneSixEndingController.ClearPlayerPortalTransit();
            _mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD111 REVIVE] External revival detected; " +
                "player rendering, collision and mass were restored.",
                MessageType.Success
            );
        }

        private void RestorePlayerState(AbsorbedPlayer player)
        {
            _absorbedPlayer = null;
            _absorbedIds.Remove(player.Body.GetInstanceID());
            RemoveMassFromGiantDeep(player.TransferredMass);
            player.Body.SetMass(player.TransferredMass);
            player.Body.MakeNonKinematic();
            player.Body.EnableCollisionDetection();
            player.Restore();
        }

        private void AddMassToGiantDeep(float mass)
        {
            if (_giantsDeep != null && mass > 0f)
            {
                _giantsDeep.SetMass(_giantsDeep.GetMass() + mass);
            }
        }

        private void RemoveMassFromGiantDeep(float mass)
        {
            if (_giantsDeep != null && mass > 0f)
            {
                _giantsDeep.SetMass(
                    Mathf.Max(
                        MinimumBodyMass,
                        _giantsDeep.GetMass() - mass
                    )
                );
            }
        }

        private bool IsNativeGiantDeepObject(Transform candidate)
        {
            return candidate == null ||
                candidate == _giantsDeep.transform ||
                candidate.IsChildOf(_giantsDeep.transform);
        }

        private static void HidePhysicalObject(GameObject root)
        {
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
            foreach (ParticleSystem particles in
                root.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear
                );
            }
            foreach (AudioSource audio in
                root.GetComponentsInChildren<AudioSource>(true))
            {
                audio.Stop();
                audio.enabled = false;
            }
        }

        private void PostRevivalNotification()
        {
            string text = "$RETURN_PORTAL_REVIVED";
            if (_mod.NewHorizons != null)
            {
                string translated =
                    _mod.NewHorizons.GetTranslationForUI(text);
                if (!string.IsNullOrEmpty(translated))
                {
                    text = translated;
                }
            }
            NotificationManager.SharedInstance?.PostNotification(
                new NotificationData(
                    NotificationTarget.All,
                    text,
                    3f
                )
            );
        }

        private int RemoveCoreTerrain()
        {
            int removed = 0;
            foreach (Transform candidate in
                _coreSector.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == null ||
                    Array.IndexOf(CoreTerrainNames, candidate.name) < 0)
                {
                    continue;
                }
                candidate.gameObject.SetActive(false);
                Destroy(candidate.gameObject);
                removed++;
            }
            return removed;
        }

        private static Transform FindDescendant(
            Transform root,
            string objectName
        )
        {
            if (root == null)
            {
                return null;
            }
            foreach (Transform candidate in
                root.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null && candidate.name == objectName)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static Transform FindLiveTransform(string objectName)
        {
            foreach (Transform candidate in
                Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.name == objectName)
                {
                    return candidate;
                }
            }
            return null;
        }

        private sealed class AbsorbedBody
        {
            internal readonly OWRigidbody Body;
            internal readonly float TransferredMass;

            internal AbsorbedBody(OWRigidbody body, float mass)
            {
                Body = body;
                TransferredMass = mass;
            }
        }

        private sealed class AbsorbedUnityBody
        {
            internal readonly Rigidbody Body;
            internal readonly float TransferredMass;

            internal AbsorbedUnityBody(Rigidbody body, float mass)
            {
                Body = body;
                TransferredMass = mass;
            }
        }

        private sealed class AbsorbedPlayer
        {
            private readonly Dictionary<Renderer, bool> _renderers =
                new Dictionary<Renderer, bool>();
            private readonly Dictionary<Collider, bool> _colliders =
                new Dictionary<Collider, bool>();

            internal readonly OWRigidbody Body;
            internal readonly float TransferredMass;

            internal AbsorbedPlayer(OWRigidbody body, float mass)
            {
                Body = body;
                TransferredMass = mass;
                foreach (Renderer renderer in
                    body.GetComponentsInChildren<Renderer>(true))
                {
                    _renderers[renderer] = renderer.enabled;
                }
                foreach (Collider collider in
                    body.GetComponentsInChildren<Collider>(true))
                {
                    _colliders[collider] = collider.enabled;
                }
            }

            internal void Hide()
            {
                foreach (Renderer renderer in _renderers.Keys)
                {
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                }
                foreach (Collider collider in _colliders.Keys)
                {
                    if (collider != null)
                    {
                        collider.enabled = false;
                    }
                }
            }

            internal void Restore()
            {
                foreach (KeyValuePair<Renderer, bool> pair in _renderers)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.enabled = pair.Value;
                    }
                }
                foreach (KeyValuePair<Collider, bool> pair in _colliders)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.enabled = pair.Value;
                    }
                }
            }
        }
    }

    internal sealed class Build111AbsorptionTriggerRelay : MonoBehaviour
    {
        private Build111SimpleCorePrisonController _controller;

        internal void Initialize(
            Build111SimpleCorePrisonController controller
        )
        {
            _controller = controller;
        }

        private void OnTriggerEnter(Collider hit)
        {
            _controller?.OnAreaTriggerEnter(hit);
        }
    }

    /// <summary>
    /// The old Build109 destination shortcut is disabled. Victory now occurs
    /// only when Area A actually absorbs the Interloper.
    /// </summary>
    [HarmonyPatch(
        typeof(ReturnPortalTransportVolume),
        "IsDestinationGiantDeepCore"
    )]
    internal static class Build111DisableLegacyCoreVictoryPatch
    {
        private static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(ReturnPortalTransportVolume),
        "TryTransportOWRigidbody"
    )]
    internal static class Build111ImmediateCoreAbsorptionPatch
    {
        private static void Postfix(OWRigidbody body)
        {
            Build111SimpleCorePrisonController
                .TryAbsorbTransportedBody(body);
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class Build111SimpleCorePrisonPatch
    {
        private static void Postfix(
            ReturnMod __instance,
            OWScene newScene
        )
        {
            if (newScene != OWScene.SolarSystem)
            {
                return;
            }

            Build111SimpleCorePrisonController controller =
                __instance.GetComponent<
                    Build111SimpleCorePrisonController>();
            if (controller == null)
            {
                controller = __instance.gameObject.AddComponent<
                    Build111SimpleCorePrisonController>();
            }
            controller.Initialize(__instance);
        }
    }
}
