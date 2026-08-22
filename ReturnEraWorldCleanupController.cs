using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using OWML.Common;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Removes objects that cannot exist during Return's pre-Hearthian era.
    /// This is intentionally isolated from the locked scene controllers.
    /// Objects are disabled instead of destroyed so stock initialization and
    /// serialized references cannot fail with missing-object exceptions.
    /// </summary>
    internal static class ReturnEraWorldCleanupController
    {
        private const int CleanupPasses = 3;
        private const float InitialDelaySeconds = 2f;
        private const float PassDelaySeconds = 2f;
        private const int CleanupBatchSize = 1200;

        public static IEnumerator Prepare(ReturnMod mod)
        {
            while (!LateInitializerManager.isDoneInitializing)
            {
                yield return null;
            }

            // Let New Horizons, map markers and the locked scene controllers
            // finish creating their scene-owned objects first.
            yield return new WaitForSecondsRealtime(InitialDelaySeconds);

            int totalNpc = 0;
            int totalStructures = 0;
            int totalSkeletons = 0;
            for (int pass = 0; pass < CleanupPasses; pass++)
            {
                CleanupResult result = new CleanupResult();
                yield return ApplyCleanupPassChunked(result);
                totalNpc += result.hearthianNpcs;
                totalStructures += result.hearthianStructures;
                totalSkeletons += result.nomaiSkeletons;

                if (pass + 1 < CleanupPasses)
                {
                    yield return new WaitForSecondsRealtime(
                        PassDelaySeconds
                    );
                }
            }

            if (mod != null)
            {
                ReturnDebugLog.Write(
                    "[RETURN ERA CLEANUP] Hearthian NPCs=" + totalNpc +
                    "; Hearthian structures=" + totalStructures +
                    "; Nomai skeleton objects=" + totalSkeletons +
                    ". Campfires, player, ship and Return objects preserved.",
                    MessageType.Success
                );
            }
        }

        private static IEnumerator ApplyCleanupPassChunked(
            CleanupResult result
        )
        {
            HashSet<GameObject> targets = new HashSet<GameObject>();
            Transform[] transforms =
                Resources.FindObjectsOfTypeAll<Transform>();

            int processed = 0;
            while (processed < transforms.Length)
            {
                int end = Mathf.Min(
                    processed + CleanupBatchSize,
                    transforms.Length
                );
                for (int index = processed; index < end; index++)
                {
                    Transform candidate = transforms[index];
                    if (!IsLiveSceneObject(candidate) ||
                        IsProtectedGameplayObject(candidate))
                    {
                        continue;
                    }

                    CleanupCategory category = Classify(candidate);
                    if (category == CleanupCategory.None ||
                        (category == CleanupCategory.HearthianStructure &&
                            IsCampfireRelated(candidate)))
                    {
                        continue;
                    }

                    if (!targets.Add(candidate.gameObject))
                    {
                        continue;
                    }

                    ReturnEraRemovedMarker marker = candidate.GetComponent<
                        ReturnEraRemovedMarker>();
                    if (marker == null)
                    {
                        marker = candidate.gameObject.AddComponent<
                            ReturnEraRemovedMarker>();
                    }

                    if (candidate.gameObject.activeSelf)
                    {
                        candidate.gameObject.SetActive(false);
                    }

                    if (marker.wasAlreadyCounted)
                    {
                        continue;
                    }
                    marker.wasAlreadyCounted = true;
                    switch (category)
                    {
                        case CleanupCategory.HearthianNpc:
                            result.hearthianNpcs++;
                            break;
                        case CleanupCategory.HearthianStructure:
                            result.hearthianStructures++;
                            break;
                        case CleanupCategory.NomaiSkeleton:
                            result.nomaiSkeletons++;
                            break;
                    }
                }
                processed = end;
                if (processed < transforms.Length)
                {
                    // Spread the full-scene walk across frames so the
                    // opening and Scene 6 loads do not hitch.
                    yield return null;
                }
            }

            // This stock controller deliberately reactivates quantum Nomai
            // skeletons. Stop that behavior while leaving the surrounding
            // quantum terrain and puzzle architecture intact.
            foreach (QuantumSkeletonTower tower in
                Resources.FindObjectsOfTypeAll<QuantumSkeletonTower>())
            {
                if (tower != null &&
                    tower.gameObject.scene.IsValid() &&
                    !IsProtectedGameplayObject(tower.transform))
                {
                    tower.enabled = false;
                }
            }
        }

        private static CleanupCategory Classify(Transform candidate)
        {
            string name = candidate.name ?? string.Empty;
            if (IsHearthianNpcName(name))
            {
                return CleanupCategory.HearthianNpc;
            }
            if (IsNomaiSkeletonName(name))
            {
                return CleanupCategory.NomaiSkeleton;
            }
            if (IsHearthianStructureName(name))
            {
                return CleanupCategory.HearthianStructure;
            }
            return CleanupCategory.None;
        }

        private static bool IsHearthianNpcName(string name)
        {
            if (name.IndexOf(
                    "Traveller_HEA_Player",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0)
            {
                return false;
            }

            return name.StartsWith(
                    "Villager_HEA_",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Traveller_HEA_",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Character_HEA_",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.IndexOf(
                    ":Villager_HEA_",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0 ||
                name.IndexOf(
                    ":Traveller_HEA_",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        private static bool IsNomaiSkeletonName(string name)
        {
            if (name.IndexOf(
                    "NoSkeleton",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0 ||
                name.IndexOf(
                    "AnglerfishSkeleton",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0 ||
                name.IndexOf(
                    "anglerfish_skeleton",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0 ||
                name.IndexOf(
                    "SardineSkeleton",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0)
            {
                return false;
            }

            return name.IndexOf(
                    "Skeleton",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0 ||
                name.IndexOf(
                    "DeadNomai",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0 ||
                name.IndexOf(
                    "nom_skeleton",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        private static bool IsHearthianStructureName(string name)
        {
            if (name.StartsWith(
                    "Structure_HEA_PlayerShip",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return false;
            }

            if (name.StartsWith(
                    "Structure_HEA_",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Structure_TH_HEA_",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Prefab_HEA_VillagePlank",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Architecture_Village",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Architecture_LowerVillage",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Architecture_UpperVillage",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Architecture_Observatory",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "LaunchTower",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Observatory_Exterior",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Observatory_Interior",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Observatory_Collider",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "ObservatoryMap_",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Entryway_HEA_RadioTower",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Village_UnderLaunchTowerProps",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "VillageGeyser_Boards",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                name.StartsWith(
                    "Telescope_Village",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return true;
            }

            bool hearthianPrefab = name.StartsWith(
                "Prefab_HEA_",
                StringComparison.OrdinalIgnoreCase
            );
            return hearthianPrefab &&
                name.IndexOf(
                    "Ship",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0 &&
                name.IndexOf(
                    "PlayerShip",
                    StringComparison.OrdinalIgnoreCase
                ) < 0;
        }

        private static bool IsLiveSceneObject(Transform candidate)
        {
            return candidate != null &&
                candidate.gameObject != null &&
                candidate.gameObject.scene.IsValid();
        }

        private static bool IsProtectedGameplayObject(Transform candidate)
        {
            Transform player = Locator.GetPlayerTransform();
            Transform ship = Locator.GetShipTransform();
            SurveyorProbe probeComponent = Locator.GetProbe();
            Transform probe = probeComponent == null
                ? null
                : probeComponent.transform;

            for (Transform current = candidate;
                current != null;
                current = current.parent)
            {
                if (current == player || current == ship ||
                    current == probe)
                {
                    return true;
                }

                string name = current.name ?? string.Empty;
                if (name.StartsWith(
                        "Return_",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    name.StartsWith(
                        "RETURN_",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    string.Equals(
                        name,
                        "Player_Body",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    string.Equals(
                        name,
                        "Ship_Body",
                        StringComparison.OrdinalIgnoreCase
                    ) ||
                    string.Equals(
                        name,
                        "Probe_Body",
                        StringComparison.OrdinalIgnoreCase
                    ))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsCampfireRelated(Transform candidate)
        {
            for (Transform current = candidate;
                current != null;
                current = current.parent)
            {
                string name = current.name ?? string.Empty;
                if (name.IndexOf(
                        "Campfire",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0 ||
                    name.IndexOf(
                        "Marshmallow",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0 ||
                    name.IndexOf(
                        "SleepingAtCampfire",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private enum CleanupCategory
        {
            None,
            HearthianNpc,
            HearthianStructure,
            NomaiSkeleton
        }

        private class CleanupResult
        {
            public int hearthianNpcs;
            public int hearthianStructures;
            public int nomaiSkeletons;
        }
    }

    /// <summary>
    /// Prevents stock sector/story toggles from making an era-incompatible
    /// object visible again later in the same loop.
    /// </summary>
    internal sealed class ReturnEraRemovedMarker : MonoBehaviour
    {
        [NonSerialized]
        public bool wasAlreadyCounted;

        private void OnEnable()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class ReturnEraWorldCleanupPatch
    {
        private static void Postfix(
            ReturnMod __instance,
            OWScene newScene
        )
        {
            if (newScene != OWScene.SolarSystem || __instance == null)
            {
                return;
            }

            try
            {
                __instance.StartCoroutine(
                    ReturnEraWorldCleanupController.Prepare(__instance)
                );
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN ERA CLEANUP] Could not start cleanup: " +
                    exception,
                    MessageType.Error
                );
            }
        }
    }
}
