using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Persists the already-established scene-six checkpoint only after the
    /// title screen has loaded. Saving while the SolarSystem scene is still
    /// active can retain outgoing loop state, so it must never happen here.
    /// </summary>
    internal static class ReturnSceneSixCheckpointPersistenceController
    {
        private const string RevivalCheckpointCondition =
            "RETURN_REVIVE_AT_BRITTLE_HOLLOW";

        private static bool _savePending;
        private static bool _saveRoutineRunning;
        private static bool _restoreRoutineRunning;

        public static void QueueTitleScreenSave()
        {
            _savePending = true;
        }

        public static void CancelPendingTitleScreenSave()
        {
            _savePending = false;
        }

        public static void SaveFromTitleScreen(ReturnMod mod)
        {
            if (!_savePending || _saveRoutineRunning || mod == null)
            {
                return;
            }

            mod.StartCoroutine(SaveWhenPlayerDataIsReady(mod));
        }

        public static void RestoreFromTitleScreen(ReturnMod mod)
        {
            if (_restoreRoutineRunning || mod == null)
            {
                return;
            }

            mod.StartCoroutine(RestoreWhenPlayerDataIsReady(mod));
        }

        private static IEnumerator RestoreWhenPlayerDataIsReady(
            ReturnMod mod
        )
        {
            _restoreRoutineRunning = true;
            float timeout = Time.realtimeSinceStartup + 60f;
            while (!PlayerData.IsLoaded() &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (PlayerData.IsLoaded())
            {
                SceneSixController.RestoreActiveStateFromSave();
                if (SceneSixController.IsActive)
                {
                    mod.ModHelper.Console.WriteLine(
                        "[RETURN CHECKPOINT] Scene 6 restored after the " +
                        "title screen finished reading player data.",
                        MessageType.Success
                    );
                }
            }
            else
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN CHECKPOINT] Player data was not ready before " +
                    "the title-screen restore timeout.",
                    MessageType.Error
                );
            }

            _restoreRoutineRunning = false;
        }

        private static IEnumerator SaveWhenPlayerDataIsReady(ReturnMod mod)
        {
            _saveRoutineRunning = true;
            float timeout = Time.realtimeSinceStartup + 10f;
            while (!PlayerData.IsLoaded() &&
                   Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            // A new expedition may reset the save while this routine is
            // waiting; never write the stale scene-six checkpoint into it.
            if (!_savePending)
            {
                _saveRoutineRunning = false;
                yield break;
            }

            try
            {
                if (!PlayerData.IsLoaded())
                {
                    throw new InvalidOperationException(
                        "Player data was not ready on the title screen."
                    );
                }

                PlayerData.SetPersistentCondition(
                    RevivalCheckpointCondition,
                    true
                );
                PlayerData.SaveCurrentGame();
                _savePending = false;
                mod.ModHelper.Console.WriteLine(
                    "[RETURN CHECKPOINT] Scene 6 checkpoint saved from " +
                    "the title screen without retaining loop state.",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN CHECKPOINT] Scene 6 checkpoint could not be " +
                    "saved without interrupting the game: " + exception,
                    MessageType.Error
                );
            }
            finally
            {
                _saveRoutineRunning = false;
            }
        }
    }

    [HarmonyPatch(
        typeof(SceneSixMainMenuResetController),
        nameof(SceneSixMainMenuResetController.PrepareForMainMenu)
    )]
    internal static class ReturnQueueSceneSixCheckpointSavePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (SceneSixController.IsActive)
            {
                ReturnSceneSixCheckpointPersistenceController
                    .QueueTitleScreenSave();
            }
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class ReturnSaveSceneSixCheckpointOnTitlePatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            ReturnMod __instance,
            OWScene newScene
        )
        {
            if (newScene == OWScene.TitleScreen)
            {
                ReturnSceneSixCheckpointPersistenceController
                    .RestoreFromTitleScreen(__instance);
                ReturnSceneSixCheckpointPersistenceController
                    .SaveFromTitleScreen(__instance);
            }
        }
    }
}
