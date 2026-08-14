using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using System.Text;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Optional Scene 6 props. This controller is isolated from the locked
    /// SceneSixController so a failed prop can never interrupt player setup.
    /// </summary>
    internal static class SceneSixAdditionsController
    {
        public static IEnumerator Prepare(ReturnMod mod)
        {
            // Wait until the locked Scene 6 setup has restored the player,
            // gravity-cannon sector, translator and revival computer.
            yield return new WaitForSecondsRealtime(10f);

            if (!SceneSixController.IsActive ||
                LoadManager.GetCurrentScene() != OWScene.SolarSystem)
            {
                yield break;
            }

            OWRigidbody brittleHollow = FindBody("BrittleHollow_Body");
            Transform parent = FindChildByName(
                brittleHollow == null ? null : brittleHollow.transform,
                "Interactables_GravityCannon"
            );
            Sector sector = FindSector(
                brittleHollow == null ? null : brittleHollow.transform,
                "Sector_GravityCannon"
            );

            if (brittleHollow == null || parent == null || sector == null)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN ADDITIONS] Brittle Hollow gravity-cannon " +
                    "references were unavailable. Existing Scene 6 was " +
                    "left untouched.",
                    MessageType.Error
                );
                yield break;
            }

            TryCreateRecording(
                mod,
                brittleHollow,
                parent,
                sector,
                "Return_Recording_1",
                new Vector3(-224.4084f, 5.1523f, 91.4020f),
                new Quaternion(
                    0.143889f,
                    0.143520f,
                    0.695843f,
                    0.688840f
                ),
                BuildSceneFiveKeys()
            );
            TryCreateRecording(
                mod,
                brittleHollow,
                parent,
                sector,
                "Return_Recording_2",
                new Vector3(-223.9839f, -6.5741f, 91.8540f),
                new Quaternion(
                    0.143889f,
                    0.143520f,
                    0.695843f,
                    0.688840f
                ),
                new[]
                {
                    "$RETURN_RECORDING2_01",
                    "$RETURN_RECORDING2_02",
                    "$RETURN_RECORDING2_03"
                }
            );
            TryCreateRecording(
                mod,
                brittleHollow,
                parent,
                sector,
                "Return_Recording_3",
                new Vector3(-223.4348f, -2.1944f, 94.0511f),
                new Quaternion(
                    0.143889f,
                    0.143520f,
                    0.695843f,
                    0.688840f
                ),
                new[]
                {
                    "$RETURN_RECORDING3_01",
                    "$RETURN_RECORDING3_02",
                    "$RETURN_RECORDING3_03",
                    "$RETURN_RECORDING3_04"
                }
            );
            TryCreateWarpCore(
                mod,
                brittleHollow,
                parent,
                sector
            );

            Physics.SyncTransforms();
        }

        private static string[] BuildSceneFiveKeys()
        {
            string[] keys = new string[18];
            for (int index = 0; index < keys.Length; index++)
            {
                keys[index] =
                    "$RETURN_SCENE5_" + (index + 1).ToString("D2");
            }
            return keys;
        }

        private static void TryCreateRecording(
            ReturnMod mod,
            OWRigidbody brittleHollow,
            Transform parent,
            Sector sector,
            string objectName,
            Vector3 localPosition,
            Quaternion localRotation,
            string[] textKeys
        )
        {
            try
            {
                if (FindNamedObject(objectName, brittleHollow.transform) !=
                    null)
                {
                    return;
                }

                GameObject template = FindRecorderTemplate(
                    brittleHollow.transform
                );
                if (template == null)
                {
                    throw new InvalidOperationException(
                        "No complete vanilla Nomai recorder template was " +
                        "available."
                    );
                }

                GameObject recording = UnityEngine.Object.Instantiate(
                    template,
                    parent,
                    false
                );
                recording.name = objectName;
                recording.SetActive(false);

                TextAsset textAsset = new TextAsset(
                    BuildNomaiXml(mod, textKeys)
                );
                textAsset.name = objectName + "_Text";

                NomaiText text =
                    recording.GetComponentInChildren<NomaiText>(true);
                if (text == null)
                {
                    UnityEngine.Object.Destroy(recording);
                    throw new InvalidOperationException(
                        "The cloned recorder lost its NomaiText component."
                    );
                }

                text.SetTextAsset(textAsset);
                text.VerifyInitialized();
                PlaceAtLocalTransform(
                    recording.transform,
                    brittleHollow,
                    localPosition,
                    localRotation
                );
                RestorePropState(recording, sector, true);
                LockRecordingToGravityCannon(recording);
                recording.SetActive(true);
                RestoreRecordingAudioVolume(
                    recording,
                    text,
                    objectName,
                    mod
                );

                mod.ModHelper.Console.WriteLine(
                    "[RETURN RECORDING] " + objectName +
                    " cloned from a complete vanilla recorder; blocks=" +
                    text.GetNumTextBlocks() + "; renderers=" +
                    recording.GetComponentsInChildren<Renderer>(true).Length +
                    ".",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN RECORDING] " + objectName +
                    " was skipped without affecting Scene 6: " + exception,
                    MessageType.Error
                );
            }
        }

        private static void RestoreRecordingAudioVolume(
            GameObject recording,
            NomaiText text,
            string objectName,
            ReturnMod mod
        )
        {
            // A Nomai recorder is selected through its audio effect volume,
            // not through a raycast collider. Templates found in an unloaded
            // sector often have their SphereShape disabled. A late clone then
            // keeps that disabled state, so NomaiAudioDetector never tells the
            // translator that a recording is nearby.
            GameObject volumeObject = text.gameObject;
            int effectVolumeLayer =
                LayerMask.NameToLayer("BasicEffectVolume");
            if (effectVolumeLayer >= 0)
            {
                volumeObject.layer = effectVolumeLayer;
            }

            SphereShape shape = volumeObject.GetComponent<SphereShape>();
            NomaiAudioVolume audioVolume =
                volumeObject.GetComponent<NomaiAudioVolume>();
            if (shape == null || audioVolume == null)
            {
                throw new InvalidOperationException(
                    "The cloned recorder lost its Nomai audio volume."
                );
            }

            shape.enabled = true;
            shape.SetActivation(true);
            audioVolume.enabled = true;
            audioVolume.SetVolumeActivation(true);

            mod.ModHelper.Console.WriteLine(
                "[RETURN RECORDING AUDIO] name=" + objectName +
                "; object=" + volumeObject.name +
                "; layer=" + volumeObject.layer +
                "; shapeEnabled=" + shape.enabled +
                "; shapeActive=" + shape.active +
                "; radius=" + shape.radius.ToString("F2") +
                "; nomaiText=" + (text != null) +
                "; audioEnabled=" + audioVolume.enabled + ".",
                MessageType.Success
            );
        }

        private static void TryCreateWarpCore(
            ReturnMod mod,
            OWRigidbody brittleHollow,
            Transform parent,
            Sector sector
        )
        {
            const string objectName = "Return_PickableWarpCore";
            try
            {
                // New Horizons preserves a held item while reloading configs
                // and temporarily reparents it away from Brittle Hollow. A
                // body-local search misses that item and creates a duplicate
                // on the ground, so check the entire live scene instead.
                if (FindAnyWarpCore(objectName) != null)
                {
                    return;
                }

                WarpCoreItem template = FindAdvancedWarpCoreTemplate();
                if (template == null)
                {
                    throw new InvalidOperationException(
                        "No complete advanced WarpCoreItem template was " +
                        "available."
                    );
                }

                GameObject core = UnityEngine.Object.Instantiate(
                    template.gameObject,
                    parent,
                    false
                );
                core.name = objectName;
                core.SetActive(false);
                PlaceAtLocalTransform(
                    core.transform,
                    brittleHollow,
                    new Vector3(-223.2253f, -4.6674f, 94.4738f),
                    new Quaternion(
                        0.143889f,
                        0.143520f,
                        0.695843f,
                        0.688841f
                    )
                );

                WarpCoreItem item = core.GetComponent<WarpCoreItem>();
                if (item == null)
                {
                    UnityEngine.Object.Destroy(core);
                    throw new InvalidOperationException(
                        "The cloned core lost its WarpCoreItem component."
                    );
                }

                RestorePropState(core, sector, true);
                item.SetSector(sector);
                item.EnableInteraction(true);
                item.SetColliderActivation(true);
                core.SetActive(true);

                mod.ModHelper.Console.WriteLine(
                    "[RETURN WARP CORE] Visible pickable advanced warp core " +
                    "created; type=" + item.GetWarpCoreType() + ".",
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN WARP CORE] Core was skipped without affecting " +
                    "Scene 6: " + exception,
                    MessageType.Error
                );
            }
        }

        private static WarpCoreItem FindAnyWarpCore(string objectName)
        {
            foreach (WarpCoreItem core in
                Resources.FindObjectsOfTypeAll<WarpCoreItem>())
            {
                if (core != null &&
                    core.gameObject.scene.IsValid() &&
                    core.name == objectName)
                {
                    return core;
                }
            }
            return null;
        }

        private static string BuildNomaiXml(
            ReturnMod mod,
            string[] textKeys
        )
        {
            StringBuilder xml = new StringBuilder("<NomaiObject>");
            for (int index = 0; index < textKeys.Length; index++)
            {
                string translatedText =
                    mod.NewHorizons.GetTranslationForDialogue(
                        textKeys[index]
                    );
                if (string.IsNullOrEmpty(translatedText))
                {
                    translatedText = textKeys[index];
                }

                xml.Append("<TextBlock><ID>");
                xml.Append(index + 1);
                xml.Append("</ID><Text>");
                xml.Append(System.Security.SecurityElement.Escape(
                    translatedText
                ));
                xml.Append("</Text></TextBlock>");
            }
            xml.Append("</NomaiObject>");
            return xml.ToString();
        }

        private static GameObject FindRecorderTemplate(Transform body)
        {
            GameObject fallback = null;
            foreach (NomaiText candidate in
                Resources.FindObjectsOfTypeAll<NomaiText>())
            {
                if (candidate == null ||
                    !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                Transform recorderRoot = candidate.transform.parent;
                while (recorderRoot != null &&
                    !recorderRoot.name.StartsWith("Prefab_NOM_Recorder"))
                {
                    recorderRoot = recorderRoot.parent;
                }
                if (recorderRoot == null)
                {
                    continue;
                }

                GameObject completeRecorder = recorderRoot.gameObject;
                if (IsChildOf(recorderRoot, body))
                {
                    return completeRecorder;
                }
                if (fallback == null)
                {
                    fallback = completeRecorder;
                }
            }
            return fallback;
        }

        private static void LockRecordingToGravityCannon(
            GameObject recording
        )
        {
            OWRigidbody body = recording.GetComponent<OWRigidbody>();
            if (body != null)
            {
                body.SetVelocity(Vector3.zero);
                body.SetAngularVelocity(Vector3.zero);
                body.DisableKinematicSimulation();
                body.MakeKinematic();
                body.SetIsTargetable(false);
            }

            Rigidbody unityBody = recording.GetComponent<Rigidbody>();
            if (unityBody != null)
            {
                unityBody.useGravity = false;
                unityBody.isKinematic = true;
                unityBody.constraints = RigidbodyConstraints.FreezeAll;
            }
        }

        private static WarpCoreItem FindAdvancedWarpCoreTemplate()
        {
            WarpCoreItem fallback = null;
            foreach (WarpCoreItem candidate in
                Resources.FindObjectsOfTypeAll<WarpCoreItem>())
            {
                if (candidate == null ||
                    !candidate.gameObject.scene.IsValid() ||
                    candidate.GetWarpCoreType() != WarpCoreType.Vessel)
                {
                    continue;
                }

                if (candidate.name == "Prefab_NOM_WarpCoreVessel")
                {
                    return candidate;
                }
                if (fallback == null)
                {
                    fallback = candidate;
                }
            }
            return fallback;
        }

        private static void RestorePropState(
            GameObject prop,
            Sector sector,
            bool restoreColliders
        )
        {
            foreach (SectoredMonoBehaviour component in
                prop.GetComponentsInChildren<SectoredMonoBehaviour>(true))
            {
                component.SetSector(sector);
            }

            foreach (OWRenderer renderer in
                prop.GetComponentsInChildren<OWRenderer>(true))
            {
                renderer.SetActivation(true);
                renderer.SetLODActivation(true);
            }
            foreach (Renderer renderer in
                prop.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
            }

            if (!restoreColliders)
            {
                return;
            }

            foreach (OWCollider collider in
                prop.GetComponentsInChildren<OWCollider>(true))
            {
                collider.ListenForParentBodySuspension();
                collider.SetLODLevel(0);
                collider.SetActivation(true);
            }
            foreach (Collider collider in
                prop.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = true;
            }
        }

        private static void PlaceAtLocalTransform(
            Transform target,
            OWRigidbody body,
            Vector3 localPosition,
            Quaternion localRotation
        )
        {
            target.position = body.transform.TransformPoint(localPosition);
            target.rotation = body.GetRotation() * localRotation;
            target.localScale = Vector3.one;
        }

        private static OWRigidbody FindBody(string objectName)
        {
            foreach (OWRigidbody body in
                Resources.FindObjectsOfTypeAll<OWRigidbody>())
            {
                if (body != null &&
                    body.gameObject.scene.IsValid() &&
                    body.name == objectName)
                {
                    return body;
                }
            }
            return null;
        }

        private static Sector FindSector(Transform root, string objectName)
        {
            if (root == null)
            {
                return null;
            }
            foreach (Sector sector in
                Resources.FindObjectsOfTypeAll<Sector>())
            {
                if (sector != null &&
                    sector.gameObject.scene.IsValid() &&
                    sector.name == objectName &&
                    IsChildOf(sector.transform, root))
                {
                    return sector;
                }
            }
            return null;
        }

        private static Transform FindChildByName(
            Transform root,
            string objectName
        )
        {
            if (root == null)
            {
                return null;
            }
            foreach (Transform candidate in
                Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.name == objectName &&
                    IsChildOf(candidate, root))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static GameObject FindNamedObject(
            string objectName,
            Transform root
        )
        {
            Transform result = FindChildByName(root, objectName);
            return result == null ? null : result.gameObject;
        }

        private static bool IsChildOf(Transform child, Transform parent)
        {
            Transform current = child;
            while (current != null)
            {
                if (current == parent)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class SceneSixAdditionsPatch
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
                    SceneSixAdditionsController.Prepare(__instance)
                );
            }
        }
    }
}
