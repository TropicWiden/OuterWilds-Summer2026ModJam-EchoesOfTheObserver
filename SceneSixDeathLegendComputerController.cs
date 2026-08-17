using HarmonyLib;
using OWML.Common;
using System;
using System.IO;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Build109: after the player has seen Return's terminal death ending,
    /// future Scene 6 loops give the revival computer a second text ring.
    /// The condition is saved later by the established title-screen save path,
    /// never while the SolarSystem scene is still active.
    /// </summary>
    internal static class SceneSixDeathLegendComputerController
    {
        private const string ProgressFileName =
            "return-progress.json";

        private static bool _storageChecked;
        private static bool _storageUnlocked;

        private const string CountdownText =
            "Revival will become impossible in " +
            "&lt;TimeMinutesRemaining&gt; minutes.";

        private const string LegendText =
            "If you feel lost, check your ship log and map.";

        internal static void MarkDeathEndingSeen()
        {
            if (!SceneSixController.IsActive ||
                !SceneSixEndingController.IsEndingActive)
            {
                return;
            }

            _storageChecked = true;
            _storageUnlocked = true;

            try
            {
                ReturnMod.Instance?.ModHelper.Storage.Save(
                    new ReturnProgressData
                    {
                        DeathEndingSeen = true
                    },
                    ProgressFileName
                );
            }
            catch (Exception exception)
            {
                ReturnMod.Instance?.ModHelper.Console.WriteLine(
                    "[RETURN BUILD109] Could not save the independent " +
                    "death-ending legend state: " + exception,
                    MessageType.Error
                );
            }
            ReturnMod.Instance?.ModHelper.Console.WriteLine(
                "[RETURN BUILD109] Terminal Scene 6 death ending saved; " +
                "the legend ring is unlocked for the next loop.",
                MessageType.Success
            );
        }

        /// <summary>
        /// Deletes the mod's own progress file (and clears the cached
        /// in-memory flags) when a brand-new expedition is started, so the
        /// new game begins with a completely fresh story state. The vanilla
        /// player save is intentionally left untouched.
        /// </summary>

        internal static void ResetProgressState()
        {
            _storageChecked = false;
            _storageUnlocked = false;

            try
            {
                ReturnMod mod = ReturnMod.Instance;
                string folder = mod?.ModHelper?.Manifest?.ModFolderPath;
                if (string.IsNullOrEmpty(folder))
                {
                    return;
                }

                string filePath = Path.Combine(folder, ProgressFileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception exception)
            {
                ReturnMod.Instance?.ModHelper.Console.WriteLine(
                    "[RETURN BUILD118] Could not delete the mod progress " +
                    "file for a new expedition: " + exception,
                    MessageType.Warning
                );
            }
        }

        internal static void AddLegendRingIfUnlocked(
            ReturnMod mod,
            OWRigidbody brittleHollow,
            ref GameObject computerObject
        )
        {
            if (mod == null ||
                mod.NewHorizons == null ||
                brittleHollow == null ||
                computerObject == null ||
                computerObject.GetComponent<
                    ReturnDeathLegendComputerMarker>() != null ||
                !IsLegendUnlocked(mod))
            {
                return;
            }

            GameObject replacement = null;
            try
            {
                string xml =
                    "<NomaiObject>" +
                    "<TextBlock><ID>1</ID><Text>" +
                    CountdownText +
                    "</Text></TextBlock>" +
                    "<TextBlock><ID>2</ID><Text>" +
                    LegendText +
                    "</Text></TextBlock>" +
                    "</NomaiObject>";
                const string textInfo =
                    "{\"type\":\"computer\"," +
                    "\"location\":\"unspecified\"," +
                    "\"position\":{},\"rotation\":{}}";

                Transform oldTransform = computerObject.transform;
                Transform oldParent = oldTransform.parent;
                Vector3 worldPosition = oldTransform.position;
                Quaternion worldRotation = oldTransform.rotation;

                replacement = mod.NewHorizons.CreateNomaiText(
                    xml,
                    textInfo,
                    brittleHollow.gameObject
                );
                if (replacement == null)
                {
                    throw new InvalidOperationException(
                        "New Horizons returned no two-ring computer."
                    );
                }

                replacement.name = "Return_RevivalComputer";
                replacement.transform.position = worldPosition;
                replacement.transform.rotation = worldRotation;
                if (oldParent != null)
                {
                    replacement.transform.SetParent(oldParent, true);
                }
                replacement.AddComponent<
                    ReturnDeathLegendComputerMarker>();
                replacement.SetActive(true);

                Physics.SyncTransforms();
                UnityEngine.Object.Destroy(computerObject);
                computerObject = replacement;

                mod.ModHelper.Console.WriteLine(
                    "[RETURN BUILD109] Revival computer rebuilt with the " +
                    "post-death Giant's Deep legend ring.",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                if (replacement != null)
                {
                    UnityEngine.Object.Destroy(replacement);
                }
                mod.ModHelper.Console.WriteLine(
                    "[RETURN BUILD109] Could not add the death-ending " +
                    "legend ring; the original countdown computer remains: " +
                    exception,
                    MessageType.Error
                );
            }
        }

        private static bool IsLegendUnlocked(ReturnMod mod)
        {
            if (_storageUnlocked)
            {
                return true;
            }

            if (!_storageChecked)
            {
                _storageChecked = true;
                try
                {
                    ReturnProgressData progress =
                        mod.ModHelper.Storage.Load<ReturnProgressData>(
                            ProgressFileName
                        );
                    _storageUnlocked =
                        progress != null && progress.DeathEndingSeen;
                }
                catch
                {
                    // A missing progress file is the normal first-play state.
                    _storageUnlocked = false;
                }
            }

            return _storageUnlocked;
        }
    }

    [Serializable]
    internal sealed class ReturnProgressData
    {
        public bool DeathEndingSeen { get; set; }
    }

    internal sealed class ReturnDeathLegendComputerMarker : MonoBehaviour
    {
    }

    [HarmonyPatch(
        typeof(SceneSixEndingController),
        nameof(SceneSixEndingController.TrySetTerminalGameOver)
    )]
    internal static class ReturnMarkDeathEndingLegendPatch
    {
        private static void Postfix(bool __result)
        {
            if (__result)
            {
                SceneSixDeathLegendComputerController
                    .MarkDeathEndingSeen();
            }
        }
    }

    [HarmonyPatch(typeof(SceneSixController), "CreateRevivalComputer")]
    internal static class ReturnAddDeathLegendRingPatch
    {
        private static void Postfix(
            ReturnMod mod,
            OWRigidbody brittleHollow,
            ref GameObject __result
        )
        {
            SceneSixDeathLegendComputerController
                .AddLegendRingIfUnlocked(
                    mod,
                    brittleHollow,
                    ref __result
                );
        }
    }
}
