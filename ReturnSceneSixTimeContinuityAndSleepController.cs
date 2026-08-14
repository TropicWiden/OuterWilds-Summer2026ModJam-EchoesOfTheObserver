using HarmonyLib;
using OWML.Common;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Return
{
    [HarmonyPatch(
        typeof(InterloperTrajectoryController),
        nameof(InterloperTrajectoryController.Apply)
    )]
    internal static class ReturnSceneSixStartClockPatch
    {
        private static int _resetSceneHandle = int.MinValue;

        [HarmonyPrefix]
        private static void Prefix(ReturnMod mod)
        {
            if (!SceneSixController.IsActive)
            {
                return;
            }

            try
            {
                Scene activeScene = SceneManager.GetActiveScene();
                int activeSceneHandle = activeScene.handle;
                float inheritedSeconds = TimeLoop.GetSecondsElapsed();

                // Scene 1-5 and Scene 6 share the SolarSystem scene. Reset
                // the stock loop epoch exactly once, immediately before the
                // Scene 6 trajectory reads it. Never reset again for an
                // in-scene death/resurrection.
                if (_resetSceneHandle != activeSceneHandle)
                {
                    AccessTools.Field(typeof(TimeLoop), "_timeOffset")
                        .SetValue(null, -Time.timeSinceLevelLoad);
                    AccessTools.Field(typeof(TimeLoop), "_isTimeFlowing")
                        .SetValue(null, true);
                    _resetSceneHandle = activeSceneHandle;
                }
                TimeLoop.SetTimeLoopEnabled(true);
                mod?.ModHelper.Console.WriteLine(
                    "[RETURN CLOCK] Scene 6 epoch initialized; inherited " +
                    "seconds=" + inheritedSeconds.ToString("F2") +
                    "; scene6 seconds=" +
                    TimeLoop.GetSecondsElapsed().ToString("F2") +
                    "; sceneHandle=" + activeSceneHandle + ".",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                mod?.ModHelper.Console.WriteLine(
                    "[RETURN CLOCK] Failed safely while starting the " +
                    "Scene 6 clock: " + exception,
                    MessageType.Error
                );
            }
        }
    }

    [HarmonyPatch(typeof(Campfire), "ShouldWakeUp")]
    internal static class ReturnSceneSixTerminalWakePatch
    {
        private const float WakeBeforeTerminalSeconds = 85f;

        [HarmonyPostfix]
        private static void Postfix(ref bool __result)
        {
            if (__result || !SceneSixController.IsActive ||
                SceneSixEndingController.IsEndingActive)
            {
                return;
            }

            float secondsUntilTerminal =
                InterloperTrajectoryController.TerminalLoopTimeSeconds -
                TimeLoop.GetSecondsElapsed();
            if (secondsUntilTerminal < WakeBeforeTerminalSeconds)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Last-resort guard for a player already asleep when the Interloper
    /// reaches periapsis. It prevents the stock game-over presentation from
    /// running at campfire fast-forward speed and skipping its orange text.
    /// </summary>
    [HarmonyPatch(
        typeof(InterloperTerminalController),
        "TriggerTerminalDeath"
    )]
    internal static class ReturnSceneSixTerminalSleepSafetyPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (!SceneSixController.IsActive)
            {
                return;
            }

            try
            {
                if (PlayerState.IsSleepingAtCampfire())
                {
                    foreach (Campfire campfire in
                        Resources.FindObjectsOfTypeAll<Campfire>())
                    {
                        if (campfire != null &&
                            campfire.gameObject.scene.IsValid() &&
                            campfire.gameObject.activeInHierarchy)
                        {
                            campfire.StopSleeping(true);
                        }
                    }
                }

                OWTime.SetTimeScale(1f);
                OWTime.SetMaxDeltaTime(1f / 15f);
            }
            catch (Exception exception)
            {
                ReturnMod.Instance?.ModHelper.Console.WriteLine(
                    "[RETURN CLOCK] Terminal sleep cleanup failed safely: " +
                    exception,
                    MessageType.Error
                );
            }
        }
    }
}
