// Last Change: 9/17/2026 5:33pm
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace MalbersAnimations
{
    [Serializable]
    public class SerializableType : ISerializationCallbackReceiver
    {
        [SerializeField] string assemblyQualifiedName = string.Empty;

        public Type Type { get; private set; }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            assemblyQualifiedName = Type?.AssemblyQualifiedName ?? assemblyQualifiedName;
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (!TryGetType(assemblyQualifiedName, out var type))
            {
                // Debug.LogWarning($"Type {assemblyQualifiedName} not found");
                return;
            }
            Type = type;
        }

        static bool TryGetType(string typeString, out Type type)
        {
            type = Type.GetType(typeString);
            return type != null || !string.IsNullOrEmpty(typeString);
        }

        // Implicit conversion from SerializableType to Type
        public static implicit operator Type(SerializableType sType) => sType.Type;

        // Implicit conversion from Type to SerializableType
        public static implicit operator SerializableType(Type type) => new() { Type = type };
    }

    public class TypeFilterAttribute : PropertyAttribute
    {
        public Func<Type, bool> Filter { get; }

        public TypeFilterAttribute(Type filterType)
        {
            Filter = type => !type.IsAbstract &&
                             !type.IsInterface &&
                             !type.IsGenericType &&
                             type.InheritsOrImplements(filterType);
        }
    }


