// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [System.Serializable]
    public class MTags
    {
        public List<Tag> tags = new();

        public MTags(Tag[] tags1)
        {
            if (tags1 == null) return;
            tags = new List<Tag>(tags1);
        }

        public MTags() => tags = new List<Tag>();

        public MTags(Tag oneTag) => tags = new List<Tag> { oneTag };

        public MTags(List<Tag> tags1) => tags = tags1 != null ? new List<Tag>(tags1) : new List<Tag>(); // MWC — null-safe, matches the Tag[] overload

        public int Length => tags.Count;
        public int Count => tags.Count;

        public bool ValidObjects => tags != null && tags.Count > 0;

        public Tag this[int index] => tags[index];

        /// <summary>Returns true if the list contains the given Tag.</summary>
        public bool HasTag(Tag tag)
        {
            if (tag == null) return false;
            foreach (var t in tags)
                if (t != null && t.ID == tag.ID) return true;
            return false;
        }

        /// <summary>Returns true if the list contains a Tag with the given ID.</summary>
        public bool HasTag(int id)
        {
            foreach (var t in tags)
                if (t != null && t.ID == id) return true;
            return false;
        }

        /// <summary>Returns true if the list contains any of the given Tags.</summary>
        public bool HasTag(params Tag[] checkTags)
        {
            foreach (var check in checkTags)
                if (HasTag(check)) return true;
            return false;
        }

        /// <summary>Returns true if the list contains ALL of the given Tags.</summary>
        public bool HasAllTags(params Tag[] checkTags)
        {
            foreach (var check in checkTags)
                if (!HasTag(check)) return false;
            return true;
        }

        /// <summary>Returns true if the list contains the given Tag (same as HasTag).</summary>
        public bool Contains(Tag tag) => HasTag(tag);


        public void Merge(MTags tags)
        {
            if (tags == null || tags.Length == 0) return;

            MTags merged = new(this.tags);

            foreach (var t in tags.tags)
                if (t != null && !merged.HasTag(t)) merged.tags.Add(t);

            this.tags = merged.tags;
        }

        public void Merge(Tag[] tags)
        {
            if (tags == null || tags.Length == 0) return;

            MTags merged = new(this.tags);
            foreach (var t in tags)
                if (t != null && !merged.HasTag(t)) merged.tags.Add(t);
            this.tags = merged.tags;
        }


        /// <summary>
        /// Find out if any of the GameObjects in the list of Tags matches the given GameObject. Returns false if the list is empty or null.
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        public bool GameObjectHasTag(Transform t) => t != null && GameObjectHasTag(t.gameObject);

        /// <summary>
        /// Find out if any of the GameObjects in the list of Tags matches the given GameObject. Returns false if the list is empty or null.
        /// </summary>
        /// <param name="go"></param>
        /// <returns></returns>
        public bool GameObjectHasTag(GameObject go)
        {
            if (go == null) return false;

            foreach (var t in tags)
                if (t != null && t.Contains(go)) return true;
            return false;
        }

        public static bool Migrate(ref Tag[] tags_Legacy, ref MTags tags)
        {
            if (tags != null || tags_Legacy == null) return false;
            tags = new MTags(tags_Legacy);
            tags_Legacy = null;
            return true;
        }

        public void Add(Tag tag)
        {
            if (tag == null || HasTag(tag)) return;
            tags.Add(tag);
        }


        //create a implicit operator to convert from MTags to List<Tag>
        public static implicit operator List<Tag>(MTags mTags) => mTags.tags;
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(MTags))]
    public class MTagsDrawer : PropertyDrawer
    {
        // ── Constants ──────────────────────────────────────────────────
        private const float PillHeight = 20f;
        private const float PillMarginX = 4f;
        private const float PillMarginY = 2f;
        private const float XBtnSize = 14f;
        private const float XPad = 6f;
        private const float HelpBoxPad = 4f; // padding inside the helpbox border

        // ── Static style & texture cache (shared across all drawer instances) ──
        private static GUIStyle pillStyle;
        private static GUIStyle pillLabelStyle;
        private static GUIStyle pillBgStyle;
        private static GUIStyle xButtonStyle;
        private static GUIStyle addButtonStyle;
        private static GUIStyle fieldLabelStyle;

        private static readonly Dictionary<Color, Texture2D> PillTextureCache = new();

        // ── Per-instance tag cache ─────────────────────────────────────
        private readonly List<Tag> cachedAllProjectTags = new();
        private readonly List<Tag> cachedAvailableTags = new();
        private int lastKnownTagCount = -1;
        private bool projectCacheBuilt;

        // ── Style init ─────────────────────────────────────────────────
        private static bool TryInitStyles()
        {
            if (GUI.skin == null) return false;

            if (pillStyle != null &&
                pillLabelStyle != null &&
                pillBgStyle != null &&
                xButtonStyle != null &&
                addButtonStyle != null &&
                fieldLabelStyle != null) return true;

            pillStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 3, 3),
                margin = new RectOffset(2, 2, 2, 2),
                fixedHeight = PillHeight,
            };

            pillLabelStyle = new GUIStyle(pillStyle);

            pillBgStyle = new GUIStyle(GUIStyle.none)
            {
                border = new RectOffset(10, 10, 10, 10),
            };

            xButtonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 1),
            };

            Texture2D addBG = BuildPillTexture(10, (int)PillHeight, Color.white);
            addButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 0, 2),
                margin = new RectOffset(2, 2, 2, 2),
                fixedHeight = PillHeight,
                fixedWidth = 22,
                border = new RectOffset(8, 8, 8, 8),
            };
            addButtonStyle.normal.background = addBG;
            addButtonStyle.hover.background = addBG;
            addButtonStyle.active.background = addBG;

            fieldLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(2, 6, 0, 0),
            };

            return true;
        }

        // ── Tag cache ──────────────────────────────────────────────────
        private void EnsureProjectCacheBuilt()
        {
            if (projectCacheBuilt) return;

            cachedAllProjectTags.Clear();
            string[] guids = AssetDatabase.FindAssets("t:Tag");
            foreach (string guid in guids)
            {
                Tag t = AssetDatabase.LoadAssetAtPath<Tag>(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null) cachedAllProjectTags.Add(t);
            }

            projectCacheBuilt = true;
        }

        private void RebuildAvailableCache(SerializedProperty tagsProp)
        {
            cachedAvailableTags.Clear();
            foreach (Tag t in cachedAllProjectTags)
            {
                bool found = false;
                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    if (tagsProp.GetArrayElementAtIndex(i).objectReferenceValue == t)
                    { found = true; break; }
                }
                if (!found) cachedAvailableTags.Add(t);
            }
            lastKnownTagCount = tagsProp.arraySize;
        }

        // ── Height calculation ─────────────────────────────────────────
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!TryInitStyles()) return PillHeight + HelpBoxPad * 2f;

            SerializedProperty tagsProp = property.FindPropertyRelative("tags");
            float availableWidth = EditorGUIUtility.currentViewWidth
                                 - EditorGUI.indentLevel * 15f
                                 - 26f;

            float addBtnW = addButtonStyle.fixedWidth
                          + addButtonStyle.margin.left
                          + addButtonStyle.margin.right;

            int rows = 1;
            float x = fieldLabelStyle.CalcSize(new GUIContent(ObjectNames.NicifyVariableName(property.name))).x;

            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                Tag tag = tagsProp.GetArrayElementAtIndex(i).objectReferenceValue as Tag;
                if (tag == null) continue;

                string label2 = !string.IsNullOrEmpty(tag.DisplayName) ? tag.DisplayName : tag.name;
                float pillW = pillStyle.CalcSize(new GUIContent(label2)).x + XBtnSize + XPad + 4f;

                if (x + pillW > availableWidth - addBtnW && x > 0f)
                {
                    rows++;
                    x = 0f;
                }
                x += pillW + PillMarginX;
            }

            float rowsHeight = rows * (PillHeight + PillMarginY) + HelpBoxPad * 2f;
            return rowsHeight;
        }

        // ── Drawing ────────────────────────────────────────────────────
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!TryInitStyles()) return;

            SerializedProperty tagsProp = property.FindPropertyRelative("tags");

            EnsureProjectCacheBuilt();
            if (tagsProp.arraySize != lastKnownTagCount)
                RebuildAvailableCache(tagsProp);

            // Draw helpbox background manually
            GUI.Box(position, GUIContent.none);

            float availableWidth = position.width - HelpBoxPad * 2f;
            float addBtnW = addButtonStyle.fixedWidth
                                 + addButtonStyle.margin.left
                                 + addButtonStyle.margin.right;

            float x = position.x + HelpBoxPad;
            float y = position.y + HelpBoxPad;
            int removeIndex = -1;

            // Draw the variable name label inline with the pills
            string fieldLabel = ObjectNames.NicifyVariableName(property.name);
            float labelW = fieldLabelStyle.CalcSize(new GUIContent(fieldLabel)).x;
            Rect fieldLabelRect = new(x, y, labelW, PillHeight);
            GUI.Label(fieldLabelRect, fieldLabel, fieldLabelStyle);
            x += labelW;

            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                Tag tag = tagsProp.GetArrayElementAtIndex(i).objectReferenceValue as Tag;
                if (tag == null) continue;

                string label2 = !string.IsNullOrEmpty(tag.DisplayName) ? tag.DisplayName : tag.name;
                float pillW = pillStyle.CalcSize(new GUIContent(label2)).x + XBtnSize + XPad + 4f;

                // Wrap to next row
                if (x + pillW > position.x + availableWidth - addBtnW + HelpBoxPad && x > position.x + HelpBoxPad)
                {
                    x = position.x + HelpBoxPad;
                    y += PillHeight + PillMarginY;
                }

                Rect pillRect = new(x, y, pillW, PillHeight);

                float brightness = tag.Color.r * 0.299f + tag.Color.g * 0.587f + tag.Color.b * 0.114f;
                Color textColor = brightness > 0.55f ? Color.black : Color.white;

                // Pill background (repaint only)
                if (Event.current.type == EventType.Repaint)
                {
                    pillBgStyle.normal.background = GetPillTexture(tag.Color);
                    pillBgStyle.Draw(pillRect, GUIContent.none, false, false, false, false);
                }

                // Label
                Rect labelRect = new(
                    pillRect.x + 8f, pillRect.y,
                    pillRect.width - XBtnSize - XPad, pillRect.height);
                pillLabelStyle.normal.textColor = textColor;
                GUI.Label(labelRect, label2, pillLabelStyle);

                // ✕ button
                Rect xRect = new(
                    pillRect.xMax - XBtnSize - 4f,
                    pillRect.y + (pillRect.height - XBtnSize) * 0.5f,
                    XBtnSize, XBtnSize);
                xButtonStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.65f);
                if (GUI.Button(xRect, "✕", xButtonStyle))
                    removeIndex = i;

                // Double-click to select asset
                if (Event.current.type == EventType.MouseDown &&
                    Event.current.clickCount == 2 &&
                    pillRect.Contains(Event.current.mousePosition))
                {
                    EditorGUIUtility.PingObject((UnityEngine.Object)tag);
                    Selection.activeObject = tag;
                    Event.current.Use();
                }

                x += pillW + PillMarginX;
            }

            // "+" button — inline after last pill
            Rect addRect = new(x, y, addButtonStyle.fixedWidth, PillHeight);
            // MWC — no longer disabled when every tag is already used: the popup can create new ones
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
                GUI.contentColor = Color.white;

                if (GUI.Button(addRect, "+", addButtonStyle))
                {
                    //MWC: use the searchable IDPickerPopup (same as the ID fields) instead of a flat GenericMenu
                    var available = cachedAvailableTags.ConvertAll<IDs>(t => t);
                    var capturedSO = property.serializedObject;
                    var targetObj = capturedSO.targetObject;
                    var tagsPath = tagsProp.propertyPath;

                    PopupWindow.Show(addRect, new IDPickerPopup(available, null, chosen =>
                    {
                        if (chosen is not Tag captured) return; //Ignore the [None] option

                        Undo.RecordObject(capturedSO.targetObject, "Add Tag");
                        capturedSO.Update();
                        tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                        tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).objectReferenceValue = captured;
                        capturedSO.ApplyModifiedProperties();
                    },
                    // MWC — create a new Tag asset and append it to the list
                    () =>
                    {
                        var newTag = IDPickerPopup.CreateIDAsset(typeof(Tag));
                        if (newTag == null || targetObj == null) return;

                        var so = new SerializedObject(targetObj);
                        var tags = so.FindProperty(tagsPath);
                        if (tags == null) return;

                        tags.InsertArrayElementAtIndex(tags.arraySize); // ApplyModifiedProperties registers its own Undo
                        tags.GetArrayElementAtIndex(tags.arraySize - 1).objectReferenceValue = newTag;
                        so.ApplyModifiedProperties();
                    },
                    "Create New Tag"));
                }

                GUI.backgroundColor = prevBg;
                GUI.contentColor = Color.white;
            }

            // Apply removal
            if (removeIndex >= 0)
            {
                Undo.RecordObject(property.serializedObject.targetObject, "Remove Tag");
                tagsProp.DeleteArrayElementAtIndex(removeIndex);
                property.serializedObject.ApplyModifiedProperties();
            }
        }

        // ── Texture helpers ────────────────────────────────────────────
        private static Texture2D GetPillTexture(Color color)
        {
            if (!PillTextureCache.TryGetValue(color, out Texture2D tex) || tex == null)
            {
                tex = BuildPillTexture(10, (int)PillHeight, color);
                PillTextureCache[color] = tex;
            }
            return tex;
        }

        private static Texture2D BuildPillTexture(int cornerRadius, int height, Color color)
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
                        float dist = Vector2.Distance(new Vector2(cx, cy),
                                                       new Vector2(cornerRadius - 1, cornerRadius - 1));
                        float alpha = Mathf.Clamp01(cornerRadius - dist);
                        tex.SetPixel(px, py, new Color(color.r, color.g, color.b, color.a * alpha));
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