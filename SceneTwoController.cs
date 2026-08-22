using HarmonyLib;
using OWML.Common;
using System.Collections;
using UnityEngine;

namespace Return
{
    internal static class SceneTwoController
    {
        private const string SolanumModelPath =
            "QuantumMoon_Body/Sector_QuantumMoon/State_EYE/" +
            "Interactables_EYEState/ConversationPivot/" +
            "Character_NOM_Solanum/Nomai_ANIM_SkyWatching_Idle";

        // These airborne-raycast transforms are all local to
        // StatueIsland_Body, exactly as named in the New Horizons log.
        private static readonly Vector3 BoxLocalPosition =
            new Vector3(-20.59075f, 2.582907f, 81.04213f);
        private static readonly Vector3 BoxLocalEuler =
            new Vector3(18.08531f, 171.9661f, 358.8447f);
        private static readonly Vector3 DazLocalPosition =
            new Vector3(-24.47976f, 2.64086f, 78.42301f);
        private static readonly Vector3 DazLocalEuler =
            new Vector3(19.45472f, 154.4375f, 1.515509f);
        private static readonly Vector3 YarrowLocalPosition =
            new Vector3(-17.09615f, 2.67903f, 80.03678f);
        private static readonly Vector3 YarrowLocalEuler =
            new Vector3(21.13428f, 190.8719f, 355.8451f);

        private static bool _transitionStarted;
        private static bool _sceneThreeStarted;
        private static GameObject _sceneRoot;
        private static Transform _statueLookTarget;
        private static CharacterDialogueTree _dialogue;
        private static bool _timePaused;
        private static OWRigidbody _frozenIslandBody;
        private static IslandController _frozenIslandController;
        private static bool _islandFrozen;

        public static bool TryBegin(ReturnMod mod)
        {
            if (_transitionStarted || mod == null)
            {
                return _transitionStarted;
            }

            DialogueConditionManager conditions =
                DialogueConditionManager.SharedInstance;
            if (conditions == null ||
                !conditions.GetConditionState("RETURN_MINE_TEAM_HEARD"))
            {
                return false;
            }

            _transitionStarted = true;
            mod.StartCoroutine(Transition(mod));
            return true;
        }

        public static void Reset()
        {
            UnfreezeStatueIsland();
            RestoreTimePause();
            SceneOnePrisonController.EndSceneOne();
            _transitionStarted = false;
            _sceneThreeStarted = false;
            _sceneRoot = null;
            _statueLookTarget = null;
            _dialogue = null;
        }

        private static void RestoreTimePause()
        {
            if (!_timePaused)
            {
                return;
            }

            if (OWTime.IsPaused(OWTime.PauseType.Reading))
            {
                OWTime.Unpause(OWTime.PauseType.Reading);
            }
            _timePaused = false;
        }

        private static void FreezeStatueIsland(OWRigidbody islandBody)
        {
            if (islandBody == null || _islandFrozen)
            {
                return;
            }

            _frozenIslandBody = islandBody;
            _frozenIslandController =
                islandBody.GetComponentInChildren<IslandController>(true);
            if (_frozenIslandController != null)
            {
                _frozenIslandController.enabled = false;
            }
            islandBody.MakeKinematic();
            _islandFrozen = true;
        }

        public static void UnfreezeStatueIsland()
        {
            if (!_islandFrozen)
            {
                return;
            }

            if (_frozenIslandBody != null)
            {
                _frozenIslandBody.MakeNonKinematic();
            }
            if (_frozenIslandController != null)
            {
                _frozenIslandController.enabled = true;
            }

            _frozenIslandBody = null;
            _frozenIslandController = null;
            _islandFrozen = false;
        }

