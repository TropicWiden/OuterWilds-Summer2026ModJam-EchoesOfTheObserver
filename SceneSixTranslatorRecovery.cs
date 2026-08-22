using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Isolated safety layer for restoring the stock translator in Scene 6.
    /// This deliberately lives outside the locked SceneSixController.
    /// </summary>
    internal static class SceneSixTranslatorRecovery
    {
        private const string ReturnPatchOwner = "Known-Mouse.Return";
        private const string RecoveryPatchOwner =
            "Known-Mouse.Return.SceneSixTranslatorRecovery";

        private static bool _unityExplorerGuardInstalled;

        public static IEnumerator Recover(ReturnMod mod)
        {
            InstallUnityExplorerGamepadGuard(mod);

            // Repeat after player/tool initialization because some modded
            // objects finish Awake/Start several frames after scene completion.
            for (int attempt = 1; attempt <= 12; attempt++)
            {
                if (!SceneSixController.IsActive ||
                    LoadManager.GetCurrentScene() != OWScene.SolarSystem)
                {
                    yield break;
                }

                RemoveSceneOneTranslatorBlocks();

                ToolModeSwapper swapper = Locator.GetToolModeSwapper();
                NomaiTranslator translator =
                    swapper == null ? null : swapper.GetTranslator();

                if (swapper != null && translator != null)
                {
                    translator.gameObject.SetActive(true);

                    if (attempt == 1 || attempt == 12)
                    {
                        ReturnDebugLog.Write(
                            "[RETURN TRANSLATOR] Recovery pass " + attempt +
                            ": tool object active=" +
                            translator.gameObject.activeInHierarchy +
                            "; current mode=" + swapper.GetToolMode() +
                            "; blocking patches=" +
                            DescribeBlockingPatchOwners() + ".",
                            MessageType.Success
                        );
                    }
                }
                else if (attempt == 12)
                {
                    ReturnDebugLog.Write(
                        "[RETURN TRANSLATOR] Player translator components " +
                        "were not available after scene initialization.",
                        MessageType.Error
                    );
                }

                yield return new WaitForSecondsRealtime(0.75f);
            }
        }

        private static void RemoveSceneOneTranslatorBlocks()
        {
            Harmony harmony = new Harmony(RecoveryPatchOwner);
            UnpatchOwner(
                harmony,
                AccessTools.Method(typeof(ToolModeSwapper), "EquipToolMode"),
                HarmonyPatchType.Prefix
            );
            UnpatchOwner(
                harmony,
                AccessTools.Method(typeof(NomaiTranslator), "EquipTool"),
                HarmonyPatchType.Prefix
            );
            UnpatchOwner(
                harmony,
                AccessTools.Method(
                    typeof(ToolModeSwapper),
                    "IsTranslatorEquipPromptAllowed"
                ),
                HarmonyPatchType.Postfix
            );
            UnpatchOwner(
                harmony,
                AccessTools.Method(
                    typeof(ToolModeSwapper),
                    "GetAutoEquipTranslator"
                ),
                HarmonyPatchType.Postfix
            );
        }

        private static void UnpatchOwner(
            Harmony harmony,
            MethodBase original,
            HarmonyPatchType patchType
        )
        {
            if (original != null)
            {
                harmony.Unpatch(original, patchType, ReturnPatchOwner);
            }
        }

        private static string DescribeBlockingPatchOwners()
        {
            MethodBase equip = AccessTools.Method(
                typeof(ToolModeSwapper),
                "EquipToolMode"
            );
            Patches info = equip == null ? null : Harmony.GetPatchInfo(equip);
            if (info == null || info.Prefixes.Count == 0)
            {
                return "none";
            }

            string owners = string.Empty;
            foreach (Patch patch in info.Prefixes)
            {
                if (owners.Length > 0)
                {
                    owners += ", ";
                }
                owners += patch.owner;
            }
            return owners;
        }

        private static void InstallUnityExplorerGamepadGuard(ReturnMod mod)
        {
            if (_unityExplorerGuardInstalled)
            {
                return;
            }

            Type interceptor = AccessTools.TypeByName(
                "UniverseLib.Input.IGamepadInputInterceptor"
            );
            MethodInfo isButtonPressed = interceptor == null
                ? null
                : AccessTools.Method(interceptor, "IsButtonPressed");
            MethodInfo finalizer = AccessTools.Method(
                typeof(SceneSixTranslatorRecovery),
                nameof(GamepadInputFinalizer)
            );

            if (isButtonPressed == null || finalizer == null)
            {
                return;
            }

            try
            {
                new Harmony(RecoveryPatchOwner + ".UnityExplorerGuard").Patch(
                    isButtonPressed,
                    finalizer: new HarmonyMethod(finalizer)
                );
                _unityExplorerGuardInstalled = true;
                ReturnDebugLog.Write(
                    "[RETURN INPUT] Installed a guard for Unity Explorer's " +
                    "stale DualSense button binding.",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN INPUT] Could not install the Unity Explorer " +
                    "gamepad guard: " + exception,
                    MessageType.Warning
                );
            }
        }

        private static Exception GamepadInputFinalizer(
            Exception __exception,
            ref bool __result
        )
        {
            if (__exception is InvalidOperationException &&
                __exception.Message.Contains("has been added to system"))
            {
                __result = false;
                return null;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class SceneSixTranslatorRecoveryPatch
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
                    SceneSixTranslatorRecovery.Recover(__instance)
                );
            }
        }
    }
}