#if UNITY_EDITOR

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    [CustomPropertyDrawer(typeof(SerializableType))]
    public class SerializableTypeDrawer : PropertyDrawer
    {
        static bool DefaultFilter(Type type)
            => !type.IsAbstract && !type.IsInterface && !type.IsGenericType;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var typeIdProperty = property.FindPropertyRelative("assemblyQualifiedName");

            // Label
            float labelWidth = EditorGUIUtility.labelWidth;
            var labelRect = new Rect(position.x, position.y, labelWidth, position.height);
            var buttonRect = new Rect(position.x + labelWidth, position.y, position.width - labelWidth, position.height);

            EditorGUI.LabelField(labelRect, label);

            // Resolve display name from the stored assembly-qualified name
            string currentAQN = typeIdProperty.stringValue;
            string displayName;

            if (string.IsNullOrEmpty(currentAQN))
            {
                displayName = "Select Type…";
            }
            else
            {
                var resolved = Type.GetType(currentAQN);
                displayName = resolved != null ? resolved.Name : currentAQN.Split(',')[0].Trim();
            }

            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(displayName), FocusType.Keyboard, EditorStyles.popup))
            {
                var typeFilter = (TypeFilterAttribute)Attribute.GetCustomAttribute(fieldInfo, typeof(TypeFilterAttribute));
                Func<Type, bool> filter = typeFilter != null ? typeFilter.Filter : DefaultFilter;

                var capturedProp = typeIdProperty;
                var capturedSO = property.serializedObject;

                PopupWindow.Show(buttonRect, new SerializableTypePickerPopup(currentAQN, filter, chosen =>
                {
                    capturedSO.Update();
                    capturedProp.stringValue = chosen.AssemblyQualifiedName ?? chosen.FullName ?? chosen.Name;
                    capturedSO.ApplyModifiedProperties();
                }));
            }
        }
    }

    // ─── Type Picker Popup (namespace drill-down) ────────────────────────────────
    //
    //  Root → UnityEngine → VFX → [Type list]
    //  Search bar always visible; typing bypasses drill-down and shows flat results.

    public class SerializableTypePickerPopup : PopupWindowContent
    {
        // ─── Layout ──────────────────────────────────────────────────────────────

        private const float WIN_W = 340f;
        private const float WIN_H = 380f;
        private const float HEADER_H = 26f;
        private const float SEARCH_H = 22f;
        private const float NS_ROW_H = 26f;
        private const float TYPE_H = 34f;
        private const float COUNT_H = 16f;
        private const float NS_HDR_H = 18f;

        private static readonly Color C_HEADER_BG = new(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color C_ROW_SEL = new(0.18f, 0.37f, 0.73f, 0.55f);
        private static readonly Color C_ROW_HOVER = new(1f, 1f, 1f, 0.06f);
        private static readonly Color C_BACK_HOVER = new(1f, 1f, 1f, 0.08f);
        private static readonly Color C_CHEVRON = new(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Color C_FULL_NAME = new(0.55f, 0.55f, 0.55f, 1f);

        // ─── Data ────────────────────────────────────────────────────────────────

        private readonly struct TypeEntry
        {
            public readonly string Namespace;
            public readonly string Aqn;
            public readonly string ShortName;
            public readonly string FullName;

            public TypeEntry(string ns, string aqn, string sn, string fn)
            { Namespace = ns; Aqn = aqn; ShortName = sn; FullName = fn; }
        }

        private readonly string _currentAQN;
        private readonly Action<Type> _onSelected;
        private readonly List<TypeEntry> _all;

        // Namespace path (segments drilled into so far)
        private readonly List<string> _path = new();

        // Search
        private string _search = string.Empty;
        private List<TypeEntry> _filtered;
        private int _hoveredIndex = -1;
        private Vector2 _scroll;

        // Styles
        private GUIStyle _sRow;
        private GUIStyle _sRowSel;
        private GUIStyle _sHeader;
        private GUIStyle _sBack;
        private GUIStyle _sCount;
        private GUIStyle _sChevron;
        private GUIStyle _sFullName;
        private GUIStyle _sNsLabel;
        private GUIStyle _sNsHdr;

        // ─── Constructor ─────────────────────────────────────────────────────────

        public SerializableTypePickerPopup(string currentAQN, Func<Type, bool> filter, Action<Type> onSelected)
        {
            _currentAQN = currentAQN;
            _onSelected = onSelected;
            _all = BuildTypeList(filter);
            RebuildFilter();
        }

        // ─── PopupWindowContent ───────────────────────────────────────────────────

        public override Vector2 GetWindowSize() => new(WIN_W, WIN_H);

        public override void OnGUI(Rect rect)
        {
            InitStyles();
            DrawHeader();
            DrawSearchBar();
            HandleKeyboard();

            if (!string.IsNullOrWhiteSpace(_search)) DrawFlatList(rect);
            else DrawDrillLevel(rect);
        }

        // ─── Drill-down level ─────────────────────────────────────────────────────

        private void DrawDrillLevel(Rect rect)
        {
            string prefix = CurrentPrefix();
            List<string> childSegs = GetChildSegments(prefix);
            List<TypeEntry> localTypes = GetTypesAt(prefix);

            EditorGUILayout.LabelField(
                $"{childSegs.Count} namespace{(childSegs.Count != 1 ? "s" : "")}  ·  {localTypes.Count} type{(localTypes.Count != 1 ? "s" : "")}",
                _sCount, GUILayout.Height(COUNT_H));

            float listH = WIN_H - HEADER_H - SEARCH_H - COUNT_H - 4f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listH));

            // ── Child namespace segments ──────────────────────────────────────────
            for (int i = 0; i < childSegs.Count; i++)
            {
                string seg = childSegs[i];
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(NS_ROW_H));
                bool isHov = i == _hoveredIndex;

                if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                var labelRect = new Rect(rowRect.x + 10f, rowRect.y + 4f, rowRect.width - 28f, NS_ROW_H - 4f);
                EditorGUI.LabelField(labelRect, seg, _sNsLabel);

                var chevRect = new Rect(rowRect.xMax - 18f, rowRect.y + 4f, 14f, NS_ROW_H - 4f);
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
                }
            }

            // ── Types living directly at this namespace ───────────────────────────
            for (int i = 0; i < localTypes.Count; i++)
            {
                var entry = localTypes[i];
                int flatIdx = childSegs.Count + i;
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(TYPE_H));
                bool isCur = entry.Aqn == _currentAQN;
                bool isHov = flatIdx == _hoveredIndex;

                if (isCur) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                var shortRect = new Rect(rowRect.x + 10f, rowRect.y + 3f, rowRect.width - 10f, 16f);
                var fullRect = new Rect(rowRect.x + 10f, rowRect.y + 18f, rowRect.width - 10f, 13f);
                EditorGUI.LabelField(shortRect, entry.ShortName, isCur ? _sRowSel : _sRow);
                EditorGUI.LabelField(fullRect, entry.FullName, _sFullName);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIdx; editorWindow.Repaint(); }
                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                    SelectEntry(entry.Aqn);
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Flat search list ─────────────────────────────────────────────────────

        private void DrawFlatList(Rect rect)
        {
            EditorGUILayout.LabelField(
                $"{_filtered.Count} type{(_filtered.Count != 1 ? "s" : "")}",
                _sCount, GUILayout.Height(COUNT_H));

            float listH = WIN_H - HEADER_H - SEARCH_H - COUNT_H - 4f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listH));

            string lastNs = null;
            int flatIndex = 0;

            foreach (var entry in _filtered)
            {
                // Namespace separator
                if (entry.Namespace != lastNs)
                {
                    lastNs = entry.Namespace;
                    var ghRect = EditorGUILayout.GetControlRect(GUILayout.Height(NS_HDR_H));
                    EditorGUI.DrawRect(ghRect, C_HEADER_BG);
                    EditorGUI.LabelField(new Rect(ghRect.x + 6f, ghRect.y, ghRect.width - 6f, ghRect.height),
                        string.IsNullOrEmpty(lastNs) ? "(Global)" : lastNs, _sNsHdr);
                }

                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(TYPE_H));
                bool isCur = entry.Aqn == _currentAQN;
                bool isHov = flatIndex == _hoveredIndex;

                if (isCur) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                var shortRect = new Rect(rowRect.x + 10f, rowRect.y + 3f, rowRect.width - 10f, 16f);
                var fullRect = new Rect(rowRect.x + 10f, rowRect.y + 18f, rowRect.width - 10f, 13f);
                EditorGUI.LabelField(shortRect, entry.ShortName, isCur ? _sRowSel : _sRow);
                EditorGUI.LabelField(fullRect, entry.FullName, _sFullName);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIndex; editorWindow.Repaint(); }
                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                    SelectEntry(entry.Aqn);

                flatIndex++;
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Header ───────────────────────────────────────────────────────────────

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
                EditorGUI.LabelField(titleRect, "Select Type", _sHeader);
            }
        }

        // ─── Search bar ───────────────────────────────────────────────────────────

        private void DrawSearchBar()
        {
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("SerializableTypeSearch");
            var searchRect = EditorGUILayout.GetControlRect(GUILayout.Height(SEARCH_H));
            _search = EditorGUI.TextField(searchRect, _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) { _hoveredIndex = -1; _scroll = Vector2.zero; RebuildFilter(); }
            EditorGUI.FocusTextInControl("SerializableTypeSearch");
        }

        // ─── Namespace helpers ────────────────────────────────────────────────────

        private string CurrentPrefix() => _path.Count == 0 ? string.Empty : string.Join(".", _path);

        private List<string> GetChildSegments(string prefix)
        {
            var segs = new List<string>(16);

            foreach (var e in _all)
            {
                string ns = e.Namespace;
                if (string.IsNullOrEmpty(ns)) continue;

                string child;
                if (string.IsNullOrEmpty(prefix))
                {
                    int dot = ns.IndexOf('.');
                    child = dot < 0 ? ns : ns.Substring(0, dot);
                }
                else
                {
                    if (!ns.StartsWith(prefix + ".", StringComparison.Ordinal)) continue;
                    string rest = ns.Substring(prefix.Length + 1);
                    int dot = rest.IndexOf('.');
                    child = dot < 0 ? rest : rest.Substring(0, dot);
                }

                if (!segs.Contains(child)) segs.Add(child);
            }

            segs.Sort((a, b) =>
            {
                if (string.IsNullOrEmpty(prefix))
                {
                    int oa = RootOrder(a), ob = RootOrder(b);
                    if (oa != ob) return oa.CompareTo(ob);
                }
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });

            return segs;
        }

        private List<TypeEntry> GetTypesAt(string prefix)
        {
            var result = new List<TypeEntry>(32);
            foreach (var e in _all)
                if (e.Namespace == prefix) result.Add(e);
            result.Sort((a, b) => string.Compare(a.ShortName, b.ShortName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static int RootOrder(string seg)
        {
            if (seg == "UnityEngine") return 0;
            if (seg == "UnityEditor") return 1;
            if (seg == "MalbersAnimations") return 2;
            return 3;
        }

        // ─── Keyboard ─────────────────────────────────────────────────────────────

        private void HandleKeyboard()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;

            bool inSearch = !string.IsNullOrWhiteSpace(_search);

            if (ev.keyCode == KeyCode.DownArrow)
            {
                int max = inSearch
                    ? _filtered.Count - 1
                    : GetChildSegments(CurrentPrefix()).Count + GetTypesAt(CurrentPrefix()).Count - 1;
                _hoveredIndex = Mathf.Min(_hoveredIndex + 1, max);
                ev.Use();
            }
            if (ev.keyCode == KeyCode.UpArrow)
            { _hoveredIndex = Mathf.Max(_hoveredIndex - 1, 0); ev.Use(); }

            if (ev.keyCode == KeyCode.Return && _hoveredIndex >= 0)
            {
                if (inSearch)
                {
                    if (_hoveredIndex < _filtered.Count) SelectEntry(_filtered[_hoveredIndex].Aqn);
                }
                else
                {
                    var childSegs = GetChildSegments(CurrentPrefix());
                    var localTypes = GetTypesAt(CurrentPrefix());
                    if (_hoveredIndex < childSegs.Count)
                    {
                        _path.Add(childSegs[_hoveredIndex]);
                        _hoveredIndex = -1;
                        _scroll = Vector2.zero;
                        editorWindow.Repaint();
                    }
                    else
                    {
                        int ti = _hoveredIndex - childSegs.Count;
                        if (ti < localTypes.Count) SelectEntry(localTypes[ti].Aqn);
                    }
                }
                ev.Use();
            }

            if (ev.keyCode == KeyCode.Escape)
            {
                if (_path.Count > 0)
                { _path.RemoveAt(_path.Count - 1); _hoveredIndex = -1; _scroll = Vector2.zero; editorWindow.Repaint(); }
                else editorWindow.Close();
                ev.Use();
            }
        }

        // ─── Data ─────────────────────────────────────────────────────────────────

        private void RebuildFilter()
        {
            if (string.IsNullOrWhiteSpace(_search)) { _filtered = _all; return; }
            string q = _search.Trim();
            _filtered = _all.FindAll(e =>
                e.ShortName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void SelectEntry(string aqn)
        {
            var type = Type.GetType(aqn);
            if (type != null) _onSelected?.Invoke(type);
            editorWindow.Close();
        }

        private static List<TypeEntry> BuildTypeList(Func<Type, bool> filter)
        {
            var list = new List<TypeEntry>(256);

            foreach (var asm in MalbersReflection.GetLoadedAssemblies())
            {
                string asmName = asm.GetName().Name ?? string.Empty;
                if (asmName.StartsWith("System", StringComparison.Ordinal) ||
                    asmName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                    asmName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                    asmName.StartsWith("netstandard", StringComparison.Ordinal) ||
                    asmName.StartsWith("Mono.", StringComparison.Ordinal))
                    continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (!filter(t)) continue;
                    string ns = t.Namespace ?? string.Empty;
                    string sn = t.Name;
                    string fn = t.FullName ?? sn;
                    string aqn = t.AssemblyQualifiedName ?? fn;
                    list.Add(new(ns, aqn, sn, fn));
                }
            }

            list.Sort((a, b) => string.Compare(a.ShortName, b.ShortName, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // ─── Styles ──────────────────────────────────────────────────────────────

        private void InitStyles()
        {
            _sRow ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Color.white }
            };
            _sRowSel ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
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
            _sCount ??= new(EditorStyles.centeredGreyMiniLabel);
            _sChevron ??= new(EditorStyles.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_CHEVRON }
            };
            _sFullName ??= new(EditorStyles.label)
            {
                fontSize = 9,
                normal = { textColor = C_FULL_NAME }
            };
            _sNsLabel ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(0.75f, 0.85f, 1f) }
            };
            _sNsHdr ??= new(EditorStyles.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };
        }
    }
#endif
}