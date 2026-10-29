// Last Change: 9/17/2026 6:05pm
using System.Collections.Generic;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/scriptables/tags")]
    [AddComponentMenu("Malbers/Utilities/Tools/Tags"), DefaultExecutionOrder(-500)]
    public class Tags : MonoBehaviour
    {
        /// <summary>Every enabled Tags component in the scene. Only for enumerating holders; to look up objects by Tag use <see cref="Tag.gameObjects"/> instead (O(1) per tag)</summary>
        public static List<Tags> TagsHolders;

        /// <summary>List of tags for this component</summary>
        public List<Tag> tags = new();

        /// <summary>Tag IDs of this component as a Hash Set, so lookups are O(1) and duplicates collapse</summary>
        private readonly HashSet<int> HashTag = new();

        /// <summary>Registers this holder and adds the GameObject to each Tag's runtime set</summary>
        private void OnEnable()
        {
            TagsHolders ??= new List<Tags>();
            TagsHolders.Add(this);
            foreach (var tag in tags)
                if (tag) tag.Add(gameObject);
        }

        /// <summary>Unregisters this holder and removes the GameObject from each Tag's runtime set</summary>
        private void OnDisable()
        {
            TagsHolders?.Remove(this);
            foreach (var tag in tags)
                if (tag) tag.Remove(gameObject);
        }

        /// <summary>Builds the ID Hash Set from the serialized tag list</summary>
        public void Awake()
        {
            HashTag.Clear();
            foreach (var tag in tags)
                if (tag) HashTag.Add(tag.ID);
        }

        /// <summary>True if this component holds the Tag</summary>
        public bool HasTag(Tag tag) => tag != null && HashTag.Contains(tag.ID);

        /// <summary>True if this component holds a Tag with the given ID</summary>
        public bool HasTag(int key) => HashTag.Contains(key);

        /// <summary>True if this component holds ANY of the Tags</summary>
        public bool HasTag(List<Tag> Tag)
        {
            if (Tag == null) return false;
            foreach (var tag in Tag)
                if (tag != null && HashTag.Contains(tag.ID)) return true;
            return false;
        }

        /// <summary>True if this component holds ALL of the Tags</summary>
        public bool HasAllTag(List<Tag> Tag)
        {
            if (Tag == null) return false;
            foreach (var tag in Tag)
                if (tag == null || !HashTag.Contains(tag.ID)) return false;
            return true;
        }

        /// <summary>True if this component holds ANY of the Tags</summary>
        public bool HasTag(params Tag[] enteringTags)
        {
            if (enteringTags == null) return false;
            foreach (var tag in enteringTags)
                if (tag != null && HashTag.Contains(tag.ID)) return true;
            return false;
        }

        /// <summary>True if this component holds ALL of the Tags</summary>
        public bool HasAllTags(params Tag[] enteringTags)
        {
            if (enteringTags == null) return false;
            foreach (var tag in enteringTags)
                if (tag == null || !HashTag.Contains(tag.ID)) return false;
            return true;
        }

        /// <summary>True if this component holds ANY of the Tag IDs</summary>
        public bool HasTag(params int[] enteringTags)
        {
            if (enteringTags == null) return false;
            foreach (var tag in enteringTags)
                if (HashTag.Contains(tag)) return true;
            return false;
        }

        /// <summary>True if this component holds ALL of the Tag IDs</summary>
        public bool HasAllTags(params int[] enteringTags)
        {
            if (enteringTags == null) return false;
            foreach (var tag in enteringTags)
                if (!HashTag.Contains(tag)) return false;
            return true;
        }

        /// <summary>Adds a Tag at runtime and registers the GameObject on it</summary>
        public void AddTag(Tag t)
        {
            if (t == null || HashTag.Contains(t.ID)) return;
            tags.Add(t);
            HashTag.Add(t.ID);
            if (isActiveAndEnabled) t.Add(gameObject);
        }

        /// <summary>Removes a Tag at runtime and unregisters the GameObject from it</summary>
        public void RemoveTag(Tag t)
        {
            if (t == null || !HashTag.Contains(t.ID)) return;
            tags.Remove(t);
            HashTag.Remove(t.ID);
            t.Remove(gameObject);
        }

        #region STATIC METHODS Return GameObjects by Tag

        /// <summary>First GameObject tagged with the Tag, or null</summary>
        public static GameObject GameObjectbyTagFirst(Tag tag) => tag != null ? tag.FindFirst() : null;

        /// <summary>All GameObjects holding a Tag with the given ID, or null when there are none. Scans every holder; prefer the Tag overload</summary>
        public static List<GameObject> GameObjectbyTag(int tag)
        {
            if (TagsHolders == null || TagsHolders.Count == 0) return null;
            var go = new List<GameObject>();
            foreach (var item in TagsHolders)
                if (item.HasTag(tag)) go.Add(item.gameObject);
            return go.Count == 0 ? null : go;
        }

        /// <summary>All GameObjects tagged with the Tag (a copy of the runtime set)</summary>
        public static List<GameObject> GameObjectbyTag(Tag tag) => tag != null ? new List<GameObject>(tag.gameObjects) : new List<GameObject>();

        /// <summary>Union of the GameObjects tagged with ANY of the Tags, without duplicates</summary>
        public static List<GameObject> GameObjectbyTag(Tag[] tags) => UnionByTag(tags);

        /// <summary>Union of the GameObjects tagged with ANY of the Tags, without duplicates</summary>
        public static List<GameObject> GameObjectbyTag(List<Tag> tags) => UnionByTag(tags);

        /// <summary>Union of the GameObjects tagged with ANY of the Tags, without duplicates</summary>
        public static List<GameObject> GameObjectbyTag(MTags tags) => UnionByTag(tags?.tags);

        /// <summary>Merges the runtime sets of every non-null Tag into one list</summary>
        private static List<GameObject> UnionByTag(IEnumerable<Tag> tags)
        {
            var set = new HashSet<GameObject>();
            if (tags != null)
                foreach (var tag in tags)
                    if (tag != null) set.UnionWith(tag.gameObjects);
            return new List<GameObject>(set);
        }

        #endregion


#if UNITY_EDITOR
        [ContextMenu("Set Player Tag")]
        private void SetPlayerTag()
        {
            tags ??= new();

            var playerTag = MTools.GetInstance<Tag>("Player");

            if (playerTag == null) return;

            if (!tags.Contains(playerTag))
                tags.Add(playerTag);
            else
                Debug.LogWarning("The Player Tag is already added to this GameObject", this);
        }
#endif
    }

    /// <summary>
    /// Malbers Tag queries. Three scopes, one meaning each:
    /// <b>HasMalbersTag</b> checks the GameObject itself (O(1) per tag via <see cref="Tag.gameObjects"/>);
    /// <b>HasMalbersTagInParent</b> checks the closest enabled <see cref="Tags"/> holder up the hierarchy (itself included);
    /// <b>HasMalbersTagInChildren</b> / <b>FindWithMalbersTag</b> check the GameObject and every descendant.
    /// </summary>
    public static class Tag_Transform_Extension
    {
        #region Self (this GameObject only)

        /// <summary>True if this GameObject has an enabled Tags component holding the Tag</summary>
        public static bool HasMalbersTag(this GameObject go, Tag tag) => go != null && tag != null && tag.Contains(go);

        /// <summary>True if this GameObject has an enabled Tags component holding ANY of the Tags</summary>
        public static bool HasMalbersTag(this GameObject go, List<Tag> tags)
        {
            if (go == null || tags == null) return false;
            foreach (var tag in tags)
                if (tag != null && tag.Contains(go)) return true;
            return false;
        }

        /// <summary>True if this GameObject has an enabled Tags component holding ANY of the Tags</summary>
        public static bool HasMalbersTag(this GameObject go, params Tag[] tags)
        {
            if (go == null || tags == null) return false;
            foreach (var tag in tags)
                if (tag != null && tag.Contains(go)) return true;
            return false;
        }

        /// <summary>True if this GameObject has an enabled Tags component holding ALL of the Tags</summary>
        public static bool HasAllMalbersTag(this GameObject go, List<Tag> tags)
        {
            if (go == null || tags == null) return false;
            foreach (var tag in tags)
                if (tag == null || !tag.Contains(go)) return false;
            return true;
        }

        /// <summary>True if this GameObject has an enabled Tags component holding ALL of the Tags</summary>
        public static bool HasAllMalbersTag(this GameObject go, params Tag[] tags)
        {
            if (go == null || tags == null) return false;
            foreach (var tag in tags)
                if (tag == null || !tag.Contains(go)) return false;
            return true;
        }

        /// <summary>True if this Transform's GameObject holds the Tag</summary>
        public static bool HasMalbersTag(this Transform t, Tag tag) => t != null && HasMalbersTag(t.gameObject, tag);
        /// <summary>True if this Transform's GameObject holds ANY of the Tags</summary>
        public static bool HasMalbersTag(this Transform t, List<Tag> tags) => t != null && HasMalbersTag(t.gameObject, tags);
        /// <summary>True if this Transform's GameObject holds ANY of the Tags</summary>
        public static bool HasMalbersTag(this Transform t, params Tag[] tags) => t != null && HasMalbersTag(t.gameObject, tags);
        /// <summary>True if this Component's GameObject holds the Tag</summary>
        public static bool HasMalbersTag(this Component c, Tag tag) => c != null && HasMalbersTag(c.gameObject, tag);
        /// <summary>True if this Component's GameObject holds ANY of the Tags</summary>
        public static bool HasMalbersTag(this Component c, List<Tag> tags) => c != null && HasMalbersTag(c.gameObject, tags);
        /// <summary>True if this Component's GameObject holds ANY of the Tags</summary>
        public static bool HasMalbersTag(this Component c, params Tag[] tags) => c != null && HasMalbersTag(c.gameObject, tags);

        #endregion

        #region In Parent (closest Tags holder up the hierarchy)

        /// <summary>Closest enabled Tags component on this GameObject or its parents, or null</summary>
        private static Tags GetTagInParent(GameObject go) => go != null ? go.GetComponentInParent<Tags>(false) : null;

        /// <summary>True if the closest Tags holder up the hierarchy holds the Tag</summary>
        public static bool HasMalbersTagInParent(this GameObject go, Tag tag) { var c = GetTagInParent(go); return c != null && c.HasTag(tag); }
        /// <summary>True if the closest Tags holder up the hierarchy holds ANY of the Tags</summary>
        public static bool HasMalbersTagInParent(this GameObject go, List<Tag> tags) { var c = GetTagInParent(go); return c != null && c.HasTag(tags); }
        /// <summary>True if the closest Tags holder up the hierarchy holds ANY of the Tags</summary>
        public static bool HasMalbersTagInParent(this GameObject go, params Tag[] tags) { var c = GetTagInParent(go); return c != null && c.HasTag(tags); }
        /// <summary>True if the closest Tags holder up the hierarchy holds ALL of the Tags</summary>
        public static bool HasAllMalbersTagInParent(this GameObject go, List<Tag> tags) { var c = GetTagInParent(go); return c != null && c.HasAllTag(tags); }
        /// <summary>True if the closest Tags holder up the hierarchy holds ALL of the Tags</summary>
        public static bool HasAllMalbersTagInParent(this GameObject go, params Tag[] tags) { var c = GetTagInParent(go); return c != null && c.HasAllTags(tags); }

        /// <summary>True if the closest Tags holder up the hierarchy holds the Tag</summary>
        public static bool HasMalbersTagInParent(this Transform t, Tag tag) => t != null && HasMalbersTagInParent(t.gameObject, tag);
        /// <summary>True if the closest Tags holder up the hierarchy holds ANY of the Tags</summary>
        public static bool HasMalbersTagInParent(this Transform t, List<Tag> tags) => t != null && HasMalbersTagInParent(t.gameObject, tags);
        /// <summary>True if the closest Tags holder up the hierarchy holds ANY of the Tags</summary>
        public static bool HasMalbersTagInParent(this Transform t, params Tag[] tags) => t != null && HasMalbersTagInParent(t.gameObject, tags);
        /// <summary>True if the closest Tags holder up the hierarchy holds the Tag</summary>
        public static bool HasMalbersTagInParent(this Component c, Tag tag) => c != null && HasMalbersTagInParent(c.gameObject, tag);
        /// <summary>True if the closest Tags holder up the hierarchy holds ANY of the Tags</summary>
        public static bool HasMalbersTagInParent(this Component c, List<Tag> tags) => c != null && HasMalbersTagInParent(c.gameObject, tags);
        /// <summary>True if the closest Tags holder up the hierarchy holds ANY of the Tags</summary>
        public static bool HasMalbersTagInParent(this Component c, params Tag[] tags) => c != null && HasMalbersTagInParent(c.gameObject, tags);

        #endregion

        #region In Children (this GameObject or any descendant)

        /// <summary>True if this GameObject or any descendant holds the Tag</summary>
        public static bool HasMalbersTagInChildren(this GameObject go, Tag tag) => go != null && tag != null && tag.FindInHierarchy(go.transform) != null;

        public static bool HasMalbersTagInChildren(this Component c, List<Tag> tags) => c != null && HasMalbersTagInChildren(c.gameObject, tags);


        /// <summary>True if this GameObject or any descendant holds ANY of the Tags</summary>
        public static bool HasMalbersTagInChildren(this GameObject go, List<Tag> tags)
        {
            if (go == null || tags == null) return false;
            foreach (var tag in tags)
                if (tag != null && tag.FindInHierarchy(go.transform) != null) return true;
            return false;
        }

        /// <summary>True if every Tag is held by this GameObject or one of its descendants</summary>
        public static bool HasAllMalbersTagInChildren(this GameObject go, List<Tag> tags)
        {
            if (go == null || tags == null) return false;
            foreach (var tag in tags)
                if (tag == null || tag.FindInHierarchy(go.transform) == null) return false;
            return true;
        }

        /// <summary>True if this Transform's GameObject or any descendant holds the Tag</summary>
        public static bool HasMalbersTagInChildren(this Transform t, Tag tag) => t != null && HasMalbersTagInChildren(t.gameObject, tag);
        /// <summary>True if this Component's GameObject or any descendant holds the Tag</summary>
        public static bool HasMalbersTagInChildren(this Component c, Tag tag) => c != null && HasMalbersTagInChildren(c.gameObject, tag);

        /// <summary>First GameObject holding the Tag on this GameObject or its descendants, or null</summary>
        public static GameObject FindWithMalbersTag(this GameObject go, Tag tag) => go != null && tag != null ? tag.FindInHierarchy(go.transform) : null;

        /// <summary>First Transform holding the Tag on this Transform or its descendants, or null</summary>
        public static Transform FindWithMalbersTag(this Transform t, Tag tag) => t != null && tag != null ? tag.FindInHierarchyT(t) : null;

        #endregion
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(Tags)), CanEditMultipleObjects]
    public class TagsEd : Editor
    {
        private SerializedProperty tags;

        private GUIStyle pillStyle;
        private GUIStyle pillLabelStyle;
        private GUIStyle pillBgStyle;
        private GUIStyle xButtonStyle;
        private GUIStyle addButtonStyle;

        // Pill texture cache: avoids creating a new Texture2D every repaint
        private readonly Dictionary<Color, Texture2D> pillTextureCache = new();

        // Cached tag lists — rebuilt only on enable or when the assigned count changes
        private readonly List<Tag> cachedAllProjectTags = new();
        private readonly List<Tag> cachedAvailableTags = new();
        private int lastKnownTagCount = -1;

        protected virtual void OnEnable()
        {
            tags = serializedObject.FindProperty("tags");
            RebuildTagCache();
        }

        private void OnDisable()
        {
            // Clean up cached textures when inspector is closed
            foreach (var tex in pillTextureCache.Values)
                if (tex != null) DestroyImmediate(tex);
            pillTextureCache.Clear();
        }

        // ── Style initialisation ───────────────────────────────────────
        // Called every OnInspectorGUI but returns instantly once ready.
        // The null-check includes GUI.skin so it retries if skin wasn't
        // available yet, without permanently locking out initialization.
        private bool TryInitStyles()
        {
            // GUI.skin can be null on early frames — bail and retry next repaint
            if (GUI.skin == null) return false;

            // Each style is checked individually so a domain reload (which nulls them
            // all) forces a full rebuild even if the method previously succeeded.
            if (pillStyle != null &&
                pillLabelStyle != null &&
                pillBgStyle != null &&
                xButtonStyle != null &&
                addButtonStyle != null) return true;

            pillStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 3, 3),
                margin = new RectOffset(2, 2, 2, 2),
                fixedHeight = 20,
            };

            pillLabelStyle = new GUIStyle(pillStyle);

            // 9-slice style for pill backgrounds — only normal.background is swapped per pill
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

            Texture2D addBG = BuildPillTexture(10, 20, Color.white);
            addButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 0, 2),
                margin = new RectOffset(2, 2, 2, 2),
                fixedHeight = 20,
                fixedWidth = 22,
                border = new RectOffset(8, 8, 8, 8),
            };
            addButtonStyle.normal.background = addBG;
            addButtonStyle.hover.background = addBG;
            addButtonStyle.active.background = addBG;

            return true;
        }

        // ── Tag cache ──────────────────────────────────────────────────
        private void RebuildTagCache()
        {
            cachedAllProjectTags.Clear();
            string[] guids = AssetDatabase.FindAssets("t:Tag");
            foreach (string guid in guids)
            {
                Tag t = AssetDatabase.LoadAssetAtPath<Tag>(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null) cachedAllProjectTags.Add(t);
            }
            RebuildAvailableCache();
            lastKnownTagCount = tags.arraySize;
        }

        private void RebuildAvailableCache()
        {
            cachedAvailableTags.Clear();
            foreach (Tag t in cachedAllProjectTags)
            {
                bool found = false;
                for (int i = 0; i < tags.arraySize; i++)
                {
                    if (tags.GetArrayElementAtIndex(i).objectReferenceValue == t) { found = true; break; }
                }
                if (!found) cachedAvailableTags.Add(t);
            }
        }

        // ── Inspector GUI ──────────────────────────────────────────────
        public override void OnInspectorGUI()
        {
            // Bail silently until Unity's GUI skin is ready
            if (!TryInitStyles()) return;

            serializedObject.Update();

            // Rebuild available-tags cache only when the assigned count changes
            if (tags.arraySize != lastKnownTagCount)
            {
                RebuildAvailableCache();
                lastKnownTagCount = tags.arraySize;
            }

            float inspectorWidth = EditorGUIUtility.currentViewWidth - 26f;
            float x = 0f;
            int removeIndex = -1;
            float addBtnW = addButtonStyle.fixedWidth
                                 + addButtonStyle.margin.left
                                 + addButtonStyle.margin.right;

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < tags.arraySize; i++)
            {
                Tag tag = tags.GetArrayElementAtIndex(i).objectReferenceValue as Tag;
                if (tag == null) continue;

                string label = !string.IsNullOrEmpty(tag.DisplayName) ? tag.DisplayName : tag.name;
                float xBtnSize = 14f;
                float xPad = 6f;
                float pillW = pillStyle.CalcSize(new GUIContent(label)).x + xBtnSize + xPad + 4f;

                // Wrap row when pill + add button won't fit
                if (x + pillW > inspectorWidth - addBtnW && x > 0f)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    x = 0f;
                }

                // Reserve rect — no Button, so no hover state
                Rect pillRect = GUILayoutUtility.GetRect(
                    new GUIContent(label), pillStyle,
                    GUILayout.Width(pillW), GUILayout.Height(20f));

                float brightness = tag.Color.r * 0.299f + tag.Color.g * 0.587f + tag.Color.b * 0.114f;
                Color textColor = brightness > 0.70f ? Color.black : Color.white;

                // Draw pill background with 9-slice so rounded corners aren't stretched
                // Must only be called during Repaint — other events (e.g. MouseDown) will throw
                if (Event.current.type == EventType.Repaint)
                {
                    pillBgStyle.normal.background = GetPillTexture(tag.Color);
                    pillBgStyle.Draw(pillRect, GUIContent.none, false, false, false, false);
                }

                // Draw label
                Rect labelRect = new(
                    pillRect.x + 8f, pillRect.y,
                    pillRect.width - xBtnSize - xPad, pillRect.height);
                pillLabelStyle.normal.textColor = textColor;
                GUI.Label(labelRect, label, pillLabelStyle);

                // Double-click pill to select the Tag asset in the Project window
                if (Event.current.type == EventType.MouseDown &&
                    Event.current.clickCount == 2 &&
                    pillRect.Contains(Event.current.mousePosition))
                {
                    EditorGUIUtility.PingObject((Object)tag);
                    Selection.activeObject = tag;
                    Event.current.Use();
                }
                Rect xRect = new(
                    pillRect.xMax - xBtnSize - 4f,
                    pillRect.y + (pillRect.height - xBtnSize) * 0.5f,
                    xBtnSize, xBtnSize);
                xButtonStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.65f);
                if (GUI.Button(xRect, "✕", xButtonStyle))
                    removeIndex = i;

                x += pillW + 4f;
            }

            // "+" button — inline after last pill
            // MWC — always enabled now: even with no unassigned tags, the picker still
            // offers "Create New Tag…", so the button must never be disabled.
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
                GUI.contentColor = Color.white;

                // Pre-capture rect so PopupWindow.Show has a valid anchor position
                Rect addBtnRect = GUILayoutUtility.GetRect(
                    new GUIContent("+"), addButtonStyle,
                    GUILayout.Width(addButtonStyle.fixedWidth),
                    GUILayout.Height(addButtonStyle.fixedHeight));

                if (GUI.Button(addBtnRect, "+", addButtonStyle))
                {
                    var tagInstances = new List<IDs>(cachedAvailableTags.Count);
                    foreach (var t in cachedAvailableTags) tagInstances.Add(t);

                    // MWC — added onCreateNew so the popup can spawn a new Tag asset
                    PopupWindow.Show(addBtnRect, new IDPickerPopup(tagInstances, null,
                        chosen => AddTagToList(chosen as Tag),
                        onCreateNew: CreateAndAddNewTag,
                        createLabel: "Create New Tag…"));
                }

                GUI.backgroundColor = prevBg;
                GUI.contentColor = Color.white;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            if (removeIndex >= 0)
            {
                Undo.RecordObject(target, "Remove Tag");
                tags.DeleteArrayElementAtIndex(removeIndex);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ── Tag creation / assignment ──────────────────────────────────
        // MWC — default folder for newly created Tag assets (per project convention)
        private const string NewTagFolder =
            "Assets/Malbers Animations/Common/Scriptable Assets/Tags";

        /// <summary>Adds an existing Tag asset to the component's list (skips nulls/dupes).</summary>
        private void AddTagToList(Tag tag)
        {
            if (tag == null) return;

            serializedObject.Update();

            // Skip if it is already assigned
            for (int i = 0; i < tags.arraySize; i++)
                if (tags.GetArrayElementAtIndex(i).objectReferenceValue == tag) return;

            Undo.RecordObject(target, "Add Tag");
            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).objectReferenceValue = tag;
            serializedObject.ApplyModifiedProperties();

            RebuildTagCache();
        }

        /// <summary>Creates a new Tag asset in the default folder, adds it to the
        /// list and pings it in the Project window so it can be renamed.</summary>
        private void CreateAndAddNewTag()
        {
            EnsureFolder(NewTagFolder);

            var newTag = CreateInstance<Tag>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{NewTagFolder}/New Tag.asset");
            AssetDatabase.CreateAsset(newTag, path);

            // Sync the ID/DisplayName with the asset's final (unique) name
            newTag.DisplayName = newTag.name;
            newTag.ID = newTag.name.GetHashCode();
            EditorUtility.SetDirty(newTag);
            AssetDatabase.SaveAssets();

            AddTagToList(newTag);

            EditorGUIUtility.PingObject((Object)newTag); // MWC — cast avoids ambiguity with Tag's implicit int operator
            Selection.activeObject = newTag;
        }

        /// <summary>Ensures a project folder path exists, creating parents as needed.</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);                       // "Assets" is always valid → recursion base
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ── Texture helpers ────────────────────────────────────────────

        /// <summary>Returns a cached pill texture for the given color, creating one if needed.</summary>
        private Texture2D GetPillTexture(Color color)
        {
            if (!pillTextureCache.TryGetValue(color, out Texture2D tex) || tex == null)
            {
                tex = BuildPillTexture(10, 20, color);
                pillTextureCache[color] = tex;
            }
            return tex;
        }

        /// <summary>Creates a rounded-rectangle Texture2D.</summary>
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