        private static IEnumerator Transition(ReturnMod mod)
        {
            ReturnDebugLog.Write(
                "[RETURN SCENE 2] Entering the Statue Workshop.",
                MessageType.Success
            );

            SceneOnePrisonController.EndSceneOne();

            PlayerCameraEffectController cameraEffects =
                FindSceneComponent<PlayerCameraEffectController>();
            if (cameraEffects != null)
            {
                cameraEffects.CloseEyes(0.55f);
            }

            yield return new WaitForSeconds(0.65f);

            OWRigidbody statueIslandBody =
                FindSceneBody("StatueIsland_Body");
            if (statueIslandBody == null)
            {
                ReturnDebugLog.Write(
                    "[RETURN SCENE 2] Could not locate " +
                    "StatueIsland_Body.",
                    MessageType.Error
                );
                if (cameraEffects != null)
                {
                    cameraEffects.OpenEyes(0.5f, false);
                }
                _transitionStarted = false;
                yield break;
            }

            Vector3 playerPosition;
            Quaternion playerRotation;
            BuildScene(
                mod,
                statueIslandBody,
                out playerPosition,
                out playerRotation
            );

            // Freeze the island before the player arrives so the Statue
            // Workshop dialogue cannot be interrupted by the vanilla tornado
            // toss. It is released again when Scene Six starts.
            FreezeStatueIsland(statueIslandBody);

            OWRigidbody playerBody = Locator.GetPlayerBody();
            PlayerCharacterController playerController =
                Locator.GetPlayerController();
            PlayerLockOnTargeting lockOnTargeting =
                Locator.GetPlayerTransform()
                    .GetComponent<PlayerLockOnTargeting>();
            Vector3 playerLocalPosition =
                statueIslandBody.transform.InverseTransformPoint(
                    playerPosition
                );
            Quaternion playerLocalRotation = Quaternion.Inverse(
                statueIslandBody.GetRotation()
            ) * playerRotation;

            playerBody.WarpToPositionRotation(
                playerPosition,
                playerRotation
            );
            playerBody.SetVelocity(
                statueIslandBody.GetPointVelocity(playerPosition)
            );
            playerBody.SetAngularVelocity(
                statueIslandBody.GetAngularVelocity()
            );
            if (playerController != null)
            {
                playerController.LockMovement(true);
            }
            if (lockOnTargeting != null && _statueLookTarget != null)
            {
                lockOnTargeting.LockOn(
                    _statueLookTarget,
                    100f,
                    false,
                    1f
                );
            }
            Physics.SyncTransforms();

            yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;

            if (cameraEffects != null)
            {
                cameraEffects.OpenEyes(0.9f, false);
            }

            float dialogueStartTime = Time.unscaledTime + 1f;
            while (Time.unscaledTime < dialogueStartTime)
            {
                MaintainSceneTwoPlayer(
                    playerBody,
                    statueIslandBody,
                    playerLocalPosition,
                    playerLocalRotation
                );
                yield return null;
            }

            // Freeze the world clock while the Statue Workshop dialogue plays,
            // so the player cannot die before Scene Six begins. This happens
            // only after the eye-opening animation has finished, otherwise the
            // eyelid would freeze half-closed and leave a black screen.
            if (!OWTime.IsPaused(OWTime.PauseType.Reading))
            {
                OWTime.Pause(OWTime.PauseType.Reading);
                _timePaused = true;
            }

            if (_dialogue != null && !_dialogue.InConversation())
            {
                _dialogue.StartConversation();
            }

            while (_dialogue != null && _dialogue.InConversation())
            {
                MaintainSceneTwoPlayer(
                    playerBody,
                    statueIslandBody,
                    playerLocalPosition,
                    playerLocalRotation
                );
                yield return null;
            }

            RestoreTimePause();

            ReturnDebugLog.Write(
                "[RETURN SCENE 2] Statue Workshop dialogue completed.",
                MessageType.Success
            );

            yield return SceneThreeController.Enter(
                mod,
                cameraEffects
            );
        }

        private static void MaintainSceneTwoPlayer(
            OWRigidbody playerBody,
            OWRigidbody statueIslandBody,
            Vector3 localPosition,
            Quaternion localRotation
        )
        {
            if (playerBody == null || statueIslandBody == null)
            {
                return;
            }

            Vector3 worldPosition =
                statueIslandBody.transform.TransformPoint(localPosition);
            Quaternion worldRotation =
                statueIslandBody.GetRotation() * localRotation;
            playerBody.WarpToPositionRotation(
                worldPosition,
                worldRotation
            );
            playerBody.SetVelocity(
                statueIslandBody.GetPointVelocity(worldPosition)
            );
            playerBody.SetAngularVelocity(
                statueIslandBody.GetAngularVelocity()
            );
        }

