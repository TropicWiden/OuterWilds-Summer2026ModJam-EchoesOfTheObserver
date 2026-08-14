using System;
using HarmonyLib;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Keeps Brittle Hollow in its loop-start state by making only its
    /// still-attached planetary fragments immune to Hollow's Lantern meteor
    /// damage. MeteorController still completes the rest of Impact(), so the
    /// stock impact particles, light, audio and meteor cleanup remain intact.
    ///
    /// This does not intercept FragmentIntegrity.HandleImpact or any Return
    /// portal method. Player/ship physics and player-created black holes are
    /// therefore free to produce their normal later consequences.
    /// </summary>
    [HarmonyPatch(
        typeof(FragmentIntegrity),
        nameof(FragmentIntegrity.GetIgnoreMeteorDamage)
    )]
    internal static class ReturnBrittleHollowMeteorImmunityPatch
    {
        private const string BrittleHollowBodyName =
            "BrittleHollow_Body";

        private static void Postfix(
            FragmentIntegrity __instance,
            ref bool __result
        )
        {
            try
            {
                if (__result || __instance == null ||
                    LoadManager.GetCurrentScene() != OWScene.SolarSystem)
                {
                    return;
                }

                if (IsAttachedToBrittleHollow(__instance.transform))
                {
                    __result = true;
                }
            }
            catch
            {
                // Preservation is optional and must never interrupt stock
                // impact handling or any locked Return gameplay system.
            }
        }

        private static bool IsAttachedToBrittleHollow(Transform fragment)
        {
            for (Transform current = fragment;
                current != null;
                current = current.parent)
            {
                if (string.Equals(
                        current.name,
                        BrittleHollowBodyName,
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
