using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Return
{
    internal enum ReturnPortalType
    {
        Black,
        White
    }

    /// <summary>
    /// Identifies the independently launched physics carrier. A later build
    /// can attach the actual singularity visuals and transfer volume here
    /// without replacing the launch/flight implementation.
    /// </summary>
    internal sealed class ReturnPortalEndpoint : MonoBehaviour
    {
        private static ReturnPortalEndpoint _blackEndpoint;
        private static ReturnPortalEndpoint _whiteEndpoint;
        internal static readonly List<ReturnPortalEndpoint> ActiveEndpoints =
            new List<ReturnPortalEndpoint>();

        public ReturnPortalType PortalType { get; private set; }
        public bool Launched { get; set; }

        public OWRigidbody Body { get; private set; }

        public void Initialize(ReturnPortalType portalType)
        {
            PortalType = portalType;
            Launched = false;
            Body = GetComponent<OWRigidbody>();
            if (!ActiveEndpoints.Contains(this))
            {
                ActiveEndpoints.Add(this);
            }
            if (portalType == ReturnPortalType.Black)
            {
                _blackEndpoint = this;
            }
            else
            {
                _whiteEndpoint = this;
            }
        }

        public ReturnPortalEndpoint GetCounterpart()
        {
            ReturnPortalEndpoint counterpart =
                PortalType == ReturnPortalType.Black
                    ? _whiteEndpoint
                    : _blackEndpoint;
            return counterpart != null && counterpart.isActiveAndEnabled
                ? counterpart
                : null;
        }

        private void OnDestroy()
        {
            ActiveEndpoints.Remove(this);
            if (_blackEndpoint == this)
            {
                _blackEndpoint = null;
            }
            if (_whiteEndpoint == this)
            {
                _whiteEndpoint = null;
            }
        }
    }

    /// <summary>
    /// A black-to-white moving portal volume. Entry data is transformed from
    /// the source carrier's moving frame into the destination carrier's moving
    /// frame, preserving position, orientation, linear velocity and angular
    /// velocity relative to the two endpoints.
    /// </summary>
    internal sealed class ReturnPortalTransportVolume : MonoBehaviour
    {
        private const float ReentryLockSeconds = 0.4f;
        private static readonly Dictionary<int, float> ReentryLocks =
            new Dictionary<int, float>();

        private ReturnMod _mod;
        private ReturnPortalEndpoint _endpoint;
        private SphereCollider _trigger;
        private OWCollider _owCollider;
        private bool _armed;

        public void Initialize(
            ReturnMod mod,
            ReturnPortalEndpoint endpoint
        )
        {
            _mod = mod;
            _endpoint = endpoint;
            _trigger = GetComponent<SphereCollider>();
            _owCollider = GetComponent<OWCollider>();
            _armed = false;
            if (_owCollider != null)
            {
                _owCollider.SetActivation(false);
            }
            else if (_trigger != null)
            {
                _trigger.enabled = false;
            }
            StartCoroutine(ArmAfterLaunchClearance());
        }

        private IEnumerator ArmAfterLaunchClearance()
        {
            // The carrier begins at the player camera. Do not let the second
            // endpoint immediately warp its launcher into the first endpoint.
            float timeout = Time.time + 2f;
            OWRigidbody playerBody = Locator.GetPlayerBody();
            while (_endpoint != null &&
                playerBody != null &&
                Time.time < timeout &&
                Vector3.Distance(
                    _endpoint.transform.position,
                    playerBody.GetPosition()
                ) < 3f)
            {
                yield return new WaitForFixedUpdate();
            }

            if (_endpoint == null || _trigger == null)
            {
                yield break;
            }

            if (Build111SimpleCorePrisonController.IsBodyAbsorbed(
                _endpoint.Body))
            {
                yield break;
            }

            if (_owCollider != null)
            {
                _owCollider.SetActivation(true);
            }
            else
            {
                _trigger.enabled = true;
            }
            _armed = true;
            _mod?.ModHelper.Console.WriteLine(
                "[RETURN PORTAL VOLUME ARMED] type=" +
                _endpoint.PortalType + ".",
                MessageType.Success
            );
        }

        private void OnTriggerEnter(Collider hitCollider)
        {
            if (!_armed || _endpoint == null || hitCollider == null)
            {
                return;
            }
            if (_endpoint.PortalType != ReturnPortalType.Black)
            {
                return;
            }

            Rigidbody unityBody = hitCollider.attachedRigidbody;
            if (unityBody == null)
            {
                return;
            }

            ReturnPortalEndpoint hitEndpoint =
                unityBody.GetComponent<ReturnPortalEndpoint>();
            if (hitEndpoint != null)
            {
                return;
            }

            ReturnPortalEndpoint destination =
                _endpoint.GetCounterpart();
            if (destination == null || destination.Body == null)
            {
                return;
            }

            OWRigidbody body = unityBody.GetComponent<OWRigidbody>();
            if (body != null)
            {
                if (!CanTransportAstroObject(body))
                {
                    return;
                }
                TryTransportOWRigidbody(body, destination);
            }
            else if (!unityBody.isKinematic)
            {
                TryTransportUnityRigidbody(unityBody, destination);
            }
        }

        private static bool CanTransportAstroObject(OWRigidbody body)
        {
            if (body == null)
            {
                return false;
            }
            AstroObject astroObject = body.GetComponent<AstroObject>();
            if (astroObject == null)
            {
                return true;
            }
            AstroObject.Name name = astroObject.GetAstroObjectName();
            return name != AstroObject.Name.Sun &&
                name != AstroObject.Name.GiantsDeep;
        }

        private void TryTransportOWRigidbody(
            OWRigidbody body,
            ReturnPortalEndpoint destination
        )
        {
            int bodyId = body.GetInstanceID();
            if (IsReentryLocked(bodyId))
            {
                return;
            }
            LockReentry(bodyId);

            Transform sourceTransform = _endpoint.transform;
            Transform destinationTransform = destination.transform;
            OWRigidbody sourceBody = _endpoint.Body;
            OWRigidbody destinationBody = destination.Body;

            Vector3 localPosition =
                sourceTransform.InverseTransformPoint(body.GetPosition());
            Quaternion localRotation =
                Quaternion.Inverse(sourceTransform.rotation) *
                body.GetRotation();
            Vector3 localRelativeVelocity =
                sourceTransform.InverseTransformDirection(
                    body.GetVelocity() -
                    sourceBody.GetPointVelocity(body.GetPosition())
                );
            Vector3 localRelativeAngularVelocity =
                sourceTransform.InverseTransformDirection(
                    body.GetAngularVelocity() -
                    sourceBody.GetAngularVelocity()
                );

            Vector3 destinationPosition =
                destinationTransform.TransformPoint(localPosition);
            Quaternion destinationRotation =
                destinationTransform.rotation * localRotation;
            Vector3 destinationVelocity =
                destinationBody.GetPointVelocity(destinationPosition) +
                destinationTransform.TransformDirection(
                    localRelativeVelocity
                );
            Vector3 destinationAngularVelocity =
                destinationBody.GetAngularVelocity() +
                destinationTransform.TransformDirection(
                    localRelativeAngularVelocity
                );

            body.WarpToPositionRotation(
                destinationPosition,
                destinationRotation
            );
            body.SetVelocity(destinationVelocity);
            body.SetAngularVelocity(destinationAngularVelocity);
            if (body.CompareTag("Player"))
            {
                SceneSixEndingController.MarkPlayerPortalTransit();
            }
            NotifyPlayerWarpIfNeeded(body);

            if (!Physics.autoSyncTransforms)
            {
                Physics.SyncTransforms();
            }

            _mod.ModHelper.Console.WriteLine(
                "[RETURN PORTAL TRANSIT] body=" + body.name +
                "; from=" + _endpoint.PortalType +
                "; to=" + destination.PortalType +
                "; relativeSpeed=" +
                localRelativeVelocity.magnitude.ToString("F2") + ".",
                MessageType.Success
            );

            AstroObject astroObject = body.GetComponent<AstroObject>();
            if (astroObject != null &&
                astroObject.GetAstroObjectName() ==
                    AstroObject.Name.Comet &&
                IsDestinationGiantDeepCore(destination))
            {
                SceneSixEndingController.BeginVictoryEnding();
            }
        }

        private static bool IsDestinationGiantDeepCore(
            ReturnPortalEndpoint destination
        )
        {
            SurveyorProbe probe =
                destination.GetComponent<SurveyorProbe>();
            SectorDetector detector =
                probe == null ? null : probe.GetSectorDetector();
            if (detector != null && detector.IsWithinSector("GDCore"))
            {
                return true;
            }

            OWRigidbody giantsDeep =
                InterloperTrajectoryController.FindBody(
                    "GiantsDeep_Body"
                );
            return giantsDeep != null &&
                Vector3.Distance(
                    destination.transform.position,
                    giantsDeep.GetPosition()
                ) < 220f;
        }

        private void TryTransportUnityRigidbody(
            Rigidbody body,
            ReturnPortalEndpoint destination
        )
        {
            int bodyId = body.GetInstanceID();
            if (IsReentryLocked(bodyId))
            {
                return;
            }
            LockReentry(bodyId);

            Transform sourceTransform = _endpoint.transform;
            Transform destinationTransform = destination.transform;
            OWRigidbody sourceBody = _endpoint.Body;
            OWRigidbody destinationBody = destination.Body;

            Vector3 localPosition =
                sourceTransform.InverseTransformPoint(body.position);
            Quaternion localRotation =
                Quaternion.Inverse(sourceTransform.rotation) *
                body.rotation;
            Vector3 localRelativeVelocity =
                sourceTransform.InverseTransformDirection(
                    body.velocity -
                    sourceBody.GetPointVelocity(body.position)
                );
            Vector3 localRelativeAngularVelocity =
                sourceTransform.InverseTransformDirection(
                    body.angularVelocity -
                    sourceBody.GetAngularVelocity()
                );
            Vector3 destinationPosition =
                destinationTransform.TransformPoint(localPosition);

            body.position = destinationPosition;
            body.rotation = destinationTransform.rotation * localRotation;
            body.velocity =
                destinationBody.GetPointVelocity(destinationPosition) +
                destinationTransform.TransformDirection(
                    localRelativeVelocity
                );
            body.angularVelocity =
                destinationBody.GetAngularVelocity() +
                destinationTransform.TransformDirection(
                    localRelativeAngularVelocity
                );
            if (!Physics.autoSyncTransforms)
            {
                Physics.SyncTransforms();
            }
        }

        private static bool IsReentryLocked(int bodyId)
        {
            float lockUntil;
            return ReentryLocks.TryGetValue(bodyId, out lockUntil) &&
                Time.time < lockUntil;
        }

        private static void LockReentry(int bodyId)
        {
            ReentryLocks[bodyId] = Time.time + ReentryLockSeconds;
        }

        private static void NotifyPlayerWarpIfNeeded(OWRigidbody body)
        {
            if (body.CompareTag("Player"))
            {
                GlobalMessenger.FireEvent("WarpPlayer");
                GlobalMessenger.FireEvent("PlayerRepositioned");
            }
            else if (body.CompareTag("Ship") &&
                (PlayerState.IsInsideShip() ||
                    PlayerState.UsingShipComputer() ||
                    PlayerState.AtFlightConsole()))
            {
                GlobalMessenger.FireEvent("PlayerRepositioned");
            }
        }
    }

    /// <summary>
    /// Waits until the cloned vanilla SingularityController has completed its
    /// own Start pass before expanding it. The source models are serialized in
    /// their collapsed state, and their Start method disables the controller.
    /// </summary>
    internal sealed class ReturnPortalVisualActivator : MonoBehaviour
    {
        private ReturnMod _mod;
        private ReturnPortalType _portalType;
        private SingularityController _singularity;

        public void Initialize(
            ReturnMod mod,
            ReturnPortalType portalType,
            SingularityController singularity
        )
        {
            _mod = mod;
            _portalType = portalType;
            _singularity = singularity;
        }

        private IEnumerator Start()
        {
            // The copied controller is initially disabled, so its own Start
            // has not run yet. Enable it first, let Start apply the serialized
            // collapsed state, then re-enable and expand it on the next frame.
            yield return null;

            if (_singularity == null)
            {
                yield break;
            }

            _singularity.enabled = true;
            yield return null;

            if (_singularity == null)
            {
                yield break;
            }

            OWRenderer owRenderer =
                _singularity.GetComponent<OWRenderer>();
            if (owRenderer != null)
            {
                owRenderer.SetLODActivation(true);
            }
            _singularity.enabled = true;
            _singularity.Create();

            yield return new WaitForSeconds(0.75f);

            Renderer renderer =
                _singularity.GetComponent<Renderer>();
            if (owRenderer != null)
            {
                owRenderer.SetActivation(true);
                float radius =
                    owRenderer.sharedMaterial.GetFloat("_Radius");
                owRenderer.SetMaterialProperty(
                    Shader.PropertyToID("_Radius"),
                    Mathf.Max(0.2f, radius)
                );
            }
            if (renderer != null)
            {
                renderer.enabled = true;
            }
            _mod?.ModHelper.Console.WriteLine(
                "[RETURN PORTAL VISUAL READY] type=" + _portalType +
                "; state=" + _singularity.GetState() +
                "; rendererEnabled=" +
                (renderer != null && renderer.enabled) + ".",
                renderer != null && renderer.enabled
                    ? MessageType.Success
                    : MessageType.Warning
            );
        }
    }

    /// <summary>
    /// Reuses the stock scout marker but continuously replaces its localized
    /// label, including after a language change or marker reconstruction.
    /// </summary>
    internal sealed class ReturnPortalMarkerLabel : MonoBehaviour
    {
        private ReturnMod _mod;
        private ReturnPortalType _portalType;
        private ProbeHUDMarker _marker;
        private float _nextRefreshTime;

        public void Initialize(
            ReturnMod mod,
            ReturnPortalType portalType
        )
        {
            _mod = mod;
            _portalType = portalType;
            _marker = GetComponent<ProbeHUDMarker>();
            ApplyLabel();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }
            _nextRefreshTime = Time.unscaledTime + 0.5f;
            ApplyLabel();
        }

        private void ApplyLabel()
        {
            if (_marker == null || _mod == null)
            {
                return;
            }

            string key = _portalType == ReturnPortalType.Black
                ? "$RETURN_PORTAL_MARKER_BLACK"
                : "$RETURN_PORTAL_MARKER_WHITE";
            string label = _mod.NewHorizons == null
                ? key
                : _mod.NewHorizons.GetTranslationForUI(key);
            if (string.IsNullOrEmpty(label) || label == key)
            {
                label = key;
            }

            Traverse markerTraverse = Traverse.Create(_marker);
            markerTraverse.Field("_markerLabel").SetValue(label);
            CanvasMarker canvas = markerTraverse
                .Field("_canvasMarker")
                .GetValue<CanvasMarker>();
            if (canvas != null)
            {
                canvas.SetLabel(label);
            }
        }
    }

    /// <summary>
    /// Registers an endpoint with the stock solar-system map. The cloned
    /// scout's ProbeHUDMarker only owns the helmet/ship HUD marker; this
    /// separate marker is what makes the endpoint visible in map view.
    /// </summary>
    internal sealed class ReturnPortalMapMarker : MonoBehaviour
    {
        private ReturnPortalType _portalType;
        private MapMarkerManager _manager;
        private CanvasMapMarker _marker;
        private float _nextRefreshTime;

        public void Initialize(
            ReturnMod mod,
            ReturnPortalType portalType,
            OWRigidbody targetBody
        )
        {
            _portalType = portalType;
            MapController mapController = Locator.GetMapController();
            _manager = mapController == null
                ? null
                : mapController.GetMarkerManager();
            if (_manager == null || targetBody == null)
            {
                throw new InvalidOperationException(
                    "The map marker manager or portal body was unavailable."
                );
            }

            _marker = _manager.InstantiateNewMarker(true);
            _marker.SetLabel(GetLabel());
            _manager.RegisterMarker(_marker, targetBody);
            _marker.SetColor(
                portalType == ReturnPortalType.Black
                    ? new Color(0.72f, 0.42f, 1f, 1f)
                    : new Color(0.45f, 0.9f, 1f, 1f)
            );
            _marker.SetVisibility(true);
            _marker.OnMarkerDestroyed += OnMarkerDestroyed;
        }

        private void Update()
        {
            if (_marker == null || Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }
            _nextRefreshTime = Time.unscaledTime + 0.5f;
            _marker.SetLabel(GetLabel());
            if (!_marker.IsVisible())
            {
                _marker.SetVisibility(true);
            }
        }

        private string GetLabel()
        {
            return _portalType == ReturnPortalType.Black
                ? "$RETURN_PORTAL_MARKER_BLACK"
                : "$RETURN_PORTAL_MARKER_WHITE";
        }

        private void OnMarkerDestroyed(CanvasMapMarker marker)
        {
            if (_marker == marker)
            {
                _marker.OnMarkerDestroyed -= OnMarkerDestroyed;
                _marker = null;
            }
        }

        private void OnDestroy()
        {
            if (_marker == null)
            {
                return;
            }

            // Registration used the endpoint OWRigidbody, so the stock
            // CanvasMapMarker destroys and unregisters itself when that body
            // is destroyed. Only detach our callback here to avoid a second
            // unregister during Unity's component-destruction pass.
            _marker.OnMarkerDestroyed -= OnMarkerDestroyed;
            _marker = null;
        }
    }

    /// <summary>
    /// Adds portal-tool controls to only the Return advanced warp core. The
    /// stock ItemTool continues to own pickup, carrying, dropping and sockets.
    /// </summary>
    internal sealed class ReturnWarpCoreToolBehaviour : MonoBehaviour
    {
        private static readonly Vector3 RevivalLocalPosition =
            new Vector3(-224.8589f, 0.8171f, 92.6140f);

        private static readonly Quaternion RevivalLocalRotation =
            new Quaternion(
                -0.177269f,
                -0.096406f,
                -0.683298f,
                -0.701703f
            );

        private ReturnMod _mod;
        private WarpCoreItem _core;
        private bool _blackLaunched;
        private bool _whiteLaunched;
        private bool _launchInProgress;
        private bool _revivalInProgress;

        private ScreenPrompt _blackLaunchPrompt;
        private ScreenPrompt _whiteLaunchPrompt;
        private ScreenPrompt _revivePrompt;
        private bool _promptsRegistered;
        private bool _coreVisualShown = true;

        public void Initialize(ReturnMod mod, WarpCoreItem core)
        {
            RemovePrompts();
            _mod = mod;
            _core = core;
            _blackLaunched = false;
            _whiteLaunched = false;
            _launchInProgress = false;
            _revivalInProgress = false;
            InitializePrompts();
            RefreshPromptText(force: true);

            _mod.ModHelper.Console.WriteLine(
                "[RETURN WARP TOOL] Controls attached to " +
                core.gameObject.name +
                "; black=toolOptionLeft; white=toolOptionRight; " +
                "revive=toolOptionDown.",
                MessageType.Success
            );
        }

        private void InitializePrompts()
        {
            PromptManager promptManager = Locator.GetPromptManager();
            if (promptManager == null)
            {
                throw new InvalidOperationException(
                    "PromptManager was unavailable."
                );
            }

            _blackLaunchPrompt = new ScreenPrompt(
                InputLibrary.toolOptionLeft,
                Translate("$RETURN_PORTAL_LAUNCH_BLACK")
            );
            _whiteLaunchPrompt = new ScreenPrompt(
                InputLibrary.toolOptionRight,
                Translate("$RETURN_PORTAL_LAUNCH_WHITE")
            );
            _revivePrompt = new ScreenPrompt(
                InputLibrary.toolOptionDown,
                Translate("$RETURN_PORTAL_REVIVE_PROMPT")
            );

            SetPromptVisibility(false);
            promptManager.AddScreenPrompt(
                _blackLaunchPrompt,
                PromptPosition.UpperRight
            );
            promptManager.AddScreenPrompt(
                _whiteLaunchPrompt,
                PromptPosition.UpperRight
            );
            promptManager.AddScreenPrompt(
                _revivePrompt,
                PromptPosition.UpperRight
            );
            _promptsRegistered = true;
        }

        private void Update()
        {
            if (_mod == null || _core == null)
            {
                SetPromptVisibility(false);
                return;
            }

            UpdateCoreVisibilityForActiveTool();
            bool held = IsCoreHeld();
            bool controlsAllowed =
                held &&
                SceneSixController.IsActive &&
                LoadManager.GetCurrentScene() == OWScene.SolarSystem &&
                OWInput.IsInputMode(InputMode.Character) &&
                !OWTime.IsPaused() &&
                !PlayerState.InConversation();

            if (!controlsAllowed)
            {
                SetPromptVisibility(false);
                return;
            }

            SetPromptVisibility(true);

            bool launchBlack = OWInput.IsNewlyPressed(
                InputLibrary.toolOptionLeft
            );
            bool launchWhite = OWInput.IsNewlyPressed(
                InputLibrary.toolOptionRight
            );
            bool revive = OWInput.IsNewlyPressed(
                InputLibrary.toolOptionDown
            );

            if (launchBlack)
            {
                InputLibrary.toolOptionLeft.ConsumeInput();
                TryLaunchPortal(ReturnPortalType.Black);
            }
            else if (launchWhite)
            {
                InputLibrary.toolOptionRight.ConsumeInput();
                TryLaunchPortal(ReturnPortalType.White);
            }

            if (revive)
            {
                InputLibrary.toolOptionDown.ConsumeInput();
                if (!_revivalInProgress)
                {
                    StartCoroutine(RevivePlayer());
                }
            }
        }

        private void UpdateCoreVisibilityForActiveTool()
        {
            ToolModeSwapper swapper = Locator.GetToolModeSwapper();
            ItemTool itemTool =
                swapper == null ? null : swapper.GetItemCarryTool();
            bool carried =
                itemTool != null && itemTool.GetHeldItem() == _core;
            bool visible = !carried ||
                (swapper.GetToolMode() == ToolMode.Item &&
                    itemTool.IsEquipped());
            if (visible == _coreVisualShown)
            {
                return;
            }

            _coreVisualShown = visible;
            Traverse.Create(_core)
                .Method("SetVisible", visible)
                .GetValue();
        }

        private bool IsCoreHeld()
        {
            ToolModeSwapper swapper = Locator.GetToolModeSwapper();
            ItemTool itemTool =
                swapper == null ? null : swapper.GetItemCarryTool();
            return swapper != null &&
                swapper.GetToolMode() == ToolMode.Item &&
                itemTool != null &&
                itemTool.IsEquipped() &&
                itemTool.GetHeldItem() == _core;
        }

        private void TryLaunchPortal(ReturnPortalType portalType)
        {
            if (_launchInProgress)
            {
                return;
            }
            if (HasLaunched(portalType))
            {
                PostNotification(
                    portalType == ReturnPortalType.Black
                        ? "$RETURN_PORTAL_ALREADY_BLACK"
                        : "$RETURN_PORTAL_ALREADY_WHITE"
                );
                return;
            }
            StartCoroutine(LaunchPortal(portalType));
        }

        /// <summary>
        /// Restores the stock scout launch state on a cloned carrier. A clone
        /// made from a currently-launched scout copies an already-enabled
        /// collider, which makes ProbeAnchor skip ActivateCollider() and its
        /// "IgnoreProbeCollider" broadcast; that leaves the carrier able to
        /// physically hit and kill the player. Disable the collider and age
        /// the launch time so the vanilla grace period and broadcast run.
        /// </summary>
        private static void DisableStockProbeMapMarker(GameObject carrier)
        {
            if (carrier == null)
            {
                return;
            }
            foreach (MapMarker marker in
                carrier.GetComponentsInChildren<MapMarker>(true))
            {
                if (marker == null)
                {
                    continue;
                }
                try
                {
                    Traverse markerTraverse = Traverse.Create(marker);
                    markerTraverse.Field("_disableMapMarker").SetValue(true);
                    CanvasMapMarker canvasMarker = markerTraverse
                        .Field("_canvasMarker")
                        .GetValue<CanvasMapMarker>();
                    if (canvasMarker != null)
                    {
                        MapController mapController =
                            Locator.GetMapController();
                        MapMarkerManager manager = mapController == null
                            ? null
                            : mapController.GetMarkerManager();
                        manager?.UnregisterMarker(canvasMarker);
                        UnityEngine.Object.Destroy(canvasMarker);
                    }
                    marker.enabled = false;
                }
                catch (Exception exception)
                {
                    ReturnMod.Instance?.ModHelper.Console.WriteLine(
                        "[RETURN PORTAL MARKER] Could not disable the " +
                        "stock scout map marker: " + exception.Message,
                        MessageType.Warning
                    );
                }
            }
        }

        private void PreparePortalCarrierForLaunch(GameObject carrier)
        {
            ProbeAnchor anchor =
                carrier.GetComponentInChildren<ProbeAnchor>(true);
            if (anchor == null)
            {
                return;
            }

            Collider collider = anchor.GetCollider();
            if (collider != null)
            {
                collider.enabled = false;
            }

            Traverse.Create(anchor)
                .Field("_launchTime")
                .SetValue(float.NegativeInfinity);
        }

        /// <summary>
        /// Makes every physical collider on the carrier ignore the player's
        /// colliders, then broadcasts the stock probe-ignore event so other
        /// listeners (such as the ship) also ignore the cloned carrier.
        /// </summary>
        private void IgnoreCarrierCollisionsWithPlayer(
            GameObject carrier,
            OWRigidbody playerBody
        )
        {
            if (carrier == null || playerBody == null)
            {
                return;
            }

            Collider[] carrierColliders =
                carrier.GetComponentsInChildren<Collider>(true);
            Collider[] playerColliders =
                playerBody.GetComponentsInChildren<Collider>(true);
            int ignoredPairs = 0;
            foreach (Collider carrierCollider in carrierColliders)
            {
                if (carrierCollider == null || carrierCollider.isTrigger)
                {
                    continue;
                }
                foreach (Collider playerCollider in playerColliders)
                {
                    if (playerCollider == null)
                    {
                        continue;
                    }
                    Physics.IgnoreCollision(
                        carrierCollider,
                        playerCollider,
                        true
                    );
                    ignoredPairs++;
                }
            }

            ProbeAnchor anchor =
                carrier.GetComponentInChildren<ProbeAnchor>(true);
            Collider anchorCollider =
                anchor == null ? null : anchor.GetCollider();
            if (anchorCollider != null)
            {
                try
                {
                    GlobalMessenger<Collider>.FireEvent(
                        "IgnoreProbeCollider",
                        anchorCollider
                    );
                }
                catch (Exception exception)
                {
                    _mod?.ModHelper.Console.WriteLine(
                        "[RETURN PORTAL LAUNCH] The probe-ignore broadcast " +
                        "failed for the cloned carrier: " + exception.Message,
                        MessageType.Warning
                    );
                }
            }

            if (ignoredPairs > 0)
            {
                _mod?.ModHelper.Console.WriteLine(
                    "[RETURN PORTAL LAUNCH] Carrier colliders ignore the " +
                    "player; pairs=" + ignoredPairs + ".",
                    MessageType.Success
                );
            }
        }

        private IEnumerator LaunchPortal(ReturnPortalType portalType)
        {
            _launchInProgress = true;
            GameObject clone = null;
            Transform launchTransform = null;
            OWRigidbody playerBody = null;
            Exception setupException = null;
            try
            {
                SurveyorProbe template = Locator.GetProbe();
                OWCamera playerCamera = Locator.GetPlayerCamera();
                playerBody = Locator.GetPlayerBody();
                if (template == null ||
                    playerCamera == null ||
                    playerBody == null)
                {
                    throw new InvalidOperationException(
                        "The stock scout launch references were unavailable."
                    );
                }

                launchTransform = playerCamera.transform;
                clone = UnityEngine.Object.Instantiate(
                    template.gameObject
                );
                clone.name = portalType == ReturnPortalType.Black
                    ? "Return_BlackPortalCarrier"
                    : "Return_WhitePortalCarrier";
                clone.SetActive(false);
                clone.transform.position =
                    launchTransform.position + launchTransform.forward;
                clone.transform.rotation = launchTransform.rotation;

                ReturnPortalEndpoint endpoint =
                    clone.GetComponent<ReturnPortalEndpoint>();
                if (endpoint == null)
                {
                    endpoint = clone.AddComponent<ReturnPortalEndpoint>();
                }
                endpoint.Initialize(portalType);
                DisableStockProbeMapMarker(clone);

                PreparePortalCarrierForLaunch(clone);

                // SurveyorProbe.Start establishes its light volume and then
                // stores the probe in the retrieved state. Let that one-time
                // setup finish before calling Launch on the independent copy.
                clone.SetActive(true);
            }
            catch (Exception exception)
            {
                setupException = exception;
            }

            if (setupException != null)
            {
                HandleLaunchFailure(clone, setupException);
                _launchInProgress = false;
                yield break;
            }

            yield return null;

            try
            {
                if (clone == null)
                {
                    throw new InvalidOperationException(
                        "The portal carrier was destroyed during setup."
                    );
                }

                SurveyorProbe probe = clone.GetComponent<SurveyorProbe>();
                if (probe == null)
                {
                    throw new InvalidOperationException(
                        "The cloned scout lost SurveyorProbe."
                    );
                }

                Vector3 launchVelocity =
                    playerBody.GetVelocity() +
                    launchTransform.forward * 80f;
                probe.Launch(
                    launchTransform,
                    launchVelocity,
                    false,
                    0f
                );
                IgnoreCarrierCollisionsWithPlayer(clone, playerBody);
                ReturnPortalEndpoint endpoint =
                    clone.GetComponent<ReturnPortalEndpoint>();
                InstallPortalVisualAndVolume(
                    clone,
                    endpoint,
                    portalType
                );
                endpoint.Launched = true;
                ReturnPortalMarkerLabel markerLabel =
                    clone.GetComponent<ReturnPortalMarkerLabel>();
                if (markerLabel == null)
                {
                    markerLabel = clone.AddComponent<
                        ReturnPortalMarkerLabel>();
                }
                markerLabel.Initialize(_mod, portalType);

                ReturnPortalMapMarker mapMarker =
                    clone.GetComponent<ReturnPortalMapMarker>();
                if (mapMarker == null)
                {
                    mapMarker = clone.AddComponent<
                        ReturnPortalMapMarker>();
                }
                mapMarker.Initialize(
                    _mod,
                    portalType,
                    probe.GetOWRigidbody()
                );

                foreach (ProbeCamera camera in
                    clone.GetComponentsInChildren<ProbeCamera>(true))
                {
                    camera.enabled = false;
                }

                SetLaunched(portalType, true);
                PostNotification(
                    portalType == ReturnPortalType.Black
                        ? "$RETURN_PORTAL_LAUNCHED_BLACK"
                        : "$RETURN_PORTAL_LAUNCHED_WHITE"
                );
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN PORTAL LAUNCH] type=" + portalType +
                    "; position=" + probe.GetOWRigidbody().GetPosition() +
                    "; velocity=" + probe.GetOWRigidbody().GetVelocity() +
                    "; retrievalLinked=false.",
                    MessageType.Success
                );

                RefreshPromptText();
            }
            catch (Exception exception)
            {
                HandleLaunchFailure(clone, exception);
            }
            _launchInProgress = false;
        }

        private void HandleLaunchFailure(
            GameObject clone,
            Exception exception
        )
        {
            if (clone != null)
            {
                UnityEngine.Object.Destroy(clone);
            }
            PostNotification("$RETURN_PORTAL_LAUNCH_FAILED");
            _mod.ModHelper.Console.WriteLine(
                "[RETURN PORTAL LAUNCH] Failed without affecting " +
                "Scene 6: " + exception,
                MessageType.Error
            );
        }

        private void InstallPortalVisualAndVolume(
            GameObject carrier,
            ReturnPortalEndpoint endpoint,
            ReturnPortalType portalType
        )
        {
            Renderer[] scoutRenderers =
                carrier.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in scoutRenderers)
            {
                renderer.enabled = false;
            }
            foreach (Light light in
                carrier.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }
            foreach (OWLight2 light in
                carrier.GetComponentsInChildren<OWLight2>(true))
            {
                light.enabled = false;
            }
            foreach (LightSourceVolume volume in
                carrier.GetComponentsInChildren<LightSourceVolume>(true))
            {
                volume.SetVolumeActivation(false);
            }

            SingularityController source =
                FindSingularityTemplate(portalType);
            if (source == null)
            {
                foreach (Renderer renderer in scoutRenderers)
                {
                    renderer.enabled = true;
                }
                ApplyPortalMaterialFallback(carrier, portalType);
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN PORTAL VISUAL] The vanilla " + portalType +
                    " singularity model was unavailable; material fallback " +
                    "was used.",
                    MessageType.Warning
                );
            }
            else
            {
                GameObject visual = UnityEngine.Object.Instantiate(
                    source.gameObject,
                    carrier.transform,
                    false
                );
                visual.name = "Return_" + portalType +
                    "PortalSingularityVisual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = source.transform.localScale;

                if (portalType == ReturnPortalType.White)
                {
                    NormalizeWhiteHoleVisualScale(visual, source.transform);
                }

                foreach (Collider collider in
                    visual.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }
                foreach (Shape shape in
                    visual.GetComponentsInChildren<Shape>(true))
                {
                    shape.SetActivation(false);
                }

                SingularityController singularity =
                    visual.GetComponent<SingularityController>();
                if (singularity != null)
                {
                    ReturnPortalVisualActivator activator =
                        visual.AddComponent<
                            ReturnPortalVisualActivator>();
                    activator.Initialize(
                        _mod,
                        portalType,
                        singularity
                    );
                }

                _mod.ModHelper.Console.WriteLine(
                    "[RETURN PORTAL VISUAL] type=" + portalType +
                    "; source=" + source.name +
                    "; scale=" + visual.transform.localScale + ".",
                    MessageType.Success
                );
            }

            if (portalType == ReturnPortalType.Black)
            {
                GameObject triggerObject = new GameObject(
                    "Return_BlackPortalTransportVolume"
                );
                triggerObject.layer = LayerMask.NameToLayer(
                    "BasicEffectVolume"
                );
                triggerObject.transform.SetParent(
                    carrier.transform,
                    false
                );
                triggerObject.transform.localPosition = Vector3.zero;
                triggerObject.transform.localRotation = Quaternion.identity;
                triggerObject.transform.localScale = Vector3.one;

                SphereCollider trigger =
                    triggerObject.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = 0.7f;
                OWCollider owCollider =
                    triggerObject.AddComponent<OWCollider>();
                owCollider.IgnorePhysicsSwapDelay();
                owCollider.ListenForParentBodySuspension();

                ReturnPortalTransportVolume transport =
                    triggerObject.AddComponent<
                        ReturnPortalTransportVolume>();
                transport.Initialize(_mod, endpoint);

                _mod.ModHelper.Console.WriteLine(
                    "[RETURN PORTAL VOLUME] type=Black; radius=" +
                    trigger.radius.ToString("F2") +
                    "; direction=BlackToWhite; paired=" +
                    (endpoint.GetCounterpart() != null) + ".",
                    MessageType.Success
                );
            }
            else
            {
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN PORTAL VOLUME] type=White; exitOnly=True.",
                    MessageType.Success
                );
            }
        }

        private void NormalizeWhiteHoleVisualScale(
            GameObject visual,
            Transform sourceTransform
        )
        {
            const float targetWorldRadius = 0.7f;
            float sourceShaderRadius = 0f;
            Renderer rootRenderer = visual.GetComponent<Renderer>();
            if (rootRenderer != null &&
                rootRenderer.sharedMaterial != null &&
                rootRenderer.sharedMaterial.HasProperty("_Radius"))
            {
                sourceShaderRadius =
                    rootRenderer.sharedMaterial.GetFloat("_Radius");
            }

            float multiplier = 1f;
            if (sourceShaderRadius > 0.001f)
            {
                multiplier = targetWorldRadius / sourceShaderRadius;
            }
            else
            {
                float renderedDiameter = 0f;
                foreach (Renderer renderer in
                    visual.GetComponentsInChildren<Renderer>(true))
                {
                    Vector3 size = renderer.bounds.size;
                    renderedDiameter = Mathf.Max(
                        renderedDiameter,
                        size.x,
                        size.y,
                        size.z
                    );
                }
                if (renderedDiameter > 0.001f)
                {
                    multiplier = targetWorldRadius * 2f /
                        renderedDiameter;
                }
            }

            multiplier = Mathf.Clamp(multiplier, 0.0001f, 1f);
            visual.transform.localScale =
                sourceTransform.localScale * multiplier;
            _mod.ModHelper.Console.WriteLine(
                "[RETURN WHITE PORTAL SCALE] sourceRadius=" +
                sourceShaderRadius.ToString("F3") +
                "; multiplier=" + multiplier.ToString("F5") +
                "; finalScale=" + visual.transform.localScale + ".",
                MessageType.Success
            );
        }

        private static SingularityController FindSingularityTemplate(
            ReturnPortalType portalType
        )
        {
            string expectedName =
                portalType == ReturnPortalType.Black
                    ? "SingularityController_BlackHole"
                    : "SingularityController_WhiteHole";
            SingularityController nameFallback = null;
            foreach (SingularityController controller in
                Resources.FindObjectsOfTypeAll<SingularityController>())
            {
                if (controller == null ||
                    !controller.gameObject.scene.IsValid() ||
                    controller.name.StartsWith(
                        "Return_",
                        StringComparison.Ordinal
                    ))
                {
                    continue;
                }
                if (controller.name == expectedName)
                {
                    return controller;
                }
                if (nameFallback == null &&
                    controller.name.IndexOf(
                        portalType == ReturnPortalType.Black
                            ? "BlackHole"
                            : "WhiteHole",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0)
                {
                    nameFallback = controller;
                }
            }
            return nameFallback;
        }

        private void ApplyPortalMaterialFallback(
            GameObject carrier,
            ReturnPortalType portalType
        )
        {
            string[] preferredNames = portalType == ReturnPortalType.Black
                ? new[]
                {
                    "Effects_HGT_TimeLoopExperiment_BlackHole_mat",
                    "Effects_NOM_WarpCoreBlack_mat",
                    "Effects_NOM_ShuttleBlackHole_mat"
                }
                : new[]
                {
                    "Effects_HGT_TimeLoopExperiment_WhiteHole_mat",
                    "Effects_NOM_WarpCoreWhite_mat",
                    "Effects_NOM_ShuttleWhiteHole_mat"
                };

            Material source = FindMaterial(preferredNames);
            if (source == null)
            {
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN PORTAL MATERIAL] No vanilla " + portalType +
                    " singularity material was loaded; the physics carrier " +
                    "kept its scout material.",
                    MessageType.Warning
                );
                return;
            }

            Material portalMaterial = new Material(source);
            portalMaterial.name = "Return_" + portalType +
                "PortalMaterial";
            int rendererCount = 0;
            foreach (Renderer renderer in
                carrier.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer ||
                    renderer.sharedMaterials.Length == 0)
                {
                    continue;
                }

                Material[] replacements =
                    new Material[renderer.sharedMaterials.Length];
                for (int index = 0;
                    index < replacements.Length;
                    index++)
                {
                    replacements[index] = portalMaterial;
                }
                renderer.sharedMaterials = replacements;
                renderer.enabled = true;
                rendererCount++;
            }

            _mod.ModHelper.Console.WriteLine(
                "[RETURN PORTAL MATERIAL] type=" + portalType +
                "; source=" + source.name +
                "; renderers=" + rendererCount + ".",
                MessageType.Success
            );
        }

        private static Material FindMaterial(string[] preferredNames)
        {
            foreach (string preferredName in preferredNames)
            {
                foreach (Material material in
                    Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (material != null &&
                        material.name.StartsWith(
                            preferredName,
                            StringComparison.Ordinal
                        ))
                    {
                        return material;
                    }
                }
            }
            return null;
        }

        private IEnumerator RevivePlayer()
        {
            _revivalInProgress = true;
            bool revived = false;
            try
            {
                OWRigidbody brittleHollow = FindBody(
                    "BrittleHollow_Body"
                );
                OWRigidbody playerBody = Locator.GetPlayerBody();
                if (brittleHollow == null || playerBody == null)
                {
                    throw new InvalidOperationException(
                        "Brittle Hollow or the player body was unavailable."
                    );
                }

                Vector3 worldPosition =
                    brittleHollow.transform.TransformPoint(
                        RevivalLocalPosition
                    );
                Quaternion worldRotation =
                    brittleHollow.GetRotation() * RevivalLocalRotation;
                ReturnPortalPlayerDetachment.DetachFromPlayerBeforeRevive();
                playerBody.WarpToPositionRotation(
                    worldPosition,
                    worldRotation
                );
                playerBody.SetVelocity(
                    brittleHollow.GetPointVelocity(worldPosition)
                );
                playerBody.SetAngularVelocity(
                    brittleHollow.GetAngularVelocity()
                );

                PlayerLockOnTargeting lockOn =
                    Locator.GetPlayerTransform()
                        .GetComponent<PlayerLockOnTargeting>();
                if (lockOn != null)
                {
                    lockOn.BreakLock();
                }
                Physics.SyncTransforms();
                revived = true;
            }
            catch (Exception exception)
            {
                PostNotification("$RETURN_PORTAL_REVIVE_FAILED");
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN REVIVE] Failed without affecting Scene 6: " +
                    exception,
                    MessageType.Error
                );
            }

            if (revived)
            {
                yield return new WaitForFixedUpdate();
                SceneSixEndingController.ClearPlayerPortalTransit();
                PostNotification("$RETURN_PORTAL_REVIVED");
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN REVIVE] Player returned to the Brittle Hollow " +
                    "gravity-cannon checkpoint without resetting the loop.",
                    MessageType.Success
                );
            }
            _revivalInProgress = false;
        }

        private bool HasLaunched(ReturnPortalType portalType)
        {
            return portalType == ReturnPortalType.Black
                ? _blackLaunched
                : _whiteLaunched;
        }

        private void SetLaunched(
            ReturnPortalType portalType,
            bool launched
        )
        {
            if (portalType == ReturnPortalType.Black)
            {
                _blackLaunched = launched;
            }
            else
            {
                _whiteLaunched = launched;
            }
        }

        private void RefreshPromptText(bool force = false)
        {
            if (_blackLaunchPrompt == null)
            {
                return;
            }
            _blackLaunchPrompt.SetText(Translate(
                _blackLaunched
                    ? "$RETURN_PORTAL_ALREADY_BLACK"
                    : "$RETURN_PORTAL_LAUNCH_BLACK"
            ));
            _blackLaunchPrompt.SetDisplayState(
                _blackLaunched
                    ? ScreenPrompt.DisplayState.GrayedOut
                    : ScreenPrompt.DisplayState.Normal
            );
            _whiteLaunchPrompt.SetText(Translate(
                _whiteLaunched
                    ? "$RETURN_PORTAL_ALREADY_WHITE"
                    : "$RETURN_PORTAL_LAUNCH_WHITE"
            ));
            _whiteLaunchPrompt.SetDisplayState(
                _whiteLaunched
                    ? ScreenPrompt.DisplayState.GrayedOut
                    : ScreenPrompt.DisplayState.Normal
            );
        }

        private void SetPromptVisibility(bool visible)
        {
            if (_blackLaunchPrompt == null)
            {
                return;
            }
            RefreshPromptText();
            _blackLaunchPrompt.SetVisibility(visible);
            _whiteLaunchPrompt.SetVisibility(visible);
            _revivePrompt.SetVisibility(visible);
        }

        private string Translate(string key)
        {
            if (_mod == null || _mod.NewHorizons == null)
            {
                return key;
            }
            string translated =
                _mod.NewHorizons.GetTranslationForUI(key);
            return string.IsNullOrEmpty(translated) ? key : translated;
        }

        private void PostNotification(string key)
        {
            NotificationManager manager =
                NotificationManager.SharedInstance;
            if (manager != null)
            {
                manager.PostNotification(
                    new NotificationData(
                        NotificationTarget.All,
                        Translate(key),
                        3f
                    )
                );
            }
        }

        private static OWRigidbody FindBody(string objectName)
        {
            foreach (OWRigidbody body in
                Resources.FindObjectsOfTypeAll<OWRigidbody>())
            {
                if (body != null &&
                    body.gameObject.scene.IsValid() &&
                    body.name == objectName)
                {
                    return body;
                }
            }
            return null;
        }

        private void OnDestroy()
        {
            RemovePrompts();
        }

        private void RemovePrompts()
        {
            if (!_promptsRegistered)
            {
                return;
            }
            PromptManager manager = Locator.GetPromptManager();
            if (manager != null)
            {
                if (_blackLaunchPrompt != null)
                {
                    manager.RemoveScreenPrompt(_blackLaunchPrompt);
                }
                if (_whiteLaunchPrompt != null)
                {
                    manager.RemoveScreenPrompt(_whiteLaunchPrompt);
                }
                if (_revivePrompt != null)
                {
                    manager.RemoveScreenPrompt(_revivePrompt);
                }
            }
            _promptsRegistered = false;
            _blackLaunchPrompt = null;
            _whiteLaunchPrompt = null;
            _revivePrompt = null;
        }
    }

    internal static class SceneSixWarpCoreToolController
    {
        public static bool IsReturnWarpCoreHeld()
        {
            ToolModeSwapper swapper = Locator.GetToolModeSwapper();
            ItemTool itemTool =
                swapper == null ? null : swapper.GetItemCarryTool();
            WarpCoreItem held = itemTool == null
                ? null
                : itemTool.GetHeldItem() as WarpCoreItem;
            return swapper != null &&
                swapper.GetToolMode() == ToolMode.Item &&
                itemTool != null &&
                itemTool.IsEquipped() &&
                held != null &&
                held.name == "Return_PickableWarpCore";
        }

        public static IEnumerator Prepare(ReturnMod mod)
        {
            // A normal load creates the core after ten seconds. A New
            // Horizons config reload instead restores the held core early,
            // before PromptManager is ready. Poll for both dependencies so a
            // single early failure cannot permanently remove the controls.
            for (int attempt = 0; attempt < 300; attempt++)
            {
                if (!SceneSixController.IsActive ||
                    LoadManager.GetCurrentScene() != OWScene.SolarSystem)
                {
                    yield break;
                }

                WarpCoreItem core = FindCanonicalReturnCore();
                if (core != null && Locator.GetPromptManager() != null)
                {
                    bool initialized = false;
                    try
                    {
                        RemoveDuplicateReturnCores(mod, core);
                        ReturnWarpCoreToolBehaviour behaviour =
                            core.GetComponent<
                                ReturnWarpCoreToolBehaviour
                            >();
                        if (behaviour == null)
                        {
                            behaviour = core.gameObject.AddComponent<
                                ReturnWarpCoreToolBehaviour
                            >();
                        }
                        behaviour.Initialize(mod, core);
                        initialized = true;
                    }
                    catch (Exception exception)
                    {
                        mod.ModHelper.Console.WriteLine(
                            "[RETURN WARP TOOL] Initialization failed " +
                            "without affecting Scene 6: " + exception,
                            MessageType.Error
                        );
                    }
                    if (initialized)
                    {
                        yield break;
                    }
                }

                yield return new WaitForSecondsRealtime(0.1f);
            }

            mod.ModHelper.Console.WriteLine(
                "[RETURN WARP TOOL] Return_PickableWarpCore was not found; " +
                "Scene 6 was left untouched.",
                MessageType.Warning
            );
        }

        private static WarpCoreItem FindCanonicalReturnCore()
        {
            ToolModeSwapper swapper = Locator.GetToolModeSwapper();
            ItemTool itemTool = swapper == null
                ? null
                : swapper.GetItemCarryTool();
            WarpCoreItem held = itemTool == null
                ? null
                : itemTool.GetHeldItem() as WarpCoreItem;
            if (IsReturnCore(held))
            {
                return held;
            }

            WarpCoreItem fallback = null;
            foreach (WarpCoreItem core in
                Resources.FindObjectsOfTypeAll<WarpCoreItem>())
            {
                if (!IsReturnCore(core))
                {
                    continue;
                }
                if (core.gameObject.activeInHierarchy)
                {
                    return core;
                }
                fallback = core;
            }
            return fallback;
        }

        private static bool IsReturnCore(WarpCoreItem core)
        {
            return core != null &&
                core.gameObject.scene.IsValid() &&
                core.name == "Return_PickableWarpCore";
        }

        private static void RemoveDuplicateReturnCores(
            ReturnMod mod,
            WarpCoreItem canonical
        )
        {
            int removed = 0;
            foreach (WarpCoreItem core in
                Resources.FindObjectsOfTypeAll<WarpCoreItem>())
            {
                if (!IsReturnCore(core) || core == canonical)
                {
                    continue;
                }
                UnityEngine.Object.Destroy(core.gameObject);
                removed++;
            }
            if (removed > 0)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN WARP TOOL] Removed " + removed +
                    " duplicate Return warp core instance(s) after reload.",
                    MessageType.Success
                );
            }
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class SceneSixWarpCoreToolPatch
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
                    SceneSixWarpCoreToolController.Prepare(__instance)
                );
            }
        }
    }

}

