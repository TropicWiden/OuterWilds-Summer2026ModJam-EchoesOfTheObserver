using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Return
{
    /// <summary>
    /// Builds Return's title-only black-hole/white-hole display. Nothing in
    /// this file runs in the SolarSystem scene or changes gameplay portals.
    /// </summary>
    internal static class ReturnTitleScreenController
    {
        private const string OrbitRigName =
            "Return_Title_BlackWhiteOrbit";

        private static ReturnMod _mod;
        private static bool _registered;
        private static Texture2D _subtitleTexture;
        private static Sprite _subtitleSprite;

        public static void Register(ReturnMod mod, INewHorizons newHorizons)
        {
            if (_registered || mod == null || newHorizons == null)
            {
                return;
            }

            _registered = true;
            _mod = mod;

            try
            {
                // Do not register a New Horizons title-scene builder here.
                // That registration changes the title/solar-system loading
                // context and can alter the initial spatial phase used by
                // the Interloper interception calculation. Waiting for NH's
                // title assets and then editing the already-loaded menu keeps
                // the exact same visual without participating in world load.
                newHorizons.GetAllTitleScreensLoadedEvent().AddListener(
                    ApplyToLoadedTitleScreen
                );
                mod.StartCoroutine(ApplyWhenReady());
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN TITLE] Registration failed without affecting " +
                    "gameplay: " + exception,
                    MessageType.Error
                );
            }
        }

        private static IEnumerator ApplyWhenReady()
        {
            float timeout = Time.realtimeSinceStartup + 20f;
            while (LoadManager.GetCurrentScene() == OWScene.TitleScreen &&
                   Time.realtimeSinceStartup < timeout)
            {
                GameObject sceneRoot = GameObject.Find("Scene");
                if (TryBuild(sceneRoot))
                {
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private static void ApplyToLoadedTitleScreen()
        {
            if (LoadManager.GetCurrentScene() != OWScene.TitleScreen)
            {
                return;
            }

            GameObject sceneRoot = GameObject.Find("Scene");
            TryBuild(sceneRoot);
        }

        private static void BuildTitleScreen(GameObject sceneRoot)
        {
            TryBuild(sceneRoot);
        }

        private static bool TryBuild(GameObject sceneRoot)
        {
            if (sceneRoot == null ||
                LoadManager.GetCurrentScene() != OWScene.TitleScreen)
            {
                return false;
            }

            Transform pivot = FindDescendant(
                sceneRoot.transform,
                "PlanetPivot"
            );
            if (pivot == null)
            {
                return false;
            }

            ApplySubtitle(sceneRoot.transform);

            Transform existing = pivot.Find(OrbitRigName);
            if (existing != null)
            {
                return true;
            }

            Transform vanillaPlanet = FindDescendant(pivot, "PlanetRoot");
            if (vanillaPlanet != null)
            {
                vanillaPlanet.gameObject.SetActive(false);
            }

            GameObject rig = new GameObject(OrbitRigName);
            rig.transform.SetParent(pivot, false);
            rig.transform.localPosition = Vector3.zero;
            rig.transform.localRotation = Quaternion.identity;
            rig.transform.localScale = Vector3.one;

            Transform blackHole = CreateHole(
                rig.transform,
                "Return_Title_BlackHole",
                true
            );
            Transform whiteHole = CreateHole(
                rig.transform,
                "Return_Title_WhiteHole",
                false
            );

            ReturnTitleOrbitAnimator animator =
                rig.AddComponent<ReturnTitleOrbitAnimator>();
            animator.Initialize(blackHole, whiteHole);

            ReturnDebugLog.Write(
                "[RETURN TITLE] Replaced the menu planet with an orbiting " +
                "black-hole/white-hole pair.",
                MessageType.Success
            );
            return true;
        }

        private static void ApplySubtitle(Transform sceneRoot)
        {
            Transform subtitle = FindDescendant(
                sceneRoot,
                "Logo_EchoesOfTheEye"
            );
            Image image = subtitle == null
                ? null
                : subtitle.GetComponent<Image>();
            if (image == null || _mod == null)
            {
                return;
            }

            if (_subtitleSprite == null)
            {
                string path = Path.Combine(
                    _mod.ModHelper.Manifest.ModFolderPath,
                    "subtitle.png"
                );
                if (!File.Exists(path))
                {
                    return;
                }

                _subtitleTexture = new Texture2D(
                    2,
                    2,
                    TextureFormat.ARGB32,
                    false
                );
                _subtitleTexture.name = "Return_Title_Subtitle_Texture";
                if (!_subtitleTexture.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(_subtitleTexture);
                    _subtitleTexture = null;
                    return;
                }

                _subtitleSprite = Sprite.Create(
                    _subtitleTexture,
                    new Rect(
                        0f,
                        0f,
                        _subtitleTexture.width,
                        _subtitleTexture.height
                    ),
                    new Vector2(0.5f, 0.5f),
                    100f
                );
                _subtitleSprite.name = "Return_Title_Subtitle_Sprite";
            }

            image.sprite = _subtitleSprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.SetNativeSize();
            subtitle.gameObject.SetActive(true);
        }

        private static Transform CreateHole(
            Transform parent,
            string objectName,
            bool black
        )
        {
            GameObject root = new GameObject(objectName);
            root.transform.SetParent(parent, false);

            // The title camera can use the same screen-space singularity
            // shader as the real Brittle Hollow and White Hole portals. The
            // surrounding sphere only supplies draw bounds; the shader draws
            // both the core and the refraction/distortion itself.
            if (TryCreateVanillaSingularity(root.transform, objectName, black))
            {
                return root.transform;
            }

            Color coreColor = black
                ? new Color(0.002f, 0.004f, 0.012f, 1f)
                : new Color(0.90f, 0.98f, 1f, 1f);
            Color ringColor = black
                ? new Color(0.30f, 0.65f, 1f, 1f)
                : new Color(0.62f, 0.92f, 1f, 1f);
            Color secondRingColor = black
                ? new Color(0.55f, 0.28f, 1f, 1f)
                : new Color(1f, 1f, 1f, 1f);

            GameObject core = GameObject.CreatePrimitive(
                PrimitiveType.Sphere
            );
            core.name = objectName + "_Core";
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = Vector3.one * 5.2f;
            RemoveCollider(core);
            SetMaterial(
                core.GetComponent<Renderer>(),
                CreateOpaqueMaterial(coreColor, !black)
            );

            GameObject halo = GameObject.CreatePrimitive(
                PrimitiveType.Sphere
            );
            halo.name = objectName + "_Halo";
            halo.transform.SetParent(root.transform, false);
            halo.transform.localScale = Vector3.one * 6.5f;
            RemoveCollider(halo);
            SetMaterial(
                halo.GetComponent<Renderer>(),
                CreateHaloMaterial(
                    black
                        ? new Color(0.18f, 0.48f, 1f, 0.14f)
                        : new Color(0.55f, 0.90f, 1f, 0.22f)
                )
            );

            GameObject ringA = CreateTorus(
                objectName + "_RingA",
                3.45f,
                0.20f,
                ringColor
            );
            ringA.transform.SetParent(root.transform, false);
            ringA.transform.localRotation =
                Quaternion.Euler(67f, 0f, black ? 18f : -18f);

            GameObject ringB = CreateTorus(
                objectName + "_RingB",
                3.85f,
                0.10f,
                secondRingColor
            );
            ringB.transform.SetParent(root.transform, false);
            ringB.transform.localRotation =
                Quaternion.Euler(108f, black ? 28f : -28f, 0f);

            return root.transform;
        }

        private static bool TryCreateVanillaSingularity(
            Transform parent,
            string objectName,
            bool black
        )
        {
            Shader shader = Shader.Find("Outer Wilds/Effects/Singularity");
            if (shader == null)
            {
                ReturnDebugLog.Write(
                    "[RETURN TITLE] The stock singularity shader was not " +
                    "available; retaining the safe fallback visual.",
                    MessageType.Warning
                );
                return false;
            }

            const float visibleRadius = 2.6f;
            const float distortionRadius = 6.5f;
            const float distortionFadeDistance = 4.5f;

            GameObject visual = GameObject.CreatePrimitive(
                PrimitiveType.Sphere
            );
            visual.name = objectName + "_VanillaSingularity";
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one *
                (distortionRadius * 2f);
            RemoveCollider(visual);

            Material material = new Material(shader);
            material.name = black
                ? "Return_Title_BlackHole_Singularity"
                : "Return_Title_WhiteHole_Singularity";
            material.SetFloat("_MassScale", black ? 1f : -1f);
            material.SetFloat("_MaxDistortRadius", distortionRadius);
            material.SetFloat(
                "_DistortFadeDist",
                distortionFadeDistance
            );
            material.SetFloat("_Radius", visibleRadius);
            material.SetColor(
                "_Color",
                black
                    ? new Color(0f, 0f, 0f, 1f)
                    : new Color(1.8820289f, 1.8820289f, 1.8820289f, 1f)
            );

            SetMaterial(visual.GetComponent<Renderer>(), material);
            return true;
        }

        private static GameObject CreateTorus(
            string objectName,
            float majorRadius,
            float minorRadius,
            Color color
        )
        {
            const int majorSegments = 64;
            const int minorSegments = 10;
            Vector3[] vertices = new Vector3[
                majorSegments * minorSegments
            ];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] triangles = new int[
                majorSegments * minorSegments * 6
            ];

            for (int major = 0; major < majorSegments; major++)
            {
                float majorAngle = major * Mathf.PI * 2f /
                    majorSegments;
                float majorCos = Mathf.Cos(majorAngle);
                float majorSin = Mathf.Sin(majorAngle);
                for (int minor = 0; minor < minorSegments; minor++)
                {
                    float minorAngle = minor * Mathf.PI * 2f /
                        minorSegments;
                    float minorCos = Mathf.Cos(minorAngle);
                    float minorSin = Mathf.Sin(minorAngle);
                    int vertex = major * minorSegments + minor;
                    float radial = majorRadius +
                        minorRadius * minorCos;
                    vertices[vertex] = new Vector3(
                        radial * majorCos,
                        radial * majorSin,
                        minorRadius * minorSin
                    );
                    normals[vertex] = new Vector3(
                        minorCos * majorCos,
                        minorCos * majorSin,
                        minorSin
                    );

                    int nextMajor = (major + 1) % majorSegments;
                    int nextMinor = (minor + 1) % minorSegments;
                    int triangle = vertex * 6;
                    int a = vertex;
                    int b = nextMajor * minorSegments + minor;
                    int c = nextMajor * minorSegments + nextMinor;
                    int d = major * minorSegments + nextMinor;
                    triangles[triangle] = a;
                    triangles[triangle + 1] = b;
                    triangles[triangle + 2] = c;
                    triangles[triangle + 3] = a;
                    triangles[triangle + 4] = c;
                    triangles[triangle + 5] = d;
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = objectName + "_Mesh";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            GameObject torus = new GameObject(objectName);
            torus.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = torus.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateOpaqueMaterial(color, true);
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return torus;
        }

        private static Material CreateOpaqueMaterial(
            Color color,
            bool emissive
        )
        {
            Shader shader = Shader.Find("Standard") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Diffuse");
            Material material = new Material(shader);
            material.name = "Return_Title_Opaque";
            material.color = color;
            if (material.HasProperty("_EmissionColor") && emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 4f);
            }
            return material;
        }

        private static Material CreateHaloMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard") ??
                Shader.Find("Unlit/Transparent") ??
                Shader.Find("Unlit/Color");
            Material material = new Material(shader);
            material.name = "Return_Title_Halo";
            material.color = color;
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)
                    UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)
                    UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 3f);
            }
            return material;
        }

        private static void SetMaterial(
            Renderer renderer,
            Material material
        )
        {
            if (renderer == null)
            {
                return;
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = true;
        }

        private static void RemoveCollider(GameObject gameObject)
        {
            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string objectName
        )
        {
            if (root == null)
            {
                return null;
            }
            if (string.Equals(
                    root.name,
                    objectName,
                    StringComparison.Ordinal
                ))
            {
                return root;
            }
            for (int index = 0; index < root.childCount; index++)
            {
                Transform result = FindDescendant(
                    root.GetChild(index),
                    objectName
                );
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }

    internal sealed class ReturnTitleOrbitAnimator : MonoBehaviour
    {
        private const float OrbitRadius = 12f;
        private const float OrbitDegreesPerSecond = 13f;

        private Transform _blackHole;
        private Transform _whiteHole;
        private float _angle;

        public void Initialize(
            Transform blackHole,
            Transform whiteHole
        )
        {
            _blackHole = blackHole;
            _whiteHole = whiteHole;
            _angle = 28f;
            UpdatePositions();
        }

        private void LateUpdate()
        {
            if (_blackHole == null || _whiteHole == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                transform.rotation = Quaternion.LookRotation(
                    -camera.transform.forward,
                    camera.transform.up
                );
            }

            _angle = Mathf.Repeat(
                _angle + OrbitDegreesPerSecond * Time.unscaledDeltaTime,
                360f
            );
            UpdatePositions();
            _blackHole.Rotate(Vector3.up, 18f * Time.unscaledDeltaTime);
            _whiteHole.Rotate(Vector3.up, -18f * Time.unscaledDeltaTime);
        }

        private void UpdatePositions()
        {
            float radians = _angle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(
                Mathf.Cos(radians),
                Mathf.Sin(radians),
                0f
            ) * OrbitRadius;
            _blackHole.localPosition = offset;
            _whiteHole.localPosition = -offset;
        }
    }

    /// <summary>
    /// Shows the "if you feel lost, hug the little blue fish" hint as a
    /// title-screen popup, in the same style as the vanilla startup popups
    /// (title plus a Continue prompt). It appears once each time the title
    /// menu finishes its entry animation.
    /// </summary>
    [HarmonyPatch(typeof(TitleScreenManager), "OnTitleMenuAnimationComplete")]
    internal static class ReturnTitleMenuHintPopupPatch
    {
        private const string HintKey = "$RETURN_ENTRY_HINT";
        private static bool _shownSinceTitleLoad;

        public static void ResetShownFlag()
        {
            _shownSinceTitleLoad = false;
        }

        private static void Postfix(TitleScreenManager __instance)
        {
            if (_shownSinceTitleLoad ||
                __instance == null ||
                LoadManager.GetCurrentScene() != OWScene.TitleScreen)
            {
                return;
            }
            _shownSinceTitleLoad = true;

            try
            {
                Traverse manager = Traverse.Create(__instance);
                PopupMenu popup = manager
                    .Field("_okCancelPopup")
                    .GetValue<PopupMenu>();
                if (popup == null)
                {
                    return;
                }

                ScreenPrompt continuePrompt = manager
                    .Field("_continuePrompt")
                    .GetValue<ScreenPrompt>();
                string text = TranslateHint();

                // Mirror the vanilla startup-popup flow: keep the input
                // module live so the popup can be confirmed with the gamepad.
                OWMenuInputModule inputModule = manager
                    .Field("_inputModule")
                    .GetValue<OWMenuInputModule>();
                CanvasGroup raycastBlocker = manager
                    .Field("_titleMenuRaycastBlocker")
                    .GetValue<CanvasGroup>();
                if (inputModule != null)
                {
                    inputModule.EnableInputs();
                }
                if (raycastBlocker != null)
                {
                    raycastBlocker.blocksRaycasts = false;
                }

                popup.OnPopupConfirm -= OnHintPopupClosed;
                popup.OnPopupCancel -= OnHintPopupClosed;
                popup.ResetPopup();
                popup.SetUpPopup(
                    text,
                    InputLibrary.menuConfirm,
                    null,
                    continuePrompt,
                    null,
                    true,
                    false
                );
                popup.OnPopupConfirm += OnHintPopupClosed;
                popup.OnPopupCancel += OnHintPopupClosed;
                popup.EnableMenu(true);
            }
            catch (Exception exception)
            {
                ReturnDebugLog.Write(
                    "[RETURN TITLE] Could not show the entry hint popup: " +
                    exception,
                    MessageType.Warning
                );
            }
        }


        /// <summary>
        /// Re-selects the default main-menu button after the hint popup is
        /// dismissed, so gamepad navigation keeps working. Without this the
        /// popup steals the EventSystem selection and the title menu is left
        /// with nothing selected (mouse still works, gamepad does not).
        /// </summary>
        private static void OnHintPopupClosed()
        {
            TitleScreenManager manager = UnityEngine.Object
                .FindObjectOfType<TitleScreenManager>();
            if (manager == null)
            {
                return;
            }
            Traverse traverse = Traverse.Create(manager);
            PopupMenu popup = traverse
                .Field("_okCancelPopup")
                .GetValue<PopupMenu>();
            if (popup != null)
            {
                popup.OnPopupConfirm -= OnHintPopupClosed;
                popup.OnPopupCancel -= OnHintPopupClosed;
            }

            Selectable target = null;
            Selectable resumeGame = traverse
                .Field("_resumeGameButton")
                .GetValue<Selectable>();
            Selectable newGame = traverse
                .Field("_newGameButton")
                .GetValue<Selectable>();
            if (resumeGame != null &&
                resumeGame.gameObject.activeInHierarchy &&
                resumeGame.interactable)
            {
                target = resumeGame;
            }
            else if (newGame != null &&
                newGame.gameObject.activeInHierarchy &&
                newGame.interactable)
            {
                target = newGame;
            }
            if (target == null)
            {
                target = newGame;
            }
            if (target == null)
            {
                return;
            }
            SelectableAudioPlayer audioPlayer =
                target.GetComponent<SelectableAudioPlayer>();
            if (audioPlayer != null)
            {
                audioPlayer.SilenceNextSelectEvent();
            }
            OWMenuInputModule menuInput = Locator.GetMenuInputModule();
            if (menuInput != null)
            {
                menuInput.SelectOnNextUpdate(target);
            }
        }

        private static string TranslateHint()
        {
            ReturnMod mod = ReturnMod.Instance;
            if (mod?.NewHorizons != null)
            {
                string translated = mod.NewHorizons.GetTranslationForUI(
                    HintKey
                );
                if (!string.IsNullOrEmpty(translated) &&
                    translated != HintKey)
                {
                    return translated;
                }
            }
            return "If you feel lost, try hugging the little blue fish. " +
                "Remember to check your ship log.";
        }
    }

    /// <summary>
    /// Re-arms the hint popup each time the TitleScreen scene finishes
    /// loading, so returning to the main menu shows it again.
    /// </summary>
    [HarmonyPatch(typeof(TitleScreenManager), "OnCompleteSceneLoad")]
    internal static class ReturnTitleMenuHintResetPatch
    {
        private static void Postfix()
        {
            ReturnTitleMenuHintPopupPatch.ResetShownFlag();
        }
    }
}