        private static IEnumerator EnterSceneThreePlaceholder(
            ReturnMod mod,
            PlayerCameraEffectController cameraEffects,
            OWRigidbody playerBody,
            OWRigidbody statueIslandBody,
            Vector3 playerLocalPosition,
            Quaternion playerLocalRotation
        )
        {
            if (_sceneThreeStarted)
            {
                yield break;
            }
            _sceneThreeStarted = true;

            if (cameraEffects != null)
            {
                cameraEffects.CloseEyes(0.55f);
            }

            float closedAt = Time.unscaledTime + 0.65f;
            while (Time.unscaledTime < closedAt)
            {
                MaintainSceneTwoPlayer(
                    playerBody,
                    statueIslandBody,
                    playerLocalPosition,
                    playerLocalRotation
                );
                yield return null;
            }

            ReturnDebugLog.Write(
                "[RETURN SCENE 3] Entered the scene-three black-screen " +
                "placeholder after completing the statue dialogue.",
                MessageType.Success
            );
        }

        private static void BuildScene(
            ReturnMod mod,
            OWRigidbody statueIslandBody,
            out Vector3 playerPosition,
            out Quaternion playerRotation
        )
        {
            _sceneRoot = new GameObject(
                "Return_Scene2_StatueWorkshop"
            );
            _sceneRoot.transform.SetParent(
                statueIslandBody.transform,
                false
            );
            _sceneRoot.transform.localPosition = BoxLocalPosition;
            _sceneRoot.transform.localRotation =
                Quaternion.Euler(BoxLocalEuler);

            CreateFishContainer(_sceneRoot.transform);

            GameObject lookTargetObject = new GameObject(
                "RETURN_STATUE_LOOK_TARGET"
            );
            _statueLookTarget = lookTargetObject.transform;
            _statueLookTarget.SetParent(_sceneRoot.transform, false);
            _statueLookTarget.localPosition =
                new Vector3(0f, 2.4f, 7.5f);
            _statueLookTarget.localRotation = Quaternion.identity;

            Sector workshopSector = FindSceneSector(
                statueIslandBody.transform,
                "Sector_StatueIslandInterior"
            );
            Vector3 boxCenter =
                _sceneRoot.transform.position +
                _sceneRoot.transform.up * 0.65f;
            Transform daz = SpawnNomaiAtLocalTransform(
                mod,
                statueIslandBody,
                workshopSector,
                "RETURN_DAZ",
                DazLocalPosition,
                DazLocalEuler
            );
            Transform yarrow = SpawnNomaiAtLocalTransform(
                mod,
                statueIslandBody,
                workshopSector,
                "RETURN_YARROW",
                YarrowLocalPosition,
                YarrowLocalEuler
            );

            if (mod.NewHorizons != null)
            {
                (CharacterDialogueTree tree, RemoteDialogueTrigger remote) =
                    mod.NewHorizons.SpawnDialogue(
                        mod,
                        statueIslandBody.gameObject,
                        "dialogue/statue_test.xml",
                        0f,
                        0f,
                        null,
                        0f
                    );
                _dialogue = tree;
                if (_dialogue != null)
                {
                    _dialogue.gameObject.name =
                        "RETURN_STATUE_TEST_DIALOGUE";
                    _dialogue.transform.SetParent(
                        statueIslandBody.transform,
                        true
                    );
                    _dialogue.transform.position = daz != null
                        ? daz.position
                        : boxCenter;
                }
            }

            // Do not load a layout saved for the previous statue-search
            // implementation.  It used a different parent and coordinate
            // space and would move these objects back into old geometry.
            PlacementController.Attach(
                mod,
                _sceneRoot.transform,
                daz,
                yarrow,
                null
            );

            playerPosition =
                _sceneRoot.transform.TransformPoint(
                    new Vector3(0f, 0.46f, 0f)
                );
            playerRotation = _sceneRoot.transform.rotation;

            LogPlacedTransform(
                mod,
                statueIslandBody,
                "BOX",
                _sceneRoot.transform
            );
            LogPlacedTransform(mod, statueIslandBody, "DAZ", daz);
            LogPlacedTransform(mod, statueIslandBody, "YARROW", yarrow);
            ReturnDebugLog.Write(
                "[RETURN SCENE 2 BINDING] Exact StatueIsland_Body local " +
                "transforms applied to the box and two Nomai.",
                MessageType.Success
            );
        }

