using HarmonyLib;
using OWML.Common;
using System;
using System.Reflection;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Scene 6 deliberately does not support New Horizons' live config
    /// reload. Returning to the title screen instead receives the same held
    /// item cleanup as a terminal loop death, while the next SolarSystem load
    /// remains the normal, verified fresh-loop path.
    /// </summary>
    internal static class SceneSixMainMenuResetController
    {
        private const string ReturnCoreName =
            "Return_PickableWarpCore";

        public static void PrepareForMainMenu()
        {
            if (!SceneSixController.IsActive ||
                LoadManager.GetCurrentScene() != OWScene.SolarSystem)
            {
                return;
            }

            try
            {
                SceneSixController.MarkRevivalCheckpoint();
                SceneSixEndingController.MarkTerminalDeath();
                ClearHeldCoreReference();
                ClearNewHorizonsHeldItemState();

                ReturnDebugLog.Write(
                    "[RETURN MAIN MENU RESET] Scene 6 exit treated as a " +
                    "terminal-loop death for held-item persistence. The " +
                    "next save load will begin a fresh loop.",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN MAIN MENU RESET] Cleanup failed safely: " +
                    exception,
                    MessageType.Error
                );
            }
        }

        internal static void PrepareFreshLoopReset()
        {
            if (!SceneSixController.IsActive ||
                LoadManager.GetCurrentScene() != OWScene.SolarSystem)
            {
                return;
            }

            try
            {
                SceneSixController.MarkRevivalCheckpoint();
                ClearHeldCoreReference();
                ClearNewHorizonsHeldItemState();
                ReturnDebugLog.Write(
                    "[RETURN FRESH LOOP] Held items cleared; next " +
                    "SolarSystem load starts a fresh 17-minute loop.",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN FRESH LOOP] Cleanup failed safely: " +
                    exception,
                    MessageType.Error
                );
            }
        }

        private static void ClearHeldCoreReference()
        {
            ToolModeSwapper swapper = Locator.GetToolModeSwapper();
            ItemTool itemTool = swapper == null
                ? null
                : swapper.GetItemCarryTool();
            OWItem held = itemTool == null
                ? null
                : itemTool.GetHeldItem();
            if (held == null || held.name != ReturnCoreName)
            {
                return;
            }

            // Do not change tool mode or input while the pause menu is
            // closing. The outgoing scene will destroy the item; clearing
            // this reference only prevents New Horizons from carrying it
            // into the next loop.
            Traverse.Create(itemTool)
                .Field("_heldItem")
                .SetValue(null);
            UnityEngine.Object.Destroy(held.gameObject);
        }

        private static void ClearNewHorizonsHeldItemState()
        {
            Type handlerType = AccessTools.TypeByName(
                "NewHorizons.Handlers.HeldItemHandler"
            );
            MethodInfo resetMethod = handlerType == null
                ? null
                : AccessTools.Method(
                    handlerType,
                    "OnDeathSequenceComplete"
                );
            resetMethod?.Invoke(null, null);
        }
    }

    [HarmonyPatch]
    internal static class ReturnDisableDebugReloadMenuPatch
    {
        private const string DebugReloadTypeName =
            "NewHorizons.Utility.DebugTools.DebugReload";

        private static bool Prepare()
        {
            return AccessTools.TypeByName(DebugReloadTypeName) != null;
        }

        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName(DebugReloadTypeName);
            return AccessTools.Method(type, "InitializePauseMenu");
        }

        private static void Postfix()
        {
            HideReloadButton();
        }

        internal static void HideReloadButton()
        {
            if (!SceneSixController.IsActive)
            {
                return;
            }

            Type type = AccessTools.TypeByName(DebugReloadTypeName);
            FieldInfo field = type == null
                ? null
                : AccessTools.Field(type, "_reloadButton");
            SubmitAction button = field == null
                ? null
                : field.GetValue(null) as SubmitAction;
            if (button != null)
            {
                button.gameObject.SetActive(false);
            }
        }
    }

    [HarmonyPatch]
    internal static class ReturnKeepDebugReloadMenuDisabledPatch
    {
        private const string DebugReloadTypeName =
            "NewHorizons.Utility.DebugTools.DebugReload";

        private static bool Prepare()
        {
            return AccessTools.TypeByName(DebugReloadTypeName) != null;
        }

        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName(DebugReloadTypeName);
            return AccessTools.Method(type, "UpdateReloadButton");
        }

        private static void Postfix()
        {
            ReturnDisableDebugReloadMenuPatch.HideReloadButton();
        }
    }

    [HarmonyPatch(
        typeof(LoadManager),
        nameof(LoadManager.LoadScene),
        new[]
        {
            typeof(OWScene),
            typeof(LoadManager.FadeType),
            typeof(float),
            typeof(bool)
        }
    )]
    internal static class ReturnMainMenuTerminalResetPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(OWScene scene)
        {
            if (scene == OWScene.TitleScreen)
            {
                SceneSixMainMenuResetController.PrepareForMainMenu();
            }
        }
    }

    [HarmonyPatch(
        typeof(LoadManager),
        nameof(LoadManager.LoadSceneAsync),
        new[]
        {
            typeof(OWScene),
            typeof(bool),
            typeof(LoadManager.FadeType),
            typeof(float),
            typeof(bool)
        }
    )]
    internal static class ReturnMainMenuAsyncTerminalResetPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(OWScene scene)
        {
            if (scene == OWScene.TitleScreen)
            {
                SceneSixMainMenuResetController.PrepareForMainMenu();
            }
        }
    }

    [HarmonyPatch(
        typeof(UITextLibrary),
        nameof(UITextLibrary.GetString)
    )]
    internal static class ReturnPauseMeditateTextPatch
    {
        private static void Postfix(
            ref string __result,
            UITextType TextID
        )
        {
            if (SceneSixController.IsActive &&
                TextID == UITextType.PauseMeditate)
            {
                __result = GetMeditateToEndText();
            }
        }

        private static string GetMeditateToEndText()
        {
            ReturnMod mod = ReturnMod.Instance;
            if (mod?.NewHorizons != null)
            {
                string translated = mod.NewHorizons.GetTranslationForUI(
                    "$RETURN_MEDITATE_TO_END"
                );
                if (!string.IsNullOrEmpty(translated) &&
                    translated != "$RETURN_MEDITATE_TO_END")
                {
                    return translated;
                }
            }

            TextTranslation translations = TextTranslation.Get();
            if (translations != null &&
                translations.GetLanguage() ==
                    TextTranslation.Language.CHINESE_SIMPLE)
            {
                return "冥想到终点";
            }
            return "Meditate to the End";
        }
    }

    [HarmonyPatch(
        typeof(PlayerData),
        nameof(PlayerData.ResetGame)
    )]
    internal static class ReturnNewGameStartsSceneOnePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            SceneSixController.ResetForNewGame();
            SceneSixDeathLegendComputerController
                .ResetProgressState();
            ReturnSceneSixCheckpointPersistenceController
                .CancelPendingTitleScreenSave();
            ReturnDebugLog.Write(
                "[RETURN NEW GAME] Scene-6 checkpoint cleared; the new " +
                "expedition starts from Scene 1 (the Nomai mine).",
                MessageType.Success
            );
        }
    }
}
