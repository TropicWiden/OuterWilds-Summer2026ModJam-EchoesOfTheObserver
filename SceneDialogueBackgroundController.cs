using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace Return
{
    /// <summary>
    /// Shows a page-synced illustration behind the black-screen dialogues
    /// (Scenes 4 and 5). Images live in the mod's backgrounds folder and are
    /// named after the dialogue page key, e.g. $RETURN_SCENE5_06.png for page
    /// 06, or $RETURN_SCENE5_10-13.png when pages 10-13 share one image.
    /// </summary>
    internal static class SceneDialogueBackgroundController
    {
        private sealed class PageRange
        {
            public int StartPage;
            public int EndPage;
            public string FileName;
        }

        private static readonly List<PageRange> _ranges =
            new List<PageRange>();
        private static readonly Dictionary<string, string> _singlePages =
            new Dictionary<string, string>();
        private static readonly Dictionary<string, Texture2D> _textureCache =
            new Dictionary<string, Texture2D>();

        private static ReturnMod _mod;
        private static string _prefix;
        private static string _folder;
        private static Image _image;
        private static GameObject _root;
        private static string _currentKey;
        private static bool _started;

        public static void Begin(ReturnMod mod, string prefix)
        {
            End();
            if (mod == null || string.IsNullOrEmpty(prefix))
            {
                return;
            }

            _mod = mod;
            _prefix = prefix;
            _folder = Path.Combine(
                mod.ModHelper.Manifest.ModFolderPath,
                "backgrounds"
            );
            _singlePages.Clear();
            _ranges.Clear();

            try
            {
                if (Directory.Exists(_folder))
                {
                    foreach (string file in Directory.GetFiles(
                        _folder,
                        "*.png"
                    ))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        Match single = Regex.Match(
                            name,
                            @"^\$?([A-Z0-9_]+)_(\d+)$"
                        );
                        if (single.Success)
                        {
                            _singlePages[
                                "$" + single.Groups[1].Value + "_" +
                                single.Groups[2].Value
                            ] = file;
                            continue;
                        }

                        Match range = Regex.Match(
                            name,
                            @"^\$?([A-Z0-9_]+)_(\d+)-(\d+)$"
                        );
                        if (range.Success)
                        {
                            _ranges.Add(new PageRange
                            {
                                StartPage = int.Parse(range.Groups[2].Value),
                                EndPage = int.Parse(range.Groups[3].Value),
                                FileName = file
                            });
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log("Failed while scanning backgrounds: " + exception);
            }

            _started = true;
        }

        public static bool EnsureAttached()
        {
            if (!_started || _image != null)
            {
                return _image != null;
            }

            try
            {
                Canvas dialogueCanvas = FindDialogueCanvas();
                if (dialogueCanvas == null)
                {
                    return false;
                }

                _root = new GameObject("RETURN_DIALOGUE_BACKDROP");
                _root.transform.SetParent(dialogueCanvas.transform, false);
                _root.transform.SetAsFirstSibling();
                _image = _root.AddComponent<Image>();
                _image.raycastTarget = false;
                _image.preserveAspect = true;
                RectTransform rect = _root.GetComponent<RectTransform>();
                // Top-aligned, 60% of the screen height so the illustration
                // never overlaps the dialogue text at the bottom.
                RectTransform parentRect =
                    (RectTransform)rect.parent;
                float parentHeight = parentRect.rect.height;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(
                    0f,
                    -parentHeight * 0.60f
                );
                rect.offsetMax = Vector2.zero;
                return true;
            }
            catch (Exception exception)
            {
                Log("Failed while attaching backdrop: " + exception);
                return false;
            }
        }

        public static void ShowPageForPage(int pageNumber)
        {
            if (!_started || pageNumber < 0)
            {
                return;
            }

            ShowPage(
                "$" + _prefix + "_" + pageNumber.ToString("D2")
            );
        }

        public static void ShowPage(string pageKey)
        {
            if (!_started || string.IsNullOrEmpty(pageKey))
            {
                return;
            }

            EnsureAttached();
            if (_image == null)
            {
                return;
            }

            string file = ResolveFile(pageKey);
            if (file == null)
            {
                // No dedicated image for this page: keep the current one.
                return;
            }

            if (pageKey == _currentKey)
            {
                return;
            }

            Texture2D texture = LoadTexture(file);
            if (texture == null)
            {
                return;
            }

            if (_image.sprite != null)
            {
                UnityEngine.Object.Destroy(_image.sprite);
            }
            _image.sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
            _currentKey = pageKey;
        }

        public static void End()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
            _root = null;
            _image = null;
            _currentKey = null;
            _started = false;
        }

        private static string ResolveFile(string pageKey)
        {
            if (_singlePages.TryGetValue(pageKey, out string exact))
            {
                return exact;
            }

            Match page = Regex.Match(pageKey, @"_(\d+)$");
            if (!page.Success)
            {
                return null;
            }
            int pageNumber = int.Parse(page.Groups[1].Value);
            foreach (PageRange range in _ranges)
            {
                if (pageNumber >= range.StartPage &&
                    pageNumber <= range.EndPage)
                {
                    return range.FileName;
                }
            }
            return null;
        }

        private static Texture2D LoadTexture(string file)
        {
            if (_textureCache.TryGetValue(file, out Texture2D cached))
            {
                return cached;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                Texture2D texture =
                    new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    Log("Failed to decode image: " + file);
                    return null;
                }
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                _textureCache[file] = texture;
                return texture;
            }
            catch (Exception exception)
            {
                Log("Failed to load image: " + file + " :: " + exception);
                return null;
            }
        }

        private static Canvas FindDialogueCanvas()
        {
            foreach (Canvas canvas in
                Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (canvas == null ||
                    !canvas.gameObject.scene.IsValid())
                {
                    continue;
                }
                if (canvas.GetComponent<DialogueBoxVer2>() != null)
                {
                    return canvas;
                }
            }
            return null;
        }

        private static void Log(string message)
        {
            if (_mod != null)
            {
                ReturnDebugLog.Write(
                    "[RETURN BACKDROP] " + message,
                    OWML.Common.MessageType.Warning
                );
            }
        }
    }
}