        private static void CreateFishContainer(Transform parent)
        {
            Material glass = CreateTransparentMaterial();

            CreateBoxPart(
                "Scene2_Box_Floor",
                parent,
                new Vector3(0f, 0.05f, 0f),
                new Vector3(2.4f, 0.1f, 1.6f),
                glass
            );
            CreateBoxPart(
                "Scene2_Box_Left",
                parent,
                new Vector3(-1.15f, 0.65f, 0f),
                new Vector3(0.1f, 1.3f, 1.6f),
                glass
            );
            CreateBoxPart(
                "Scene2_Box_Right",
                parent,
                new Vector3(1.15f, 0.65f, 0f),
                new Vector3(0.1f, 1.3f, 1.6f),
                glass
            );
            CreateBoxPart(
                "Scene2_Box_Front",
                parent,
                new Vector3(0f, 0.65f, -0.75f),
                new Vector3(2.4f, 1.3f, 0.1f),
                glass
            );
            CreateBoxPart(
                "Scene2_Box_Back",
                parent,
                new Vector3(0f, 0.65f, 0.75f),
                new Vector3(2.4f, 1.3f, 0.1f),
                glass
            );

            GameObject glowObject =
                new GameObject("Scene2_Box_SoftWhiteGlow");
            glowObject.transform.SetParent(parent, false);
            glowObject.transform.localPosition =
                new Vector3(0f, 0.65f, 0f);

            Light glow = glowObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.92f, 0.97f, 1f);
            glow.intensity = 0.38f;
            glow.range = 3.5f;
            glow.shadows = LightShadows.None;
        }

