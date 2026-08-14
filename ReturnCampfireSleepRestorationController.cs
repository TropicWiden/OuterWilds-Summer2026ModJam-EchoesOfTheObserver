using HarmonyLib;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Keeps the complete stock campfire interaction branch alive while the
    /// pre-Hearthian cleanup removes surrounding Hearthian scene objects.
    /// This is isolated from the locked scene, input and time-loop code.
    /// </summary>
    [HarmonyPatch(
        typeof(ReturnEraWorldCleanupController),
        "IsCampfireRelated"
    )]
    internal static class ReturnCampfireHierarchyProtectionPatch
    {
        private static void Postfix(
            Transform candidate,
            ref bool __result
        )
        {
            try
            {
                if (!__result && candidate != null &&
                    candidate.GetComponentInChildren<Campfire>(true) != null)
                {
                    // A Hearthian NPC/structure root can own the campfire as a
                    // descendant. Disabling that root also disables the fire's
                    // interaction volume, sleep prompt and attach point.
                    __result = true;
                }
            }
            catch
            {
                // Campfire protection must never interrupt the cleanup pass.
            }
        }
    }

    /// <summary>
    /// Restores the normal doze prompt for usable, lit campfires in Scene 6.
    /// The stock checks for character input and the final 85 seconds remain,
    /// so sleeping cannot begin during an ending or while controls are locked.
    /// </summary>
    [HarmonyPatch(typeof(Campfire), "CanSleepHereNow")]
    internal static class ReturnCampfireCanSleepPatch
    {
        private static void Postfix(
            Campfire __instance,
            ref bool __result
        )
        {
            try
            {
                if (__result || !SceneSixController.IsActive ||
                    __instance == null ||
                    !__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                __result =
                    __instance.GetState() == Campfire.State.LIT &&
                    OWInput.IsInputMode(InputMode.Character) &&
                    TimeLoop.GetSecondsRemaining() > 85f;
            }
            catch
            {
                // Fall back to the stock result if a campfire is incomplete.
            }
        }
    }

    /// <summary>
    /// Some stock scene toggles disable a fire's interaction volume even
    /// though its GameObject remains visible. Re-enable only that stock volume
    /// after Campfire.Start; no new interaction components are fabricated.
    /// </summary>
    [HarmonyPatch(typeof(Campfire), "Start")]
    internal static class ReturnCampfireInteractionRestorationPatch
    {
        private static void Postfix(Campfire __instance)
        {
            try
            {
                if (SceneSixController.IsActive && __instance != null &&
                    __instance.gameObject.activeInHierarchy)
                {
                    __instance.SetInteractionEnabled(true);
                }
            }
            catch
            {
                // A decorative/incomplete campfire is left in its stock state.
            }
        }
    }
}
