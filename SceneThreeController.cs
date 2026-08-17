using HarmonyLib;
using OWML.Common;
using System;
using System.Collections;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Plays the Interloper/Nomai sequence inside the game's original
    /// death-flashback presentation without firing TriggerFlashback.
    /// The latter would reload the solar system and restart the time loop.
    /// </summary>
    internal sealed class SceneThreeController : MonoBehaviour,
        IStreamingTexturesSubscriber
    {
        private const string StoryBundle =
            "invisibleplanet/textures/5_story";
        private const int FirstSlide = 100;
        private const int LastSlide = 111;
        private const float SlideDuration = 0.7f;

        private ReturnMod _mod;
        private StreamingIteratedTextureAssetBundle _bundle;
        private bool _subscriptionStarted;
        private bool _keepPlayerSafe;
        private OWRigidbody _safeIslandBody;
        private Vector3 _safeLocalOffset;

        public static IEnumerator Enter(
            ReturnMod mod,
            PlayerCameraEffectController cameraEffects
        )
        {
            if (mod == null)
            {
                yield break;
            }

            if (cameraEffects != null)
            {
                cameraEffects.CloseEyes(0.55f);
            }
            yield return new WaitForSeconds(0.65f);

            GameObject host = new GameObject(
                "RETURN_SCENE_3_GHOST_MATTER_MEMORY"
            );
            SceneThreeController controller =
                host.AddComponent<SceneThreeController>();
            controller._mod = mod;

            yield return controller.Play();
        }

        private void StartKeepingPlayerSafe()
        {
            _safeIslandBody = null;
            foreach (OWRigidbody body in
                Resources.FindObjectsOfTypeAll<OWRigidbody>())
            {
                if (body != null &&
                    body.gameObject.scene.IsValid() &&
                    body.name == "StatueIsland_Body")
                {
                    _safeIslandBody = body;
                    break;
                }
            }
            _safeLocalOffset = new Vector3(0f, 120f, 0f);
            _keepPlayerSafe = true;
        }

        private void Update()
        {
            if (!_keepPlayerSafe)
            {
                return;
            }

            OWRigidbody player = Locator.GetPlayerBody();
            if (player == null || _safeIslandBody == null)
            {
                return;
            }

            Vector3 worldPosition =
                _safeIslandBody.transform.TransformPoint(_safeLocalOffset);
            player.WarpToPositionRotation(
                worldPosition,
                _safeIslandBody.GetRotation()
            );
            player.SetVelocity(
                _safeIslandBody.GetPointVelocity(worldPosition)
            );
            player.SetAngularVelocity(
                _safeIslandBody.GetAngularVelocity()
            );
        }

        private IEnumerator Play()
        {
            _mod.ModHelper.Console.WriteLine(
                "[RETURN SCENE 3] Loading the original Echoes of the " +
                "Eye story slides.",
                MessageType.Info
            );

            // The scene-2 placement debug HUD must not linger on screen
            // during the cinematic scenes that follow.
            PlacementController.ScenePlacementComponent.SuppressHud = true;

            // The player is physically standing on Statue Island while the
            // black-screen scenes play. Keep them hovering high above it so
            // the vanilla tornado toss can never kill them mid-dialogue.
            StartKeepingPlayerSafe();

            if (!StreamingManager.isStreamingEnabled ||
                !StreamingManager.StreamingAssetAvailable(StoryBundle))
            {
                Fail(
                    "The DLC story texture bundle is unavailable. " +
                    "Echoes of the Eye must be installed and enabled."
                );
                yield break;
            }

            StreamingManager.ConvertTextureAssetBundleToIterable(
                StoryBundle
            );
            StreamingManager.RegisterStreamingTextureSubscriber(
                StoryBundle,
                this
            );
            _subscriptionStarted = true;
            StreamingManager.LoadStreamingAssets(StoryBundle);

            float loadDeadline = Time.realtimeSinceStartup + 20f;
            while (!SlidesReady() &&
                Time.realtimeSinceStartup < loadDeadline)
            {
                yield return null;
            }

            if (!SlidesReady())
            {
                Fail("Timed out while loading slides 100 through 111.");
                ReleaseStreaming();
                yield break;
            }

            Flashback flashback = FindSceneComponent<Flashback>();
            if (flashback == null)
            {
                Fail("Could not locate the game's Flashback object.");
                ReleaseStreaming();
                yield break;
            }

            OWCamera flashbackCamera = flashback.GetComponent<OWCamera>();
            AudioListener listener =
                flashback.GetComponentInChildren<AudioListener>(true);
            Transform screen = GetField<Transform>(
                flashback,
                "_screenTransform"
            );
            Transform mask = GetField<Transform>(
                flashback,
                "_maskTransform"
            );
            GameObject streams = GetField<GameObject>(
                flashback,
                "_forwardStreams"
            );
            GameObject reverseStreams = GetField<GameObject>(
                flashback,
                "_reverseStreams"
            );
            FlashbackAudioController audio =
                GetField<FlashbackAudioController>(
                    flashback,
                    "_audioController"
                );

            if (flashbackCamera == null || screen == null ||
                mask == null || streams == null)
            {
                Fail("The original flashback presentation is incomplete.");
                ReleaseStreaming();
                yield break;
            }

            AccessTools.Method(
                typeof(Flashback),
                "ResetEffects"
            ).Invoke(flashback, null);

            OWCamera previousCamera = Locator.GetActiveCamera();
            if (previousCamera != null && previousCamera != flashbackCamera)
            {
                previousCamera.enabled = false;
            }

            flashback.transform.position = Vector3.zero;
            flashbackCamera.clearFlags = CameraClearFlags.Color;
            flashbackCamera.backgroundColor = Color.black;
            flashbackCamera.enabled = true;
            flashbackCamera.postProcessingSettings.eyeMask.openness = 1f;
            GlobalMessenger<OWCamera>.FireEvent(
                "SwitchActiveCamera",
                flashbackCamera
            );
            if (listener != null)
            {
                listener.enabled = true;
            }

            screen.gameObject.SetActive(true);
            mask.gameObject.SetActive(true);
            streams.SetActive(true);
            if (reverseStreams != null)
            {
                reverseStreams.SetActive(false);
            }

            Renderer screenRenderer = screen.GetComponent<Renderer>();
            Renderer[] streamRenderers =
                streams.GetComponentsInChildren<Renderer>(true);
            // Use Flashback's own initialized light list. Enumerating every
            // inactive child also finds editor-only OWLight components whose
            // underlying Unity lights were never initialized.
            OWLight[] maskLights = GetField<OWLight[]>(
                flashback,
                "_maskLights"
            );
            if (screenRenderer == null)
            {
                Fail("The original flashback screen has no renderer.");
                ReleaseStreaming();
                yield break;
            }

            int fadeId = Shader.PropertyToID("_Fade");
            MaterialPropertyBlock streamProperties =
                new MaterialPropertyBlock();
            float screenStart = GetField(
                flashback,
                "_screenStartDist",
                12f
            );
            float screenEnd = GetField(
                flashback,
                "_screenEndDist",
                1f
            );
            float maskStart = GetField(
                flashback,
                "_maskStartDist",
                10f
            );
            float maskEnd = GetField(
                flashback,
                "_maskEndDist",
                -1f
            );

            screen.SetLocalPositionZ(screenStart);
            mask.SetLocalPositionZ(maskStart);
            screenRenderer.material.color = new Color(1f, 1f, 1f, 0f);
            screenRenderer.material.SetColor(
                "_StaticTint",
                new Color(1f, 1f, 1f, 0f)
            );
            SetSlide(screenRenderer, LastSlide);

            for (int i = 0; i < maskLights.Length; i++)
            {
                maskLights[i].SetIntensity(0f);
                maskLights[i].FadeTo(1f, 2.2f);
            }
            if (audio != null)
            {
                audio.StartFlashback();
            }
            const float revealDuration = 2.2f;
            float revealStart = Time.time;
            while (Time.time - revealStart < revealDuration)
            {
                float t = Mathf.Clamp01(
                    (Time.time - revealStart) / revealDuration
                );
                screenRenderer.material.color =
                    new Color(1f, 1f, 1f, t);
                screen.SetLocalPositionZ(
                    Mathf.Lerp(screenStart, screenEnd, t * 0.35f)
                );
                mask.SetLocalPositionZ(
                    Mathf.Lerp(maskStart, maskEnd, t)
                );
                SetStreamFade(
                    streamRenderers,
                    streamProperties,
                    fadeId,
                    t
                );
                yield return null;
            }

            float playbackStart = Time.time;
            float playbackEnd = playbackStart +
                (LastSlide - FirstSlide + 1) * SlideDuration + 1.4f;
            if (audio != null)
            {
                audio.StartPlayback(playbackStart, playbackEnd);
            }

            // The requested flashback runs backwards: the final image of
            // the Nomai extinction is shown first, followed by its causes.
            for (int slide = LastSlide; slide >= FirstSlide; slide--)
            {
                SetSlide(screenRenderer, slide);
                float frameStart = Time.time;
                while (Time.time - frameStart < SlideDuration)
                {
                    float progress = Mathf.InverseLerp(
                        playbackStart,
                        playbackEnd,
                        Time.time
                    );
                    screen.SetLocalPositionZ(
                        Mathf.Lerp(
                            Mathf.Lerp(
                                screenStart,
                                screenEnd,
                                0.35f
                            ),
                            screenEnd,
                            progress
                        )
                    );
                    yield return null;
                }
            }

            float closeStart = Time.time;
            const float closeDuration = 1.2f;
            while (Time.time - closeStart < closeDuration)
            {
                float t = Mathf.Clamp01(
                    (Time.time - closeStart) / closeDuration
                );
                screenRenderer.material.color =
                    new Color(1f, 1f, 1f, 1f - t);
                flashbackCamera.postProcessingSettings.eyeMask.openness =
                    1f - t;
                yield return null;
            }

            screenRenderer.material.SetTexture("_MainTex", null);
            screenRenderer.material.SetColor(
                "_StaticTint",
                new Color(1f, 1f, 1f, 0f)
            );
            screen.gameObject.SetActive(false);
            mask.gameObject.SetActive(false);
            streams.SetActive(false);
            ReleaseStreaming();

            _mod.ModHelper.Console.WriteLine(
                "[RETURN SCENE 3] Reverse ghost-matter memory completed.",
                MessageType.Success
            );

            yield return PlayBlackScreenDialogue(
                "dialogue/scene_four.xml",
                "RETURN_SCENE_4_DIALOGUE",
                "RETURN_SCENE4"
            );

            // Let the dialogue UI fully close before opening the next tree.
            yield return null;
            yield return null;

            yield return PlayBlackScreenDialogue(
                "dialogue/scene_five.xml",
                "RETURN_SCENE_5_DIALOGUE",
                "RETURN_SCENE5"
            );

            _mod.ModHelper.Console.WriteLine(
                "[RETURN SCENE 5] Dialogue completed.",
                MessageType.Success
            );
            _keepPlayerSafe = false;
            SceneSixController.Begin(_mod);
        }

        private IEnumerator PlayBlackScreenDialogue(
            string dialoguePath,
            string objectName,
            string imagePrefix
        )
        {
            if (_mod.NewHorizons == null)
            {
                Fail("New Horizons is unavailable for " + dialoguePath);
                yield break;
            }

            OWRigidbody statueIslandBody = null;
            foreach (OWRigidbody body in
                Resources.FindObjectsOfTypeAll<OWRigidbody>())
            {
                if (body != null &&
                    body.gameObject.scene.IsValid() &&
                    body.name == "StatueIsland_Body")
                {
                    statueIslandBody = body;
                    break;
                }
            }

            if (statueIslandBody == null)
            {
                Fail("Could not locate StatueIsland_Body for dialogue.");
                yield break;
            }

            var spawned = _mod.NewHorizons.SpawnDialogue(
                _mod,
                statueIslandBody.gameObject,
                dialoguePath,
                0f,
                0f,
                null,
                0f
            );
            CharacterDialogueTree dialogue = spawned.Item1;
            if (dialogue == null)
            {
                Fail("Could not spawn " + dialoguePath);
                yield break;
            }

            dialogue.gameObject.name = objectName;
            dialogue.transform.SetParent(
                statueIslandBody.transform,
                false
            );
            dialogue.transform.localPosition = Vector3.zero;

            // StartConversation verifies/loads the XML immediately, but a
            // frame here also allows New Horizons to finish its components.
            SceneDialogueBackgroundController.Begin(_mod, imagePrefix);
            yield return null;
            dialogue.StartConversation();

            // Show the first page's illustration; the AdvancePage event
            // switches the rest as the player pages through the dialogue.
            SceneDialogueBackgroundController.ShowPageForPage(1);
            dialogue.OnAdvancePage += OnDialogueAdvancePage;

            while (dialogue != null && dialogue.InConversation())
            {
                SceneDialogueBackgroundController.EnsureAttached();
                yield return null;
            }

            dialogue.OnAdvancePage -= OnDialogueAdvancePage;
            SceneDialogueBackgroundController.End();
        }

        private static void OnDialogueAdvancePage(
            string nodeName,
            int pageNum
        )
        {
            SceneDialogueBackgroundController.ShowPageForPage(pageNum + 1);
        }

        private void SetSlide(Renderer renderer, int streamingId)
        {
            Texture texture = _bundle.GetTexture(streamingId);
            renderer.material.SetTexture("_MainTex", texture);
        }

        private bool SlidesReady()
        {
            if (_bundle == null)
            {
                return false;
            }
            for (int id = FirstSlide; id <= LastSlide; id++)
            {
                if (!_bundle.IsTextureAvailable(id))
                {
                    return false;
                }
            }
            return true;
        }

        private static void SetStreamFade(
            Renderer[] renderers,
            MaterialPropertyBlock properties,
            int fadeId,
            float value
        )
        {
            properties.SetFloat(fadeId, value);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].SetPropertyBlock(properties);
            }
        }

        private void ReleaseStreaming()
        {
            if (!_subscriptionStarted)
            {
                return;
            }
            StreamingManager.UnregisterStreamingTextureSubscriber(
                StoryBundle,
                this
            );
            if (_bundle == null || _bundle.subscriberCount <= 0)
            {
                StreamingManager.UnloadStreamingAssets(StoryBundle);
            }
            _subscriptionStarted = false;
        }

        private void Fail(string message)
        {
            _mod.ModHelper.Console.WriteLine(
                "[RETURN SCENE 3] " + message,
                MessageType.Error
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

        private static T GetField<T>(object instance, string fieldName)
        {
            return (T)AccessTools.Field(
                instance.GetType(),
                fieldName
            ).GetValue(instance);
        }

        private static float GetField(
            object instance,
            string fieldName,
            float fallback
        )
        {
            var field = AccessTools.Field(
                instance.GetType(),
                fieldName
            );
            return field == null
                ? fallback
                : (float)field.GetValue(instance);
        }

        public void OnBeginSubscription(
            StreamingIteratedTextureAssetBundle streamingAssetBundle
        )
        {
            _bundle = streamingAssetBundle;
            int[] ids = new int[LastSlide - FirstSlide + 1];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = FirstSlide + i;
            }
            _bundle.SetAutoLoad(false);
            _bundle.LoadTexturesManual(ids);
        }

        public void OnAssetBundleBeginLoad(
            StreamingIteratedTextureAssetBundle streamingAssetBundle
        )
        {
        }

        public void OnTexturesLoaded(
            StreamingTextureAssetBundle textureAssetBundle
        )
        {
        }

        public void OnTexturesUnloaded()
        {
        }

        public void OnTextureLoaded(int index, Texture texture)
        {
        }

        public void OnTextureUnloaded(int index)
        {
        }
    }
}
