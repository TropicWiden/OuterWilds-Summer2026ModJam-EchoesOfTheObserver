using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Return
{
    public class ReturnMod : ModBehaviour
    {
        private static readonly Vector3 MineSpawnLocalPosition =
            new Vector3(-33.204f, -3.038f, -111.262f);

        private static readonly Quaternion MineSpawnLocalRotation =
            new Quaternion(-0.2706f, -0.5436f, 0.6632f, 0.4375f);

        private static readonly Vector3 MineBoxFloorLocalPosition =
            new Vector3(-33.50925f, -13.68641f, -109.4957f);

        private static readonly Vector3 MineBoxFloorLocalNormal =
            new Vector3(-0.2541258f, -0.07989983f, -0.9638653f).normalized;

        public static ReturnMod Instance;
        public INewHorizons NewHorizons;

        private OWRigidbody _timberHearthBody;
        private GameObject _sceneOneRoot;
        private bool _enteredFishBox;
        private bool _showIntroTitle;
        private bool _mineWarpCompleted;
        private float _introTitleStartTime;
        private Font _introTitleFont;

        public void Awake()
        {
            Instance = this;
        }

        public void Start()
        {
            ModHelper.Console.WriteLine("Return is loaded!", MessageType.Success);

            NewHorizons = ModHelper.Interaction.TryGetModApi<INewHorizons>(
                "xen.NewHorizons"
            );

            if (NewHorizons != null)
            {
                ReturnTitleScreenController.Register(this, NewHorizons);
                NewHorizons.LoadConfigs(this);
            }
            else
            {
                ModHelper.Console.WriteLine(
                    "Return could not access the New Horizons API.",
                    MessageType.Error
                );
            }

            new Harmony("Known-Mouse.Return").PatchAll(Assembly.GetExecutingAssembly());
            LoadManager.OnStartSceneLoad += OnStartSceneLoad;
            LoadManager.OnCompleteSceneLoad += OnCompleteSceneLoad;
        }

        public void OnDestroy()
        {
            LoadManager.OnStartSceneLoad -= OnStartSceneLoad;
            LoadManager.OnCompleteSceneLoad -= OnCompleteSceneLoad;
        }

        public void Update()
        {
            if (_timberHearthBody == null || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.f8Key.wasPressedThisFrame)
            {
                LogPlayerPosition();
            }
        }

        public void OnGUI()
        {
            if (!_showIntroTitle)
            {
                return;
            }

            float elapsed = _mineWarpCompleted
                ? Time.unscaledTime - _introTitleStartTime
                : 0f;
            const float holdDuration = 3.5f;
            const float fadeDuration = 1.2f;
            if (elapsed >= holdDuration + fadeDuration)
            {
                _showIntroTitle = false;
                return;
            }

            float alpha = elapsed <= holdDuration
                ? 1f
                : 1f - (elapsed - holdDuration) / fadeDuration;

            Color previousGuiColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture
            );
            GUI.color = previousGuiColor;

            if (_introTitleFont == null)
            {
                _introTitleFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Arial" },
                    72
                );
            }

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.font = _introTitleFont;
            titleStyle.fontSize = Mathf.RoundToInt(
                Mathf.Clamp(Screen.height * 0.075f, 42f, 82f)
            );
            titleStyle.fontStyle = FontStyle.Normal;
            titleStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);

            GUI.Label(
                new Rect(0f, 0f, Screen.width, Screen.height),
                GetIntroTitleText(),
                titleStyle
            );
        }

        private string GetIntroTitleText()
        {
            if (NewHorizons != null)
            {
                string translated = NewHorizons.GetTranslationForUI(
                    "$RETURN_INTRO_AGE"
                );
                if (!string.IsNullOrEmpty(translated) &&
                    translated != "$RETURN_INTRO_AGE")
                {
                    return translated;
                }
            }

            TextTranslation translations = TextTranslation.Get();
            if (translations != null &&
                translations.GetLanguage() ==
                    TextTranslation.Language.CHINESE_SIMPLE)
            {
                return "1.4亿年后";
            }
            return "140 Million Years Later";
        }

        private void OnStartSceneLoad(OWScene previousScene, OWScene newScene)
        {
            if (newScene != OWScene.SolarSystem)
            {
                return;
            }

            // Start covering the screen before SolarSystem begins loading. This
            // prevents even the first frame of the vanilla campfire wake-up
            // sequence from reaching the display.
            _showIntroTitle = true;
            _mineWarpCompleted = false;
            _introTitleStartTime = float.PositiveInfinity;
        }

        private void OnCompleteSceneLoad(OWScene previousScene, OWScene newScene)
        {
            _timberHearthBody = null;
            _sceneOneRoot = null;
            _enteredFishBox = false;

            if (newScene == OWScene.SolarSystem)
            {
                _showIntroTitle = true;
                _mineWarpCompleted = false;
                _introTitleStartTime = float.PositiveInfinity;
                StartCoroutine(PrepareMineScene());
            }
            else
            {
                _showIntroTitle = false;
                _mineWarpCompleted = false;
            }
        }

        private IEnumerator PrepareMineScene()
        {
            while (Locator.GetPlayerBody() == null)
            {
                yield return null;
            }

            _timberHearthBody = FindSceneBody("TimberHearth_Body");
            if (_timberHearthBody == null)
            {
                ModHelper.Console.WriteLine(
                    "Could not find the Timber Hearth rigidbody.",
                    MessageType.Error
                );
                yield break;
            }

            // Keep the original initialization delay. Moving the player before
            // Timber Hearth and the player controller settle changes the final
            // spawn despite using the same recorded coordinates.
            // Keep the cover visible while the vanilla wake prompt owns the
            // pause. Do not warp the player until that initialization has
            // finished, otherwise the controller can resolve inside geometry.
            yield return CompleteSecondWakeConfirmation();

            while (OWTime.IsPaused(OWTime.PauseType.Sleeping) ||
                   OWTime.IsPaused(OWTime.PauseType.Initializing))
            {
                yield return null;
            }

            yield return new WaitForSeconds(2f);

            Vector3 spawnPosition = _timberHearthBody.transform.TransformPoint(
                MineSpawnLocalPosition
            );
            Quaternion spawnRotation = _timberHearthBody.GetRotation() *
                                       MineSpawnLocalRotation;

            CreateSceneOnePrototype();
            PrepareMineNomaiAndDialogue();
            CreateNomaiWallLighting();

            OWRigidbody playerBody = Locator.GetPlayerBody();
            WarpPlayer(playerBody, spawnPosition, spawnRotation);
            ApplyFishPlayerScale(playerBody);
            EnableFishSwimming(playerBody);
            HidePlayerBodyModel(playerBody);
            DisablePlayerFlashlight();
            DisableMineGeysers(spawnPosition);

            yield return new WaitForFixedUpdate();
            yield return null;

            // The full-screen cover must remain indefinitely through loading and
            // the vanilla wait-for-wake-input state. Only begin its timer after
            // the warp has been applied and the player has spent a rendered
            // frame at the mine position.
            _mineWarpCompleted = true;
            ShowIntroTitle();

            ModHelper.Console.WriteLine(
                "[RETURN SCENE 1] Original water restored. The fish box is on " +
                "the mine floor, geysers are disabled, the translator is blocked, " +
                "and silent suit movement is active with all suit geometry hidden. " +
                "Approach the three Nomai and use the normal talk interaction.",
                MessageType.Success
            );
        }

        private IEnumerator CompleteSecondWakeConfirmation()
        {
            // The title-screen confirmation has already happened before the
            // SolarSystem scene exists. This handles only the separate in-world
            // "wake up" confirmation hidden behind Return's black cover.
            while (!LateInitializerManager.isDoneInitializing)
            {
                yield return null;
            }

            PlayerCameraEffectController cameraEffects = null;
            foreach (PlayerCameraEffectController candidate in
                     Resources.FindObjectsOfTypeAll<PlayerCameraEffectController>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.gameObject.CompareTag("MainCamera"))
                {
                    cameraEffects = candidate;
                    break;
                }
            }

            if (cameraEffects == null)
            {
                yield break;
            }

            Traverse wakeState = Traverse.Create(cameraEffects);
            bool waitingForWakeInput =
                wakeState.Field<bool>("_waitForWakeInput").Value;
            if (!waitingForWakeInput)
            {
                yield break;
            }

            ScreenPrompt wakePrompt =
                wakeState.Field<ScreenPrompt>("_wakePrompt").Value;
            wakeState.Field<bool>("_waitForWakeInput").Value = false;

            LateInitializerManager.pauseOnInitialization = false;

            PauseCommandListener pauseListener =
                Locator.GetPauseCommandListener();
            if (pauseListener != null)
            {
                pauseListener.RemovePauseCommandLock();
            }

            PromptManager promptManager = Locator.GetPromptManager();
            if (promptManager != null && wakePrompt != null)
            {
                promptManager.RemoveScreenPrompt(wakePrompt);
            }

            OWTime.Unpause(OWTime.PauseType.Sleeping);
            cameraEffects.WakeUp();

            ModHelper.Console.WriteLine(
                "[RETURN INTRO] Completed the second, in-world wake " +
                "confirmation behind the black cover.",
                MessageType.Info
            );
        }

        private void CreateSceneOnePrototype()
        {
            Vector3 localUp = MineBoxFloorLocalNormal;
            Vector3 localForward = MineSpawnLocalRotation * Vector3.forward;
            localForward = Vector3.ProjectOnPlane(localForward, localUp).normalized;

            _sceneOneRoot = new GameObject("Return_Scene1_Prototype");
            _sceneOneRoot.transform.SetParent(_timberHearthBody.transform, false);
            _sceneOneRoot.transform.localPosition = MineBoxFloorLocalPosition;
            _sceneOneRoot.transform.localRotation = Quaternion.LookRotation(
                localForward,
                localUp
            );
            _sceneOneRoot.transform.localScale = Vector3.one;

            Material frameMaterial = CreateTransparentGlowingMaterial(
                "Return_BoxGlass",
                new Color(0.92f, 0.97f, 1f, 0.16f),
                0.24f
            );
            Material fishMaterial = CreateGlowingMaterial(
                "Return_FishBlue",
                new Color(0.05f, 0.35f, 0.9f, 1f),
                0.35f
            );
            Material eyeMaterial = CreateGlowingMaterial(
                "Return_FishEyes",
                new Color(0.75f, 0.95f, 1f, 1f),
                0.7f
            );
            Material pupilMaterial = CreateGlowingMaterial(
                "Return_FishPupils",
                new Color(0.01f, 0.02f, 0.03f, 1f),
                0f
            );

            CreateBoxFrame(_sceneOneRoot.transform, frameMaterial);

            GameObject boxGlow = new GameObject("FishBox_SoftWhiteGlow");
            boxGlow.transform.SetParent(_sceneOneRoot.transform, false);
            boxGlow.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            Light glowLight = boxGlow.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(0.92f, 0.97f, 1f);
            glowLight.intensity = 0.38f;
            glowLight.range = 3.5f;
            glowLight.shadows = LightShadows.None;

            CreateFourEyedFish(
                _sceneOneRoot.transform,
                new Vector3(-0.45f, 0.45f, 0f),
                Quaternion.Euler(0f, 25f, 0f),
                fishMaterial,
                eyeMaterial,
                pupilMaterial
            );
            CreateFourEyedFish(
                _sceneOneRoot.transform,
                new Vector3(0.35f, 0.7f, 0.15f),
                Quaternion.Euler(0f, -35f, 0f),
                fishMaterial,
                eyeMaterial,
                pupilMaterial
            );

            GameObject triggerObject = new GameObject("FishBox_StoryTrigger");
            triggerObject.transform.SetParent(_sceneOneRoot.transform, false);
            triggerObject.transform.localPosition = new Vector3(0f, 0.75f, 0f);

            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(1.9f, 1.4f, 1.25f);
            triggerObject.AddComponent<ReturnFishBoxTrigger>();

            ModHelper.Console.WriteLine(
                $"[RETURN SCENE 1 BOX] Local position: " +
                $"({MineBoxFloorLocalPosition.x:F3}, " +
                $"{MineBoxFloorLocalPosition.y:F3}, " +
                $"{MineBoxFloorLocalPosition.z:F3})",
                MessageType.Info
            );
        }

        private static void CreateBoxFrame(Transform parent, Material material)
        {
            // Open-top frame: floor plus four low walls.
            CreatePrimitivePart(
                "FishBox_Floor", parent,
                new Vector3(0f, 0.05f, 0f),
                new Vector3(2.4f, 0.1f, 1.6f),
                material,
                true
            );
            CreatePrimitivePart(
                "FishBox_Wall_Left", parent,
                new Vector3(-1.15f, 0.65f, 0f),
                new Vector3(0.1f, 1.3f, 1.6f),
                material,
                true
            );
            CreatePrimitivePart(
                "FishBox_Wall_Right", parent,
                new Vector3(1.15f, 0.65f, 0f),
                new Vector3(0.1f, 1.3f, 1.6f),
                material,
                true
            );
            CreatePrimitivePart(
                "FishBox_Wall_Front", parent,
                new Vector3(0f, 0.65f, -0.75f),
                new Vector3(2.4f, 1.3f, 0.1f),
                material,
                true
            );
            CreatePrimitivePart(
                "FishBox_Wall_Back", parent,
                new Vector3(0f, 0.65f, 0.75f),
                new Vector3(2.4f, 1.3f, 0.1f),
                material,
                true
            );
        }

        private static void CreateFourEyedFish(
            Transform parent,
            Vector3 localPosition,
            Quaternion localRotation,
            Material bodyMaterial,
            Material eyeMaterial,
            Material pupilMaterial
        )
        {
            GameObject fishRoot = new GameObject("BlueFourEyedFish");
            fishRoot.transform.SetParent(parent, false);
            fishRoot.transform.localPosition = localPosition;
            fishRoot.transform.localRotation = localRotation;
            fishRoot.transform.localScale = Vector3.one * 0.25f;

            GameObject body = CreatePrimitivePart(
                "Body", fishRoot.transform, Vector3.zero,
                new Vector3(0.75f, 0.35f, 0.3f),
                bodyMaterial,
                false,
                PrimitiveType.Sphere
            );

            CreatePrimitivePart(
                "Tail", fishRoot.transform,
                new Vector3(-0.42f, 0f, 0f),
                new Vector3(0.25f, 0.28f, 0.08f),
                bodyMaterial,
                false,
                PrimitiveType.Sphere
            );

            float[] eyeY = { 0.09f, -0.09f };
            float[] eyeZ = { -0.13f, 0.13f };
            int eyeNumber = 0;

            foreach (float y in eyeY)
            {
                foreach (float z in eyeZ)
                {
                    eyeNumber++;
                    Vector3 eyePosition = new Vector3(0.34f, y, z);
                    CreatePrimitivePart(
                        "Eye_" + eyeNumber,
                        fishRoot.transform,
                        eyePosition,
                        Vector3.one * 0.105f,
                        eyeMaterial,
                        false,
                        PrimitiveType.Sphere
                    );
                    CreatePrimitivePart(
                        "Pupil_" + eyeNumber,
                        fishRoot.transform,
                        eyePosition + new Vector3(0.085f, 0f, 0f),
                        Vector3.one * 0.045f,
                        pupilMaterial,
                        false,
                        PrimitiveType.Sphere
                    );
                }
            }
        }

        private static GameObject CreatePrimitivePart(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool keepCollider,
            PrimitiveType primitiveType = PrimitiveType.Cube
        )
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }

            if (!keepCollider)
            {
                Collider collider = part.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }
            }

            return part;
        }

        private static Material CreateGlowingMaterial(
            string name,
            Color color,
            float emissionStrength
        )
        {
            Shader shader = Shader.Find("Standard");
            Material material = new Material(shader);
            material.name = name;
            material.color = color;

            if (emissionStrength > 0f)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emissionStrength);
            }

            return material;
        }

        private static Material CreateTransparentGlowingMaterial(
            string name,
            Color color,
            float emissionStrength
        )
        {
            Shader shader = Shader.Find("Standard");
            Material material = new Material(shader);
            material.name = name;
            material.color = color;

            // Configure Unity's Standard shader for transparent alpha blending.
            // Keeping ZWrite disabled lets the fish remain visible through every
            // side of the box instead of being hidden by the first glass face.
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Mode", 3f);
            material.SetInt(
                "_SrcBlend",
                (int)UnityEngine.Rendering.BlendMode.SrcAlpha
            );
            material.SetInt(
                "_DstBlend",
                (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha
            );
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.Transparent;

            material.EnableKeyword("_EMISSION");
            material.SetColor(
                "_EmissionColor",
                new Color(1f, 1f, 1f, 1f) * emissionStrength
            );
            return material;
        }

        private void PrepareMineNomaiAndDialogue()
        {
            Transform coleus = FindSceneTransform("RETURN_COLEUS");
            Transform cycad = FindSceneTransform("RETURN_CYCAD");
            Transform oeno = FindSceneTransform("RETURN_OENO");
            Transform[] nomai = { coleus, cycad, oeno };

            int foundCount = 0;
            foreach (Transform npc in nomai)
            {
                if (npc != null)
                {
                    foundCount++;
                }
            }

            if (foundCount == 0)
            {
                ModHelper.Console.WriteLine(
                    "[RETURN NOMAI] No mine Nomai props were found.",
                    MessageType.Error
                );
                return;
            }

            Vector3 boxCenter = _sceneOneRoot.transform.position +
                                _sceneOneRoot.transform.up * 0.65f;
            Vector3 boxUp = _sceneOneRoot.transform.up;
            Vector3[] standingOffsets =
            {
                _sceneOneRoot.transform.forward * 3.4f,
                -_sceneOneRoot.transform.forward * 1.2f -
                    _sceneOneRoot.transform.right * 3.2f,
                -_sceneOneRoot.transform.forward * 1.2f +
                    _sceneOneRoot.transform.right * 3.2f
            };

            for (int i = 0; i < nomai.Length; i++)
            {
                Transform npc = nomai[i];
                if (npc == null)
                {
                    ModHelper.Console.WriteLine(
                        "[RETURN NOMAI] A mine Nomai prop was not found; " +
                        "its dialogue trigger could not be attached.",
                        MessageType.Error
                    );
                    continue;
                }

                // Reposition all three around the box instead of leaving two of
                // them behind distant cave geometry. Probe downward so every
                // character's root is placed directly on the real mine floor.
                Vector3 desiredPosition =
                    _sceneOneRoot.transform.position + standingOffsets[i];
                RaycastHit floorHit;
                if (TryFindRockWall(
                        desiredPosition + boxUp * 5f,
                        -boxUp,
                        out floorHit
                    ))
                {
                    npc.position = floorHit.point + floorHit.normal * 0.035f;
                }
                else
                {
                    npc.position = desiredPosition;
                }

                Vector3 radialUp = (
                    npc.position - _timberHearthBody.GetPosition()
                ).normalized;
                Vector3 towardCenter = Vector3.ProjectOnPlane(
                    boxCenter - npc.position,
                    radialUp
                ).normalized;

                if (towardCenter.sqrMagnitude > 0.001f)
                {
                    // Empirical in-game orientation: this copied Solanum model
                    // visually faces along transform.forward.
                    npc.rotation = Quaternion.LookRotation(
                        towardCenter,
                        radialUp
                    );
                }

                AttachMineDialogue(npc);

                Vector3 localPosition =
                    _timberHearthBody.transform.InverseTransformPoint(
                        npc.position
                    );
                ModHelper.Console.WriteLine(
                    $"[RETURN NOMAI POSITION] {npc.name}: " +
                    $"({localPosition.x:F3}, {localPosition.y:F3}, " +
                    $"{localPosition.z:F3})",
                    MessageType.Info
                );
            }

            Physics.SyncTransforms();
            ModHelper.Console.WriteLine(
                $"[RETURN NOMAI] Oriented and attached dialogue to " +
                $"{foundCount} mine Nomai.",
                MessageType.Success
            );
        }

        private void AttachMineDialogue(Transform npc)
        {
            if (NewHorizons == null)
            {
                return;
            }

            (CharacterDialogueTree dialogue, RemoteDialogueTrigger remote) =
                NewHorizons.SpawnDialogue(
                    this,
                    _timberHearthBody.gameObject,
                    "dialogue/mine_team.xml",
                    1.25f,
                    1.4f,
                    null,
                    1.5f
                );

            if (dialogue == null)
            {
                ModHelper.Console.WriteLine(
                    $"[RETURN DIALOGUE] Failed to create trigger for {npc.name}.",
                    MessageType.Error
                );
                return;
            }

            dialogue.gameObject.name = npc.name + "_DIALOGUE";
            dialogue.transform.SetParent(npc, false);
            dialogue.transform.localPosition = new Vector3(0f, 1.35f, 0f);
            dialogue.transform.localRotation = Quaternion.identity;
            dialogue.transform.localScale = Vector3.one;
        }

        private void ShowIntroTitle()
        {
            _introTitleStartTime = Time.unscaledTime;
            _showIntroTitle = true;
        }

        private static void DisablePlayerFlashlight()
        {
            foreach (Flashlight flashlight in
                     Resources.FindObjectsOfTypeAll<Flashlight>())
            {
                if (flashlight == null ||
                    !flashlight.gameObject.scene.IsValid())
                {
                    continue;
                }

                flashlight.TurnOff(false);
                flashlight.enabled = false;

                foreach (OWLight2 light in flashlight.GetLights())
                {
                    if (light != null)
                    {
                        light.enabled = false;
                    }
                }
            }
        }

        private void CreateNomaiWallLighting()
        {
            if (_sceneOneRoot == null)
            {
                return;
            }

            Transform torchTemplate = FindNomaiWallTorchTemplate();
            Vector3 origin = _sceneOneRoot.transform.position +
                             _sceneOneRoot.transform.up * 2.4f;
            Vector3[] directions =
            {
                _sceneOneRoot.transform.right,
                -_sceneOneRoot.transform.right,
                _sceneOneRoot.transform.forward,
                -_sceneOneRoot.transform.forward
            };

            int createdCount = 0;
            foreach (Vector3 direction in directions)
            {
                RaycastHit wallHit;
                if (!TryFindRockWall(origin, direction, out wallHit))
                {
                    continue;
                }

                CreateNomaiWallLamp(
                    torchTemplate,
                    wallHit.point,
                    wallHit.normal,
                    createdCount + 1
                );
                createdCount++;
            }

            ModHelper.Console.WriteLine(
                $"[RETURN LIGHTING] Embedded {createdCount} Nomai wall lamp(s) " +
                "around the fish box.",
                createdCount > 0 ? MessageType.Success : MessageType.Warning
            );
        }

        private bool TryFindRockWall(
            Vector3 origin,
            Vector3 direction,
            out RaycastHit result
        )
        {
            result = new RaycastHit();
            float nearestDistance = float.PositiveInfinity;

            foreach (RaycastHit hit in Physics.RaycastAll(
                         origin,
                         direction.normalized,
                         24f,
                         Physics.DefaultRaycastLayers,
                         QueryTriggerInteraction.Ignore
                     ))
            {
                if (hit.collider == null ||
                    hit.distance >= nearestDistance)
                {
                    continue;
                }

                string path = GetObjectPath(hit.collider.transform);
                bool isTimberHearthRock =
                    path.StartsWith("TimberHearth_Body/") &&
                    (path.Contains("Terrain") ||
                     path.Contains("Geometry") ||
                     path.Contains("BatchedMesh"));

                if (!isTimberHearthRock)
                {
                    continue;
                }

                nearestDistance = hit.distance;
                result = hit;
            }

            return nearestDistance < float.PositiveInfinity;
        }

        private void CreateNomaiWallLamp(
            Transform template,
            Vector3 position,
            Vector3 wallNormal,
            int lampNumber
        )
        {
            GameObject lampRoot;
            if (template != null)
            {
                lampRoot = Instantiate(template.gameObject);
                lampRoot.name = "RETURN_NOMAI_WALL_LAMP_" + lampNumber;
                lampRoot.SetActive(true);
                lampRoot.transform.SetParent(
                    _timberHearthBody.transform,
                    true
                );
                lampRoot.transform.localScale = Vector3.one;
            }
            else
            {
                lampRoot = CreateFallbackNomaiLamp(lampNumber);
            }

            Vector3 radialUp = (
                position - _timberHearthBody.GetPosition()
            ).normalized;
            Vector3 lampUp = Vector3.ProjectOnPlane(
                radialUp,
                wallNormal
            ).normalized;
            if (lampUp.sqrMagnitude < 0.001f)
            {
                lampUp = _sceneOneRoot.transform.up;
            }

            lampRoot.transform.position = position + wallNormal * 0.035f;
            lampRoot.transform.rotation = Quaternion.LookRotation(
                -wallNormal,
                lampUp
            );

            GameObject lightObject = new GameObject("Return_Nomai_AreaLight");
            lightObject.transform.SetParent(lampRoot.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 0f, -0.3f);

            Light areaLight = lightObject.AddComponent<Light>();
            areaLight.type = LightType.Point;
            areaLight.color = new Color(0.72f, 0.9f, 1f);
            areaLight.intensity = 1.35f;
            areaLight.range = 11f;
            areaLight.shadows = LightShadows.None;
        }

        private GameObject CreateFallbackNomaiLamp(int lampNumber)
        {
            GameObject root = new GameObject(
                "RETURN_NOMAI_WALL_LAMP_" + lampNumber
            );
            root.transform.SetParent(_timberHearthBody.transform, true);

            Material frameMaterial = CreateGlowingMaterial(
                "Return_NomaiLampFrame",
                new Color(0.22f, 0.18f, 0.12f, 1f),
                0f
            );
            Material glowMaterial = CreateGlowingMaterial(
                "Return_NomaiLampGlow",
                new Color(0.65f, 0.88f, 1f, 1f),
                0.9f
            );

            CreatePrimitivePart(
                "NomaiLamp_Frame",
                root.transform,
                Vector3.zero,
                new Vector3(0.75f, 0.75f, 0.12f),
                frameMaterial,
                false,
                PrimitiveType.Cylinder
            ).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            CreatePrimitivePart(
                "NomaiLamp_Glow",
                root.transform,
                new Vector3(0f, 0f, -0.1f),
                new Vector3(0.42f, 0.42f, 0.13f),
                glowMaterial,
                false,
                PrimitiveType.Sphere
            );

            return root;
        }

        private static Transform FindNomaiWallTorchTemplate()
        {
            foreach (Transform candidate in
                     Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.name.StartsWith("Prefab_NOM_WallTorch"))
                {
                    return candidate;
                }
            }

            return null;
        }

        public void OnPlayerEnteredFishBox()
        {
            if (_enteredFishBox)
            {
                return;
            }

            DialogueConditionManager conditionManager =
                DialogueConditionManager.SharedInstance;
            bool heardMiningDialogue =
                conditionManager != null &&
                conditionManager.GetConditionState(
                    "RETURN_MINE_TEAM_HEARD"
                );

            if (!heardMiningDialogue)
            {
                ModHelper.Console.WriteLine(
                    "[RETURN STORY] Fish box touched before the mining-team " +
                    "dialogue was completed. Scene 2 remains locked.",
                    MessageType.Info
                );
                return;
            }

            _enteredFishBox = true;
            ModHelper.Console.WriteLine(
                "[RETURN STORY] Mining-team dialogue completed and fish box " +
                "touched. Loading the Scene 2 placeholder.",
                MessageType.Success
            );

            LoadManager.LoadScene(
                OWScene.EyeOfTheUniverse,
                LoadManager.FadeType.ToBlack,
                1f,
                true
            );
        }

        private static void HidePlayerBodyModel(OWRigidbody playerBody)
        {
            if (playerBody == null)
            {
                return;
            }

            // First-person tools and UI use ordinary MeshRenderers. The visible
            // Hearthian body is skinned, so this removes the body without hiding
            // the interaction reticle or dialogue interface.
            foreach (SkinnedMeshRenderer renderer in
                     playerBody.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                renderer.enabled = false;
            }
        }

        private static void ApplyFishPlayerScale(OWRigidbody playerBody)
        {
            if (playerBody == null)
            {
                return;
            }

            // A one-fifth player produces a consistent five-times-larger world:
            // mine geometry, water, props, and Nomai all keep matching scales.
            playerBody.transform.localScale = Vector3.one * 0.2f;
        }

        private static void EnableFishSwimming(OWRigidbody playerBody)
        {
            if (playerBody == null)
            {
                return;
            }

            PlayerSpacesuit spacesuit =
                playerBody.GetComponentInChildren<PlayerSpacesuit>(true);
            if (spacesuit != null && !spacesuit.IsWearingSuit(false))
            {
                // Suit mechanics provide six-direction underwater movement. The
                // helmet stays off and oxygen is handled by FishOxygenPatch.
                spacesuit.SuitUp(false, true, false);
            }

            foreach (JetpackThrusterAudio audio in
                     playerBody.GetComponentsInChildren<JetpackThrusterAudio>(true))
            {
                foreach (AudioSource source in
                         audio.GetComponentsInChildren<AudioSource>(true))
                {
                    source.Stop();
                    source.mute = true;
                }
                audio.enabled = false;
            }

            foreach (ThrusterFlameController flame in
                     playerBody.GetComponentsInChildren<ThrusterFlameController>(true))
            {
                flame.enabled = false;
            }

            foreach (ThrusterParticlesBehavior particles in
                     playerBody.GetComponentsInChildren<ThrusterParticlesBehavior>(true))
            {
                particles.enabled = false;
                ParticleSystem system = particles.GetComponent<ParticleSystem>();
                if (system != null)
                {
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            foreach (ThrusterWashController wash in
                     playerBody.GetComponentsInChildren<ThrusterWashController>(true))
            {
                wash.enabled = false;
            }

            foreach (Renderer renderer in
                     playerBody.GetComponentsInChildren<Renderer>(true))
            {
                string lowerName = renderer.name.ToLowerInvariant();
                if (lowerName.Contains("suit") ||
                    lowerName.Contains("helmet") ||
                    lowerName.Contains("jetpack") ||
                    lowerName.Contains("thruster"))
                {
                    renderer.enabled = false;
                }
            }
        }

        private void DisableMineGeysers(Vector3 spawnPosition)
        {
            int disabledCount = 0;

            foreach (GeyserController geyser in
                     Resources.FindObjectsOfTypeAll<GeyserController>())
            {
                if (geyser == null || !geyser.gameObject.scene.IsValid() ||
                    !IsMineGeyser(geyser.transform, spawnPosition))
                {
                    continue;
                }

                geyser.DeactivateGeyser();
                geyser.gameObject.SetActive(false);
                disabledCount++;
            }

            foreach (GeyserFluidVolume volume in
                     Resources.FindObjectsOfTypeAll<GeyserFluidVolume>())
            {
                if (volume != null && volume.gameObject.scene.IsValid() &&
                    IsMineGeyser(volume.transform, spawnPosition))
                {
                    volume.gameObject.SetActive(false);
                }
            }

            foreach (GeyserAudioController audio in
                     Resources.FindObjectsOfTypeAll<GeyserAudioController>())
            {
                if (audio != null && audio.gameObject.scene.IsValid() &&
                    IsMineGeyser(audio.transform, spawnPosition))
                {
                    foreach (AudioSource source in
                             audio.GetComponentsInChildren<AudioSource>(true))
                    {
                        source.Stop();
                        source.mute = true;
                    }
                    audio.gameObject.SetActive(false);
                }
            }

            ModHelper.Console.WriteLine(
                $"[RETURN GEYSER] Disabled {disabledCount} mine geyser controller(s).",
                MessageType.Info
            );
        }

        private static bool IsMineGeyser(
            Transform target,
            Vector3 spawnPosition
        )
        {
            string path = GetObjectPath(target);
            return path.StartsWith("TimberHearth_Body/") &&
                   (path.Contains("Sector_NomaiMines") ||
                    Vector3.Distance(target.position, spawnPosition) <= 60f);
        }

        private void LogPlayerPosition()
        {
            OWRigidbody playerBody = Locator.GetPlayerBody();
            if (playerBody == null)
            {
                return;
            }

            Vector3 localPosition = _timberHearthBody.transform.InverseTransformPoint(
                playerBody.GetPosition()
            );
            Quaternion localRotation = Quaternion.Inverse(
                _timberHearthBody.GetRotation()
            ) * playerBody.GetRotation();

            ModHelper.Console.WriteLine(
                $"[RETURN SPAWN] Position: " +
                $"({localPosition.x:F3}, {localPosition.y:F3}, {localPosition.z:F3}); " +
                $"Rotation: ({localRotation.x:F4}, {localRotation.y:F4}, " +
                $"{localRotation.z:F4}, {localRotation.w:F4})",
                MessageType.Success
            );
        }

        private void WarpPlayer(
            OWRigidbody playerBody,
            Vector3 position,
            Quaternion rotation
        )
        {
            playerBody.WarpToPositionRotation(position, rotation);
            playerBody.SetVelocity(_timberHearthBody.GetPointVelocity(position));
            playerBody.SetAngularVelocity(_timberHearthBody.GetAngularVelocity());
        }

        private static OWRigidbody FindSceneBody(string objectName)
        {
            foreach (OWRigidbody body in Resources.FindObjectsOfTypeAll<OWRigidbody>())
            {
                if (body != null &&
                    body.gameObject.scene.IsValid() &&
                    body.gameObject.name == objectName)
                {
                    return body;
                }
            }

            return null;
        }

        private static Transform FindSceneTransform(string objectName)
        {
            foreach (Transform candidate in
                     Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.name == objectName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string GetObjectPath(Transform target)
        {
            string path = target.name;
            Transform current = target.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
    }

    public class ReturnFishBoxTrigger : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            OWRigidbody playerBody = Locator.GetPlayerBody();
            if (playerBody == null || ReturnMod.Instance == null)
            {
                return;
            }

            Transform current = other.transform;
            while (current != null)
            {
                if (current == playerBody.transform)
                {
                    ReturnMod.Instance.OnPlayerEnteredFishBox();
                    return;
                }

                current = current.parent;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerResources), "UpdateOxygen")]
    public static class FishOxygenPatch
    {
        private static bool Prefix()
        {
            // The protagonist is a fish and cannot suffocate underwater.
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerCameraEffectController), "WakeUp")]
    public static class SkipVanillaWakeUpVisualPatch
    {
        private static bool Prefix(PlayerCameraEffectController __instance)
        {
            // Preserve the WakeUp event expected by the rest of the game, but
            // replace the long eyelid animation with an effectively instant one.
            __instance.OpenEyes(0.01f, false);
            GlobalMessenger.FireEvent("WakeUp");
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerBreathingAudio), "OnWakeUp")]
    public static class SilenceVanillaWakeGaspPatch
    {
        private static bool Prefix()
        {
            // This private event handler normally schedules the audible gasp and
            // applies the vanilla wake-up mixer transition.
            return false;
        }
    }

    [HarmonyPatch(typeof(ToolModeSwapper), "EquipToolMode")]
    public static class BlockTranslatorToolModePatch
    {
        private static bool Prefix(ToolMode mode)
        {
            return mode != ToolMode.Translator;
        }
    }

    [HarmonyPatch(
        typeof(Flashlight),
        nameof(Flashlight.TurnOn),
        new[] { typeof(bool) }
    )]
    public static class DisableFlashlightTurnOnPatch
    {
        private static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(NomaiTranslator), "EquipTool")]
    public static class BlockTranslatorEquipPatch
    {
        private static bool Prefix()
        {
            return false;
        }
    }

    [HarmonyPatch(typeof(ToolModeSwapper), "IsTranslatorEquipPromptAllowed")]
    public static class HideTranslatorPromptPatch
    {
        private static void Postfix(ref bool __result)
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(ToolModeSwapper), "GetAutoEquipTranslator")]
    public static class DisableAutoTranslatorPatch
    {
        private static void Postfix(ref bool __result)
        {
            __result = false;
        }
    }
}
