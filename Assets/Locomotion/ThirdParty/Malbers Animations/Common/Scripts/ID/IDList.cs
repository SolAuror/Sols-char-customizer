// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    //Wrapper container for a typed ID list rendered as pill/chip tags in the inspector.
    //Uses the same type-targeted drawer pattern as IDList<T> and MTags.
    //Usage: public IDPills<StateID> stateIDs;
    [System.Serializable]
    public class IDList<T> where T : IDs
    {
        public List<T> items = new();
        internal string name;

        public int Count => items?.Count ?? 0;
        public int Length => items?.Count ?? 0;
        public bool HasItems => items != null && items.Count > 0;

        public T this[int i] => items[i];
        public bool Contains(T id) => id != null && items != null && items.Contains(id);

        public IDList(T[] items1)
        {
            if (items1 == null) return;
            items = new List<T>(items1);
        }

        public IDList() => items = new List<T>();

        public IDList(T oneItem) => items = new List<T> { oneItem };

        public IDList(List<T> items1) => items = new List<T>(items1);

        /// <summary>Returns true if the list contains the given ID.</summary>
        public bool HasID(T id)
        {
            if (id == null) return false;
            foreach (var t in items)
                if (t != null && t.ID == id.ID) return true;
            return false;
        }

        /// <summary>Returns true if the list contains any of the given IDs.</summary>
        public bool HasID(params T[] checkIDs)
        {
            foreach (var check in checkIDs)
                if (HasID(check)) return true;
            return false;
        }

        /// <summary>Returns true if the list contains ALL of the given IDs.</summary>
        public bool HasID_All(params T[] checkIDs)
        {
            foreach (var check in checkIDs)
                if (!HasID(check)) return false;
            return true;
        }

        /// <summary>Returns true if the list contains a ID with the given ID(int).</summary>
        public bool Contains(int id)
        {
            foreach (var t in items)
                if (t != null && t.ID == id) return true;
            return false;
        }


        public static void MigrateIDList(List<T> legacy, IDList<T> target)
        {
            if (legacy == null || legacy.Count == 0) return;
            foreach (var id in legacy)
                if (id != null && !target.Contains(id)) target.items.Add(id);
            legacy.Clear();
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(IDList<>), true)]
    public class IDPillsDrawer : PropertyDrawer
    {
        // ── Constants ──────────────────────────────────────────────────
        private const float PillHeight = 20f;
        private const float PillMarginX = 4f;
        private const float PillMarginY = 2f;
        private const float XBtnSize = 14f;
        private const float XPad = -2f;
        private const float HelpBoxPad = 2f;

        // ── Shared style cache ─────────────────────────────────────────
        private static GUIStyle s_PillStyle;
        private static GUIStyle s_PillLabelStyle;
        private static GUIStyle s_PillBgStyle;
        private static GUIStyle s_XBtnStyle;
        private static GUIStyle s_AddBtnStyle;
        private static GUIStyle s_FieldLabelStyle;

        private static readonly Dictionary<Color, Texture2D> s_TexCache = new();

        // ── Per-instance caches ────────────────────────────────────────
        private readonly List<IDs> m_ProjectIDs = new();
        private readonly List<IDs> m_AvailableIDs = new();
        private int m_LastIDCount = -1;
        private System.Type m_ItemType;

        // ── Clipboard (shared across all IDPills drawers) ─────────────
        private static readonly List<IDs> s_Clipboard = new();
        private static string s_ClipboardTypeName = string.Empty;

        // ── Type detection — reads T from IDPills<T> ───────────────────
        private System.Type GetItemType()
        {
            if (m_ItemType != null) return m_ItemType;

            var args = fieldInfo.FieldType.GetGenericArguments();
            m_ItemType = args.Length > 0 && typeof(IDs).IsAssignableFrom(args[0])
                ? args[0]
                : typeof(IDs);

            return m_ItemType;
        }

        // ── Style init ─────────────────────────────────────────────────
        private static bool TryInitStyles()
        {
            if (GUI.skin == null) return false;
            if (s_PillStyle != null) return true;

            s_PillStyle = new(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new(8, 8, 3, 3),
                margin = new(2, 2, 2, 2),
                fixedHeight = PillHeight,
            };

            s_PillLabelStyle = new(s_PillStyle);

            s_PillBgStyle = new(GUIStyle.none)
            {
                border = new(10, 10, 10, 10),
            };

            s_XBtnStyle = new(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new(0, 0, 0, 1),
            };

            s_AddBtnStyle = new(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new(2, 2, 0, 2),
                margin = new(2, 2, 2, 2),
                fixedHeight = PillHeight,
                fixedWidth = 22,
                border = new(3, 3, 3, 3),
            };

            s_FieldLabelStyle = new(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new(2, 6, 0, 0),
            };

            return true;
        }

        // ── ID cache ───────────────────────────────────────────────────
        private void EnsureCache(SerializedProperty itemsProp)
        {
            if (m_LastIDCount == itemsProp.arraySize && m_ProjectIDs.Count > 0) return;

            var type = GetItemType();
            m_ProjectIDs.Clear();

            string[] guids = AssetDatabase.FindAssets("t:" + type.Name);
            foreach (string guid in guids)
            {
                var inst = AssetDatabase.LoadAssetAtPath<IDs>(AssetDatabase.GUIDToAssetPath(guid));
                if (inst != null) m_ProjectIDs.Add(inst);
            }

            m_AvailableIDs.Clear();
            foreach (var id in m_ProjectIDs)
            {
                bool found = false;
                for (int i = 0; i < itemsProp.arraySize; i++)
                {
                    if (itemsProp.GetArrayElementAtIndex(i).objectReferenceValue == id)
                    { found = true; break; }
                }
                if (!found) m_AvailableIDs.Add(id);
            }
            m_AvailableIDs.Sort((a, b) => a.ID.CompareTo(b.ID));

            m_LastIDCount = itemsProp.arraySize;
        }

        // ── Height ─────────────────────────────────────────────────────
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryInitStyles()) return PillHeight + HelpBoxPad * 2f;

            var itemsProp = property.FindPropertyRelative("items");
            float availW = EditorGUIUtility.currentViewWidth - EditorGUI.indentLevel * 15f - 26f;
            float addBtnW = s_AddBtnStyle.fixedWidth + s_AddBtnStyle.margin.left + s_AddBtnStyle.margin.right;

            string fieldLabel = ObjectNames.NicifyVariableName(property.name);
            float x = s_FieldLabelStyle.CalcSize(new(fieldLabel)).x;

            int rows = 1;
            for (int i = 0; i < itemsProp.arraySize; i++)
            {
                var ids = itemsProp.GetArrayElementAtIndex(i).objectReferenceValue as IDs;
                if (ids == null) continue;

                string rawLbl = !string.IsNullOrEmpty(ids.DisplayName) ? ids.DisplayName : ids.name;
                string lbl = rawLbl.Contains('/') ? rawLbl[(rawLbl.LastIndexOf('/') + 1)..] : rawLbl; // MWC — strip category prefix (e.g. "Weapon/Bow" → "Bow")
                float pillW = s_PillStyle.CalcSize(new(lbl)).x + XBtnSize + XPad + 4f;

                if (x + pillW > availW - addBtnW && x > 0f)
                {
                    rows++;
                    x = 0f;
                }
                x += pillW + PillMarginX;
            }

            return rows * (PillHeight + PillMarginY) + HelpBoxPad * 2f;
        }

        // ── Drawing ────────────────────────────────────────────────────
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!TryInitStyles()) return;

            var itemsProp = property.FindPropertyRelative("items");
            EnsureCache(itemsProp);

            GUI.Box(position, GUIContent.none);

            float availW = position.width - HelpBoxPad * 2f;
            float addBtnW = s_AddBtnStyle.fixedWidth + s_AddBtnStyle.margin.left + s_AddBtnStyle.margin.right;

            float x = position.x + HelpBoxPad;
            float y = position.y + HelpBoxPad;
            int removeIndex = -1;

            // ── Field label (right-click = copy/paste menu) ────────────
            string fieldLabel = ObjectNames.NicifyVariableName(property.name);
            float labelW = s_FieldLabelStyle.CalcSize(new(fieldLabel)).x;
            Rect labelRect = new(x, y, labelW, PillHeight);

            GUI.Label(labelRect, new GUIContent(fieldLabel, label.tooltip), s_FieldLabelStyle);

            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 1 &&
                labelRect.Contains(Event.current.mousePosition))
            {
                ShowCopyPasteMenu(property, itemsProp);
                Event.current.Use();
            }

            x += labelW;

            // ── Pills ──────────────────────────────────────────────────
            for (int i = 0; i < itemsProp.arraySize; i++)
            {
                var ids = itemsProp.GetArrayElementAtIndex(i).objectReferenceValue as IDs;
                if (ids == null) continue;

                string rawLbl = !string.IsNullOrEmpty(ids.DisplayName) ? ids.DisplayName : ids.name;
                string lbl = rawLbl.Contains('/') ? rawLbl[(rawLbl.LastIndexOf('/') + 1)..] : rawLbl; // MWC — strip category prefix (e.g. "Weapon/Bow" → "Bow")
                float pillW = s_PillStyle.CalcSize(new(lbl)).x + XBtnSize + XPad + 4f;

                if (x + pillW > position.x + availW - addBtnW + HelpBoxPad && x > position.x + HelpBoxPad)
                {
                    x = position.x + HelpBoxPad;
                    y += PillHeight + PillMarginY;
                }

                Rect pillRect = new(x, y, pillW, PillHeight);
                Color idColor = ids.IDColor;
                float brightness = idColor.r * 0.299f + idColor.g * 0.587f + idColor.b * 0.114f;
                Color textColor = brightness > 0.55f ? Color.black : Color.white;

                if (Event.current.type == EventType.Repaint)
                {
                    s_PillBgStyle.normal.background = GetPillTex(idColor);
                    s_PillBgStyle.Draw(pillRect, GUIContent.none, false, false, false, false);
                }

                Rect pillLabelRect = new(pillRect.x, pillRect.y, pillRect.width - XBtnSize - XPad, pillRect.height);
                s_PillLabelStyle.normal.textColor = textColor;
                GUI.Label(pillLabelRect, lbl, s_PillLabelStyle);

                Rect xRect = new(
                    pillRect.xMax - XBtnSize - 4f,
                    pillRect.y + (pillRect.height - XBtnSize) * 0.5f,
                    XBtnSize, XBtnSize + 2f);
                s_XBtnStyle.normal.textColor = new(textColor.r, textColor.g, textColor.b, 0.65f);
                if (GUI.Button(xRect, "✕", s_XBtnStyle))
                    removeIndex = i;

                if (Event.current.type == EventType.MouseDown &&
                    Event.current.clickCount == 2 &&
                    pillRect.Contains(Event.current.mousePosition))
                {
                    EditorGUIUtility.PingObject((Object)ids);
                    Selection.activeObject = ids;
                    Event.current.Use();
                }

                x += pillW + PillMarginX;
            }

            // ── "+" button ─────────────────────────────────────────────
            Rect addRect = new(x, y, s_AddBtnStyle.fixedWidth, PillHeight);
            // MWC — no longer disabled when the project has no free IDs left: the popup can create new ones
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new(0.4f, 0.4f, 0.4f, 1f);
                GUI.contentColor = Color.white;

                if (GUI.Button(addRect, "+", s_AddBtnStyle))
                {
                    var capturedProp = property;
                    var capturedItems = itemsProp;
                    var itemType = GetItemType();
                    var targetObj = property.serializedObject.targetObject;
                    var itemsPath = itemsProp.propertyPath;

                    // MWC — create a new ID asset and append it to the list
                    System.Action onCreateNew = null;

                    if (itemType != null && !itemType.IsAbstract)
                    {
                        onCreateNew = () =>
                        {
                            var newID = IDPickerPopup.CreateIDAsset(itemType);
                            if (newID == null || targetObj == null) return;

                            var so = new SerializedObject(targetObj);
                            var items = so.FindProperty(itemsPath);
                            if (items == null) return;

                            items.InsertArrayElementAtIndex(items.arraySize); // ApplyModifiedProperties registers its own Undo
                            items.GetArrayElementAtIndex(items.arraySize - 1).objectReferenceValue = newID;
                            so.ApplyModifiedProperties();
                            m_LastIDCount = -1;
                        };
                    }

                    PopupWindow.Show(addRect, new IDPickerPopup(m_AvailableIDs, null, chosen =>
                    {
                        if (chosen == null) return;
                        Undo.RecordObject(capturedProp.serializedObject.targetObject, "Add ID");
                        capturedProp.serializedObject.Update();
                        capturedItems.InsertArrayElementAtIndex(capturedItems.arraySize);
                        capturedItems.GetArrayElementAtIndex(capturedItems.arraySize - 1).objectReferenceValue = chosen;
                        capturedProp.serializedObject.ApplyModifiedProperties();
                        m_LastIDCount = -1;
                    },
                    onCreateNew,
                    $"Create New {ObjectNames.NicifyVariableName(itemType?.Name ?? "ID")}"));
                }

                GUI.backgroundColor = prevBg;
                GUI.contentColor = Color.white;
            }

            // ── Apply removal ──────────────────────────────────────────
            if (removeIndex >= 0)
            {
                Undo.RecordObject(property.serializedObject.targetObject, "Remove ID");
                itemsProp.DeleteArrayElementAtIndex(removeIndex);
                property.serializedObject.ApplyModifiedProperties();
                m_LastIDCount = -1;
            }
        }

        // ── Copy / Paste ───────────────────────────────────────────────
        private void ShowCopyPasteMenu(SerializedProperty property, SerializedProperty itemsProp)
        {
            var menu = new GenericMenu();
            var typeName = GetItemType().Name;

            menu.AddItem(new GUIContent("Copy"), false, () =>
            {
                s_Clipboard.Clear();
                for (int i = 0; i < itemsProp.arraySize; i++)
                {
                    if (itemsProp.GetArrayElementAtIndex(i).objectReferenceValue is IDs id)
                        s_Clipboard.Add(id);
                }
                s_ClipboardTypeName = typeName;
            });

            bool canPaste = s_Clipboard.Count > 0 && s_ClipboardTypeName == typeName;
            if (canPaste)
            {
                menu.AddItem(new GUIContent("Paste"), false, () =>
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "Paste ID Pills");
                    property.serializedObject.Update();
                    itemsProp.ClearArray();
                    for (int i = 0; i < s_Clipboard.Count; i++)
                    {
                        itemsProp.InsertArrayElementAtIndex(i);
                        itemsProp.GetArrayElementAtIndex(i).objectReferenceValue = s_Clipboard[i];
                    }
                    property.serializedObject.ApplyModifiedProperties();
                    m_LastIDCount = -1;
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste"));
            }

            menu.ShowAsContext();
        }

        // ── Texture helpers ────────────────────────────────────────────
        private static Texture2D GetPillTex(Color color)
        {
            if (!s_TexCache.TryGetValue(color, out var tex) || tex == null)
            {
                tex = BuildPillTex(10, (int)PillHeight, color);
                s_TexCache[color] = tex;
            }
            return tex;
        }

        private static Texture2D BuildPillTex(int cornerRadius, int height, Color color)
        {
            int width = Mathf.Max(cornerRadius * 2 + 4, 32);
            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.hideFlags = HideFlags.DontSave;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float cx = Mathf.Min(px, width - 1 - px);
                    float cy = Mathf.Min(py, height - 1 - py);

                    if (cx < cornerRadius && cy < cornerRadius)
                    {
                        float dist = Vector2.Distance(new(cx, cy), new(cornerRadius - 1, cornerRadius - 1));
                        float alpha = Mathf.Clamp01(cornerRadius - dist);
                        tex.SetPixel(px, py, new(color.r, color.g, color.b, color.a * alpha));
                    }
                    else
                    {
                        tex.SetPixel(px, py, color);
                    }
                }
            }

            tex.Apply();
            return tex;
        }
    }
#endif
}
