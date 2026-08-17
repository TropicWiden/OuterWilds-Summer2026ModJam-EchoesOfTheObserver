using System;
using HarmonyLib;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Keeps Brittle Hollow in its loop-start state by making only its
    /// still-attached planetary fragments immune to Hollow's Lantern meteor
    /// damage. MeteorController still completes the rest of Impact(), so the
    /// meteor cleanup and kinematic settle remain intact.
    ///
    /// Since immune fragments never break, the vanilla impact particles,
    /// light and audio would otherwise keep spawning on intact fragments for
    /// the whole loop. The FX suppressor below removes those three effects
    /// only for hits on immune Brittle Hollow fragments; the meteor still
    /// flies in, vanishes and gets recycled exactly like vanilla.
    /// </summary>
    internal sealed class ReturnMeteorImpactFxSuppressor : MonoBehaviour
    {
        public bool SuppressImpactFx;
    }

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

        internal static bool IsImmuneBrittleHollowFragment(
            FragmentIntegrity fragment
        )
        {
            return fragment != null &&
                fragment.GetIgnoreMeteorDamage() &&
                IsAttachedToBrittleHollow(fragment.transform);
        }

        internal static bool IsAttachedToBrittleHollow(Transform fragment)
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

    /// <summary>
    /// Suppresses meteor impact particles, light and audio when the meteor
    /// strikes an intact Brittle Hollow fragment. The meteor still performs
    /// the full vanilla impact state change and recycling.
    /// </summary>
    [HarmonyPatch(
        typeof(MeteorController),
        nameof(MeteorController.Impact)
    )]
    internal static class ReturnMeteorImpactFxSuppressionPatch
    {
        private static void Prefix(
            MeteorController __instance,
            GameObject hitObject
        )
        {
            if (__instance == null || hitObject == null)
            {
                return;
            }

            FragmentIntegrity fragment =
                hitObject.GetComponentInParent<FragmentIntegrity>();
            if (!ReturnBrittleHollowMeteorImmunityPatch
                .IsImmuneBrittleHollowFragment(fragment))
            {
                return;
            }

            ReturnMeteorImpactFxSuppressor suppressor =
                __instance.GetComponent<ReturnMeteorImpactFxSuppressor>();
            if (suppressor == null)
            {
                suppressor = __instance.gameObject.AddComponent<
                    ReturnMeteorImpactFxSuppressor
                >();
            }
            suppressor.SuppressImpactFx = true;
        }

        private static void Postfix(MeteorController __instance)
        {
            if (__instance == null)
            {
                return;
            }

            ReturnMeteorImpactFxSuppressor suppressor =
                __instance.GetComponent<ReturnMeteorImpactFxSuppressor>();
            if (suppressor == null || !suppressor.SuppressImpactFx)
            {
                return;
            }
            suppressor.SuppressImpactFx = false;

            try
            {
                Light impactLight = Traverse.Create(__instance)
                    .Field("_impactLight")
                    .GetValue<Light>();
                if (impactLight != null)
                {
                    impactLight.enabled = false;
                }

                ParticleSystem[] impactParticles =
                    Traverse.Create(__instance)
                        .Field("_impactParticles")
                        .GetValue<ParticleSystem[]>();
                if (impactParticles != null)
                {
                    foreach (ParticleSystem particleSystem in
                        impactParticles)
                    {
                        if (particleSystem != null)
                        {
                            particleSystem.Stop(
                                true,
                                ParticleSystemStopBehavior
                                    .StopEmittingAndClear
                            );
                        }
                    }
                }

                OWAudioSource impactSource = Traverse.Create(__instance)
                    .Field("_impactSource")
                    .GetValue<OWAudioSource>();
                if (impactSource != null)
                {
                    impactSource.Stop();
                }
            }
            catch
            {
                // FX suppression is best-effort and must never break the
                // locked meteor impact path.
            }
        }
    }

    /// <summary>
    /// Prevents the impact audio from ever starting on meteors that hit
    /// immune Brittle Hollow fragments. The suppressor flag is only true
    /// during the single Impact() call that triggered it.
    /// </summary>
    [HarmonyPatch(
        typeof(OWAudioSource),
        nameof(OWAudioSource.PlayOneShot),
        new[] { typeof(AudioType), typeof(float) }
    )]
    internal static class ReturnMeteorImpactAudioSkipPatch
    {
        private static bool Prefix(OWAudioSource __instance)
        {
            ReturnMeteorImpactFxSuppressor suppressor =
                __instance == null
                    ? null
                    : __instance.GetComponentInParent<
                        ReturnMeteorImpactFxSuppressor
                    >();
            return suppressor == null || !suppressor.SuppressImpactFx;
        }
    }

    /// <summary>
    /// Keeps Brittle Hollow in its loop-start state by stopping the
    /// time-based Cannon Path collapse. These timed fragments (including
    /// the gravity-cannon path) break on their own as the loop advances in
    /// vanilla, then fall past the checkpoint and kill the player right
    /// after every revive. Meteor damage is already suppressed above; this
    /// closes the timer path so the planet never spontaneously
    /// disintegrates by itself.
    /// </summary>
    [HarmonyPatch(
        typeof(TimedFragmentIntegrity),
        "CanBreak"
    )]
    internal static class ReturnTimedFragmentBreakSuppressionPatch
    {
        private static bool Prefix(
            TimedFragmentIntegrity __instance,
            ref bool __result
        )
        {
            try
            {
                if (__instance != null &&
                    LoadManager.GetCurrentScene() == OWScene.SolarSystem &&
                    ReturnBrittleHollowMeteorImmunityPatch
                        .IsAttachedToBrittleHollow(__instance.transform))
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
                // Preservation is optional and must never interrupt the
                // stock fragment path.
            }
            return true;
        }
    }

    /// <summary>
    /// Cancels the timed break itself. TimedFragmentIntegrity.Awake
    /// schedules OnLatestTimeReached at _latestTime, which zeroes the
    /// fragment's integrity and detaches it. Skipping that callback keeps
    /// every Cannon Path fragment attached for the whole loop.
    /// </summary>
    [HarmonyPatch(
        typeof(TimedFragmentIntegrity),
        "OnLatestTimeReached"
    )]
    internal static class ReturnTimedFragmentLatestTimeSuppressionPatch
    {
        private static bool Prefix(TimedFragmentIntegrity __instance)
        {
            try
            {
                if (__instance != null &&
                    LoadManager.GetCurrentScene() == OWScene.SolarSystem &&
                    ReturnBrittleHollowMeteorImmunityPatch
                        .IsAttachedToBrittleHollow(__instance.transform))
                {
                    return false;
                }
            }
            catch
            {
                // Preservation is optional and must never interrupt the
                // stock fragment path.
            }
            return true;
        }
    }
}
