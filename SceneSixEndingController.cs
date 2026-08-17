using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Return
{
    internal enum ReturnEndingType
    {
        Victory
    }

    /// <summary>
    /// Owns Scene 6's portal-impact recovery, terminal failure ending and
    /// victory ending without changing the locked SceneSixController.
    /// </summary>
    internal static class SceneSixEndingController
    {
        private const float PortalImpactWindowSeconds = 15f;
        private static readonly Vector3 RevivalLocalPosition =
            new Vector3(-224.8589f, 0.8171f, 92.6140f);
        private static readonly Quaternion RevivalLocalRotation =
            new Quaternion(
                -0.177269f,
                -0.096406f,
                -0.683298f,
                -0.701703f
            );

        private static ReturnMod _mod;
        private static ReturnEndingOverlay _overlay;
        private static bool _playerUsedPortal;
        private static float _lastPortalTransitTime;
        private static bool _portalRecoveryActive;
        private static bool _terminalDeathPending;
        private static bool _endingActive;
        private static bool _prisonReviveGraceActive;
        private static float _prisonReviveGraceUntil;
        private static float _reviveImpactImmunityUntil =
            float.NegativeInfinity;

        public static bool IsEndingActive => _endingActive;

        public static void Prepare(ReturnMod mod)
        {
            _mod = mod;
            _playerUsedPortal = false;
            _lastPortalTransitTime = float.NegativeInfinity;
            _portalRecoveryActive = false;
            _terminalDeathPending = false;
            _endingActive = false;
            _prisonReviveGraceActive = false;
            _prisonReviveGraceUntil = float.NegativeInfinity;
            _reviveImpactImmunityUntil = float.NegativeInfinity;

            if (mod == null)
            {
                return;
            }
            _overlay = mod.GetComponent<ReturnEndingOverlay>();
            if (_overlay == null)
            {
                _overlay = mod.gameObject.AddComponent<ReturnEndingOverlay>();
            }
            _overlay.Initialize(mod);
        }

        public static void MarkPlayerPortalTransit()
        {
            if (SceneSixController.IsActive)
            {
                _playerUsedPortal = true;
                _lastPortalTransitTime = Time.time;
            }
        }

        public static void ClearPlayerPortalTransit()
        {
            _playerUsedPortal = false;
            _lastPortalTransitTime = float.NegativeInfinity;
        }

        /// <summary>
        /// Short window after a prison revive during which impact deaths
        /// are intercepted so the physics engine cannot kill the player
        /// while the planet frame velocity settles.
        /// </summary>
        public static void ArmPrisonReviveGrace(float seconds)
        {
            _prisonReviveGraceActive = true;
            _prisonReviveGraceUntil = Time.time + seconds;
        }

        /// <summary>
        /// Suppresses vanilla impact damage (and therefore impact deaths)
        /// for a short window after any revive path, giving the physics
        /// engine time to re-settle the player on the planet frame or in
        /// the ship seat without the old body's velocity killing them.
        /// </summary>
        public static void ArmReviveImpactImmunity(float seconds)
        {
            float until = Time.time + seconds;
            if (until > _reviveImpactImmunityUntil)
            {
                _reviveImpactImmunityUntil = until;
            }
        }

        public static bool IsReviveImpactImmunityActive
        {
            get
            {
                return SceneSixController.IsActive &&
                    Time.time < _reviveImpactImmunityUntil;
            }
        }

        /// <summary>
        /// Keeps the player's velocity locked to the revival body for the
        /// first fixed frames after a checkpoint revive or the Scene 6
        /// spawn. This stops the old body's velocity or a recently falling
        /// planet fragment from pushing the player into a fatal impact
        /// while the physics engine settles.
        /// </summary>
        public static void StartReviveVelocityGuard(
            OWRigidbody playerBody,
            OWRigidbody sourceBody,
            int frameCount
        )
        {
            if (_mod == null || playerBody == null || sourceBody == null)
            {
                return;
            }
            _mod.StartCoroutine(
                GuardRevivalVelocity(playerBody, sourceBody, frameCount)
            );
        }

        private static IEnumerator GuardRevivalVelocity(
            OWRigidbody playerBody,
            OWRigidbody sourceBody,
            int frameCount
        )
        {
            for (int frame = 0; frame < frameCount; frame++)
            {
                yield return new WaitForFixedUpdate();
                if (playerBody == null || sourceBody == null)
                {
                    yield break;
                }
                Vector3 targetVelocity =
                    sourceBody.GetPointVelocity(playerBody.GetPosition());
                playerBody.SetVelocity(targetVelocity);
                playerBody.SetAngularVelocity(
                    sourceBody.GetAngularVelocity()
                );
                Rigidbody unityBody = playerBody.GetRigidbody();
                if (unityBody != null && !unityBody.isKinematic)
                {
                    unityBody.velocity = targetVelocity;
                    unityBody.angularVelocity =
                        sourceBody.GetAngularVelocity();
                }
            }
        }

        public static void MarkTerminalDeath()
        {
            if (!SceneSixController.IsActive)
            {
                return;
            }
            _terminalDeathPending = true;
            ClearPlayerPortalTransit();
        }

        public static bool TryHandleLoopDeath(DeathManager deathManager)
        {
            if (!SceneSixController.IsActive ||
                deathManager == null ||
                _terminalDeathPending ||
                _endingActive ||
                _portalRecoveryActive)
            {
                return false;
            }

            bool prisonGraceImpact =
                _prisonReviveGraceActive &&
                Time.time < _prisonReviveGraceUntil &&
                deathManager.GetDeathType() == DeathType.Impact;
            if (prisonGraceImpact && _mod != null)
            {
                _portalRecoveryActive = true;
                _mod.StartCoroutine(
                    RecoverInsideLoop(deathManager, false)
                );
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN LOOP RECOVERY] Intercepted prison-revive " +
                    "impact; loop time will be preserved.",
                    MessageType.Success
                );
                return true;
            }

            bool recentPortalImpact =
                deathManager.GetDeathType() == DeathType.Impact &&
                _playerUsedPortal &&
                Time.time - _lastPortalTransitTime <=
                    PortalImpactWindowSeconds;
            if (_mod == null)
            {
                return false;
            }

            if (deathManager.GetDeathType() == DeathType.Meditation)
            {
                _portalRecoveryActive = true;
                _mod.StartCoroutine(RecoverToFreshLoop(deathManager));
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN MEDITATION] Pause-menu meditation " +
                    "intercepted; starting a fresh 17-minute loop at " +
                    "the gravity cannon.",
                    MessageType.Success
                );
                return true;
            }

            _portalRecoveryActive = true;
            _mod.StartCoroutine(
                RecoverInsideLoop(deathManager, recentPortalImpact)
            );
            _mod.ModHelper.Console.WriteLine(
                "[RETURN LOOP RECOVERY] Intercepted " +
                deathManager.GetDeathType() +
                "; showPortalHint=" + recentPortalImpact +
                "; loop time will be preserved.",
                MessageType.Success
            );
            return true;
        }

        private static IEnumerator RecoverInsideLoop(
            DeathManager deathManager,
            bool showPortalHint
        )
        {
            CharacterDialogueTree dialogue = null;
            Exception setupException = null;
            if (showPortalHint)
            {
                try
                {
                    OWRigidbody brittleHollow =
                        InterloperTrajectoryController.FindBody(
                            "BrittleHollow_Body"
                        );
                    if (_mod.NewHorizons == null || brittleHollow == null)
                    {
                        throw new InvalidOperationException(
                            "The dialogue service or Brittle Hollow was " +
                            "unavailable."
                        );
                    }

                    var spawned = _mod.NewHorizons.SpawnDialogue(
                        _mod,
                        brittleHollow.gameObject,
                        "dialogue/portal_impact_hint.xml",
                        0f,
                        0f,
                        null,
                        0f
                    );
                    dialogue = spawned.Item1;
                    if (dialogue == null)
                    {
                        throw new InvalidOperationException(
                            "The portal-impact dialogue could not be created."
                        );
                    }
                    dialogue.gameObject.name =
                        "Return_PortalImpactRecoveryDialogue";
                }
                catch (Exception exception)
                {
                    setupException = exception;
                }

                if (setupException == null)
                {
                    yield return null;
                    dialogue.StartConversation();
                    while (dialogue != null && dialogue.InConversation())
                    {
                        yield return null;
                    }
                }
                else
                {
                    _mod.ModHelper.Console.WriteLine(
                        "[RETURN LOOP RECOVERY] Dialogue failed; reviving " +
                        "without leaving the player dead: " + setupException,
                        MessageType.Error
                    );
                }
            }
            else
            {
                yield return null;
            }

            ReviveAtCheckpoint(deathManager);
            if (dialogue != null)
            {
                UnityEngine.Object.Destroy(dialogue.gameObject);
            }
            _portalRecoveryActive = false;
        }

        private static IEnumerator RecoverToFreshLoop(
            DeathManager deathManager
        )
        {
            // Let the vanilla meditation death fade settle, then start
            // a brand-new 17-minute loop at the gravity-cannon spawn.
            yield return new WaitForSecondsRealtime(1.2f);

            try
            {
                Traverse death = Traverse.Create(deathManager);
                death.Field("_isDying").SetValue(false);
                death.Field("_isDead").SetValue(false);
                death.Field("_resurrectAfterDelay").SetValue(false);
                death.Field("_fakeMeditationDeath").SetValue(false);
                deathManager.enabled = false;

                ReturnPortalPlayerDetachment.DetachFromPlayerBeforeRevive();
                ClearPlayerPortalTransit();
                SceneSixMainMenuResetController.PrepareFreshLoopReset();

                PauseCommandListener pause =
                    Locator.GetPauseCommandListener();
                if (pause != null)
                {
                    pause.RemovePauseCommandLock();
                }
                OWTime.SetTimeScale(1f);
                OWInput.ChangeInputMode(InputMode.Character);
                Physics.SyncTransforms();
            }
            catch (Exception exception)
            {
                _mod?.ModHelper.Console.WriteLine(
                    "[RETURN MEDITATION] Fresh-loop cleanup failed: " +
                    exception,
                    MessageType.Error
                );
            }

            _portalRecoveryActive = false;
            yield return null;
            _mod?.ModHelper.Console.WriteLine(
                "[RETURN MEDITATION] Starting the fresh loop.",
                MessageType.Success
            );
            LoadManager.LoadScene(
                OWScene.SolarSystem,
                LoadManager.FadeType.ToBlack,
                1f,
                true
            );
        }

        private static void ReviveAtCheckpoint(DeathManager deathManager)
        {
            try
            {
                Traverse death = Traverse.Create(deathManager);
                death.Field("_isDying").SetValue(false);
                death.Field("_isDead").SetValue(false);
                death.Field("_resurrectAfterDelay").SetValue(false);
                death.Field("_fakeMeditationDeath").SetValue(false);
                deathManager.enabled = false;

                GlobalMessenger.FireEvent("PlayerResurrection");
                RestorePlayerResourcesAndVisor();
                RestoreDeathAudioMix();
                RestoreGameplayInterfaces();
                PauseCommandListener pause =
                    Locator.GetPauseCommandListener();
                if (pause != null)
                {
                    pause.RemovePauseCommandLock();
                }

                OWRigidbody brittleHollow =
                    InterloperTrajectoryController.FindBody(
                        "BrittleHollow_Body"
                    );
                OWRigidbody playerBody = Locator.GetPlayerBody();
                if (brittleHollow == null || playerBody == null)
                {
                    throw new InvalidOperationException(
                        "The revival checkpoint bodies were unavailable."
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
                SceneSixController.ClearGiantDeepVolumesFromPlayer(
                    InterloperTrajectoryController.FindBody(
                        "GiantsDeep_Body"
                    )
                );
                SceneSixController.RestoreBrittleHollowVolumes(
                    brittleHollow
                );

                PlayerLockOnTargeting lockOn =
                    Locator.GetPlayerTransform()
                        .GetComponent<PlayerLockOnTargeting>();
                if (lockOn != null)
                {
                    lockOn.BreakLock();
                }
                Physics.SyncTransforms();
                OWInput.ChangeInputMode(InputMode.Character);
                ReticleController.Show();
                PromptManager prompts = Locator.GetPromptManager();
                if (prompts != null)
                {
                    prompts.SetPromptsVisible(true);
                }
                ClearPlayerPortalTransit();
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN LOOP RECOVERY] Player revived at the " +
                    "Brittle Hollow checkpoint; loop seconds=" +
                    InterloperTrajectoryController
                        .GetSceneSixElapsedSeconds().ToString("F2") + ".",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                _mod?.ModHelper.Console.WriteLine(
                    "[RETURN LOOP RECOVERY] Revival failed: " + exception,
                    MessageType.Error
                );
            }
        }

        public static void ReviveAtCheckpointFromMenu()
        {
            if (!SceneSixController.IsActive)
            {
                return;
            }
            if (Build111SimpleCorePrisonController.IsPlayerTrapped)
            {
                return;
            }

            DeathManager deathManager = Locator.GetDeathManager();
            if (deathManager == null)
            {
                _mod?.ModHelper.Console.WriteLine(
                    "[RETURN MENU REVIVE] DeathManager was unavailable; " +
                    "cannot revive at the checkpoint.",
                    MessageType.Error
                );
                return;
            }

            ReviveAtCheckpoint(deathManager);
            _mod?.ModHelper.Console.WriteLine(
                "[RETURN MENU REVIVE] Player revived at the Brittle " +
                "Hollow base from the pause menu; loop time preserved " +
                "at " +
                InterloperTrajectoryController
                    .GetSceneSixElapsedSeconds().ToString("F2") +
                " seconds.",
                MessageType.Success
            );
        }

        internal static void RestorePlayerResourcesAndVisor()
        {
            OWRigidbody playerBody = Locator.GetPlayerBody();
            PlayerResources resources = playerBody == null
                ? null
                : playerBody.GetComponent<PlayerResources>();
            if (resources != null)
            {
                resources.DebugRefillResources();
            }

            foreach (VisorEffectController visor in
                Resources.FindObjectsOfTypeAll<VisorEffectController>())
            {
                if (visor == null ||
                    !visor.gameObject.scene.IsValid())
                {
                    continue;
                }

                Traverse visorState = Traverse.Create(visor);
                visorState.Field("_cracked").SetValue(false);
                Renderer crackRenderer = visorState
                    .Field("_crackEffectRenderer")
                    .GetValue<Renderer>();
                if (crackRenderer != null)
                {
                    crackRenderer.enabled = false;
                    crackRenderer.material.SetFloat(
                        Shader.PropertyToID("_Cutoff"),
                        1f
                    );
                }
            }
        }

        private static void RestoreDeathAudioMix()
        {
            OWAudioMixer mixer = Locator.GetAudioMixer();
            if (mixer == null)
            {
                return;
            }

            Traverse mixerState = Traverse.Create(mixer);
            mixerState.Field("_deathMixed").SetValue(false);
            AudioParameter nonEndTimes = mixerState
                .Field("_nonEndTimesVolume")
                .GetValue<AudioParameter>();
            AudioParameter endTimes = mixerState
                .Field("_endTimesVolume")
                .GetValue<AudioParameter>();
            nonEndTimes?.FadeTo(1f, 0.25f);
            endTimes?.FadeTo(1f, 0.25f);
        }

        private static void RestoreGameplayInterfaces()
        {
            foreach (HUDCanvas hud in
                Resources.FindObjectsOfTypeAll<HUDCanvas>())
            {
                if (hud != null &&
                    hud.gameObject.scene.IsValid() &&
                    hud.gameObject.activeInHierarchy)
                {
                    hud.enabled = true;
                }
            }

            OWCamera activeCamera = Locator.GetActiveCamera();
            foreach (ReferenceFrameGUI referenceFrameGui in
                Resources.FindObjectsOfTypeAll<ReferenceFrameGUI>())
            {
                if (referenceFrameGui == null ||
                    !referenceFrameGui.gameObject.scene.IsValid() ||
                    !referenceFrameGui.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (activeCamera != null)
                {
                    Traverse.Create(referenceFrameGui)
                        .Method("OnSwitchActiveCamera", activeCamera)
                        .GetValue();
                }
                referenceFrameGui.enabled = true;
            }

            foreach (ThrustAndAttitudeIndicator indicator in
                Resources.FindObjectsOfTypeAll<
                    ThrustAndAttitudeIndicator>())
            {
                if (indicator != null &&
                    indicator.gameObject.scene.IsValid() &&
                    indicator.gameObject.activeInHierarchy)
                {
                    indicator.enabled = true;
                }
            }

            MapController map = Locator.GetMapController();
            if (map != null)
            {
                map.enabled = true;
            }
        }

        public static bool TrySetTerminalGameOver(
            GameOverController controller
        )
        {
            if (!SceneSixController.IsActive ||
                !_terminalDeathPending ||
                controller == null)
            {
                return false;
            }

            _terminalDeathPending = false;
            _endingActive = true;
            EnsureOverlay();
            if (_overlay == null)
            {
                return false;
            }

            PlayerData.SetPersistentCondition(
                "GAME_OVER_LAST_SAVE",
                true
            );
            SceneSixController.MarkRevivalCheckpoint();
            TimeLoop.SetTimeLoopEnabled(false);
            _overlay.ShowDeath();
            _mod?.ModHelper.Console.WriteLine(
                "[RETURN ENDING] Terminal death displayed through the " +
                "shared orange ending presentation.",
                MessageType.Success
            );
            return true;
        }

        public static void BeginVictoryEnding()
        {
            if (!SceneSixController.IsActive || _endingActive)
            {
                return;
            }

            EnsureOverlay();
            if (_overlay == null)
            {
                return;
            }

            _endingActive = true;
            SceneSixController.MarkRevivalCheckpoint();
            TimeLoop.SetTimeLoopEnabled(false);
            _overlay.Show();
            _mod?.ModHelper.Console.WriteLine(
                "[RETURN ENDING] True ending started after the Interloper " +
                "entered the Giant's Deep core portal.",
                MessageType.Success
            );
        }

        private static string Translate(string key)
        {
            if (_mod == null || _mod.NewHorizons == null)
            {
                return key;
            }
            string translated = _mod.NewHorizons.GetTranslationForUI(key);
            return string.IsNullOrEmpty(translated) ? key : translated;
        }

        private static void EnsureOverlay()
        {
            if (_overlay == null && _mod != null)
            {
                _overlay = _mod.GetComponent<ReturnEndingOverlay>();
                if (_overlay == null)
                {
                    _overlay = _mod.gameObject.AddComponent<
                        ReturnEndingOverlay>();
                }
                _overlay.Initialize(_mod);
            }
        }
    }

    internal sealed class ReturnEndingOverlay : MonoBehaviour
    {
        private const float DisplaySeconds = 14f;

        private ReturnMod _mod;
        private bool _visible;
        private bool _loading;
        private string _text;
        private float _startTime;
        private Font _font;
        private bool _useFinalCreditsMusic;
        private bool _pausedForPresentation;

        public void Initialize(ReturnMod mod)
        {
            _mod = mod;
            _visible = false;
            _loading = false;
            _useFinalCreditsMusic = false;
            _pausedForPresentation = false;
        }

        public void Show()
        {
            ShowText("$RETURN_TRUE_ENDING", true, false);
        }

        public void ShowDeath()
        {
            ShowText("$RETURN_DEATH_ENDING", false, true);
        }

        private void ShowText(
            string translationKey,
            bool useFinalCreditsMusic,
            bool pauseWorld
        )
        {
            _text = Translate(translationKey);
            _visible = true;
            _loading = false;
            _useFinalCreditsMusic = useFinalCreditsMusic;
            _startTime = Time.unscaledTime;
            if (pauseWorld &&
                !OWTime.IsPaused(OWTime.PauseType.Reading))
            {
                OWTime.Pause(OWTime.PauseType.Reading);
                _pausedForPresentation = true;
            }
            OWInput.ChangeInputMode(InputMode.None);
            ReticleController.Hide();
            PromptManager prompts = Locator.GetPromptManager();
            if (prompts != null)
            {
                prompts.SetPromptsVisible(false);
            }
        }

        internal bool UsesFinalCreditsMusic()
        {
            return _useFinalCreditsMusic;
        }

        internal void HideBeforeCredits()
        {
            _visible = false;
            if (_pausedForPresentation)
            {
                OWTime.Unpause(OWTime.PauseType.Reading);
                _pausedForPresentation = false;
            }
        }

        private void Update()
        {
            if (!_visible || _loading ||
                Time.unscaledTime - _startTime < DisplaySeconds)
            {
                return;
            }

            _loading = true;
            LoadManager.LoadScene(
                OWScene.Credits_Final,
                LoadManager.FadeType.ToBlack,
                1f,
                true
            );
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture
            );
            GUI.color = previousColor;

            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Arial" },
                    42
                );
            }

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.font = _font;
            style.fontSize = Mathf.RoundToInt(
                Mathf.Clamp(Screen.height * 0.034f, 24f, 42f)
            );
            style.alignment = TextAnchor.MiddleCenter;
            style.wordWrap = true;
            style.normal.textColor = Color.white;

            float marginX = Screen.width * 0.12f;
            float marginY = Screen.height * 0.1f;
            GUI.Label(
                new Rect(
                    marginX,
                    marginY,
                    Screen.width - marginX * 2f,
                    Screen.height - marginY * 2f
                ),
                _text,
                style
            );
        }

        private string Translate(string key)
        {
            if (_mod == null || _mod.NewHorizons == null)
            {
                return key;
            }
            string translated = _mod.NewHorizons.GetTranslationForUI(key);
            return string.IsNullOrEmpty(translated) ? key : translated;
        }
    }

    [HarmonyPatch(typeof(DeathManager), "FinishDeathSequence")]
    internal static class ReturnSceneSixDeathRecoveryPatch
    {
        private static bool Prefix(DeathManager __instance)
        {
            return !SceneSixEndingController.TryHandleLoopDeath(__instance);
        }
    }

    [HarmonyPatch(
        typeof(GameOverController),
        "OnTriggerDeathOutsideTimeLoop"
    )]
    internal static class ReturnSceneSixTerminalGameOverPatch
    {
        private static bool Prefix(GameOverController __instance)
        {
            return !SceneSixEndingController.TrySetTerminalGameOver(
                __instance
            );
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class ReturnSceneSixEndingPreparePatch
    {
        private static void Postfix(
            ReturnMod __instance,
            OWScene newScene
        )
        {
            if (newScene == OWScene.SolarSystem &&
                SceneSixController.IsActive)
            {
                SceneSixEndingController.Prepare(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerResources), "OnImpact")]
    internal static class ReturnSceneSixReviveImpactImmunityPatch
    {
        private static bool Prefix()
        {
            return !SceneSixEndingController.IsReviveImpactImmunityActive;
        }
    }
}
