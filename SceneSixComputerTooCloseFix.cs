using HarmonyLib;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// A ray fired from inside a collider cannot hit that collider. When the
    /// player stands extremely close to the dynamically spawned revival
    /// computer, the translator ray starts inside its capsule and vanilla
    /// clears the text before it can display TranslatorTooCloseWarning. This
    /// restores that one missing target after the stock translator update.
    /// </summary>
    [HarmonyPatch(typeof(NomaiTranslator), "Update")]
    internal static class SceneSixComputerTooCloseFix
    {
        private const string RevivalComputerName =
            "Return_RevivalComputer";

        private static NomaiComputer _revivalComputer;

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
            NomaiText current = translator
                .Field("_currentNomaiText")
                .GetValue<NomaiText>();
            if (current != null)
            {
                // Vanilla already owns both normal text and its normal
                // too-close path whenever the forward ray has a target.
                return;
            }

            NomaiComputer computer = GetRevivalComputer();
            if (computer == null)
            {
                return;
            }

            Transform raycastTransform = translator
                .Field("_raycastTransform")
                .GetValue<Transform>();
            CapsuleCollider collider =
                computer.GetComponent<CapsuleCollider>();
            if (raycastTransform == null ||
                collider == null ||
                !collider.enabled)
            {
                return;
            }

            Vector3 rayPosition = raycastTransform.position;
            float distanceToCollider = Vector3.Distance(
                rayPosition,
                collider.ClosestPoint(rayPosition)
            );
            if (distanceToCollider >=
                computer.GetMinimumReadableDistance())
            {
                return;
            }

            NomaiComputerRing ring = computer.GetClosestRing(rayPosition);
            if (ring == null)
            {
                return;
            }

            Vector3 toRing = ring.transform.position - rayPosition;
            if (toRing.sqrMagnitude > 0.0001f &&
                Vector3.Dot(
                    raycastTransform.forward,
                    toRing.normalized
                ) < 0.25f)
            {
                return;
            }

            NomaiTranslatorProp translatorProp = translator
                .Field("_translatorProp")
                .GetValue<NomaiTranslatorProp>();
            if (translatorProp == null)
            {
                return;
            }

            translator.Field("_currentNomaiText").SetValue(computer);
            translatorProp.SetTargetingGhostText(false);
            translatorProp.ClearNomaiTextLine();
            translatorProp.SetNomaiText(
                computer,
                ring.GetEntryID()
            );
            translatorProp.SetNomaiComputerRing(ring);
            translatorProp.SetTooCloseToTarget(true);
        }

        private static NomaiComputer GetRevivalComputer()
        {
            if (_revivalComputer != null &&
                _revivalComputer.gameObject.scene.IsValid() &&
                IsRevivalComputer(_revivalComputer.transform))
            {
                return _revivalComputer;
            }

            _revivalComputer = null;
            foreach (NomaiComputer computer in
                Resources.FindObjectsOfTypeAll<NomaiComputer>())
            {
                if (computer != null &&
                    computer.gameObject.scene.IsValid() &&
                    IsRevivalComputer(computer.transform))
                {
                    _revivalComputer = computer;
                    break;
                }
            }
            return _revivalComputer;
        }

        private static bool IsRevivalComputer(Transform transform)
        {
            Transform current = transform;
            while (current != null)
            {
                if (current.name == RevivalComputerName)
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
    }
}
