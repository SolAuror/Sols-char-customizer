// Last Change: 9/17/2026 6:05pm
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Reflection; // MWC — needed to invoke each subtype's GetID() from the shared "Find ID" button
#endif


namespace MalbersAnimations
{
    public abstract class IDs : ScriptableObject
    {
        [Tooltip("Display name on the ID Selection Context Button")]
        public string DisplayName;

        [Tooltip("Integer value to Identify IDs")]
        public int ID;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(IDs reference) => reference != null ? reference.ID : 0;

        [HideInInspector, Tooltip("Debug purpose only, not used for anything else")]
        public virtual Color IDColor => new(0.2f, 0.5f, 1f, 1f);

        /// <summary> Returns if an ID is inside a list (Include or Exclude) </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Included<T>(ICollection<T> list, bool include) where T : IDs
        {
            bool isIncluded = list.Contains(this);
            return include ? isIncluded : !isIncluded;
        }

        public bool Included(ICollection<IDs> list) => Included(list, true);
        public bool Excluded(ICollection<IDs> list) => Included(list, false);

        protected virtual void OnValidate()
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(DisplayName)) DisplayName = name;
#endif
        }

#if UNITY_EDITOR
        [ContextMenu("Get ID <Hash>")]
        private void GetIDHash()
        {
            ID = Animator.StringToHash(name);
            MTools.SetDirty(this);
        }


        [ContextMenu("Set Name")]
        public void SetName() => DisplayName = name; // MWC — made public so editors can call it from a button

        protected void FindID<T>() where T : IDs
        {
            int newID = 0;
            var allAdd = MTools.GetAllInstances<T>();
            bool Found = true;

            while (Found)
            {
                newID++;
                Found = allAdd.Exists(x => (x.ID == newID && x != this));
            }
            ID = newID;
            DisplayName = name;
            MTools.SetDirty(this);
        }
#endif
    }


