using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Build109: inserts Daz's final conversation before the already-stable
    /// Scene 6 victory presentation. If dialogue setup fails, the original
    /// ending starts immediately so the player can never be trapped.
    /// </summary>
    internal static class SceneSixVictoryDialogueController
    {
        private const string DialoguePath =
            "dialogue/scene_six_victory.xml";

        private static readonly Vector3 RevivalLocalPosition =
            new Vector3(-224.8589f, 0.8171f, 92.6140f);
        private static readonly Quaternion RevivalLocalRotation =
            new Quaternion(
                -0.177269f,
                -0.096406f,
                -0.683298f,
                -0.701703f
            );

        private static bool _running;
        private static bool _bypassInterception;
        private static bool _pausedForDialogue;
        private static bool _movementLocked;
        private static int _generation;
        private static CharacterDialogueTree _dialogue;
        private static PlayerCameraEffectController _cameraEffects;

        internal static bool TryIntercept()
        {
            if (_bypassInterception ||
                !SceneSixController.IsActive ||
                SceneSixEndingController.IsEndingActive)
            {
                return false;
            }

            if (_running)
            {
                return true;
            }

            ReturnMod mod = ReturnMod.Instance;
            if (mod == null || mod.NewHorizons == null)
            {
                return false;
            }

            _running = true;
            int generation = ++_generation;
            mod.StartCoroutine(PlayThenContinue(mod, generation));
            mod.ModHelper.Console.WriteLine(
                "[RETURN BUILD109] True ending paused for Daz's final " +
                "conversation.",
                MessageType.Success
            );
            return true;
        }

        internal static void Reset()
        {
            _generation++;
            _running = false;
            _bypassInterception = false;
            ReleaseDialogueState();
            DestroyTemporaryObjects();
        }

        private static IEnumerator PlayThenContinue(
            ReturnMod mod,
            int generation
        )
        {
            TimeLoop.SetTimeLoopEnabled(false);
            ReticleController.Hide();
            PromptManager prompts = Locator.GetPromptManager();
            if (prompts != null)
            {
                prompts.SetPromptsVisible(false);
            }

            _cameraEffects = FindSceneComponent<
                PlayerCameraEffectController
            >();
            if (_cameraEffects != null)
            {
                _cameraEffects.CloseEyes(0.2f);
            }

            // Give the vanilla eye mask time to close before moving the
            // player. Unlike an OnGUI cover, this remains behind dialogue UI.
            yield return new WaitForSecondsRealtime(0.25f);

            Exception setupException = null;
            try
            {
                WarpPlayerToRevivalCheckpoint();

                PlayerCharacterController playerController =
                    Locator.GetPlayerController();
                if (playerController != null)
                {
                    playerController.LockMovement(true);
                    _movementLocked = true;
                }

                if (!OWTime.IsPaused(OWTime.PauseType.Reading))
                {
                    OWTime.Pause(OWTime.PauseType.Reading);
                    _pausedForDialogue = true;
                }
            }
            catch (Exception exception)
            {
                setupException = exception;
            }

            if (setupException == null)
            {
                try
                {
                    OWRigidbody host =
                        InterloperTrajectoryController.FindBody(
                            "BrittleHollow_Body"
                        ) ?? Locator.GetPlayerBody();
                    if (host == null)
                    {
                        throw new InvalidOperationException(
                            "No dialogue host was available."
                        );
                    }

                    var spawned = mod.NewHorizons.SpawnDialogue(
                        mod,
                        host.gameObject,
                        DialoguePath,
                        0f,
                        0f,
                        null,
                        0f
                    );
                    _dialogue = spawned.Item1;
                    if (_dialogue == null)
                    {
                        throw new InvalidOperationException(
                            "The victory dialogue could not be created."
                        );
                    }

                    _dialogue.gameObject.name =
                        "Return_SceneSixVictoryDialogue";
                    _dialogue.transform.SetParent(host.transform, false);
                    _dialogue.transform.localPosition = Vector3.zero;
                }
                catch (Exception exception)
                {
                    setupException = exception;
                }
            }

            if (setupException == null)
            {
                yield return null;
                if (generation != _generation)
                {
                    yield break;
                }

                Exception startException = null;
                try
                {
                    _dialogue.StartConversation();
                }
                catch (Exception exception)
                {
                    startException = exception;
                }

                if (startException == null)
                {
                    while (generation == _generation &&
                        _dialogue != null &&
                        _dialogue.InConversation())
                    {
                        yield return null;
                    }
                }
                else
                {
                    setupException = startException;
                }
            }

            if (generation != _generation)
            {
                yield break;
            }

            if (setupException != null)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN BUILD109] Victory dialogue failed; continuing " +
                    "to the stable ending: " + setupException,
                    MessageType.Error
                );
            }
            else
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN BUILD109] Daz's final conversation completed; " +
                    "starting the stable true-ending presentation.",
                    MessageType.Success
                );
            }

            ContinueToStableEnding();
        }

        private static void ContinueToStableEnding()
        {
            if (_dialogue != null)
            {
                UnityEngine.Object.Destroy(_dialogue.gameObject);
                _dialogue = null;
            }

            _bypassInterception = true;
            try
            {
                SceneSixEndingController.BeginVictoryEnding();
                ReleaseDialogueState();
            }
            finally
            {
                _bypassInterception = false;
                _running = false;
            }
        }

        private static void WarpPlayerToRevivalCheckpoint()
        {
            OWRigidbody brittleHollow =
                InterloperTrajectoryController.FindBody(
                    "BrittleHollow_Body"
                );
            OWRigidbody playerBody = Locator.GetPlayerBody();
            if (brittleHollow == null || playerBody == null)
            {
                throw new InvalidOperationException(
                    "The victory checkpoint bodies were unavailable."
                );
            }

            Vector3 worldPosition =
                brittleHollow.transform.TransformPoint(
                    RevivalLocalPosition
                );
            Quaternion worldRotation =
                brittleHollow.GetRotation() * RevivalLocalRotation;
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
        }

        private static void ReleaseDialogueState()
        {
            if (_pausedForDialogue)
            {
                if (OWTime.IsPaused(OWTime.PauseType.Reading))
                {
                    OWTime.Unpause(OWTime.PauseType.Reading);
                }
                _pausedForDialogue = false;
            }

            if (_movementLocked)
            {
                PlayerCharacterController playerController =
                    Locator.GetPlayerController();
                if (playerController != null)
                {
                    playerController.UnlockMovement();
                }
                _movementLocked = false;
            }

            if (_cameraEffects != null)
            {
                _cameraEffects.OpenEyes(0.01f, false);
                _cameraEffects = null;
            }
        }

        private static void DestroyTemporaryObjects()
        {
            if (_dialogue != null)
            {
                UnityEngine.Object.Destroy(_dialogue.gameObject);
                _dialogue = null;
            }
        }

        private static T FindSceneComponent<T>() where T : Component
        {
            foreach (T component in Resources.FindObjectsOfTypeAll<T>())
            {
                if (component != null &&
                    component.gameObject.scene.IsValid())
                {
                    return component;
                }
            }
            return null;
        }
    }

    [HarmonyPatch(
        typeof(SceneSixEndingController),
        nameof(SceneSixEndingController.BeginVictoryEnding)
    )]
    internal static class SceneSixVictoryDialogueInterceptPatch
    {
        private static bool Prefix()
        {
            return !SceneSixVictoryDialogueController.TryIntercept();
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class SceneSixVictoryDialogueResetPatch
    {
        private static void Postfix(OWScene newScene)
        {
            SceneSixVictoryDialogueController.Reset();
        }
    }
}
