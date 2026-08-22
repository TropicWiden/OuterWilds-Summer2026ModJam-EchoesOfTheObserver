using HarmonyLib;
using OWML.Common;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Return
{
    internal static class SceneSixController
    {
        private const string RevivalCheckpointCondition =
            "RETURN_REVIVE_AT_BRITTLE_HOLLOW";

        public static bool IsActive { get; private set; }

        public static void MarkRevivalCheckpoint()
        {
            IsActive = true;
            PlayerData.SetPersistentCondition(
                RevivalCheckpointCondition,
                true
            );
        }

        public static void RestoreActiveStateFromSave()
        {
            if (!IsActive &&
                PlayerData.IsLoaded() &&
                PlayerData.GetPersistentCondition(
                    RevivalCheckpointCondition
                ))
            {
                IsActive = true;
            }
        }

        /// <summary>
        /// Clears the scene-six checkpoint so a freshly reset game starts
        /// from Scene 1 (the Nomai mine) instead of the Brittle Hollow loop.
        /// Loaded saves that already reached Scene 6 keep their checkpoint.
        /// </summary>
        public static void ResetForNewGame()
        {
            IsActive = false;
            if (PlayerData.IsLoaded())
            {
                PlayerData.SetPersistentCondition(
                    RevivalCheckpointCondition,
                    false
                );
            }
        }

        public static void Begin(ReturnMod mod)
        {
            if (IsActive || mod == null)
            {
                return;
            }

            IsActive = true;
            // Every entry into Scene 6 starts a fresh loop: normalize the
            // saved loop count to 2 so the title screen Resume button
            // stays available after returning to the main menu (vanilla
            // shows it only when loopCount > 1).
            PlayerData.SaveLoopCount(2);
            RestoreVanillaPlayerFunctions();
            ReturnDebugLog.Write(
                "[RETURN SCENE 6] Resetting the loop and loading the " +
                "Brittle Hollow gravity cannon start.",
                MessageType.Success
            );
            LoadManager.LoadScene(
                OWScene.SolarSystem,
                LoadManager.FadeType.ToBlack,
                1f,
                true
            );
        }

        public static IEnumerator Prepare(ReturnMod mod)
        {
            while (Locator.GetPlayerBody() == null ||
                Locator.GetShipBody() == null ||
                Locator.GetPlayerSectorDetector() == null ||
                !LateInitializerManager.isDoneInitializing)
            {
                yield return null;
            }

            // The scene-complete event precedes several player/HUD Start
            // methods and New Horizons' final solar-system setup.
            yield return new WaitForSecondsRealtime(1f);

            // The Statue Island stays frozen through Scenes 2-5 so the
            // dialogue cannot be interrupted by fall damage. Release it now
            // that the Scene Six loop has loaded.
            SceneTwoController.UnfreezeStatueIsland();

            OWRigidbody brittleHollow = null;
            float bodyDeadline = Time.realtimeSinceStartup + 15f;
            while (brittleHollow == null &&
                Time.realtimeSinceStartup < bodyDeadline)
            {
                brittleHollow = FindBody("BrittleHollow_Body");
                if (brittleHollow == null)
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                }
            }

            if (brittleHollow == null)
            {
                ReturnDebugLog.Write(
                    "[RETURN SCENE 6] Could not find BrittleHollow_Body.",
                    MessageType.Error
                );
                yield break;
            }

            Transform spawn = null;
            float spawnDeadline = Time.realtimeSinceStartup + 15f;
            while (spawn == null &&
                Time.realtimeSinceStartup < spawnDeadline)
            {
                spawn = FindBrittleHollowGravityCannonSpawn(
                    brittleHollow.transform
                );
                if (spawn == null)
                {
                    yield return new WaitForSecondsRealtime(0.1f);
                }
            }

            if (spawn == null)
            {
                ReturnDebugLog.Write(
                    "[RETURN SCENE 6] Could not find " +
                    "SPAWN_GravityCannon on Brittle Hollow.",
                    MessageType.Error
                );
                yield break;
            }

            InterloperTrajectoryController.Apply(mod);

            // The first Scene 6 entry reloads the SolarSystem scene from
            // inside Scenes 1-5, so the outgoing instance can still contain
            // stale Sun/Interloper/station bodies for a moment. Verify the
            // comet received the intended state and re-apply once if not.
            yield return new WaitForSecondsRealtime(0.3f);
            if (!InterloperTrajectoryController.IsCometOnAppliedTrajectory(
                    out string trajectoryNote
                ))
            {
                ReturnDebugLog.Write(
                    "[RETURN INTERLOPER] First application failed " +
                    "verification: " + trajectoryNote +
                    "; re-applying the trajectory once.",
                    MessageType.Warning
                );
                InterloperTrajectoryController.Apply(mod);
                yield return new WaitForSecondsRealtime(0.3f);
                if (!InterloperTrajectoryController.IsCometOnAppliedTrajectory(
                        out trajectoryNote
                    ))
                {
                    ReturnDebugLog.Write(
                        "[RETURN INTERLOPER] Re-application also failed " +
                        "verification: " + trajectoryNote + ".",
                        MessageType.Error
                    );
                }
            }
            else
            {
                ReturnDebugLog.Write(
                    "[RETURN INTERLOPER] Trajectory verification passed: " +
                    trajectoryNote + ".",
                    MessageType.Success
                );
            }

            // Allow the newly loaded player, ship and planet physics to
            // finish their first initialization frame before teleporting.
            yield return new WaitForFixedUpdate();
            yield return null;

            OWRigidbody playerBody = Locator.GetPlayerBody();
            OWRigidbody shipBody = Locator.GetShipBody();
            float vanillaPlayerMass = playerBody.GetMass();
            // Hand-placed and physically verified player spawn beside the
            // Brittle Hollow gravity cannon. Keeping this in planet-local
            // space makes it follow Brittle Hollow throughout the loop.
            Vector3 playerLocalPosition = new Vector3(
                -224.8589f,
                0.8171f,
                92.6140f
            );
            Quaternion playerLocalRotation = new Quaternion(
                -0.177269f,
                -0.096406f,
                -0.683298f,
                -0.701703f
            );

            // Hand-placed and physically verified beside the Brittle Hollow
            // gravity cannon. These are planet-local coordinates, so the
            // placement remains valid while the planet moves through space.
            Vector3 shipLocalPosition = new Vector3(
                -251.2776f,
                -0.1194f,
                57.2258f
            );
            Quaternion shipLocalRotation = new Quaternion(
                0.002456f,
                0.173725f,
                0.715417f,
                0.676751f
            );

            playerBody.transform.localScale = Vector3.one;
            playerBody.SetMass(vanillaPlayerMass);

            // Keep both objects at their planet-relative destinations while
            // the universe recenters and settles. Movement is unlocked only
            // after all corrections are complete.
            for (int frame = 0; frame < 8; frame++)
            {
                WarpAtLocalTransform(
                    playerBody,
                    brittleHollow,
                    playerLocalPosition,
                    playerLocalRotation
                );
                WarpAtLocalTransform(
                    shipBody,
                    brittleHollow,
                    shipLocalPosition,
                    shipLocalRotation
                );
                Physics.SyncTransforms();
                yield return new WaitForFixedUpdate();
            }

            ForceGravityCannonSectorLoaded(brittleHollow);
            GameObject revivalComputer = CreateRevivalComputer(
                mod,
                brittleHollow
            );
            RestoreBrittleHollowVolumes(brittleHollow);

            // Give sector cull groups time to restore the cannon interface,
            // local gravity volumes and detailed collision geometry.
            for (int frame = 0; frame < 5; frame++)
            {
                WarpAtLocalTransform(
                    playerBody,
                    brittleHollow,
                    playerLocalPosition,
                    playerLocalRotation
                );
                WarpAtLocalTransform(
                    shipBody,
                    brittleHollow,
                    shipLocalPosition,
                    shipLocalRotation
                );
                RestoreBrittleHollowVolumes(brittleHollow);
                EnsureRevivalComputerActive(
                    revivalComputer,
                    brittleHollow
                );
                yield return null;
            }

            playerBody.transform.localScale = Vector3.one;
            playerBody.SetMass(vanillaPlayerMass);

            PlayerCharacterController controller =
                Locator.GetPlayerController();
            if (controller != null)
            {
                // LockMovement(false) still locks translation; its argument
                // only controls whether turning is locked as well.
                controller.UnlockMovement();
                controller.SetColliderActivation(true);
            }

            PlayerLockOnTargeting lockOn =
                Locator.GetPlayerTransform()
                    .GetComponent<PlayerLockOnTargeting>();
            if (lockOn != null)
            {
                lockOn.BreakLock();
            }

            RestoreSuitAndJetpack(playerBody);
            RestoreFlashlight();
            AddSuitVisualHider(playerBody);
            EnableShipHUDMarker();
            OWInput.ChangeInputMode(InputMode.Character);
            Physics.SyncTransforms();
            yield return null;

            // PutOnHelmet normally emits this through HUDHelmetAnimator. Fire
            // it once more after the visual hider has run so every stock HUD
            // listener is synchronized without showing the suit mesh.
            GlobalMessenger.FireEvent("HelmetHUDActivated");

            ReturnDebugLog.Write(
                "[RETURN SCENE 6] Player and ship placed beside the " +
                "Brittle Hollow gravity cannon. Vanilla player functions " +
                "restored; spacesuit visuals remain hidden. Scale=" +
                playerBody.transform.localScale + "; mass=" +
                playerBody.GetMass().ToString("F4") + "; input=" +
                OWInput.GetInputMode() + "; gravity=" +
                GetPlayerGravityMagnitude().ToString("F2") + "; ruleset=" +
                GetPlayerRulesetName() + ".",
                MessageType.Success
            );
        }

        private static GameObject CreateRevivalComputer(
            ReturnMod mod,
            OWRigidbody brittleHollow
        )
        {
            if (mod == null ||
                mod.NewHorizons == null ||
                brittleHollow == null)
            {
                ReturnDebugLog.Write(
                    "[RETURN COMPUTER] New Horizons or Brittle Hollow " +
                    "was unavailable.",
                    MessageType.Error
                );
                return null;
            }

            foreach (Transform candidate in
                brittleHollow.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null &&
                    candidate.name == "Return_RevivalComputer")
                {
                    return candidate.gameObject;
                }
            }

            const string xml =
                "<NomaiObject>" +
                "<TextBlock>" +
                "<ID>1</ID>" +
                "<Text>Revival will become impossible in " +
                "&lt;TimeMinutesRemaining&gt; minutes.</Text>" +
                "</TextBlock>" +
                "</NomaiObject>";
            const string textInfo =
                "{\"type\":\"computer\"," +
                "\"location\":\"unspecified\"," +
                "\"position\":{},\"rotation\":{}}";

            GameObject computer;
            try
            {
                computer = mod.NewHorizons.CreateNomaiText(
                    xml,
                    textInfo,
                    brittleHollow.gameObject
                );
            }
            catch (System.Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN COMPUTER] Failed to create the Nomai " +
                    "computer: " + exception,
                    MessageType.Error
                );
                return null;
            }

            if (computer == null)
            {
                ReturnDebugLog.Write(
                    "[RETURN COMPUTER] New Horizons returned no object.",
                    MessageType.Error
                );
                return null;
            }

            Vector3 localPosition = new Vector3(
                -222.2850f,
                2.7995f,
                96.8896f
            );
            Quaternion localRotation = new Quaternion(
                0.701051f,
                -0.630338f,
                0.092349f,
                0.320427f
            );

            computer.name = "Return_RevivalComputer";
            computer.transform.position =
                brittleHollow.transform.TransformPoint(localPosition);
            computer.transform.rotation =
                brittleHollow.GetRotation() * localRotation;

            Transform interactables = FindGravityCannonInteractables(
                brittleHollow.transform
            );
            if (interactables != null)
            {
                computer.transform.SetParent(interactables, true);
            }

            SetRevivalComputerSector(computer, brittleHollow);

            computer.SetActive(true);
            Physics.SyncTransforms();
            ReturnDebugLog.Write(
                "[RETURN COMPUTER] Interactive Nomai computer placed at " +
                "the recorded Brittle Hollow position.",
                MessageType.Success
            );
            return computer;
        }

        private static void EnsureRevivalComputerActive(
            GameObject computerObject,
            OWRigidbody brittleHollow
        )
        {
            if (computerObject == null || brittleHollow == null)
            {
                return;
            }

            NomaiComputer computer =
                computerObject.GetComponentInChildren<NomaiComputer>(true);
            Sector sector = FindGravityCannonSector(
                brittleHollow.transform
            );
            if (computer == null || sector == null)
            {
                return;
            }

            computer.SetSector(sector);
            var updateSectorOccupants = AccessTools.Method(
                typeof(NomaiComputer),
                "OnSectorOccupantsUpdated"
            );
            updateSectorOccupants?.Invoke(computer, null);
            computer.enabled = true;

            foreach (NomaiComputerRing ring in
                computerObject.GetComponentsInChildren<NomaiComputerRing>(
                    true
                ))
            {
                ring.enabled = true;
            }
        }

        private static void SetRevivalComputerSector(
            GameObject computerObject,
            OWRigidbody brittleHollow
        )
        {
            if (computerObject == null || brittleHollow == null)
            {
                return;
            }
            Sector sector = FindGravityCannonSector(
                brittleHollow.transform
            );
            NomaiComputer computer =
                computerObject.GetComponentInChildren<NomaiComputer>(true);
            if (computer != null && sector != null)
            {
                computer.SetSector(sector);
            }
        }

        private static Sector FindGravityCannonSector(
            Transform brittleHollow
        )
        {
            if (brittleHollow == null)
            {
                return null;
            }
            foreach (Sector candidate in
                brittleHollow.GetComponentsInChildren<Sector>(true))
            {
                if (candidate != null &&
                    candidate.name == "Sector_GravityCannon")
                {
                    return candidate;
                }
            }
            return null;
        }

        private static Transform FindGravityCannonInteractables(
            Transform brittleHollow
        )
        {
            if (brittleHollow == null)
            {
                return null;
            }
            foreach (Transform candidate in
                brittleHollow.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null &&
                    candidate.name == "Interactables_GravityCannon")
                {
                    return candidate;
                }
            }
            return null;
        }

        internal static void RestoreBrittleHollowVolumes(
            OWRigidbody brittleHollow
        )
        {
            GameObject playerDetector = Locator.GetPlayerDetector();
            if (brittleHollow == null || playerDetector == null)
            {
                return;
            }

            // Warping does not guarantee an OnTriggerEnter for the large
            // planetary volumes. Explicitly restore the same memberships the
            // player would receive by approaching Brittle Hollow normally.
            GravityVolume gravity = brittleHollow.GetAttachedGravityVolume();
            AddDetectorToVolume(gravity, playerDetector);

            foreach (PlanetoidRuleset ruleset in
                brittleHollow.GetComponentsInChildren<PlanetoidRuleset>(
                    true
                ))
            {
                if (ruleset != null && ruleset.enabled)
                {
                    AddDetectorToVolume(ruleset, playerDetector);
                }
            }
        }

        private static void AddDetectorToVolume(
            EffectVolume volume,
            GameObject detector
        )
        {
            if (volume == null || detector == null || !volume.enabled)
            {
                return;
            }
            OWTriggerVolume trigger = volume.GetOWTriggerVolume();
            if (trigger != null && !trigger.IsTrackingObject(detector))
            {
                trigger.AddObjectToVolume(detector);
            }
        }

        private static void RemoveDetectorFromVolume(
            EffectVolume volume,
            GameObject detector
        )
        {
            if (volume == null || detector == null || !volume.enabled)
            {
                return;
            }
            OWTriggerVolume trigger = volume.GetOWTriggerVolume();
            if (trigger != null && trigger.IsTrackingObject(detector))
            {
                trigger.RemoveObjectFromVolume(detector);
            }
        }

        /// <summary>
        /// Clears every Giant's Deep gravity, fluid and ruleset membership
        /// still tracked on the player. Used when the player revives out of
        /// the core prison so the old planet cannot keep pulling, drowning
        /// or accelerating them at the checkpoint or inside the ship.
        /// </summary>
        internal static void ClearGiantDeepVolumesFromPlayer(
            OWRigidbody giantsDeep
        )
        {
            GameObject playerDetector = Locator.GetPlayerDetector();
            if (giantsDeep == null || playerDetector == null)
            {
                return;
            }
            GravityVolume gravity = giantsDeep.GetAttachedGravityVolume();
            RemoveDetectorFromVolume(gravity, playerDetector);
            foreach (GravityVolume volume in
                giantsDeep.GetComponentsInChildren<GravityVolume>(true))
            {
                if (volume != null)
                {
                    RemoveDetectorFromVolume(volume, playerDetector);
                }
            }
            foreach (PlanetoidRuleset ruleset in
                giantsDeep.GetComponentsInChildren<PlanetoidRuleset>(true))
            {
                if (ruleset != null && ruleset.enabled)
                {
                    RemoveDetectorFromVolume(ruleset, playerDetector);
                }
            }
            foreach (FluidVolume fluid in
                giantsDeep.GetComponentsInChildren<FluidVolume>(true))
            {
                if (fluid != null)
                {
                    RemoveDetectorFromVolume(fluid, playerDetector);
                }
            }
        }

        /// <summary>
        /// Build118: after teleporting the player into the ship cockpit for
        /// a revive, register the ship's oxygen volumes with the player
        /// detector immediately. A warp does not fire the normal trigger
        /// enter events, so without this the player can sit with the helmet
        /// off and suffocate the moment the cockpit seat is left.
        /// </summary>
        internal static void RestoreShipOxygenVolumes(OWRigidbody shipBody)
        {
            GameObject playerDetector = Locator.GetPlayerDetector();
            if (shipBody == null || playerDetector == null)
            {
                return;
            }

            OxygenVolume[] volumes = shipBody
                .GetComponentsInChildren<OxygenVolume>(true);
            foreach (OxygenVolume volume in volumes)
            {
                if (volume != null && volume.enabled)
                {
                    AddDetectorToVolume(volume, playerDetector);
                }
            }
        }

        /// <summary>
        /// Detaches the player from any suspension body (for example Giant's
        /// Deep when the prison absorbed them) and restores a clean,
        /// non-kinematic, collidable physics state with zero velocity. Call
        /// before warping to a revival point so the old body can never leak
        /// velocity, gravity or water drag into the new position.
        /// </summary>
        internal static void ResetPlayerPhysicsForRevive(
            OWRigidbody playerBody
        )
        {
            if (playerBody == null)
            {
                return;
            }
            if (playerBody.IsSuspended())
            {
                playerBody.Unsuspend(false);
            }
            playerBody.MakeNonKinematic();
            playerBody.EnableCollisionDetection();
            playerBody.SetVelocity(Vector3.zero);
            playerBody.SetAngularVelocity(Vector3.zero);
        }

        /// <summary>
        /// Copies the source body's point velocity and angular velocity onto
        /// the player and snaps the underlying Unity rigidbody to the same
        /// values, so reviving inside a moving ship behaves exactly like
        /// unbuckling from the flight console.
        /// </summary>
        internal static void SyncPlayerVelocityToBody(
            OWRigidbody playerBody,
            OWRigidbody sourceBody
        )
        {
            if (playerBody == null || sourceBody == null)
            {
                return;
            }
            Vector3 position = playerBody.GetPosition();
            playerBody.SetVelocity(
                sourceBody.GetPointVelocity(position)
            );
            playerBody.SetAngularVelocity(
                sourceBody.GetAngularVelocity()
            );
            Rigidbody unityBody = playerBody.GetRigidbody();
            if (unityBody != null && !unityBody.isKinematic)
            {
                unityBody.velocity = playerBody.GetVelocity();
                unityBody.angularVelocity =
                    playerBody.GetAngularVelocity();
            }
        }

        private static float GetPlayerGravityMagnitude()
        {
            AlignmentForceDetector detector =
                Locator.GetPlayerForceDetector();
            return detector == null
                ? 0f
                : detector.GetForceAcceleration().magnitude;
        }

        private static string GetPlayerRulesetName()
        {
            RulesetDetector detector = Locator.GetPlayerRulesetDetector();
            PlanetoidRuleset ruleset =
                detector == null ? null : detector.GetPlanetoidRuleset();
            return ruleset == null ? "none" : ruleset.name;
        }

        private static void RestoreVanillaPlayerFunctions()
        {
            Harmony harmony = new Harmony("Known-Mouse.Return.SceneSix");
            UnpatchReturnPrefix(
                harmony,
                AccessTools.Method(typeof(PlayerResources), "UpdateOxygen")
            );
            UnpatchReturnPrefix(
                harmony,
                AccessTools.Method(
                    typeof(Flashlight),
                    nameof(Flashlight.TurnOn),
                    new[] { typeof(bool) }
                )
            );
            UnpatchReturnPrefix(
                harmony,
                AccessTools.Method(
                    typeof(ToolModeSwapper),
                    "EquipToolMode"
                )
            );
            UnpatchReturnPrefix(
                harmony,
                AccessTools.Method(typeof(NomaiTranslator), "EquipTool")
            );
            UnpatchReturnPrefix(
                harmony,
                AccessTools.Method(
                    typeof(ToolModeSwapper),
                    "IsTranslatorEquipPromptAllowed"
                )
            );
            UnpatchReturnPrefix(
                harmony,
                AccessTools.Method(
                    typeof(ToolModeSwapper),
                    "GetAutoEquipTranslator"
                )
            );
        }

        private static void UnpatchReturnPrefix(
            Harmony harmony,
            System.Reflection.MethodBase original
        )
        {
            if (original != null)
            {
                harmony.Unpatch(
                    original,
                    HarmonyPatchType.Prefix,
                    "Known-Mouse.Return"
                );
                harmony.Unpatch(
                    original,
                    HarmonyPatchType.Postfix,
                    "Known-Mouse.Return"
                );
            }
        }

        private static void RestoreSuitAndJetpack(OWRigidbody playerBody)
        {
            PlayerSpacesuit suit =
                playerBody.GetComponentInChildren<PlayerSpacesuit>(true);
            if (suit != null)
            {
                if (!suit.IsWearingSuit(false))
                {
                    suit.SuitUp(false, true, true);
                }
                else if (!suit.IsWearingHelmet())
                {
                    suit.PutOnHelmet();
                }
            }

            // Synchronize listeners even when the player object carried its
            // already-wearing state through the scene reset.
            GlobalMessenger.FireEvent("SuitUp");
            if (suit != null && suit.IsWearingHelmet())
            {
                GlobalMessenger.FireEvent("PutOnHelmet");
            }

            JetpackThrusterController jetpack =
                playerBody.GetComponent<JetpackThrusterController>();
            if (jetpack != null)
            {
                jetpack.enabled = true;
            }
            JetpackThrusterModel jetpackModel =
                playerBody.GetComponent<JetpackThrusterModel>();
            if (jetpackModel != null)
            {
                jetpackModel.enabled = true;
            }

            foreach (JetpackThrusterAudio audio in
                playerBody.GetComponentsInChildren<JetpackThrusterAudio>(true))
            {
                audio.enabled = true;
                foreach (AudioSource source in
                    audio.GetComponentsInChildren<AudioSource>(true))
                {
                    source.mute = false;
                }
            }
            foreach (ThrusterFlameController flame in
                playerBody.GetComponentsInChildren<ThrusterFlameController>(true))
            {
                flame.enabled = true;
            }
            foreach (ThrusterParticlesBehavior particles in
                playerBody.GetComponentsInChildren<ThrusterParticlesBehavior>(true))
            {
                particles.enabled = true;
            }
            foreach (ThrusterWashController wash in
                playerBody.GetComponentsInChildren<ThrusterWashController>(true))
            {
                wash.enabled = true;
            }
        }

        private static void RestoreFlashlight()
        {
            Flashlight flashlight = Locator.GetFlashlight();
            if (flashlight == null)
            {
                return;
            }
            flashlight.enabled = true;
            foreach (OWLight2 light in flashlight.GetLights())
            {
                if (light != null)
                {
                    light.enabled = true;
                }
            }
        }

        private static void EnableShipHUDMarker()
        {
            // The stock marker is intentionally hidden until the player has
            // entered the ship once. Scene 6 supplies the ship remotely, so
            // satisfy that stock prerequisite without changing inside-ship
            // state or firing entrance gameplay events.
            var enteredShipField = AccessTools.Field(
                typeof(PlayerState),
                "_hasPlayerEnteredShip"
            );
            if (enteredShipField != null)
            {
                enteredShipField.SetValue(null, true);
            }

            foreach (ShipHUDMarker marker in
                Resources.FindObjectsOfTypeAll<ShipHUDMarker>())
            {
                if (marker == null || !marker.gameObject.scene.IsValid())
                {
                    continue;
                }
                var refresh = AccessTools.Method(
                    marker.GetType(),
                    "RefreshOwnVisibility"
                );
                if (refresh != null)
                {
                    refresh.Invoke(marker, null);
                }
            }
        }

        private static void ForceGravityCannonSectorLoaded(
            OWRigidbody brittleHollow
        )
        {
            PlayerSectorDetector playerDetector =
                Locator.GetPlayerSectorDetector();
            if (playerDetector == null || brittleHollow == null)
            {
                return;
            }

            foreach (Sector sector in
                brittleHollow.GetComponentsInChildren<Sector>(true))
            {
                if (sector == null)
                {
                    continue;
                }

                bool isBrittleRoot =
                    sector.GetName() == Sector.Name.BrittleHollow;
                bool isGravityCannon =
                    sector.name == "Sector_GravityCannon";
                if (isBrittleRoot || isGravityCannon)
                {
                    sector.AddOccupant(playerDetector);
                }
            }
        }

        private static void AddSuitVisualHider(OWRigidbody playerBody)
        {
            SceneSixSuitVisualHider hider =
                playerBody.GetComponent<SceneSixSuitVisualHider>();
            if (hider == null)
            {
                hider = playerBody.gameObject.AddComponent<
                    SceneSixSuitVisualHider
                >();
            }
            hider.HideNow();
        }

        private static void FindShipPlacement(
            Transform spawn,
            OWRigidbody brittleHollow,
            out Vector3 position,
            out Quaternion rotation
        )
        {
            Vector3 up = spawn.up.normalized;
            Vector3 right = Vector3.ProjectOnPlane(
                spawn.right,
                up
            ).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(
                spawn.forward,
                up
            ).normalized;
            if (right.sqrMagnitude < 0.01f)
            {
                right = Vector3.Cross(up, Vector3.forward).normalized;
            }
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.Cross(right, up).normalized;
            }

            float[] radii = { 22f, 28f, 35f, 42f };
            for (int radiusIndex = 0;
                radiusIndex < radii.Length;
                radiusIndex++)
            {
                for (int directionIndex = 0;
                    directionIndex < 8;
                    directionIndex++)
                {
                    float angle = directionIndex * 45f;
                    Vector3 direction =
                        Quaternion.AngleAxis(angle, up) * right;
                    Vector3 desired =
                        spawn.position + direction * radii[radiusIndex];
                    RaycastHit hit;
                    if (!TryGetBrittleHollowSurface(
                            desired,
                            up,
                            brittleHollow,
                            out hit
                        ))
                    {
                        continue;
                    }

                    Vector3 surfaceNormal = hit.normal.normalized;
                    if (Vector3.Dot(surfaceNormal, up) < 0.55f)
                    {
                        continue;
                    }
                    Vector3 candidate =
                        hit.point + surfaceNormal * 5.5f;
                    Quaternion candidateRotation =
                        MakeSurfaceRotation(forward, surfaceNormal);
                    if (IsShipSpaceClear(
                            candidate,
                            candidateRotation,
                            hit.collider,
                            brittleHollow
                        ))
                    {
                        position = candidate;
                        rotation = candidateRotation;
                        return;
                    }
                }
            }

            // No safe landing patch was found. Keep the ship in open air
            // above the cannon instead of forcing it into terrain.
            position = spawn.position + up * 30f + right * 24f;
            rotation = MakeSurfaceRotation(forward, up);
        }

        private static bool TryGetBrittleHollowSurface(
            Vector3 desired,
            Vector3 up,
            OWRigidbody brittleHollow,
            out RaycastHit bestHit
        )
        {
            bestHit = new RaycastHit();
            float bestDistance = float.PositiveInfinity;
            RaycastHit[] hits = Physics.RaycastAll(
                desired + up * 35f,
                -up,
                80f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider != null &&
                    hit.distance < bestDistance &&
                    IsChildOf(
                        hit.collider.transform,
                        brittleHollow.transform
                    ))
                {
                    bestDistance = hit.distance;
                    bestHit = hit;
                }
            }
            return bestDistance < float.PositiveInfinity;
        }

        private static Quaternion MakeSurfaceRotation(
            Vector3 preferredForward,
            Vector3 surfaceNormal
        )
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                preferredForward,
                surfaceNormal
            ).normalized;
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.Cross(
                    surfaceNormal,
                    Vector3.right
                ).normalized;
            }
            return Quaternion.LookRotation(forward, surfaceNormal);
        }

        private static bool IsShipSpaceClear(
            Vector3 position,
            Quaternion rotation,
            Collider landingSurface,
            OWRigidbody brittleHollow
        )
        {
            Collider[] overlaps = Physics.OverlapBox(
                position,
                new Vector3(4.25f, 3.25f, 5.25f),
                rotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );
            foreach (Collider overlap in overlaps)
            {
                if (overlap == null ||
                    overlap == landingSurface ||
                    overlap.transform.IsChildOf(Locator.GetShipTransform()) ||
                    overlap.transform.IsChildOf(Locator.GetPlayerTransform()))
                {
                    continue;
                }
                if (IsChildOf(overlap.transform, brittleHollow.transform))
                {
                    return false;
                }
            }
            return true;
        }

        private static void WarpWithBodyVelocity(
            OWRigidbody target,
            OWRigidbody referenceBody,
            Vector3 position,
            Quaternion rotation
        )
        {
            target.WarpToPositionRotation(position, rotation);
            target.SetVelocity(referenceBody.GetPointVelocity(position));
            target.SetAngularVelocity(referenceBody.GetAngularVelocity());
        }

        private static void WarpAtLocalTransform(
            OWRigidbody target,
            OWRigidbody referenceBody,
            Vector3 localPosition,
            Quaternion localRotation
        )
        {
            Vector3 worldPosition =
                referenceBody.transform.TransformPoint(localPosition);
            Quaternion worldRotation =
                referenceBody.GetRotation() * localRotation;
            WarpWithBodyVelocity(
                target,
                referenceBody,
                worldPosition,
                worldRotation
            );
        }

        private static Transform FindBrittleHollowGravityCannonSpawn(
            Transform brittleHollow
        )
        {
            if (brittleHollow == null)
            {
                return null;
            }
            foreach (Transform candidate in
                brittleHollow.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null &&
                    candidate.name == "SPAWN_GravityCannon")
                {
                    return candidate;
                }
            }
            return null;
        }

        private static OWRigidbody FindBody(string name)
        {
            foreach (OWRigidbody body in
                Resources.FindObjectsOfTypeAll<OWRigidbody>())
            {
                if (body != null &&
                    body.gameObject.scene.IsValid() &&
                    body.name == name)
                {
                    return body;
                }
            }
            return null;
        }

        private static bool IsChildOf(Transform child, Transform parent)
        {
            if (child == null || parent == null)
            {
                return false;
            }
            Transform current = child;
            while (current != null)
            {
                if (current == parent)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }

    internal sealed class SceneSixSuitVisualHider : MonoBehaviour
    {
        private Renderer[] _renderers;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void LateUpdate()
        {
            HideNow();
        }

        public void HideNow()
        {
            if (_renderers == null)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                // Hide only the physical third-person suit meshes. Broad
                // path matching also catches HelmetHUD renderers, which makes
                // the oxygen/fuel panel disappear.
                string name = renderer.name.ToLowerInvariant();
                if (name.StartsWith(
                        "traveller_mesh_v01:playersuit_"
                    ))
                {
                    renderer.enabled = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnStartSceneLoad")]
    internal static class SceneSixStartLoadPatch
    {
        private static bool Prefix(OWScene newScene)
        {
            if (newScene == OWScene.SolarSystem)
            {
                SceneSixController.RestoreActiveStateFromSave();
            }
            return !(SceneSixController.IsActive &&
                newScene == OWScene.SolarSystem);
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class SceneSixCompleteLoadPatch
    {
        private static bool Prefix(
            ReturnMod __instance,
            OWScene newScene
        )
        {
            if (newScene == OWScene.SolarSystem)
            {
                SceneSixController.RestoreActiveStateFromSave();
            }
            if (!SceneSixController.IsActive ||
                newScene != OWScene.SolarSystem)
            {
                return true;
            }
            __instance.StartCoroutine(
                SceneSixController.Prepare(__instance)
            );
            return false;
        }
    }

    [HarmonyPatch(
        typeof(TranslatorWord),
        MethodType.Constructor,
        new[]
        {
            typeof(string),
            typeof(int),
            typeof(int),
            typeof(bool),
            typeof(float)
        }
    )]
    internal static class RevivalComputerDeadlinePatch
    {
        private static void Prefix(ref string translatedText)
        {
            if (string.IsNullOrEmpty(translatedText))
            {
                return;
            }

            bool isRevivalComputer =
                translatedText.Contains(
                    "Revival will become impossible"
                ) ||
                translatedText.Contains("分钟后将无法复活") ||
                translatedText.Contains("<TimeMinutesRemaining>") ||
                (translatedText.StartsWith(
                        "<TimeMinutesRemaining>"
                    ) &&
                    (translatedText.EndsWith(".") ||
                     translatedText.EndsWith("。")));
            if (!isRevivalComputer)
            {
                return;
            }

            string minutes = InterloperTrajectoryController
                .GetRevivalMinutesRemaining()
                .ToString();
            translatedText = translatedText.Replace(
                "<TimeMinutesRemaining>",
                minutes
            );
            translatedText = Regex.Replace(
                translatedText,
                @"\d+\s+minutes",
                minutes + " minutes",
                RegexOptions.IgnoreCase
            );
            translatedText = Regex.Replace(
                translatedText,
                @"\d+\s*分钟后将无法复活",
                minutes + "分钟后将无法复活"
            );
        }
    }
}
