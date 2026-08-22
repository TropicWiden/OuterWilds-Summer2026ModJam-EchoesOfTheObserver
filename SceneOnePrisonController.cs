using HarmonyLib;
using OWML.Common;
using System.Collections;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Build116 invisible boundary for Scene 1. A 4-meter-radius sphere is
    /// centered on the fish-box center. The boundary is soft: outward speed is
    /// damped as the player approaches, and beyond the boundary the outward
    /// velocity is cancelled while the position is eased back in small steps
    /// (no teleport, so the impact detector never sees a speed spike). While
    /// the boundary is armed the player cannot take impact damage or die.
    /// The camera sits at the player's feet so the restored full-size body
    /// still gives a low fish-eye view. No renderers, audio, or time-loop
    /// state are touched.
    /// </summary>
    internal sealed class SceneOnePrisonController : MonoBehaviour
    {
        private const float BoundaryRadius = 4f;
        private const float ClampInset = 0.05f;
        private const float SoftZoneDistance = 0.75f;
        private const float SoftZoneDamp = 0.9f;
        private const float MaxSnapPerFixedUpdate = 0.1f;
        private const float CameraFeetLift = 0.15f;
        private const float ReleaseGraceSeconds = 2.5f;
        private static readonly Vector3 BoxCenterLocal =
            new Vector3(0f, 0.65f, 0f);

        private static SceneOnePrisonController _instance;

        public static bool IsActive { get; private set; }

        private ReturnMod _mod;
        private Transform _sceneRoot;
        private bool _released;

        public static void Arm(
            ReturnMod mod,
            Transform sceneRoot
        )
        {
            if (_instance != null || mod == null || sceneRoot == null)
            {
                return;
            }

            GameObject host = new GameObject("Return_Scene1_Boundary");
            host.transform.SetParent(sceneRoot, false);
            SceneOnePrisonController controller =
                host.AddComponent<SceneOnePrisonController>();
            controller.Initialize(mod, sceneRoot);
            _instance = controller;
        }

        public static void EndSceneOne()
        {
            if (_instance == null)
            {
                return;
            }
            _instance.Release();
        }

        private void Initialize(
            ReturnMod mod,
            Transform sceneRoot
        )
        {
            _mod = mod;
            _sceneRoot = sceneRoot;
            IsActive = true;

            ReturnDebugLog.Write(
                "[RETURN SCENE 1 BOUNDARY] Armed; center=box center " +
                "(root local " + BoxCenterLocal.x.ToString("F2") + ", " +
                BoxCenterLocal.y.ToString("F2") + ", " +
                BoxCenterLocal.z.ToString("F2") + "); radius=" +
                BoundaryRadius.ToString("F1") +
                " m. Soft clamp; impact damage and death disabled while active.",
                MessageType.Success
            );
        }

        private void FixedUpdate()
        {
            if (_released || _sceneRoot == null)
            {
                return;
            }

            OWRigidbody playerBody = Locator.GetPlayerBody();
            if (playerBody == null)
            {
                return;
            }

            Vector3 centerWorld = _sceneRoot.TransformPoint(BoxCenterLocal);
            Vector3 position = playerBody.GetPosition();
            Vector3 offset = position - centerWorld;
            float distance = offset.magnitude;
            if (distance <= BoundaryRadius)
            {
                // Soft approach zone: gradually bleed off outward speed so
                // the player slows down instead of hitting a hard edge.
                if (distance > BoundaryRadius - SoftZoneDistance)
                {
                    Vector3 direction = offset / distance;
                    Vector3 velocity = playerBody.GetVelocity();
                    float outwardSpeed = Vector3.Dot(velocity, direction);
                    if (outwardSpeed > 0f)
                    {
                        playerBody.SetVelocity(
                            velocity - direction * outwardSpeed *
                            (1f - SoftZoneDamp)
                        );
                    }
                }
                return;
            }

            // Beyond the boundary: cancel the outward velocity completely,
            // then ease the position back inside with a small step per frame.
            // No teleport, so the impact detector never sees a speed spike
            // from the boundary itself.
            Vector3 dir = offset / distance;
            float step = Mathf.Min(
                distance - BoundaryRadius + ClampInset,
                MaxSnapPerFixedUpdate
            );
            Vector3 easedPosition =
                centerWorld + dir * (distance - step);
            playerBody.WarpToPositionRotation(
                easedPosition,
                playerBody.GetRotation()
            );

            Vector3 vel = playerBody.GetVelocity();
            float outward = Vector3.Dot(vel, dir);
            if (outward > 0f)
            {
                playerBody.SetVelocity(vel - dir * outward);
            }
        }

        private void LateUpdate()
        {
            if (_released || _sceneRoot == null || !IsActive)
            {
                return;
            }

            OWRigidbody playerBody = Locator.GetPlayerBody();
            OWCamera playerCamera = Locator.GetPlayerCamera();
            if (playerBody == null || playerCamera == null)
            {
                return;
            }

            // Keep the camera at the player's feet during Scene 1 so the
            // restored full-size body still gives a low fish-eye view.
            playerCamera.transform.position =
                playerBody.GetPosition() +
                playerBody.transform.up * CameraFeetLift;
        }

        private void Release()
        {
            if (_released)
            {
                return;
            }
            _released = true;

            // Detach from the scene-1 root so the grace timer survives any
            // scene teardown during the warp to Scene 2.
            transform.SetParent(null, false);

            // Later scenes keep the original tiny-fish behavior; only Scene 1
            // uses the restored full-size body with the feet-level camera.
            OWRigidbody playerBody = Locator.GetPlayerBody();
            if (playerBody != null)
            {
                playerBody.transform.localScale = Vector3.one * 0.2f;
            }

            if (_mod != null)
            {
                ReturnDebugLog.Write(
                    "[RETURN SCENE 1 BOUNDARY] Released; impact/death " +
                    "protection stays active for " +
                    ReleaseGraceSeconds.ToString("F1") +
                    "s to cover the scene-2 transition.",
                    MessageType.Success
                );
            }
            StartCoroutine(FinishReleaseAfterGrace());
        }

        private IEnumerator FinishReleaseAfterGrace()
        {
            float endTime = Time.unscaledTime + ReleaseGraceSeconds;
            while (Time.unscaledTime < endTime)
            {
                yield return null;
            }

            IsActive = false;
            if (_mod != null)
            {
                ReturnDebugLog.Write(
                    "[RETURN SCENE 1 BOUNDARY] Protection ended; death enabled.",
                    MessageType.Success
                );
            }
            _instance = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            IsActive = false;
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }

    [HarmonyPatch(typeof(DeathManager), "KillPlayer")]
    internal static class ReturnSceneOneNoDeathPatch
    {
        private static bool Prefix()
        {
            return !SceneOnePrisonController.IsActive;
        }
    }

    [HarmonyPatch(typeof(DeathManager), "FinishDeathSequence")]
    internal static class ReturnSceneOneNoDeathSequencePatch
    {
        private static bool Prefix()
        {
            return !SceneOnePrisonController.IsActive;
        }
    }

    [HarmonyPatch(typeof(PlayerResources), "OnImpact")]
    internal static class ReturnSceneOneNoImpactDamagePatch
    {
        private static bool Prefix()
        {
            return !SceneOnePrisonController.IsActive;
        }
    }
}