#if UNITY_EDITOR

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    [CustomPropertyDrawer(typeof(IDs), true)]
    public class IDDrawer : PropertyDrawer
    {
        protected GUIStyle popupStyle;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            popupStyle ??= new(GUI.skin.GetStyle("PaneOptions"))
            {
                imagePosition = ImagePosition.ImageOnly
            };

            label = EditorGUI.BeginProperty(position, label, property);

            if (property.objectReferenceValue)
                label.tooltip += $"\n ID Value: [{(property.objectReferenceValue as IDs).ID}]";

            if (label.text.Contains("Element"))
            {
                position.x += 12;
                position.width -= 12;
            }
            else
                position = EditorGUI.PrefixLabel(position, label);

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            Rect buttonRect = new(position);
            buttonRect.yMin += popupStyle.margin.top;
            buttonRect.width = popupStyle.fixedWidth + popupStyle.margin.right;
            buttonRect.x -= 20;
            buttonRect.height = EditorGUIUtility.singleLineHeight;

            if (EditorGUI.DropdownButton(buttonRect, GUIContent.none, FocusType.Passive, popupStyle))
            {
                var nameOfType = GetPropertyType(property);
                string[] guids = AssetDatabase.FindAssets("t:" + nameOfType);

                var instances = new List<IDs>();
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var inst = AssetDatabase.LoadAssetAtPath<IDs>(path);
                    if (inst != null) instances.Add(inst);
                }
                instances = instances.OrderBy(x => x.ID).ToList();

                var capturedProp = property;
                var capturedSO = property.serializedObject;
                var current = property.objectReferenceValue as IDs;

                // MWC — data needed to re-resolve the property after the (deferred) create flow
                var targetObj = capturedSO.targetObject;
                var propPath = property.propertyPath;
                var idType = IDPickerPopup.ResolveIDType(nameOfType);

                Action onCreateNew = null;

                if (idType != null)
                {
                    onCreateNew = () =>
                    {
                        var newID = IDPickerPopup.CreateIDAsset(idType);
                        if (newID == null || targetObj == null) return;

                        var so = new SerializedObject(targetObj);
                        var prop = so.FindProperty(propPath);
                        if (prop == null) return;

                        prop.objectReferenceValue = newID;
                        so.ApplyModifiedProperties();
                    };
                }

                PopupWindow.Show(buttonRect, new IDPickerPopup(instances, current, chosen =>
                {
                    capturedSO.Update();
                    capturedProp.objectReferenceValue = chosen;
                    capturedSO.ApplyModifiedProperties();
                },
                onCreateNew, $"Create New {ObjectNames.NicifyVariableName(nameOfType)}"));
            }

            position.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(position, property, GUIContent.none, false);
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        protected static string GetPropertyType(SerializedProperty property)
        {
            var type = property.type;
            var match = System.Text.RegularExpressions.Regex.Match(type, @"PPtr<\$(.*?)>");
            if (match.Success) type = match.Groups[1].Value;
            return type;
        }
    }


    // ─── IDs Base Editor ─────────────────────────────────────────────────────────
    //
    // MWC — Generic editor applied to every IDs subtype (editorForChildClasses: true).
    // Draws the default inspector plus "Set Name" and "Find ID" buttons. Types with
    // their own custom editor (e.g. Tag) are not covered here, so they call
    // DrawSetNameButton() / DrawFindIDButton().

    [CustomEditor(typeof(IDs), true), CanEditMultipleObjects]
    public class IDsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            using (new GUILayout.HorizontalScope()) // MWC — Set Name and Find ID side by side
            {
                DrawSetNameButton(targets);
                DrawFindIDButton(targets);
            }
        }

        /// <summary>Draws a button that sets each target's DisplayName to its asset file name.</summary>
        public static void DrawSetNameButton(UnityEngine.Object[] targets)
        {
            if (GUILayout.Button(new GUIContent("Set Name", "Set the Display Name to match the asset's file name")))
                SetNames(targets);
        }

        /// <summary>Sets each target's DisplayName to its asset file name (with Undo + dirty).</summary>
        public static void SetNames(UnityEngine.Object[] targets)
        {
            foreach (var t in targets)
            {
                if (t is IDs id)
                {
                    Undo.RecordObject(id, "Set Name");
                    id.SetName();
                    EditorUtility.SetDirty(id);
                }
            }
        }

        /// <summary>Draws a button that finds the next free ID value for each target via its GetID() method.</summary>
        public static void DrawFindIDButton(UnityEngine.Object[] targets)
        {
            if (GUILayout.Button(new GUIContent("Find ID", "Find the next available unique ID value")))
                FindIDs(targets);
        }

        /// <summary>Invokes each target's own GetID() (subtype wrapper around IDs.FindID&lt;T&gt;()) with Undo + dirty.</summary>
        public static void FindIDs(UnityEngine.Object[] targets)
        {
            foreach (var t in targets)
            {
                if (t is IDs id)
                {
                    var getID = id.GetType().GetMethod("GetID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (getID != null)
                    {
                        Undo.RecordObject(id, "Find ID");
                        getID.Invoke(id, null);
                        EditorUtility.SetDirty(id);
                    }
                }
            }
        }
    }


    // ─── ID Picker Popup ─────────────────────────────────────────────────────────
    //
    //  Browse mode  →  Root shows child groups first (with › chevron), then
    //                  ungrouped entries.  Clicking a group drills in.
    //                  Header shows back-button (‹) + breadcrumb path.
    //  Search mode  →  Flat filtered list; group path shown as mini-separator
    //                  whenever it changes, matching SerializableType pattern.
    //  Keyboard     →  Up/Down navigate, Enter selects/drills, Escape goes back.

    public class IDPickerPopup : PopupWindowContent
    {
        // ─── Layout ──────────────────────────────────────────────────────────────

        private const float WIN_W = 280f;
        private const float WIN_H = 340f;
        private const float HEADER_H = 26f;
        private const float SEARCH_H = 22f;
        private const float ROW_H = 24f;
        private const float GROUP_H = 26f;   // drill segment row height
        private const float COUNT_H = 16f;
        private const float SEP_H = 18f;   // flat-list group separator height

        private static readonly Color C_HEADER_BG = new(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color C_ROW_SEL = new(0.18f, 0.37f, 0.73f, 0.55f);
        private static readonly Color C_ROW_HOVER = new(1f, 1f, 1f, 0.07f);
        private static readonly Color C_BACK_HOVER = new(1f, 1f, 1f, 0.09f);
        private static readonly Color C_CHEVRON = new(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Color C_ID_TEXT = new(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color C_GROUP_TEXT = new(0.70f, 0.85f, 1.00f, 1f);
        private static readonly Color C_SEP_BG = new(0f, 0f, 0f, 1f);

        // ─── Entry ───────────────────────────────────────────────────────────────

        private readonly struct IDEntry
        {
            /// <summary> Full group path, e.g. "Animals/Mammals" </summary>
            public readonly string Group;
            /// <summary> Leaf display label, e.g. "Dog" </summary>
            public readonly string Label;
            /// <summary> "[3] " prefix – empty for Tag subtypes </summary>
            public readonly string IDStr;
            public readonly IDs Asset;

            public IDEntry(IDs asset, string group, string label, string idStr)
            { Asset = asset; Group = group; Label = label; IDStr = idStr; }
        }

        // ─── State ───────────────────────────────────────────────────────────────

        private readonly List<IDEntry> _all;
        private readonly IDs _current;
        private readonly Action<IDs> _onSelected;

        // MWC — optional "create new" action shown as a row at the top of the list
        private readonly Action _onCreateNew;
        private readonly string _createLabel;

        private readonly List<string> _path = new();   // drill-down breadcrumb

        private string _search = string.Empty;
        private List<IDEntry> _filtered;
        private int _hoveredIndex = -1;       // -1 = None row
        private Vector2 _scroll;

        // ─── Styles ──────────────────────────────────────────────────────────────

        private GUIStyle _sRow, _sRowSel, _sRowID, _sGroupRow,
                         _sChevron, _sHeader, _sBack, _sCount, _sGrpSep, _sCreate;

        // ─── Constructor ─────────────────────────────────────────────────────────

        // MWC — added optional onCreateNew/createLabel so a picker can offer to create
        // a brand new asset in-place. When onCreateNew is null the create row is hidden,
        // keeping every existing call site unchanged.
        public IDPickerPopup(List<IDs> instances, IDs current, Action<IDs> onSelected,
            Action onCreateNew = null, string createLabel = "Create New…")
        {
            _current = current;
            _onSelected = onSelected;
            _onCreateNew = onCreateNew;
            _createLabel = createLabel;
            _all = BuildEntries(instances);
            _filtered = _all;
        }

        // ─── PopupWindowContent ───────────────────────────────────────────────────

        public override Vector2 GetWindowSize() => new(WIN_W, WIN_H);

        public override void OnGUI(Rect rect)
        {
            InitStyles();
            DrawHeader();
            DrawSearchBar();
            DrawCreateRow(); // MWC — pinned above the list so it is always visible
            HandleKeyboard();

            if (!string.IsNullOrWhiteSpace(_search))
                DrawFlatList();
            else
                DrawDrillLevel();
        }

        // ─── Header ──────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            var headerRect = EditorGUILayout.GetControlRect(GUILayout.Height(HEADER_H));
            EditorGUI.DrawRect(headerRect, C_HEADER_BG);

            if (_path.Count > 0)
            {
                var backRect = new Rect(headerRect.x + 4f, headerRect.y + 3f, 22f, headerRect.height - 6f);
                var ev = Event.current;
                bool backHov = backRect.Contains(ev.mousePosition);

                if (backHov) EditorGUI.DrawRect(backRect, C_BACK_HOVER);
                EditorGUI.LabelField(backRect, "‹", _sBack);

                if (ev.type == EventType.MouseDown && backHov)
                {
                    _path.RemoveAt(_path.Count - 1);
                    _hoveredIndex = -1;
                    _scroll = Vector2.zero;
                    editorWindow.Repaint();
                    ev.Use();
                }

                var titleRect = new Rect(headerRect.x + 28f, headerRect.y, headerRect.width - 32f, headerRect.height);
                EditorGUI.LabelField(titleRect, string.Join("  ›  ", _path), _sHeader);
            }
            else
            {
                var titleRect = new Rect(headerRect.x + 8f, headerRect.y, headerRect.width - 8f, headerRect.height);
                EditorGUI.LabelField(titleRect, "Select ID", _sHeader);
            }
        }

        // ─── Search Bar ──────────────────────────────────────────────────────────

        private void DrawSearchBar()
        {
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("IDPickerSearch");
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(SEARCH_H));
            _search = EditorGUI.TextField(r, _search, EditorStyles.toolbarSearchField);

            if (EditorGUI.EndChangeCheck())
            {
                _hoveredIndex = -1;
                _scroll = Vector2.zero;
                RebuildFilter();
            }

            EditorGUI.FocusTextInControl("IDPickerSearch");
        }

        // ─── Drill-down level ─────────────────────────────────────────────────────

        private void DrawDrillLevel()
        {
            string prefix = CurrentPrefix();
            List<string> childSegs = GetChildSegments(prefix);
            List<IDEntry> localEntries = GetEntriesAt(prefix);

            EditorGUILayout.LabelField(
                $"{childSegs.Count} group{(childSegs.Count != 1 ? "s" : "")}  ·  " +
                $"{localEntries.Count} entr{(localEntries.Count != 1 ? "ies" : "y")}",
                _sCount, GUILayout.Height(COUNT_H));

            float listH = WIN_H - HEADER_H - SEARCH_H - COUNT_H - CreateRowH - 4f; // MWC — account for the pinned create row
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listH));

            // ── None row ─────────────────────────────────────────────────────────
            DrawNoneRow();

            // ── Child group segments – always first ───────────────────────────────
            for (int i = 0; i < childSegs.Count; i++)
            {
                string seg = childSegs[i];
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(GROUP_H));
                bool isHov = i == _hoveredIndex;

                if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                var labelRect = new Rect(rowRect.x + 10f, rowRect.y, rowRect.width - 28f, rowRect.height);
                var chevRect = new Rect(rowRect.xMax - 18f, rowRect.y + 4f, 14f, rowRect.height - 4f);
                EditorGUI.LabelField(labelRect, seg, _sGroupRow);
                EditorGUI.LabelField(chevRect, "›", _sChevron);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = i; editorWindow.Repaint(); }

                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                {
                    _path.Add(seg);
                    _hoveredIndex = -1;
                    _scroll = Vector2.zero;
                    editorWindow.Repaint();
                    ev.Use();
                }
            }

            // ── Entries living directly at this level ─────────────────────────────
            for (int i = 0; i < localEntries.Count; i++)
            {
                var entry = localEntries[i];
                int flatIdx = childSegs.Count + i;
                bool isCur = entry.Asset == _current;
                bool isHov = flatIdx == _hoveredIndex;
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));

                if (isCur) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                DrawEntryLabel(rowRect, entry, isCur);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIdx; editorWindow.Repaint(); }

                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                { SelectEntry(entry.Asset); ev.Use(); }
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Flat search list ─────────────────────────────────────────────────────

        private void DrawFlatList()
        {
            EditorGUILayout.LabelField(
                $"{_filtered.Count} result{(_filtered.Count != 1 ? "s" : "")}",
                _sCount, GUILayout.Height(COUNT_H));

            float listH = WIN_H - HEADER_H - SEARCH_H - COUNT_H - CreateRowH - 4f; // MWC — account for the pinned create row
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listH));

            DrawNoneRow();

            string lastGroup = null;
            int flatIdx = 0;

            foreach (var entry in _filtered)
            {
                // Group separator on change
                if (entry.Group != lastGroup)
                {
                    lastGroup = entry.Group;
                    if (!string.IsNullOrEmpty(lastGroup))
                    {
                        var sep = EditorGUILayout.GetControlRect(GUILayout.Height(SEP_H));
                        EditorGUI.DrawRect(sep, C_SEP_BG);
                        EditorGUI.LabelField(
                            new Rect(sep.x + 6f, sep.y, sep.width - 6f, sep.height),
                            lastGroup, _sGrpSep);
                    }
                }

                bool isCur = entry.Asset == _current;
                bool isHov = flatIdx == _hoveredIndex;
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));

                if (isCur) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                DrawEntryLabel(rowRect, entry, isCur);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIdx; editorWindow.Repaint(); }

                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                { SelectEntry(entry.Asset); ev.Use(); }

                flatIdx++;
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Shared row helpers ───────────────────────────────────────────────────

        /// <summary>Height reserved by the pinned create row (0 when no create callback).</summary>
        private float CreateRowH => _onCreateNew != null ? ROW_H + 3f : 0f;

        // MWC — pinned action row (drawn above the scroll view) that invokes the create callback
        private void DrawCreateRow()
        {
            if (_onCreateNew == null) return;

            var r = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));
            var ev = Event.current;
            bool hov = r.Contains(ev.mousePosition);

            if (hov) EditorGUI.DrawRect(r, C_ROW_HOVER);

            EditorGUI.LabelField(new Rect(r.x + 4f, r.y, r.width - 8f, r.height),
                "＋  " + _createLabel, _sCreate);

            EditorGUI.DrawRect(new Rect(r.x, r.yMax + 1f, r.width, 1f), C_SEP_BG); // MWC — divider from the list below

            if (ev.type == EventType.MouseMove && hov) editorWindow.Repaint();

            if (ev.type == EventType.MouseDown && hov)
            {
                // MWC — close first, then run the callback deferred: it opens a modal save
                // panel, which would otherwise fight the popup for focus.
                var callback = _onCreateNew;
                editorWindow.Close();
                EditorApplication.delayCall += () => callback.Invoke();
                ev.Use();
            }
        }

        // ─── Asset creation helper ────────────────────────────────────────────────

        /// <summary>
        /// Creates a brand-new ID ScriptableObject asset of <paramref name="idType"/>,
        /// asking the user where to save it. The new asset gets its DisplayName set from the
        /// file name and the next free ID value. Returns null if the user cancels.
        /// </summary>
        public static IDs CreateIDAsset(Type idType)
        {
            if (idType == null || idType.IsAbstract || !typeof(IDs).IsAssignableFrom(idType))
            {
                Debug.LogWarning($"Cannot create a new ID asset: invalid type [{idType}]");
                return null;
            }

            string nice = ObjectNames.NicifyVariableName(idType.Name);
            string path = EditorUtility.SaveFilePanelInProject(
                $"Create New {nice}", $"New {idType.Name}", "asset",
                $"Choose where to save the new {nice} asset");

            if (string.IsNullOrEmpty(path)) return null;

            var asset = ScriptableObject.CreateInstance(idType) as IDs;
            if (asset == null) return null;

            AssetDatabase.CreateAsset(asset, path);
            asset.DisplayName = asset.name;

            // MWC — Tags derive their ID from the asset name (see Tag.OnEnable), which was
            // still empty when the instance was created. Everything else gets the next free ID.
            if (asset is Tag) asset.ID = asset.name.GetHashCode();
            else IDsEditor.FindIDs(new UnityEngine.Object[] { asset });

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject((UnityEngine.Object)asset); // MWC — cast avoids ambiguity with the implicit int operator
            return asset;
        }

        /// <summary>Resolves a concrete IDs subclass from its type name (used by the drawers).</summary>
        public static Type ResolveIDType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            foreach (var t in TypeCache.GetTypesDerivedFrom<IDs>())
                if (!t.IsAbstract && t.Name == typeName) return t;

            return null;
        }

        private void DrawNoneRow()
        {
            bool isCur = _current == null;
            bool isHov = _hoveredIndex == -1;
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));

            if (isCur) EditorGUI.DrawRect(r, C_ROW_SEL);
            else if (isHov) EditorGUI.DrawRect(r, C_ROW_HOVER);

            EditorGUI.LabelField(new Rect(r.x + 4f, r.y, r.width - 8f, r.height),
                "None", isCur ? _sRowSel : _sRow);

            var ev = Event.current;
            if (ev.type == EventType.MouseMove && r.Contains(ev.mousePosition))
            { _hoveredIndex = -1; editorWindow.Repaint(); }

            if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
            { SelectEntry(null); ev.Use(); }
        }

        private void DrawEntryLabel(Rect rowRect, IDEntry entry, bool isCur)
        {
            var style = isCur ? _sRowSel : _sRow;

            float idW = _sRowID.CalcSize(new GUIContent(entry.IDStr)).x + 4f;
            var lblR = new Rect(rowRect.x + 4f, rowRect.y, rowRect.width - idW - 8f, rowRect.height);
            var idR = new Rect(rowRect.xMax - idW - 4f, rowRect.y, idW, rowRect.height);
            EditorGUI.LabelField(lblR, entry.Label, style);
            EditorGUI.LabelField(idR, entry.IDStr, _sRowID);
        }

        private void SelectEntry(IDs asset)
        {
            _onSelected?.Invoke(asset);
            editorWindow.Close();
        }

        // ─── Keyboard ────────────────────────────────────────────────────────────

        private void HandleKeyboard()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;

            bool inSearch = !string.IsNullOrWhiteSpace(_search);

            var childSegs = inSearch ? null : GetChildSegments(CurrentPrefix());
            var localEntries = inSearch ? _filtered : GetEntriesAt(CurrentPrefix());
            int totalRows = (childSegs?.Count ?? 0) + localEntries.Count;

            switch (ev.keyCode)
            {
                case KeyCode.DownArrow:
                    _hoveredIndex = Mathf.Min(_hoveredIndex + 1, totalRows - 1);
                    ev.Use(); editorWindow.Repaint(); break;

                case KeyCode.UpArrow:
                    _hoveredIndex = Mathf.Max(_hoveredIndex - 1, -1);
                    ev.Use(); editorWindow.Repaint(); break;

                case KeyCode.Return:
                    if (_hoveredIndex == -1)
                    {
                        SelectEntry(null);
                    }
                    else if (!inSearch && childSegs != null && _hoveredIndex < childSegs.Count)
                    {
                        // Drill into group
                        _path.Add(childSegs[_hoveredIndex]);
                        _hoveredIndex = -1;
                        _scroll = Vector2.zero;
                        editorWindow.Repaint();
                    }
                    else
                    {
                        int entryIdx = inSearch ? _hoveredIndex : _hoveredIndex - (childSegs?.Count ?? 0);
                        if (entryIdx >= 0 && entryIdx < localEntries.Count)
                            SelectEntry(localEntries[entryIdx].Asset);
                    }
                    ev.Use(); break;

                case KeyCode.Escape:
                    if (!inSearch && _path.Count > 0)
                    { _path.RemoveAt(_path.Count - 1); _hoveredIndex = -1; _scroll = Vector2.zero; editorWindow.Repaint(); }
                    else
                        editorWindow.Close();
                    ev.Use(); break;
            }
        }

        // ─── Data helpers ─────────────────────────────────────────────────────────

        private void RebuildFilter()
        {
            if (string.IsNullOrWhiteSpace(_search)) { _filtered = _all; return; }

            string q = _search.Trim();
            _filtered = _all.FindAll(e =>
                e.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Group.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.IDStr.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string CurrentPrefix() =>
            _path.Count == 0 ? string.Empty : string.Join("/", _path);

        /// <summary>
        /// Returns the immediate child group segments under <paramref name="prefix"/>,
        /// sorted alphabetically.
        /// </summary>
        private List<string> GetChildSegments(string prefix)
        {
            var segs = new List<string>(8);

            foreach (var e in _all)
            {
                if (string.IsNullOrEmpty(e.Group)) continue;

                string child;
                if (string.IsNullOrEmpty(prefix))
                {
                    int slash = e.Group.IndexOf('/');
                    child = slash < 0 ? e.Group : e.Group[..slash];
                }
                else
                {
                    if (!e.Group.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (e.Group.Length == prefix.Length) continue;  // lives here, not below
                    if (e.Group[prefix.Length] != '/') continue;

                    string rest = e.Group.Substring(prefix.Length + 1);
                    int slash = rest.IndexOf('/');
                    child = slash < 0 ? rest : rest.Substring(0, slash);
                }

                if (!segs.Contains(child)) segs.Add(child);
            }

            segs.Sort(StringComparer.OrdinalIgnoreCase);
            return segs;
        }

        /// <summary>
        /// Returns entries whose group path exactly matches <paramref name="prefix"/>,
        /// sorted alphabetically by label.
        /// </summary>
        private List<IDEntry> GetEntriesAt(string prefix)
        {
            var result = new List<IDEntry>(16);
            foreach (var e in _all)
                if (string.Equals(e.Group, prefix, StringComparison.Ordinal)) result.Add(e);
            result.Sort((a, b) => a.Asset.ID.CompareTo(b.Asset.ID)); // MWC — sort by ID value ascending instead of alphabetically
            return result;
        }

        private static List<IDEntry> BuildEntries(List<IDs> instances)
        {
            var list = new List<IDEntry>(instances.Count);

            foreach (var inst in instances)
            {
                if (inst == null) continue;

                string idStr = $"[{inst.ID}]";
                string displayName = !string.IsNullOrEmpty(inst.DisplayName) ? inst.DisplayName : inst.name;

                string group = string.Empty;
                string label = displayName;

                int slash = displayName.LastIndexOf('/');
                if (slash >= 0)
                {
                    group = displayName.Substring(0, slash);
                    label = displayName.Substring(slash + 1);
                }

                list.Add(new(inst, group, label, idStr));
            }

            return list;
        }

        // ─── Styles ───────────────────────────────────────────────────────────────

        private void InitStyles()
        {
            _sRow ??= new(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _sRowSel ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _sRowID ??= new(EditorStyles.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C_ID_TEXT }
            };
            _sGroupRow ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C_GROUP_TEXT }
            };
            _sChevron ??= new(EditorStyles.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_CHEVRON }
            };
            _sHeader ??= new(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };
            _sBack ??= new(EditorStyles.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _sCreate ??= new(EditorStyles.label) // MWC — accent style for the create-new row
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.55f, 0.85f, 0.55f) }
            };
            _sCount ??= new(EditorStyles.centeredGreyMiniLabel);
            _sGrpSep ??= new(EditorStyles.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C_GROUP_TEXT }
            };
        }
    }

#endif
}