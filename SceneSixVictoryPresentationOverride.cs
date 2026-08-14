using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Return
{
    /// <summary>
    /// Carries the true-ending presentation intent across the scene load.
    /// The flag is deliberately narrow so ordinary fast/death credits keep
    /// their original music.
    /// </summary>
    internal static class SceneSixVictoryCreditsState
    {
        public static bool UseFastCreditsWithFinalMusic { get; set; }
    }

    /// <summary>
    /// Replaces only the true-ending overlay drawing. The locked ending
    /// controller still owns timing, input lock, Credits_Final loading and
    /// therefore the existing true-ending music.
    /// </summary>
    [HarmonyPatch(typeof(ReturnEndingOverlay), "OnGUI")]
    internal static class SceneSixVictoryPresentationOverride
    {
        private const float CardSeconds = 12f;
        private const float FadeSeconds = 1.25f;

        private static readonly FieldInfo VisibleField = AccessTools.Field(
            typeof(ReturnEndingOverlay),
            "_visible"
        );
        private static readonly FieldInfo TextField = AccessTools.Field(
            typeof(ReturnEndingOverlay),
            "_text"
        );
        private static readonly FieldInfo StartTimeField = AccessTools.Field(
            typeof(ReturnEndingOverlay),
            "_startTime"
        );

        private static Font _font;

        private static bool Prefix(ReturnEndingOverlay __instance)
        {
            try
            {
                if (VisibleField == null || TextField == null ||
                    StartTimeField == null ||
                    !(bool)VisibleField.GetValue(__instance))
                {
                    return false;
                }

                string fullText = TextField.GetValue(__instance) as string;
                float startTime = (float)StartTimeField.GetValue(__instance);
                DrawPresentation(fullText, Time.unscaledTime - startTime);
                return false;
            }
            catch
            {
                // If a future game update changes a private overlay field,
                // retain the locked white-text presentation as a safe fallback.
                return true;
            }
        }

        private static void DrawPresentation(
            string fullText,
            float elapsed
        )
        {
            Color previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                Texture2D.whiteTexture
            );
            GUI.color = previousColor;

            string[] cards = SplitCards(fullText);
            int cardIndex = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Max(0f, elapsed) / CardSeconds),
                0,
                cards.Length - 1
            );
            float localTime = Mathf.Max(0f, elapsed) -
                cardIndex * CardSeconds;
            float alpha = CalculateAlpha(localTime);

            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Arial" },
                    42
                );
            }

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.font = _font;
            style.fontSize = Mathf.RoundToInt(
                Mathf.Clamp(Screen.height * 0.034f, 24f, 42f)
            );
            style.alignment = TextAnchor.MiddleCenter;
            style.wordWrap = true;
            style.normal.textColor = new Color(
                1f,
                0.46f,
                0.1f,
                alpha
            );

            float marginX = Screen.width * 0.14f;
            float marginY = Screen.height * 0.12f;
            GUI.Label(
                new Rect(
                    marginX,
                    marginY,
                    Screen.width - marginX * 2f,
                    Screen.height - marginY * 2f
                ),
                cards[cardIndex].Trim(),
                style
            );
        }

        private static float CalculateAlpha(float localTime)
        {
            if (localTime < FadeSeconds)
            {
                return Mathf.Clamp01(localTime / FadeSeconds);
            }
            if (localTime > CardSeconds - FadeSeconds)
            {
                return Mathf.Clamp01(
                    (CardSeconds - localTime) / FadeSeconds
                );
            }
            return 1f;
        }

        internal static float GetPresentationDuration(string fullText)
        {
            return SplitCards(fullText).Length * CardSeconds;
        }

        private static string[] SplitCards(string fullText)
        {
            if (string.IsNullOrEmpty(fullText))
            {
                return new[] { string.Empty };
            }

            string normalized = fullText.Replace("\r\n", "\n");
            if (normalized.Contains("\n|\n"))
            {
                return normalized.Split(
                    new[] { "\n|\n" },
                    StringSplitOptions.RemoveEmptyEntries
                );
            }

            return normalized.Split(
                new[] { "\n\n" },
                StringSplitOptions.RemoveEmptyEntries
            );
        }
    }

    /// <summary>
    /// Keeps the locked true-ending timing and fade, but opens the same fast
    /// scrolling credits scene used by a normal death ending.
    /// </summary>
    [HarmonyPatch(typeof(ReturnEndingOverlay), "Update")]
    internal static class SceneSixVictoryCreditsSceneOverride
    {
        private static readonly FieldInfo VisibleField = AccessTools.Field(
            typeof(ReturnEndingOverlay),
            "_visible"
        );
        private static readonly FieldInfo LoadingField = AccessTools.Field(
            typeof(ReturnEndingOverlay),
            "_loading"
        );
        private static readonly FieldInfo StartTimeField = AccessTools.Field(
            typeof(ReturnEndingOverlay),
            "_startTime"
        );
        private static readonly FieldInfo TextField = AccessTools.Field(
            typeof(ReturnEndingOverlay),
            "_text"
        );

        private static bool Prefix(ReturnEndingOverlay __instance)
        {
            try
            {
                if (VisibleField == null || LoadingField == null ||
                    StartTimeField == null || TextField == null)
                {
                    return true;
                }

                bool visible = (bool)VisibleField.GetValue(__instance);
                bool loading = (bool)LoadingField.GetValue(__instance);
                float startTime =
                    (float)StartTimeField.GetValue(__instance);
                string fullText = TextField.GetValue(__instance) as string;
                if (!visible || loading)
                {
                    return false;
                }

                float presentationDuration =
                    SceneSixVictoryPresentationOverride
                        .GetPresentationDuration(fullText);
                if (Time.unscaledTime - startTime < presentationDuration)
                {
                    return false;
                }

                if (!OWInput.IsInputMode(InputMode.Menu))
                {
                    OWInput.ChangeInputMode(InputMode.Menu);
                }
                bool dismiss = OWInput.IsNewlyPressed(
                    InputLibrary.menuConfirm,
                    InputMode.Menu
                ) || OWInput.IsNewlyPressed(
                    InputLibrary.cancel,
                    InputMode.Menu
                );
                if (!dismiss)
                {
                    return false;
                }

                LoadingField.SetValue(__instance, true);
                // The overlay is attached to the persistent mod object. Hide
                // it before loading credits or it will cover both credits and
                // the title screen forever.
                __instance.HideBeforeCredits();
                SceneSixVictoryDialogueController.ReleasePauseForCredits();
                SceneSixVictoryCreditsState
                    .UseFastCreditsWithFinalMusic =
                        __instance.UsesFinalCreditsMusic();
                LoadManager.LoadScene(
                    OWScene.Credits_Fast,
                    LoadManager.FadeType.ToBlack,
                    1f,
                    true
                );
                return false;
            }
            catch
            {
                SceneSixVictoryCreditsState
                    .UseFastCreditsWithFinalMusic = false;
                return true;
            }
        }
    }

    /// <summary>
    /// The fast credits scene normally has its own menu-track music. During
    /// the true ending only, replace that source before any Start methods run
    /// so the already-approved final-credits music remains unchanged.
    /// </summary>
    [HarmonyPatch(typeof(OWAudioSource), "Awake")]
    internal static class SceneSixVictoryCreditsMusicOverride
    {
        private const int FastCreditsMusicAudioType = 1808;

        private static readonly FieldInfo AudioTypeField = AccessTools.Field(
            typeof(OWAudioSource),
            "_audioLibraryClip"
        );
        private static readonly FieldInfo TrackField = AccessTools.Field(
            typeof(OWAudioSource),
            "_track"
        );

        private static void Prefix(OWAudioSource __instance)
        {
            try
            {
                if (!SceneSixVictoryCreditsState
                        .UseFastCreditsWithFinalMusic ||
                    LoadManager.GetCurrentScene() != OWScene.Credits_Fast ||
                    AudioTypeField == null || TrackField == null)
                {
                    return;
                }

                AudioType current =
                    (AudioType)AudioTypeField.GetValue(__instance);
                if ((int)current != FastCreditsMusicAudioType)
                {
                    return;
                }

                AudioTypeField.SetValue(
                    __instance,
                    AudioType.FinalCredits
                );
                TrackField.SetValue(
                    __instance,
                    OWAudioMixer.TrackName.Music
                );
            }
            catch
            {
                // Presentation-only fallback: never block scene loading.
            }
        }
    }

    /// <summary>
    /// All scene Awake calls have completed before Credits.Start, so the
    /// one-shot scene-transfer flag can now be safely cleared.
    /// </summary>
    [HarmonyPatch(typeof(Credits), "Start")]
    internal static class SceneSixVictoryCreditsStateCleanup
    {
        private static void Postfix()
        {
            SceneSixVictoryCreditsState.UseFastCreditsWithFinalMusic = false;
        }
    }
}
