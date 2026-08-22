using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OWML.Common;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Remembers the rigidbody on which a launched portal carrier is anchored.
    /// That body is the portal's host and must not be transported by its own
    /// portal volume.
    /// </summary>
    internal sealed class ReturnPortalHostBinding : MonoBehaviour
    {
        public OWRigidbody HostBody { get; private set; }

        public void Bind(OWRigidbody hostBody)
        {
            if (hostBody == null ||
                hostBody == GetComponent<ReturnPortalEndpoint>()?.Body)
            {
                return;
            }

            HostBody = CanonicalizeShipBody(hostBody);
        }

        public void Clear()
        {
            HostBody = null;
        }

        public bool IsHost(OWRigidbody candidate)
        {
            if (HostBody == null || candidate == null)
            {
                return false;
            }

            return CanonicalizeShipBody(candidate) == HostBody;
        }

        public bool IsHostCollider(
            Collider collider,
            Rigidbody unityBody,
            OWRigidbody portalBody
        )
        {
            if (HostBody == null || collider == null)
            {
                return false;
            }

            OWRigidbody candidate = unityBody == null
                ? null
                : unityBody.GetComponent<OWRigidbody>();
            if (candidate == portalBody)
            {
                return false;
            }
            if (IsHost(candidate))
            {
                return true;
            }

            Rigidbody hostUnityBody = HostBody.GetRigidbody();
            if (unityBody != null && unityBody == hostUnityBody)
            {
                return true;
            }

            // Some ship interior pieces use auxiliary Unity rigidbodies. The
            // hierarchy check treats all such colliders as part of the ship
            // (or planet) host, while independent OWRigidbodies remain free.
            return candidate == null &&
                collider.transform.IsChildOf(HostBody.transform);
        }

        private static OWRigidbody CanonicalizeShipBody(
            OWRigidbody body
        )
        {
            OWRigidbody shipBody = Locator.GetShipBody();
            if (shipBody == null || body == null)
            {
                return body;
            }

            Transform current = body.transform;
            while (current != null)
            {
                if (current == shipBody.transform)
                {
                    return shipBody;
                }
                current = current.parent;
            }

            return body;
        }
    }

    /// <summary>
    /// Safety net: before the player is teleported to the Brittle Hollow
    /// checkpoint, detach any portal carrier that is still anchored to the
    /// player so it cannot be dragged along with the revival warp.
    /// </summary>
    internal static class ReturnPortalPlayerDetachment
    {
        public static void DetachFromPlayerBeforeRevive()
        {
            try
            {
                OWRigidbody playerBody = Locator.GetPlayerBody();
                if (playerBody == null)
                {
                    return;
                }

                int detached = 0;
                foreach (ReturnPortalEndpoint endpoint in
                    Resources.FindObjectsOfTypeAll<ReturnPortalEndpoint>())
                {
                    if (endpoint == null)
                    {
                        continue;
                    }

                    ReturnPortalHostBinding binding =
                        endpoint.GetComponent<ReturnPortalHostBinding>();
                    if (binding == null || !binding.IsHost(playerBody))
                    {
                        continue;
                    }

                    ProbeAnchor anchor =
                        endpoint.GetComponentInChildren<ProbeAnchor>(true);
                    if (anchor != null && anchor.IsAnchored())
                    {
                        anchor.UnanchorFromSurface();
                    }
                    binding.Clear();
                    detached++;
                }

                if (detached > 0)
                {
                    ReturnDebugLog.Write(
                        "[RETURN PORTAL HOST] Detached " + detached +
                        " portal carrier(s) from the player before " +
                        "the checkpoint warp.",
                        MessageType.Success
                    );
                }
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PORTAL HOST] Could not detach portal carriers " +
                    "before the checkpoint warp: " + exception.Message,
                    MessageType.Warning
                );
            }
        }
    }

    /// <summary>
    /// Captures the exact surface body selected by the stock scout anchoring
    /// code. This works for planets as well as the ship's exterior/interior.
    /// </summary>
    [HarmonyPatch(typeof(ProbeAnchor), "AnchorToObject")]
    internal static class ReturnPortalAnchorHostPatch
    {
        private static bool Prefix(
            ProbeAnchor __instance,
            GameObject hitObject
        )
        {
            try
            {
                if (__instance == null || hitObject == null)
                {
                    return true;
                }

                OWRigidbody portalBody =
                    __instance.GetAttachedOWRigidbody();
                ReturnPortalEndpoint endpoint = portalBody == null
                    ? null
                    : portalBody.GetComponent<ReturnPortalEndpoint>();
                if (endpoint == null)
                {
                    return true;
                }

                OWRigidbody playerBody = Locator.GetPlayerBody();
                Transform playerTransform = Locator.GetPlayerTransform();
                if (playerBody == null && playerTransform == null)
                {
                    return true;
                }

                OWRigidbody hitBody = hitObject.GetAttachedOWRigidbody();
                bool hitPlayer = hitBody == playerBody ||
                    (playerTransform != null &&
                        hitObject.transform.IsChildOf(playerTransform));
                if (!hitPlayer)
                {
                    return true;
                }

                ReturnPortalHostBinding binding =
                    endpoint.GetComponent<ReturnPortalHostBinding>();
                if (binding != null)
                {
                    binding.Clear();
                }

                ReturnDebugLog.Write(
                    "[RETURN PORTAL HOST] Blocked anchoring type=" +
                    endpoint.PortalType + " to the player.",
                    MessageType.Success
                );
                return false;
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PORTAL HOST] Player-anchor guard failed; " +
                    "falling back to the original anchor logic: " +
                    exception.Message,
                    MessageType.Warning
                );
                return true;
            }
        }

        private static void Postfix(
            ProbeAnchor __instance,
            GameObject hitObject
        )
        {
            try
            {
                if (__instance == null || hitObject == null)
                {
                    return;
                }

                TrySuppressIntegrityNotification(__instance);

                if (!__instance.IsAnchored())
                {
                    return;
                }

                OWRigidbody portalBody =
                    __instance.GetAttachedOWRigidbody();
                ReturnPortalEndpoint endpoint = portalBody == null
                    ? null
                    : portalBody.GetComponent<ReturnPortalEndpoint>();
                OWRigidbody hostBody =
                    hitObject.GetAttachedOWRigidbody();
                if (endpoint == null ||
                    hostBody == null ||
                    hostBody == endpoint.Body)
                {
                    return;
                }

                ReturnPortalHostBinding binding =
                    endpoint.GetComponent<ReturnPortalHostBinding>();
                if (binding == null)
                {
                    binding = endpoint.gameObject.AddComponent<
                        ReturnPortalHostBinding>();
                }
                binding.Bind(hostBody);

                ReturnDebugLog.Write(
                    "[RETURN PORTAL HOST] type=" +
                    endpoint.PortalType + "; host=" + hostBody.name + ".",
                    MessageType.Info
                );
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PORTAL HOST] Could not record the anchor host: " +
                    exception.Message,
                    MessageType.Warning
                );
            }
        }

        /// <summary>
        /// Portal carriers use the stock ProbeAnchor, which posts a pinned
        /// "surface integrity" HUD notification whenever it anchors to a
        /// breakable fragment or the Ring World dam. Players do not need that
        /// scout-oriented readout for the black/white hole endpoints, so the
        /// notification is unpinned immediately after every portal anchor.
        /// </summary>
        private static void TrySuppressIntegrityNotification(
            ProbeAnchor anchor
        )
        {
            try
            {
                OWRigidbody portalBody = anchor.GetAttachedOWRigidbody();
                ReturnPortalEndpoint endpoint = portalBody == null
                    ? null
                    : portalBody.GetComponent<ReturnPortalEndpoint>();
                if (endpoint == null)
                {
                    return;
                }

                Traverse anchorTraverse = Traverse.Create(anchor);
                bool posted = anchorTraverse.Field("_notificationPosted")
                    .GetValue<bool>();
                if (!posted)
                {
                    return;
                }

                NotificationData notification = anchorTraverse
                    .Field("_probeNotification")
                    .GetValue<NotificationData>();
                if (notification != null)
                {
                    NotificationManager.SharedInstance?
                        .UnpinNotification(notification);
                }
                anchorTraverse.Field("_notificationPosted")
                    .SetValue(false);
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PORTAL HOST] Integrity notification " +
                    "suppression failed: " + exception.Message,
                    MessageType.Warning
                );
            }
        }
    }

    [HarmonyPatch(typeof(ProbeAnchor), "UnanchorFromSurface")]
    internal static class ReturnPortalUnanchorHostPatch
    {
        private static void Postfix(ProbeAnchor __instance)
        {
            try
            {
                OWRigidbody portalBody = __instance == null
                    ? null
                    : __instance.GetAttachedOWRigidbody();
                ReturnPortalHostBinding binding = portalBody == null
                    ? null
                    : portalBody.GetComponent<ReturnPortalHostBinding>();
                binding?.Clear();
            }
            catch
            {
                // Host cleanup must never interfere with stock probe behavior.
            }
        }
    }

    /// <summary>
    /// Stops a black portal from swallowing the body it is attached to. If an
    /// anchor callback was missed, the current parent and scout detectors are
    /// used as safe fallbacks.
    /// </summary>
    [HarmonyPatch(typeof(ReturnPortalTransportVolume), "OnTriggerEnter")]
    internal static class ReturnPortalHostExemptionPatch
    {
        private static readonly FieldInfo EndpointField = AccessTools.Field(
            typeof(ReturnPortalTransportVolume),
            "_endpoint"
        );

        private static bool Prefix(
            ReturnPortalTransportVolume __instance,
            Collider hitCollider
        )
        {
            try
            {
                if (__instance == null || hitCollider == null)
                {
                    return true;
                }

                Rigidbody unityBody = hitCollider.attachedRigidbody;
                if (unityBody == null)
                {
                    return true;
                }

                ReturnPortalEndpoint endpoint = EndpointField == null
                    ? null
                    : EndpointField.GetValue(__instance) as
                        ReturnPortalEndpoint;
                if (endpoint == null)
                {
                    return true;
                }

                ReturnPortalHostBinding binding =
                    endpoint.GetComponent<ReturnPortalHostBinding>();
                if (binding == null)
                {
                    binding = endpoint.gameObject.AddComponent<
                        ReturnPortalHostBinding>();
                }

                if (binding.HostBody == null &&
                    IsAnchored(endpoint))
                {
                    OWRigidbody fallbackHost = ResolveHost(endpoint);
                    if (fallbackHost != null &&
                        fallbackHost != endpoint.Body &&
                        fallbackHost != Locator.GetPlayerBody())
                    {
                        binding.Bind(fallbackHost);
                    }
                }

                if (!binding.IsHostCollider(
                    hitCollider,
                    unityBody,
                    endpoint.Body
                ))
                {
                    return true;
                }
                return false;
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PORTAL HOST] Host filter fell back to the " +
                    "original transport logic: " + exception.Message,
                    MessageType.Warning
                );
                return true;
            }
        }

        private static bool IsAnchored(ReturnPortalEndpoint endpoint)
        {
            ProbeAnchor anchor =
                endpoint.GetComponentInChildren<ProbeAnchor>(true);
            return anchor != null && anchor.IsAnchored();
        }

        private static OWRigidbody ResolveHost(
            ReturnPortalEndpoint endpoint
        )
        {
            Transform parent = endpoint.transform.parent;
            if (parent != null)
            {
                OWRigidbody parentBody =
                    parent.GetAttachedOWRigidbody();
                if (parentBody != null && parentBody != endpoint.Body)
                {
                    return parentBody;
                }
            }

            SurveyorProbe probe = endpoint.GetComponent<SurveyorProbe>();
            RulesetDetector rulesetDetector = probe == null
                ? null
                : probe.GetRulesetDetector();
            PlanetoidRuleset planetoid = rulesetDetector == null
                ? null
                : rulesetDetector.GetPlanetoidRuleset();
            if (planetoid != null)
            {
                return planetoid.GetAttachedOWRigidbody();
            }

            SectorDetector sectorDetector = probe == null
                ? null
                : probe.GetSectorDetector();
            ReferenceFrame referenceFrame = sectorDetector == null
                ? null
                : sectorDetector.GetPassiveReferenceFrame();
            return referenceFrame == null
                ? null
                : referenceFrame.GetOWRigidBody();
        }
    }

    /// <summary>
    /// Admission for transported objects now lives in
    /// ReturnPortalTransportVolume.CanTransportAstroObject: every AstroObject
    /// except the Sun and Giant's Deep may be transported by the black hole.
    /// </summary>

    /// <summary>
    /// Supplements Unity trigger callbacks with a small, allocation-free
    /// physical overlap query. Effect-volume layers do not receive callbacks
    /// from ordinary planet terrain, so this scanner forwards those real
    /// collider overlaps into the existing transport method.
    /// </summary>
    internal sealed class ReturnPortalPhysicalOverlapScanner : MonoBehaviour
    {
        private const int ColliderBufferSize = 256;

        private readonly Collider[] _colliderBuffer =
            new Collider[ColliderBufferSize];
        private readonly HashSet<int> _bodyIdsThisStep =
            new HashSet<int>();

        private ReturnPortalTransportVolume _volume;
        private ReturnPortalEndpoint _endpoint;
        private SphereCollider _trigger;
        private Action<Collider> _forwardTrigger;
        private bool _bufferWarningWritten;
        private bool _failed;
        private float _scanTimer;

        public void Initialize(ReturnPortalTransportVolume volume)
        {
            try
            {
                _volume = volume;
                _trigger = volume == null
                    ? null
                    : volume.GetComponent<SphereCollider>();
                _endpoint = volume == null
                    ? null
                    : volume.GetComponentInParent<
                        ReturnPortalEndpoint>();

                MethodInfo triggerMethod = AccessTools.Method(
                    typeof(ReturnPortalTransportVolume),
                    "OnTriggerEnter"
                );
                if (_volume == null ||
                    _trigger == null ||
                    _endpoint == null ||
                    triggerMethod == null)
                {
                    throw new InvalidOperationException(
                        "The portal overlap scanner could not find its " +
                        "transport components."
                    );
                }

                _forwardTrigger = (Action<Collider>)Delegate.CreateDelegate(
                    typeof(Action<Collider>),
                    _volume,
                    triggerMethod,
                    true
                );

                ReturnDebugLog.Write(
                    "[RETURN PORTAL OVERLAP] Physical collider scanner " +
                    "installed.",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                _failed = true;
                ReturnDebugLog.Write(
                    "[RETURN PORTAL OVERLAP] Scanner initialization failed: " +
                    exception.Message,
                    MessageType.Warning
                );
            }
        }

        private void FixedUpdate()
        {
            if (_failed ||
                _volume == null ||
                _endpoint == null ||
                _endpoint.PortalType != ReturnPortalType.Black ||
                _trigger == null ||
                !_trigger.enabled ||
                _forwardTrigger == null)
            {
                return;
            }

            _scanTimer += Time.fixedDeltaTime;
            if (_scanTimer < 0.1f)
            {
                return;
            }
            _scanTimer = 0f;

            Vector3 center = transform.TransformPoint(_trigger.center);
            Vector3 scale = transform.lossyScale;
            float radius = _trigger.radius * Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)
            );
            if (radius <= 0f)
            {
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(
                center,
                radius,
                _colliderBuffer,
                ~0,
                QueryTriggerInteraction.Ignore
            );
            if (count == ColliderBufferSize && !_bufferWarningWritten)
            {
                _bufferWarningWritten = true;
                ReturnDebugLog.Write(
                    "[RETURN PORTAL OVERLAP] The collider buffer filled; " +
                    "nearby objects will continue to be checked on later " +
                    "physics steps.",
                    MessageType.Warning
                );
            }

            _bodyIdsThisStep.Clear();
            int limit = Mathf.Min(count, ColliderBufferSize);
            for (int index = 0; index < limit; index++)
            {
                Collider collider = _colliderBuffer[index];
                _colliderBuffer[index] = null;
                if (collider == null)
                {
                    continue;
                }

                Rigidbody unityBody = collider.attachedRigidbody;
                if (unityBody == null ||
                    !_bodyIdsThisStep.Add(unityBody.GetInstanceID()))
                {
                    continue;
                }

                ReturnPortalEndpoint hitEndpoint =
                    unityBody.GetComponent<ReturnPortalEndpoint>();
                if (hitEndpoint != null)
                {
                    continue;
                }

                ReturnPortalHostBinding binding =
                    _endpoint.GetComponent<ReturnPortalHostBinding>();
                if (binding != null &&
                    binding.IsHostCollider(
                        collider,
                        unityBody,
                        _endpoint.Body
                    ))
                {
                    continue;
                }

                try
                {
                    _forwardTrigger(collider);
                }
                catch (Exception exception)
                {
                    _failed = true;
                    ReturnDebugLog.Write(
                        "[RETURN PORTAL OVERLAP] Scanner forwarding failed: " +
                        exception.Message,
                        MessageType.Warning
                    );
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(ReturnPortalTransportVolume), "Initialize")]
    internal static class ReturnPortalPhysicalOverlapInstallPatch
    {
        private static void Postfix(
            ReturnPortalTransportVolume __instance
        )
        {
            try
            {
                ReturnPortalPhysicalOverlapScanner scanner =
                    __instance.GetComponent<
                        ReturnPortalPhysicalOverlapScanner>();
                if (scanner == null)
                {
                    scanner = __instance.gameObject.AddComponent<
                        ReturnPortalPhysicalOverlapScanner>();
                }
                scanner.Initialize(__instance);
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PORTAL OVERLAP] Scanner installation failed: " +
                    exception.Message,
                    MessageType.Warning
                );
            }
        }
    }
}
