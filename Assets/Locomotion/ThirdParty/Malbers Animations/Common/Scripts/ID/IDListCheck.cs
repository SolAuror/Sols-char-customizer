// Last Change: 9/17/2026 6:40pm
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    /// <summary>
    /// A serializable list of IDs (ScriptableObjects that inherit from the "IDs" base class), with an "Include/Exclude" toggle. When "Include" is true, the list represents a set of IDs to include (i.e. Check() will return true for IDs in the list, and false for IDs not in the list). When "Include" is false, the list represents a set of IDs to exclude (i.e. Check() will return false for IDs in the list, and true for IDs not in the list). If the list is empty, it will return true for all IDs when "Include" is true (since there are no IDs to exclude), and false for all IDs when "Include" is false (since there are no IDs to include).
    /// </summary>
    /// <typeparam name="T"> The type of IDs this list will hold. Must inherit from the "IDs" base class.</typeparam>   
    [System.Serializable]
    public class IDListCheck<T> where T : IDs
    {
        public List<T> IDs;

        public bool include = true;

        private HashSet<int> m_Cache;
        private int m_CacheCount = -1; // MWC — IDs.Count the cache was built from (null/duplicate entries make the set smaller, so the set size cannot be used)

        public IDs this[int index] => IDs?[index];

        public bool Empty => IDs == null || IDs.Count == 0;

        /// <summary>
        ///  Returns true if the list is not null and contains at least one ID. Note that an IDList with Include=false can still be "Valid" even if it doesn't actually include any IDs, since it would then exclude everything.
        ///  Use the "Empty" property to check if there are no IDs in the list regardless of Include/Exclude.
        /// </summary>
        public bool Valid => IDs != null && IDs.Count > 0;

        public int Length => IDs != null ? IDs.Count : 0;
        public int Count => Length;

        public IDListCheck(List<T> list)
        {
            IDs = list ?? new List<T>();
            include = true;
        }

        public IDListCheck(List<T> list, bool include)
        {
            IDs = list ?? new List<T>();
            this.include = include;
        }

        public IDListCheck()
        {
            IDs = new List<T>();
            include = true;
        }

        public IDListCheck(IDListCheck<T> value)
        {
            if (value != null)
            {
                IDs = new List<T>(value.IDs);
                include = value.include;
            }
            else
            {
                IDs = new List<T>();
                include = true;
            }
        }

        public IDListCheck(bool include)
        {
            IDs = new List<T>();
            this.include = include;
        }

        private bool ContainsID(int id, bool TrueIfEmpty)
        {
            if (Empty) return TrueIfEmpty;            // If the list is empty, return false for Include or Exclude, and let the Check() method handle the Include/Exclude logic.

            // MWC — the cache is rebuilt when the list size changed too: the Inspector edits this same instance
            // live (add/remove pills), so a cache built before the edit answered with the old IDs for ever.
            if (m_Cache == null || m_CacheCount != IDs.Count) BuildCache();

            return include == m_Cache.Contains(id);
        }

        private void BuildCache()
        {
            m_Cache = new(IDs.Count);
            m_CacheCount = IDs.Count;
            foreach (var id in IDs)
                if (id != null) m_Cache.Add(id.ID);
        }

        /// <summary>Returns if an ID is inside the list, respecting Include/Exclude.</summary>
        public bool Check(T ID) => ID != null && ContainsID(ID.ID, false);
        /// <summary> Returns if an int ID is inside the list, respecting Include/Exclude. If the list is empty, it will return the value of TrueIfEmpty</summary>

        public bool Check(T ID, bool TrueIfEmpty) => ID != null && ContainsID(ID.ID, TrueIfEmpty);
        /// <summary>Returns if an int ID is inside the list, respecting Include/Exclude.</summary>
        public bool Check(int ID) => ContainsID(ID, false);

        /// <summary> Returns if an int ID is inside the list, respecting Include/Exclude. If the list is empty, it will return the value of TrueIfEmpty</summary>
        public bool Check(int ID, bool TrueIfEmpty) => ContainsID(ID, TrueIfEmpty);

        public bool Contains(T ID) => ID != null && !Empty && IDs.Contains(ID);

    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(IDListCheck<>), true)]
    public class IDListDrawer : PropertyDrawer
    {
        // ── Constants ──────────────────────────────────────────────────
        private const float PillHeight = 20f;
        private const float PillMarginX = 4f;
        private const float PillMarginY = 2f;
        private const float XBtnSize = 14f;
        private const float XPad = -2;
        private const float HelpBoxPad = 2f;
        private const float IncExcBtnW = 22f;
        private const float IncExcMarginR = 4f;

        // ── Colors ─────────────────────────────────────────────────────
        private static readonly Color s_IncludeColor = new(0.20f, 0.60f, 0.25f, 1f);
        private static readonly Color s_ExcludeColor = new(0.72f, 0.22f, 0.22f, 1f);

        // ── Shared style cache ─────────────────────────────────────────
        private static GUIStyle s_PillStyle;
        private static GUIStyle s_PillLabelStyle;
        private static GUIStyle s_PillBgStyle;
        private static GUIStyle s_XBtnStyle;
        private static GUIStyle s_AddBtnStyle;
        private static GUIStyle s_FieldLabelStyle;
        private static GUIStyle s_IncExcStyle;

        private static readonly Dictionary<Color, Texture2D> s_TexCache = new();

        // ── Per-instance caches ────────────────────────────────────────
        private readonly List<IDs> m_ProjectIDs = new();
        private readonly List<IDs> m_AvailableIDs = new();
        private int m_LastIDCount = -1;
        private System.Type m_ItemType;

        // ── Clipboard (shared across all IDList drawers) ───────────────
        private static readonly List<IDs> s_ClipboardIDs = new();
        private static bool s_ClipboardInclude = true;
        private static string s_ClipboardTypeName = string.Empty;
        private static bool HasClipboard => s_ClipboardIDs.Count > 0;

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

            s_AddBtnStyle = new(GUI.skin.button) // MWC — removed custom pill texture; rely on Unity's default button like s_IncExcStyle so it renders consistently at all UI scales
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

            s_IncExcStyle = new(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new(0, 0, 0, 0),
                margin = new(2, 4, 2, 2),
                fixedHeight = PillHeight,
                fixedWidth = IncExcBtnW,
                border = new(3, 3, 3, 3),
            };

            return true;
        }

        // ── Type helper ────────────────────────────────────────────────
        private System.Type GetItemType()
        {
            if (m_ItemType != null) return m_ItemType;
            var args = fieldInfo.FieldType.GetGenericArguments();
            m_ItemType = args.Length > 0 ? args[0] : typeof(IDs);
            return m_ItemType;
        }

        // ── ID cache ───────────────────────────────────────────────────
        private void EnsureCache(SerializedProperty idsProp)
        {
            if (m_LastIDCount == idsProp.arraySize && m_ProjectIDs.Count > 0) return;

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
                for (int i = 0; i < idsProp.arraySize; i++)
                {
                    if (idsProp.GetArrayElementAtIndex(i).objectReferenceValue == id)
                    { found = true; break; }
                }
                if (!found) m_AvailableIDs.Add(id);
            }
            m_AvailableIDs.Sort((a, b) => a.ID.CompareTo(b.ID)); // MWC — sort by ID value ascending (0 first)

            m_LastIDCount = idsProp.arraySize;
        }

        // ── Height ─────────────────────────────────────────────────────
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryInitStyles()) return PillHeight + HelpBoxPad * 2f;

            var idsProp = property.FindPropertyRelative("IDs");
            float availW = EditorGUIUtility.currentViewWidth - EditorGUI.indentLevel * 15f - 26f;
            float addBtnW = s_AddBtnStyle.fixedWidth + s_AddBtnStyle.margin.left + s_AddBtnStyle.margin.right;

            string fieldLabel = ObjectNames.NicifyVariableName(property.name);
            float x = s_FieldLabelStyle.CalcSize(new(fieldLabel)).x + IncExcBtnW + IncExcMarginR;

            int rows = 1;
            for (int i = 0; i < idsProp.arraySize; i++)
            {
                var ids = idsProp.GetArrayElementAtIndex(i).objectReferenceValue as IDs;
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

            var idsProp = property.FindPropertyRelative("IDs");
            var includeProp = property.FindPropertyRelative("include");

            EnsureCache(idsProp);

            GUI.Box(position, GUIContent.none);

            float availW = position.width - HelpBoxPad * 2f;
            float addBtnW = s_AddBtnStyle.fixedWidth + s_AddBtnStyle.margin.left + s_AddBtnStyle.margin.right;

            float x = position.x + HelpBoxPad;
            float y = position.y + HelpBoxPad;
            int removeIndex = -1;

            // ── Field label (right-click for copy/paste menu) ────────────────────────────────────────────
            string fieldLabel = ObjectNames.NicifyVariableName(property.name);
            float labelW = s_FieldLabelStyle.CalcSize(new(fieldLabel)).x;
            Rect labelClickRect = new(x, y, labelW, PillHeight);

            GUI.Label(labelClickRect, new GUIContent(fieldLabel, label.tooltip), s_FieldLabelStyle);

            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 1 &&
                labelClickRect.Contains(Event.current.mousePosition))
            {
                ShowCopyPasteMenu(property, idsProp, includeProp);
                Event.current.Use();
            }

            x += labelW;

            // ── Include / Exclude toggle ───────────────────────────────
            bool isInclude = includeProp.boolValue;
            Color ieColor = isInclude ? s_IncludeColor : s_ExcludeColor;
            string ieLabel = isInclude ? "✓" : "✕";
            string ieTooltip = isInclude ? "Include" : "Exclude" + " the IDs in this list.\nIf the list is empty, it will return [false] when checking";

            Rect ieRect = new(x, y, IncExcBtnW, PillHeight);

            float ieBrightness = ieColor.r * 0.299f + ieColor.g * 0.587f + ieColor.b * 0.114f;
            s_IncExcStyle.normal.textColor = ieBrightness > 0.55f ? Color.black : Color.white;
            s_IncExcStyle.hover.textColor = s_IncExcStyle.normal.textColor;

            Color prevBgIE = GUI.backgroundColor;
            GUI.backgroundColor = ieColor;

            if (GUI.Button(ieRect, new GUIContent(ieLabel, ieTooltip), s_IncExcStyle))
            {
                Undo.RecordObject(property.serializedObject.targetObject, "Toggle Include/Exclude");
                includeProp.boolValue = !includeProp.boolValue;
                property.serializedObject.ApplyModifiedProperties();
            }

            GUI.backgroundColor = prevBgIE;

            x += IncExcBtnW + IncExcMarginR;

            // ── Pills ──────────────────────────────────────────────────
            for (int i = 0; i < idsProp.arraySize; i++)
            {
                var ids = idsProp.GetArrayElementAtIndex(i).objectReferenceValue as IDs;
                if (ids == null) continue;

                string rawLbl = !string.IsNullOrEmpty(ids.DisplayName) ? ids.DisplayName : ids.name;
                string lbl = rawLbl.Contains('/') ? rawLbl[(rawLbl.LastIndexOf('/') + 1)..] : rawLbl; // MWC — strip category prefix (e.g. "Weapon/Bow" → "Bow")
                float pillW = s_PillStyle.CalcSize(new(lbl)).x + XBtnSize + XPad;

                // Wrap to next row
                if (x + pillW > position.x + availW - addBtnW + HelpBoxPad &&
                    x > position.x + HelpBoxPad)
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

                Rect labelRect = new(pillRect.x, pillRect.y, pillRect.width - XBtnSize - XPad, pillRect.height);
                s_PillLabelStyle.normal.textColor = textColor;
                GUI.Label(labelRect, lbl, s_PillLabelStyle);

                Rect xRect = new(
                    pillRect.xMax - XBtnSize - 4f,
                    pillRect.y + (pillRect.height - XBtnSize) * 0.5f,
                    XBtnSize, XBtnSize + 2);
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

                if (GUI.Button(addRect, "+", s_AddBtnStyle)) // MWC — replaced GenericMenu with IDPickerPopup for search + grouping
                {
                    var capturedProp = property;
                    var capturedIdsProp = idsProp;
                    var itemType = GetItemType();
                    var targetObj = property.serializedObject.targetObject;
                    var idsPath = idsProp.propertyPath;

                    // MWC — create a new ID asset and append it to the list
                    System.Action onCreateNew = null;

                    if (itemType != null && !itemType.IsAbstract)
                    {
                        onCreateNew = () =>
                        {
                            var newID = IDPickerPopup.CreateIDAsset(itemType);
                            if (newID == null || targetObj == null) return;

                            var so = new SerializedObject(targetObj);
                            var ids = so.FindProperty(idsPath);
                            if (ids == null) return;

                            ids.InsertArrayElementAtIndex(ids.arraySize); // ApplyModifiedProperties registers its own Undo
                            ids.GetArrayElementAtIndex(ids.arraySize - 1).objectReferenceValue = newID;
                            so.ApplyModifiedProperties();
                            m_LastIDCount = -1;
                        };
                    }

                    PopupWindow.Show(addRect, new IDPickerPopup(m_AvailableIDs, null, chosen =>
                    {
                        if (chosen == null) return;
                        Undo.RecordObject(capturedProp.serializedObject.targetObject, "Add ID");
                        capturedProp.serializedObject.Update();
                        capturedIdsProp.InsertArrayElementAtIndex(capturedIdsProp.arraySize);
                        capturedIdsProp.GetArrayElementAtIndex(capturedIdsProp.arraySize - 1).objectReferenceValue = chosen;
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
                idsProp.DeleteArrayElementAtIndex(removeIndex);
                property.serializedObject.ApplyModifiedProperties();
                m_LastIDCount = -1;
            }
        }

        // ── Copy / Paste helpers ──────────────────────────────────────────────────
        private void ShowCopyPasteMenu(SerializedProperty property,
                                       SerializedProperty idsProp,
                                       SerializedProperty includeProp)
        {
            var menu = new GenericMenu();
            var typeName = GetItemType().Name;

            menu.AddItem(new GUIContent("Copy"), false, () =>
            {
                s_ClipboardIDs.Clear();
                for (int i = 0; i < idsProp.arraySize; i++)
                {
                    if (idsProp.GetArrayElementAtIndex(i).objectReferenceValue is IDs id)
                        s_ClipboardIDs.Add(id);
                }
                s_ClipboardInclude = includeProp.boolValue;
                s_ClipboardTypeName = typeName;
            });

            bool canPaste = HasClipboard && s_ClipboardTypeName == typeName;
            if (canPaste)
            {
                menu.AddItem(new GUIContent("Paste"), false, () =>
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "Paste IDList");
                    property.serializedObject.Update();

                    idsProp.ClearArray();
                    for (int i = 0; i < s_ClipboardIDs.Count; i++)
                    {
                        idsProp.InsertArrayElementAtIndex(i);
                        idsProp.GetArrayElementAtIndex(i).objectReferenceValue = s_ClipboardIDs[i];
                    }
                    includeProp.boolValue = s_ClipboardInclude;
                    property.serializedObject.ApplyModifiedProperties();
                    m_LastIDCount = -1;
                });

                menu.AddSeparator(string.Empty);

                menu.AddItem(new GUIContent("Paste Include only"), false, () =>
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "Paste IDList Include");
                    property.serializedObject.Update();
                    includeProp.boolValue = s_ClipboardInclude;
                    property.serializedObject.ApplyModifiedProperties();
                });

                menu.AddItem(new GUIContent("Paste IDs only"), false, () =>
                {
                    Undo.RecordObject(property.serializedObject.targetObject, "Paste IDList IDs");
                    property.serializedObject.Update();
                    idsProp.ClearArray();
                    for (int i = 0; i < s_ClipboardIDs.Count; i++)
                    {
                        idsProp.InsertArrayElementAtIndex(i);
                        idsProp.GetArrayElementAtIndex(i).objectReferenceValue = s_ClipboardIDs[i];
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
        private static readonly Dictionary<Color, Texture2D> s_RectTexCache = new();

        private static Texture2D GetPillTex(Color color)
        {
            if (!s_TexCache.TryGetValue(color, out Texture2D tex) || tex == null)
            {
                tex = BuildPillTex(10, (int)PillHeight, color);
                s_TexCache[color] = tex;
            }
            return tex;
        }

        private static Texture2D BuildPillTex(int cornerRadius, int height, Color color)
        {
            int width = Mathf.Max(cornerRadius * 2 + 4, 32);
            Texture2D tex = new(width, height, TextureFormat.ARGB32, false);
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