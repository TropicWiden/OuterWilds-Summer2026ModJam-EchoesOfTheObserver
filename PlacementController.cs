using OWML.Common;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Return
{
    internal static class PlacementController
    {
        private const float MoveSpeed = 1.2f;
        private const float RotateSpeed = 35f;

        public static void Attach(
            ReturnMod mod,
            Transform root,
            Transform daz,
            Transform yarrow,
            Transform phlox
        )
        {
            if (mod == null || root == null)
            {
                return;
            }

            ScenePlacementComponent component =
                root.GetComponent<ScenePlacementComponent>();
            if (component == null)
            {
                component =
                    root.gameObject.AddComponent<ScenePlacementComponent>();
            }

            component.Initialize(mod, root, daz, yarrow, phlox);
        }

        public static void ApplySavedLayout(
            ReturnMod mod,
            Transform root,
            Transform daz,
            Transform yarrow,
            Transform phlox
        )
        {
            if (mod == null || root == null)
            {
                return;
            }

            string path = GetLayoutPath(mod);
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                PlacementLayout layout =
                    JsonUtility.FromJson<PlacementLayout>(json);
                if (layout == null || layout.entries == null)
                {
                    return;
                }

                foreach (PlacementEntry entry in layout.entries)
                {
                    Transform target = null;
                    switch (entry.name)
                    {
                        case "Box":
                            target = root;
                            break;
                        case "Daz":
                            target = daz;
                            break;
                        case "Yarrow":
                            target = yarrow;
                            break;
                        case "Phlox":
                            target = phlox;
                            break;
                    }

                    if (target == null)
                    {
                        continue;
                    }

                    target.localPosition = new Vector3(
                        entry.px,
                        entry.py,
                        entry.pz
                    );
                    target.localRotation = new Quaternion(
                        entry.qx,
                        entry.qy,
                        entry.qz,
                        entry.qw
                    );
                }

                mod.ModHelper.Console.WriteLine(
                    "[RETURN PLACEMENT] Loaded saved layout: " + path,
                    MessageType.Success
                );
            }
            catch (Exception exception)
            {
                mod.ModHelper.Console.WriteLine(
                    "[RETURN PLACEMENT] Failed to load layout: " +
                    exception.Message,
                    MessageType.Error
                );
            }
        }

        private static string GetLayoutPath(ReturnMod mod)
        {
            string folder = mod.ModHelper.Manifest.ModFolderPath;
            return Path.Combine(folder, "scene2-layout.json");
        }

        [Serializable]
        internal sealed class PlacementEntry
        {
            public string name;
            public float px;
            public float py;
            public float pz;
            public float qx;
            public float qy;
            public float qz;
            public float qw;
        }

        [Serializable]
        internal sealed class PlacementLayout
        {
            public List<PlacementEntry> entries =
                new List<PlacementEntry>();
        }

        internal sealed class ScenePlacementComponent : MonoBehaviour
        {
            private ReturnMod _mod;
            private Transform _root;
            private readonly List<Transform> _targets =
                new List<Transform>();
            private readonly List<string> _targetNames =
                new List<string>();
            private int _selectedIndex;
            private bool _enabled;

            /// <summary>
            /// Hides the scene-2 placement debug HUD in every scene. The
            /// keyboard placement controls (F4/F5/F6) still work silently.
            /// </summary>
            public static bool SuppressHud = true;

            public void Initialize(
                ReturnMod mod,
                Transform root,
                Transform daz,
                Transform yarrow,
                Transform phlox
            )
            {
                _mod = mod;
                _root = root;
                _targets.Clear();
                _targetNames.Clear();

                _targets.Add(root);
                _targetNames.Add("Box");

                if (daz != null)
                {
                    _targets.Add(daz);
                    _targetNames.Add("Daz");
                }
                if (yarrow != null)
                {
                    _targets.Add(yarrow);
                    _targetNames.Add("Yarrow");
                }
                if (phlox != null)
                {
                    _targets.Add(phlox);
                    _targetNames.Add("Phlox");
                }

                _selectedIndex = 0;
                _enabled = true;
            }

            private void Update()
            {
                if (_mod == null ||
                    _root == null ||
                    Keyboard.current == null)
                {
                    return;
                }

                if (Keyboard.current.f4Key.wasPressedThisFrame)
                {
                    _enabled = !_enabled;
                    LogEnabled();
                }

                if (!_enabled)
                {
                    return;
                }

                if (Keyboard.current.f5Key.wasPressedThisFrame &&
                    _targets.Count > 0)
                {
                    _selectedIndex =
                        (_selectedIndex + 1) % _targets.Count;
                    LogSelected();
                }

                if (Keyboard.current.f6Key.wasPressedThisFrame)
                {
                    SaveLayout();
                }

                Transform selected = _targets[_selectedIndex];
                if (selected == null)
                {
                    return;
                }

                float deltaTime = Time.deltaTime;
                Vector3 delta = Vector3.zero;
                if (Keyboard.current.leftArrowKey.isPressed)
                {
                    delta -= _root.right;
                }
                if (Keyboard.current.rightArrowKey.isPressed)
                {
                    delta += _root.right;
                }
                if (Keyboard.current.upArrowKey.isPressed)
                {
                    delta += _root.forward;
                }
                if (Keyboard.current.downArrowKey.isPressed)
                {
                    delta -= _root.forward;
                }
                if (Keyboard.current.pageUpKey.isPressed)
                {
                    delta += _root.up;
                }
                if (Keyboard.current.pageDownKey.isPressed)
                {
                    delta -= _root.up;
                }

                if (delta.sqrMagnitude > 0f)
                {
                    selected.localPosition +=
                        delta.normalized * MoveSpeed * deltaTime;
                }

                float yaw = 0f;
                if (Keyboard.current.qKey.isPressed)
                {
                    yaw -= RotateSpeed * deltaTime;
                }
                if (Keyboard.current.eKey.isPressed)
                {
                    yaw += RotateSpeed * deltaTime;
                }
                if (Mathf.Abs(yaw) > 0f)
                {
                    selected.Rotate(_root.up, yaw, Space.World);
                }

                float pitch = 0f;
                if (Keyboard.current.zKey.isPressed)
                {
                    pitch -= RotateSpeed * deltaTime;
                }
                if (Keyboard.current.xKey.isPressed)
                {
                    pitch += RotateSpeed * deltaTime;
                }
                if (Mathf.Abs(pitch) > 0f)
                {
                    selected.Rotate(_root.right, pitch, Space.World);
                }
            }

            private void OnGUI()
            {
                if (SuppressHud)
                {
                    return;
                }

                if (_mod == null ||
                    _targets.Count == 0 ||
                    _selectedIndex >= _targets.Count)
                {
                    return;
                }

                GUI.Label(
                    new Rect(10f, 10f, 900f, 30f),
                    "[RETURN PLACEMENT] " +
                    _targetNames[_selectedIndex] +
                    (_enabled ? " (on)" : " (off)") +
                    " | F4 开关  F5 切换  F6 保存 | " +
                    "方向键移动  PgUp/PgDn 升降  Q/E 转向  Z/X 俯仰"
                );
            }

            private void LogEnabled()
            {
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN PLACEMENT] Placement mode " +
                    (_enabled ? "enabled." : "disabled."),
                    MessageType.Info
                );
            }

            private void LogSelected()
            {
                _mod.ModHelper.Console.WriteLine(
                    "[RETURN PLACEMENT] Selected: " +
                    _targetNames[_selectedIndex],
                    MessageType.Info
                );
            }

            private void SaveLayout()
            {
                try
                {
                    PlacementLayout layout = new PlacementLayout();
                    for (int index = 0;
                         index < _targets.Count;
                         index++)
                    {
                        Transform target = _targets[index];
                        if (target == null)
                        {
                            continue;
                        }

                        PlacementEntry entry = new PlacementEntry
                        {
                            name = _targetNames[index],
                            px = target.localPosition.x,
                            py = target.localPosition.y,
                            pz = target.localPosition.z,
                            qx = target.localRotation.x,
                            qy = target.localRotation.y,
                            qz = target.localRotation.z,
                            qw = target.localRotation.w
                        };
                        layout.entries.Add(entry);
                    }

                    string path = GetLayoutPath(_mod);
                    File.WriteAllText(
                        path,
                        JsonUtility.ToJson(layout, true)
                    );
                    _mod.ModHelper.Console.WriteLine(
                        "[RETURN PLACEMENT] Layout saved: " + path,
                        MessageType.Success
                    );
                }
                catch (Exception exception)
                {
                    _mod.ModHelper.Console.WriteLine(
                        "[RETURN PLACEMENT] Save failed: " +
                        exception.Message,
                        MessageType.Error
                    );
                }
            }
        }
    }
}