        private static void CreateBoxPart(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material
        )
        {
            GameObject part = GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material CreateTransparentMaterial()
        {
            Material material = new Material(
                Shader.Find("Standard")
            );
            material.name = "Return_Scene2_BoxGlass";
            material.color =
                new Color(0.92f, 0.97f, 1f, 0.16f);
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
            material.EnableKeyword("_EMISSION");
            material.SetColor(
                "_EmissionColor",
                Color.white * 0.24f
            );
            material.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private static Transform SpawnNomaiAtLocalTransform(
            ReturnMod mod,
            OWRigidbody sceneBody,
            Sector sector,
            string name,
            Vector3 localPosition,
            Vector3 localEuler
        )
        {
            if (mod.NewHorizons == null)
            {
                return null;
            }

            GameObject actor = mod.NewHorizons.SpawnObject(
                mod,
                sceneBody.gameObject,
                sector,
                SolanumModelPath,
                Vector3.zero,
                Vector3.zero,
                1f,
                false
            );
            if (actor == null)
            {
                return null;
            }

            actor.name = name;
            Transform actorTransform = actor.transform;
            actorTransform.SetParent(sceneBody.transform, false);
            actorTransform.localPosition = localPosition;
            actorTransform.localRotation = Quaternion.Euler(localEuler);
            actorTransform.localScale = Vector3.one;
            return actorTransform;
        }

        private static Transform SpawnNomai(
            ReturnMod mod,
            OWRigidbody sceneBody,
            Sector sector,
            string name,
            Vector3 desiredPosition,
            Vector3 lookTarget,
            params Transform[] occupiedActors
        )
        {
            if (mod.NewHorizons == null)
            {
                return null;
            }

            Vector3 up = _sceneRoot.transform.up;
            Vector3 placedPosition;
            Vector3 placedUp;
            bool clear = TryFindClearNomaiPosition(
                desiredPosition,
                up,
                occupiedActors,
                out placedPosition,
                out placedUp
            );

            if (!clear)
            {
                RaycastHit fallbackFloor;
                if (TryFindSceneFloor(
                        desiredPosition,
                        up,
                        out fallbackFloor
                    ))
                {
                    placedPosition =
                        fallbackFloor.point +
                        fallbackFloor.normal.normalized * 0.035f;
                    placedUp = fallbackFloor.normal.normalized;
                }
                else
                {
                    placedPosition = desiredPosition;
                    placedUp = up;
                }

                ReturnDebugLog.Write(
                    "[RETURN SCENE 2 PLACEMENT] No fully clear capsule " +
                    "was found for " + name + "; using its nearest " +
                    "floor position.",
                    MessageType.Warning
                );
            }

            GameObject actor = mod.NewHorizons.SpawnObject(
                mod,
                sceneBody.gameObject,
                sector,
                SolanumModelPath,
                Vector3.zero,
                Vector3.zero,
                1f,
                false
            );
            if (actor == null)
            {
                return null;
            }

            actor.name = name;
            Transform actorTransform = actor.transform;
            actorTransform.SetParent(sceneBody.transform, true);
            actorTransform.position = placedPosition;
            up = placedUp;

            Vector3 direction = Vector3.ProjectOnPlane(
                lookTarget - actorTransform.position,
                up
            ).normalized;
            if (direction.sqrMagnitude > 0.001f)
            {
                actorTransform.rotation = Quaternion.LookRotation(
                    direction,
                    up
                );
            }

            return actorTransform;
        }

        private static bool TryFindClearBoxPosition(
            Vector3 requestedPosition,
            Quaternion requestedRotation,
            Vector3 requestedUp,
            out RaycastHit selectedFloor,
            out Vector3 selectedPoint,
            out Vector3 selectedNormal,
            out Quaternion selectedRotation
        )
        {
            selectedFloor = new RaycastHit();
            selectedPoint = requestedPosition;
            selectedNormal = requestedUp;
            selectedRotation = requestedRotation;

            Vector3 baseForward = Vector3.ProjectOnPlane(
                requestedRotation * Vector3.forward,
                requestedUp
            ).normalized;
            if (baseForward.sqrMagnitude < 0.001f)
            {
                baseForward = Vector3.Cross(
                    requestedUp,
                    requestedRotation * Vector3.right
                ).normalized;
            }
            Vector3 baseRight = Vector3.Cross(
                requestedUp,
                baseForward
            ).normalized;

            float[] radii =
            {
                0f, 0.6f, 1.2f, 1.8f, 2.4f, 3f, 3.6f, 4.2f
            };

            for (int radiusIndex = 0;
                 radiusIndex < radii.Length;
                 radiusIndex++)
            {
                int directionCount = radii[radiusIndex] == 0f ? 1 : 12;
                for (int directionIndex = 0;
                     directionIndex < directionCount;
                     directionIndex++)
                {
                    float angle = directionIndex *
                                  360f / directionCount;
                    Vector3 offset =
                        Quaternion.AngleAxis(angle, requestedUp) *
                        baseRight * radii[radiusIndex];
                    Vector3 candidate = requestedPosition + offset;

                    RaycastHit floor;
                    if (!TryFindSceneFloor(
                            candidate,
                            requestedUp,
                            out floor
                        ))
                    {
                        continue;
                    }

                    Vector3 normal = floor.normal.normalized;
                    Vector3 forward = Vector3.ProjectOnPlane(
                        baseForward,
                        normal
                    ).normalized;
                    if (forward.sqrMagnitude < 0.001f)
                    {
                        continue;
                    }

                    Quaternion rotation = Quaternion.LookRotation(
                        forward,
                        normal
                    );
                    if (!IsBoxAreaClear(
                            floor.point,
                            normal,
                            rotation,
                            floor.collider
                        ))
                    {
                        continue;
                    }

                    selectedFloor = floor;
                    selectedPoint = floor.point;
                    selectedNormal = normal;
                    selectedRotation = rotation;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindClearNomaiPosition(
            Vector3 desiredPosition,
            Vector3 up,
            Transform[] occupiedActors,
            out Vector3 selectedPosition,
            out Vector3 selectedUp
        )
        {
            selectedPosition = desiredPosition;
            selectedUp = up;

            Vector3 right = _sceneRoot.transform.right;
            float[] radii =
            {
                0f, 0.45f, 0.9f, 1.35f, 1.8f, 2.25f, 2.7f, 3.15f
            };

            for (int radiusIndex = 0;
                 radiusIndex < radii.Length;
                 radiusIndex++)
            {
                int directionCount = radii[radiusIndex] == 0f ? 1 : 12;
                for (int directionIndex = 0;
                     directionIndex < directionCount;
                     directionIndex++)
                {
                    float angle = directionIndex *
                                  360f / directionCount;
                    Vector3 offset =
                        Quaternion.AngleAxis(angle, up) *
                        right * radii[radiusIndex];
                    Vector3 candidate = desiredPosition + offset;

                    RaycastHit floor;
                    if (!TryFindSceneFloor(candidate, up, out floor))
                    {
                        continue;
                    }

                    Vector3 normal = floor.normal.normalized;
                    Vector3 foot = floor.point + normal * 0.035f;
                    if (!HasActorSeparation(
                            foot,
                            occupiedActors,
                            normal
                        ) ||
                        !IsNomaiSpaceClear(
                            foot,
                            normal,
                            floor.collider
                        ))
                    {
                        continue;
                    }

                    selectedPosition = foot;
                    selectedUp = normal;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindSceneFloor(
            Vector3 desiredPosition,
            Vector3 up,
            out RaycastHit result
        )
        {
            result = new RaycastHit();
            float bestScore = float.PositiveInfinity;
            Vector3 normalizedUp = up.normalized;
            Vector3 origin = desiredPosition + normalizedUp * 10f;

            foreach (RaycastHit hit in Physics.RaycastAll(
                         origin,
                         -normalizedUp,
                         36f,
                         Physics.DefaultRaycastLayers,
                         QueryTriggerInteraction.Ignore
                     ))
            {
                if (hit.collider == null ||
                    !IsGiantsDeepSceneGeometry(hit.collider.transform))
                {
                    continue;
                }

                float alignment = Vector3.Dot(
                    hit.normal.normalized,
                    normalizedUp
                );
                if (alignment < 0.35f)
                {
                    continue;
                }

                float score = Vector3.Distance(
                    hit.point,
                    desiredPosition
                ) + (1f - alignment) * 2f;
                if (score >= bestScore)
                {
                    continue;
                }

                bestScore = score;
                result = hit;
            }

            return bestScore < float.PositiveInfinity;
        }

        private static bool IsGiantsDeepSceneGeometry(Transform target)
        {
            string path = GetObjectPath(target);
            bool belongsToScene =
                path.StartsWith("GiantsDeep_Body/") ||
                path.StartsWith("StatueIsland_Body/");
            if (!belongsToScene || path.Contains("Return_Scene2"))
            {
                return false;
            }

            return path.Contains("Geometry") ||
                   path.Contains("Terrain") ||
                   path.Contains("Batched") ||
                   path.Contains("Collider") ||
                   path.Contains("ProbeFloor");
        }

        private static bool IsBoxAreaClear(
            Vector3 floorPoint,
            Vector3 up,
            Quaternion rotation,
            Collider floorCollider
        )
        {
            Vector3 center = floorPoint + up * 0.73f;
            Collider[] overlaps = Physics.OverlapBox(
                center,
                new Vector3(1.28f, 0.61f, 0.88f),
                rotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            foreach (Collider overlap in overlaps)
            {
                if (overlap != null && overlap != floorCollider)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsNomaiSpaceClear(
            Vector3 footPosition,
            Vector3 up,
            Collider floorCollider
        )
        {
            Vector3 bottom = footPosition + up * 0.46f;
            Vector3 top = footPosition + up * 2.45f;
            Collider[] overlaps = Physics.OverlapCapsule(
                bottom,
                top,
                0.38f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore
            );

            foreach (Collider overlap in overlaps)
            {
                if (overlap != null && overlap != floorCollider)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasActorSeparation(
            Vector3 candidate,
            Transform[] occupiedActors,
            Vector3 up
        )
        {
            if (occupiedActors == null)
            {
                return true;
            }

            foreach (Transform actor in occupiedActors)
            {
                if (actor == null)
                {
                    continue;
                }

                Vector3 separation = Vector3.ProjectOnPlane(
                    candidate - actor.position,
                    up
                );
                if (separation.magnitude < 1.1f)
                {
                    return false;
                }
            }
            return true;
        }

        private static Transform FindWorkshopStatue(
            ReturnMod mod,
            Transform islandRoot
        )
        {
            Transform selected = null;
            int bestScore = int.MinValue;

            foreach (Transform candidate in
                     islandRoot.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == null)
                {
                    continue;
                }

                string path = GetObjectPath(candidate);
                bool nameLooksLikeStatue =
                    candidate.name.IndexOf(
                        "StatueHead",
                        System.StringComparison.OrdinalIgnoreCase
                    ) >= 0 ||
                    candidate.name.IndexOf(
                        "MemoryStatue",
                        System.StringComparison.OrdinalIgnoreCase
                    ) >= 0;
                bool hasEyeAnimator =
                    candidate.GetComponentInChildren<StatueEyeAnimator>(true) !=
                    null;

                if (!nameLooksLikeStatue && !hasEyeAnimator)
                {
                    continue;
                }

                int score = 0;
                if (path.IndexOf(
                        "StatueIslandInterior",
                        System.StringComparison.OrdinalIgnoreCase
                    ) >= 0)
                {
                    score += 100;
                }
                if (path.IndexOf(
                        "Interactables",
                        System.StringComparison.OrdinalIgnoreCase
                    ) >= 0)
                {
                    score += 30;
                }
                if (path.IndexOf(
                        "Proxy",
                        System.StringComparison.OrdinalIgnoreCase
                    ) >= 0)
                {
                    score -= 200;
                }
                if (hasEyeAnimator)
                {
                    score += 300;
                }
                if (candidate.name.IndexOf(
                        "Prefab_NOM_StatueHead",
                        System.StringComparison.OrdinalIgnoreCase
                    ) >= 0)
                {
                    score += 150;
                }
                score += candidate.GetComponentsInChildren<Renderer>(true).Length;

                ReturnDebugLog.Write(
                    $"[RETURN SCENE 2 STATUE CANDIDATE] score={score}; " +
                    path,
                    MessageType.Info
                );

                if (score > bestScore)
                {
                    bestScore = score;
                    selected = candidate;
                }
            }

            if (selected == null)
            {
                foreach (Transform candidate in
                         islandRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate == null ||
                        candidate.name.IndexOf(
                            "Statue",
                            System.StringComparison.OrdinalIgnoreCase
                        ) < 0)
                    {
                        continue;
                    }

                    ReturnDebugLog.Write(
                        "[RETURN SCENE 2 STATUE NAME] " +
                        GetObjectPath(candidate),
                        MessageType.Info
                    );
                }
            }

            return selected;
        }

        private static bool TryFindWorkshopFloor(
            Vector3 origin,
            Vector3 direction,
            out RaycastHit result
        )
        {
            result = new RaycastHit();
            float nearest = float.PositiveInfinity;

            foreach (RaycastHit hit in Physics.RaycastAll(
                         origin,
                         direction.normalized,
                         12f,
                         Physics.DefaultRaycastLayers,
                         QueryTriggerInteraction.Ignore
                     ))
            {
                if (hit.collider == null || hit.distance >= nearest)
                {
                    continue;
                }

                string path = GetObjectPath(hit.collider.transform);
                bool isWorkshopFloor =
                    path.StartsWith("StatueIsland_Body/") &&
                    (path.Contains("Geometry") ||
                     path.Contains("Terrain") ||
                     path.Contains("Batched") ||
                     path.Contains("ProbeFloor"));

                if (!isWorkshopFloor)
                {
                    continue;
                }

                nearest = hit.distance;
                result = hit;
            }

            return nearest < float.PositiveInfinity;
        }

        private static Vector3 GetStatueFaceDirection(
            Transform statue,
            Vector3 up
        )
        {
            Transform eyes = FindDescendant(statue, "Statue_Eyes");
            Transform bust = FindDescendant(statue, "Statue_Bust");
            if (eyes != null && bust != null)
            {
                Vector3 face = Vector3.ProjectOnPlane(
                    GetRendererCenter(eyes) -
                    GetRendererCenter(bust),
                    up
                ).normalized;
                if (face.sqrMagnitude > 0.001f)
                {
                    return face;
                }
            }

            return statue.forward;
        }

        private static Vector3 GetRendererBasePoint(
            Transform root,
            Vector3 up
        )
        {
            float lowest = Vector3.Dot(root.position, up);
            bool found = false;

            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds bounds = renderer.bounds;
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;

                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = center + Vector3.Scale(
                                extents,
                                new Vector3(x, y, z)
                            );
                            float projection = Vector3.Dot(
                                corner,
                                up
                            );
                            if (!found || projection < lowest)
                            {
                                lowest = projection;
                                found = true;
                            }
                        }
                    }
                }
            }

            float rootProjection = Vector3.Dot(root.position, up);
            return root.position + up * (lowest - rootProjection);
        }

        private static Vector3 GetRendererCenter(Transform root)
        {
            Renderer renderer =
                root.GetComponentInChildren<Renderer>(true);
            return renderer != null
                ? renderer.bounds.center
                : root.position;
        }

        private static Transform FindDescendant(
            Transform root,
            string name
        )
        {
            foreach (Transform child in
                     root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

        private static OWRigidbody FindSceneBody(string name)
        {
            foreach (OWRigidbody body in
                     Resources.FindObjectsOfTypeAll<OWRigidbody>())
            {
                if (body != null &&
                    body.gameObject.scene.IsValid() &&
                    body.name == name)
                {
                    return body;
                }
            }
            return null;
        }

        private static Sector FindSceneSector(
            Transform root,
            string name
        )
        {
            if (root == null)
            {
                return null;
            }

            foreach (Sector sector in
                     root.GetComponentsInChildren<Sector>(true))
            {
                if (sector != null && sector.name == name)
                {
                    return sector;
                }
            }
            return root.GetComponentInChildren<Sector>(true);
        }

        private static void LogPlacedTransform(
            ReturnMod mod,
            OWRigidbody sceneBody,
            string label,
            Transform target
        )
        {
            if (mod == null || sceneBody == null || target == null)
            {
                return;
            }

            Vector3 localPosition =
                sceneBody.transform.InverseTransformPoint(target.position);
            Quaternion localRotation = Quaternion.Inverse(
                sceneBody.GetRotation()
            ) * target.rotation;
            Vector3 localEuler = localRotation.eulerAngles;

            ReturnDebugLog.Write(
                "[RETURN SCENE 2 " + label + "] position=(" +
                localPosition.x.ToString("F4") + ", " +
                localPosition.y.ToString("F4") + ", " +
                localPosition.z.ToString("F4") + "); rotation=(" +
                localEuler.x.ToString("F4") + ", " +
                localEuler.y.ToString("F4") + ", " +
                localEuler.z.ToString("F4") + ")",
                MessageType.Info
            );
        }

        private static T FindSceneComponent<T>() where T : Component
        {
            foreach (T component in Resources.FindObjectsOfTypeAll<T>())
            {
                if (component != null &&
                    component.gameObject.scene.IsValid())
                {
                    return component;
                }
            }
            return null;
        }

        private static string GetObjectPath(Transform target)
        {
            string path = target.name;
            Transform parent = target.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }

    [HarmonyPatch(typeof(ReturnMod), nameof(ReturnMod.OnPlayerEnteredFishBox))]
    internal static class SceneTwoFishBoxPatch
    {
        private static bool Prefix(ReturnMod __instance)
        {
            // Before the mining dialogue is complete, preserve Return.cs's
            // original locked-box behavior. Afterwards, Scene Two takes over.
            return !SceneTwoController.TryBegin(__instance);
        }
    }

    [HarmonyPatch(typeof(ReturnMod), "OnCompleteSceneLoad")]
    internal static class SceneTwoResetPatch
    {
        private static void Postfix(OWScene newScene)
        {
            // Reset on every completed scene load so a pause left behind by an
            // interrupted Statue Workshop dialogue can never stick forever.
            SceneTwoController.Reset();
        }
    }
}
