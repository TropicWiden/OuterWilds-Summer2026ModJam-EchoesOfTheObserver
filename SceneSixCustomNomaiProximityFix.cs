using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Keeps Return's dynamically cloned Nomai computer and recordings
    /// readable at point-blank range. This is deliberately limited to objects
    /// whose ancestors use Return's own names, leaving all vanilla text alone.
    /// </summary>
    [HarmonyPatch(typeof(NomaiTranslator), "Update")]
    internal static class SceneSixCustomNomaiProximityFix
    {
        private const string ComputerName = "Return_RevivalComputer";
        private const string RecordingPrefix = "Return_Recording_";
        private const float ComputerSurfaceMargin = 0.25f;
        private const float MinimumAimDot = 0.05f;

        private static NomaiComputer _computer;
        private static readonly List<NomaiAudioVolume> Recordings =
            new List<NomaiAudioVolume>();

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(NomaiTranslator __instance)
        {
            if (!SceneSixController.IsActive ||
                LoadManager.GetCurrentScene() != OWScene.SolarSystem ||
                __instance == null ||
                !__instance.IsEquipped())
            {
                return;
            }

            Traverse translator = Traverse.Create(__instance);
            Transform ray = translator.Field("_raycastTransform")
                .GetValue<Transform>();
            NomaiTranslatorProp prop = translator.Field("_translatorProp")
                .GetValue<NomaiTranslatorProp>();
            if (ray == null || prop == null)
            {
                return;
            }

            NomaiText currentText = translator.Field("_currentNomaiText")
                .GetValue<NomaiText>();
            if (currentText == null && RestoreComputer(translator, prop, ray))
            {
                return;
            }

            if (currentText != null)
            {
                return;
            }

            NomaiAudioVolume currentAudio = translator
                .Field("_currentNomaiAudioVolume")
                .GetValue<NomaiAudioVolume>();
            if (currentAudio != null &&
                IsReturnRecording(currentAudio.transform) &&
                ContainsPoint(currentAudio, ray.position))
            {
                return;
            }

            NomaiAudioVolume nearest = FindNearestRecording(ray.position);
            if (nearest != null)
            {
                // NomaiAudioDetector clears its selection whenever any
                // overlapping volume is exited. Re-select the recording the
                // player is still physically inside.
                __instance.SetNomaiAudio(nearest);
            }
        }

        private static bool RestoreComputer(
            Traverse translator,
            NomaiTranslatorProp prop,
            Transform ray
        )
        {
            NomaiComputer computer = GetComputer();
            if (computer == null)
            {
                return false;
            }

            CapsuleCollider collider =
                computer.GetComponent<CapsuleCollider>();
            if (collider == null || !collider.enabled)
            {
                return false;
            }

            Vector3 rayPosition = ray.position;
            float surfaceDistance = Vector3.Distance(
                rayPosition,
                collider.ClosestPoint(rayPosition)
            );
            if (surfaceDistance >
                computer.GetMinimumReadableDistance() +
                ComputerSurfaceMargin)
            {
                return false;
            }

            NomaiComputerRing ring = computer.GetClosestRing(rayPosition);
            if (ring == null)
            {
                return false;
            }

            Vector3 towardRing = ring.transform.position - rayPosition;
            if (towardRing.sqrMagnitude > 0.0001f &&
                Vector3.Dot(ray.forward, towardRing.normalized) <
                MinimumAimDot)
            {
                return false;
            }

            translator.Field("_currentNomaiText").SetValue(computer);
            prop.SetTargetingGhostText(false);
            prop.ClearNomaiTextLine();
            prop.SetNomaiText(computer, ring.GetEntryID());
            prop.SetNomaiComputerRing(ring);
            prop.SetTooCloseToTarget(true);
            return true;
        }

        private static NomaiComputer GetComputer()
        {
            if (_computer != null &&
                _computer.gameObject.scene.IsValid() &&
                HasNamedAncestor(_computer.transform, ComputerName, false))
            {
                return _computer;
            }

            _computer = null;
            foreach (NomaiComputer candidate in
                Resources.FindObjectsOfTypeAll<NomaiComputer>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    HasNamedAncestor(candidate.transform, ComputerName, false))
                {
                    _computer = candidate;
                    break;
                }
            }
            return _computer;
        }

        private static NomaiAudioVolume FindNearestRecording(
            Vector3 worldPosition
        )
        {
            RefreshRecordingsIfNeeded();
            NomaiAudioVolume nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (NomaiAudioVolume recording in Recordings)
            {
                if (recording == null ||
                    !recording.gameObject.scene.IsValid() ||
                    !recording.enabled ||
                    !ContainsPoint(recording, worldPosition))
                {
                    continue;
                }

                float distance = Vector3.SqrMagnitude(
                    recording.transform.position - worldPosition
                );
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = recording;
                }
            }
            return nearest;
        }

        private static void RefreshRecordingsIfNeeded()
        {
            bool valid = Recordings.Count > 0;
            foreach (NomaiAudioVolume recording in Recordings)
            {
                if (recording == null ||
                    !recording.gameObject.scene.IsValid())
                {
                    valid = false;
                    break;
                }
            }
            if (valid)
            {
                return;
            }

            Recordings.Clear();
            foreach (NomaiAudioVolume candidate in
                Resources.FindObjectsOfTypeAll<NomaiAudioVolume>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    IsReturnRecording(candidate.transform))
                {
                    Recordings.Add(candidate);
                }
            }
        }

        private static bool ContainsPoint(
            NomaiAudioVolume volume,
            Vector3 worldPosition
        )
        {
            SphereShape shape = volume == null
                ? null
                : volume.GetComponent<SphereShape>();
            if (shape == null || !shape.enabled || !shape.active)
            {
                return false;
            }

            Vector3 scale = volume.transform.lossyScale;
            float radius = shape.radius * Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
            );
            Vector3 center = volume.transform.TransformPoint(shape.center);
            return Vector3.SqrMagnitude(worldPosition - center) <=
                radius * radius;
        }

        private static bool IsReturnRecording(Transform transform)
        {
            return HasNamedAncestor(transform, RecordingPrefix, true);
        }

        private static bool HasNamedAncestor(
            Transform transform,
            string name,
            bool prefix
        )
        {
            Transform current = transform;
            while (current != null)
            {
                if (prefix
                        ? current.name.StartsWith(name)
                        : current.name == name)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
