using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Return
{
    /// <summary>
    /// Build118 additions:
    /// - A pause-menu button "Revive at the Brittle Hollow base" that
    ///   revives the player at the gravity-cannon checkpoint while the
    ///   17-minute loop keeps running (the countdown is not restarted).
    /// - A short entry hint notification whenever Scene 6 starts.
    /// </summary>
    internal static class SceneSixPauseAndHintController
    {
        private const string EntryHintKey = "$RETURN_SCENE6_ENTRY_HINT";
        private const string MenuReviveKey =
            "$RETURN_PORTAL_REVIVE_CHECKPOINT_MENU";

        private static SubmitAction _reviveButton;

        // ------------------------------------------------------------
        // Pause-menu revive button (OWML new menu system, the same API
        // New Horizons uses for its own pause-menu buttons)
        // ------------------------------------------------------------
        public static void SetupPauseMenu(
            ReturnMod mod,
            IPauseMenuManager pauseMenu
        )
        {
            if (mod == null || pauseMenu == null ||
                !SceneSixController.IsActive)
            {
                return;
            }

            try
            {
                string title = Translate(
                    mod,
                    MenuReviveKey,
                    "Revive at the Brittle Hollow Base"
                ).ToUpperInvariant();
                _reviveButton = pauseMenu.MakeSimpleButton(
                    title,
                    3,
                    true,
                    null
                );
                if (_reviveButton != null)
                {
                    _reviveButton.OnSubmitAction +=
                        ReviveAtCheckpointFromMenu;
                    ReturnDebugLog.Write(
                        "[RETURN PAUSE MENU] Added the Brittle Hollow " +
                        "base revive button.",
                        MessageType.Success
                    );
                }
            }
            catch (Exception exception)
            {
                _reviveButton = null;
                ReturnDebugLog.Write(
                    "[RETURN PAUSE MENU] Could not add the revive " +
                    "button: " + exception,
                    MessageType.Error
                );
            }
        }

        private static void ReviveAtCheckpointFromMenu()
        {
            if (!SceneSixController.IsActive)
            {
                return;
            }
            if (Build111SimpleCorePrisonController.IsPlayerTrapped)
            {
                ReturnDebugLog.Write(
                    "[RETURN PAUSE MENU] Revive is disabled while the " +
                    "player is trapped in the Giant's Deep prison.",
                    MessageType.Warning
                );
                return;
            }
            if (!SceneSixWarpCoreToolController.IsReturnWarpCoreHeld())
            {
                ReturnMod mod = ReturnMod.Instance;
                const string key = "$RETURN_PORTAL_REVIVE_NEED_CORE";
                string text = mod?.NewHorizons == null
                    ? key
                    : mod.NewHorizons.GetTranslationForUI(key);
                if (string.IsNullOrEmpty(text) || text == key)
                {
                    text = key;
                }
                NotificationManager.SharedInstance?.PostNotification(
                    new NotificationData(
                        NotificationTarget.All,
                        text,
                        4f
                    )
                );
                ReturnDebugLog.Write(
                    "[RETURN PAUSE MENU] Checkpoint revive requires " +
                    "holding the Return warp core.",
                    MessageType.Warning
                );
                return;
            }

            try
            {
                PauseMenuManager manager =
                    UnityEngine.Object.FindObjectOfType<
                        PauseMenuManager>();
                if (manager != null)
                {
                    Menu pauseMenu = Traverse.Create(manager)
                        .Field("_pauseMenu")
                        .GetValue<Menu>();
                    if (pauseMenu != null && pauseMenu.IsMenuEnabled())
                    {
                        pauseMenu.EnableMenu(false);
                    }
                }

                SceneSixEndingController.ReviveAtCheckpointFromMenu();
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PAUSE MENU] Revive button failed: " +
                    exception,
                    MessageType.Error
                );
            }
        }

        // ------------------------------------------------------------
        // Entry hint
        // ------------------------------------------------------------
        public static IEnumerator ShowEntryHintLater(ReturnMod mod)
        {
            yield return new WaitForSecondsRealtime(8f);
            if (!SceneSixController.IsActive ||
                LoadManager.GetCurrentScene() != OWScene.SolarSystem)
            {
                yield break;
            }

            string text = Translate(
                mod,
                EntryHintKey,
                "If you feel lost, check your ship log."
            );
            NotificationManager.SharedInstance?.PostNotification(
                new NotificationData(
                    NotificationTarget.All,
                    text,
                    10f
                )
            );
        }

        private static string Translate(
            ReturnMod mod,
            string key,
            string fallback
        )
        {
            if (mod?.NewHorizons != null)
            {
                string translated =
                    mod.NewHorizons.GetTranslationForUI(key);
                if (!string.IsNullOrEmpty(translated) &&
                    translated != key)
                {
                    return translated;
                }
            }
            return fallback;
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class ReturnEntryHintPatch
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
                    SceneSixPauseAndHintController.ShowEntryHintLater(
                        __instance
                    )
                );
            }
        }
    }

    [HarmonyPatch(typeof(Menu), "Activate")]
    internal static class ReturnPauseMenuNavigationPatch
    {
        private static void Postfix(Menu __instance)
        {
            if (!SceneSixController.IsActive || __instance == null ||
                __instance.name != "PauseMenuItems")
            {
                return;
            }

            // The pause menu's serialized option list does not contain
            // dynamically added buttons (ours, OWML's MODS, etc.). Rebuild
            // the vertical navigation over every active menu option so the
            // revive button stays reachable with gamepad/keyboard.
            List<Selectable> selectables =
                new List<Selectable>();
            foreach (MenuOption option in
                __instance.GetComponentsInChildren<MenuOption>(true))
            {
                Selectable selectable = option.GetSelectable();
                if (selectable != null &&
                    selectable.gameObject.activeInHierarchy)
                {
                    selectables.Add(selectable);
                }
            }

            int count = selectables.Count;
            if (count < 2)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int up = (i - 1 + count) % count;
                int down = (i + 1) % count;
                Navigation navigation = selectables[i].navigation;
                navigation.selectOnUp = selectables[up];
                navigation.selectOnDown = selectables[down];
                selectables[i].navigation = navigation;
            }
        }
    }

    [HarmonyPatch(typeof(PauseMenuManager), "Start")]
    internal static class ReturnForcePauseMeditateButtonPatch
    {
        private static void Prefix()
        {
            if (!SceneSixController.IsActive)
            {
                return;
            }

            try
            {
                // The vanilla pause menu only shows "Skip to Next Loop"
                // when the player has learned meditation. Scene 6 reuses
                // that vanilla slot as our "Meditate to the End" button,
                // so make sure the gate condition is always set here.
                PlayerData.SetPersistentCondition(
                    "KNOWS_MEDITATION",
                    true
                );
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PAUSE MENU] Could not set the meditation " +
                    "condition: " + exception,
                    MessageType.Error
                );
            }
        }

        private static void Postfix(PauseMenuManager __instance)
        {
            if (!SceneSixController.IsActive || __instance == null)
            {
                return;
            }

            try
            {
                GameObject skipButton = Traverse.Create(__instance)
                    .Field("_skipToNextLoopButton")
                    .GetValue<GameObject>();
                if (skipButton != null)
                {
                    skipButton.SetActive(true);
                }
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN PAUSE MENU] Could not force-show the " +
                    "Meditate to the End button: " + exception,
                    MessageType.Error
                );
            }
        }
    }
}
