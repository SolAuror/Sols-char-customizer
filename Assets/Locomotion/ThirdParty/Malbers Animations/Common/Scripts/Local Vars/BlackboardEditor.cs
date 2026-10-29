// Last Change: 9/17/2026 5:33pm
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
namespace MalbersAnimations
{
    [CustomEditor(typeof(MLocalVarsBlackboard))]
    public class BlackboardEditor : Editor
    {
        // ─── Layout constants ────────────────────────────────────────────────────
        //
        //  Per-row slot order (left → right):
        //  [DRAG_W] [FOLD_W] [BADGE_W + BADGE_GAP] [name 60%] [COL_GAP] [value ~38%] [COL_GAP] [REMOVE_W]
        //
        private const float DRAG_W = 20f;   // built-in ReorderableList drag handle  (do not draw here)
        private const float ID_W = 14f;   // variable ID label before badge
        private const float FOLD_W = 0;   // foldout toggle  – placed AFTER drag handle, no overlap
        private const float BADGE_W = 22f;
        private const float BADGE_GAP = 3f;
        private const float COL_GAP = 4f;
        private const float ROW_PAD = 2f;
        private const float REMOVE_W = 15f;   // inline trash icon button
        private const float BTN_ADD_W = 25f;   // icon-only add button in header

        // ─── Type badge colours ──────────────────────────────────────────────────

        private static readonly Color C_BOOL = new(0.15f, 0.52f, 0.82f);
        private static readonly Color C_STRING = new(0.15f, 0.70f, 0.38f);
        private static readonly Color C_INT = new(0.82f, 0.62f, 0.10f);
        private static readonly Color C_FLOAT = new(0.82f, 0.20f, 0.32f);
        private static readonly Color C_OBJECT = new(0.82f, 0.38f, 0.14f);
        private static readonly Color C_VEC3 = new(0.10f, 0.70f, 0.75f);
        private static readonly Color C_VEC2 = new(0.10f, 0.55f, 0.60f);
        private static readonly Color C_COLOR = new(0.80f, 0.20f, 0.55f);
        private static readonly Color C_STRVAR = new(0.08f, 0.48f, 0.22f);
        private static readonly Color C_BOOLVAR = new(0.10f, 0.35f, 0.62f);
        private static readonly Color C_INTVAR = new(0.60f, 0.44f, 0.05f);
        private static readonly Color C_FLOATVAR = new(0.72f, 0.10f, 0.22f);
        private static readonly Color C_ROW_EVEN = new(0.20f, 0.20f, 0.20f, 0.15f);
        private static readonly Color C_ROW_ODD = new(0.27f, 0.27f, 0.27f, 0.08f);
        private static readonly Color C_ROW_ACTIVE = new(0.18f, 0.37f, 0.73f, 0.38f);

        // ─── State ───────────────────────────────────────────────────────────────

        private SerializedProperty _variablesProp;
        private SerializedProperty _nextIdProp;
        private ReorderableList _list;
        private int _pendingRemoveIndex = -1;

        // Pre-reorder snapshot – used to fix [SerializeReference] reorder bug
        private readonly List<BlackboardVar> _snapshot = new();

        // Keyed by variable Name so order changes don't reset foldouts
        private readonly Dictionary<string, bool> _foldouts = new();
        private bool _needsRepaint;

        // ─── Lazy-init styles ────────────────────────────────────────────────────

        private GUIStyle _sBadge;
        private GUIStyle _sIdLabel;
        private GUIStyle _sColHeader;
        private GUIStyle _sRemoveBtn;

        private GUIStyle BadgeStyle => _sBadge ??= new(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 9, normal = { textColor = Color.white } };
        private GUIStyle IdStyle => _sIdLabel ??= new(EditorStyles.centeredGreyMiniLabel) { fontSize = 9 };
        private GUIStyle ColHeader => _sColHeader ??= new(EditorStyles.boldLabel) { fontSize = 11 };
        private GUIStyle RemoveStyle => _sRemoveBtn ??= new(EditorStyles.iconButton)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(2, 2, 2, 2),
            normal = { textColor = new Color(0.9f, 0.4f, 0.4f) },
            hover = { textColor = new Color(1f, 0.2f, 0.2f) },
            active = { textColor = Color.white }
        };

        // ─── Enable ──────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            _variablesProp = serializedObject.FindProperty("_variables");
            _nextIdProp = serializedObject.FindProperty("_nextId");
            BuildList();
        }

        // ─── List construction ───────────────────────────────────────────────────

        private void BuildList()
        {
            _list = new ReorderableList(serializedObject, _variablesProp,
                draggable: true, displayHeader: true,
                displayAddButton: false, displayRemoveButton: false)   // both handled manually
            {
                drawHeaderCallback = DrawHeader,
                drawElementCallback = DrawElement,
                elementHeightCallback = GetElementHeight,
                drawElementBackgroundCallback = DrawBackground,
                onReorderCallbackWithDetails = OnReorder,
                headerHeight = EditorGUIUtility.singleLineHeight + 4f
            };
        }

        // ─── Header ──────────────────────────────────────────────────────────────

        private void DrawHeader(Rect r)
        {
            float leftOffset = DRAG_W + FOLD_W + ID_W + BADGE_W + BADGE_GAP;
            float rightReserve = REMOVE_W + COL_GAP + BTN_ADD_W + COL_GAP;
            float usable = r.width - leftOffset - rightReserve;
            float nameW = usable * 0.60f;
            float valueX = r.x + leftOffset + nameW + COL_GAP + 20;

            EditorGUI.LabelField(new Rect(r.x + leftOffset, r.y, nameW, r.height), "Variable Name", ColHeader);
            EditorGUI.LabelField(new Rect(valueX, r.y, usable - nameW, r.height), "Value", ColHeader);

            var addRect = new Rect(r.xMax - BTN_ADD_W, r.y + 1f, BTN_ADD_W, r.height - 2f);
            if (GUI.Button(addRect, EditorGUIUtility.IconContent("d_Toolbar Plus")))
                ShowAddMenu();
        }

        // ─── Element drawing ─────────────────────────────────────────────────────
        private void DrawElement(Rect r, int index, bool isActive, bool isFocused)
        {
            if (index >= _variablesProp.arraySize) return;

            var elemProp = _variablesProp.GetArrayElementAtIndex(index);
            var variable = elemProp.managedReferenceValue as BlackboardVar;
            if (variable == null) return;

            float lh = EditorGUIUtility.singleLineHeight;
            float y = r.y + ROW_PAD;
            string key = FoldoutKey(variable, index);

            // ── Slot positions ─────────────────────────────────────────────────
            //   order: [DRAG] [FOLD] [ID] [BADGE] [name 60%] [value ~38%] [REMOVE]
            float foldX = r.x + DRAG_W;
            float idX = foldX + FOLD_W;
            float badgeX = idX + ID_W;
            float nameX = badgeX + BADGE_W + BADGE_GAP;
            float removeX = r.xMax - REMOVE_W;
            float usableW = removeX - nameX - COL_GAP;
            float nameW = usableW * 0.33f - COL_GAP;
            float valueX = nameX + nameW + COL_GAP;
            float valueW = removeX - valueX - COL_GAP;

            // ── Foldout (after drag handle – no overlap) ──────────────────────
            bool wasOpen = _foldouts.TryGetValue(key, out bool openVal) && openVal;
            bool isOpen = EditorGUI.Foldout(new Rect(foldX, y, FOLD_W, lh), wasOpen, GUIContent.none, true);
            if (isOpen != wasOpen) { _foldouts[key] = isOpen; _needsRepaint = true; }

            // ── ID label ──────────────────────────────────────────────────────
            EditorGUI.LabelField(new Rect(idX, y, ID_W, lh), variable.Id.ToString(), IdStyle);

            // ── Type badge ────────────────────────────────────────────────────
            DrawBadge(new Rect(badgeX, y + 1f, BADGE_W, lh - 2f), variable);

            // ── Name field (read-only in play mode) ───────────────────────────
            var nameProp = elemProp.FindPropertyRelative("_name");
            using (new EditorGUI.DisabledGroupScope(Application.isPlaying))
                EditorGUI.PropertyField(new Rect(nameX, y, nameW, lh), nameProp, GUIContent.none);

            // ── Value field ───────────────────────────────────────────────────
            DrawValueField(elemProp, variable, new Rect(valueX, y, valueW, lh));

            // ── Inline trash remove button ────────────────────────────────────
            var removeRect = new Rect(removeX, y + 1f, REMOVE_W, lh - 2f);
            var trashIcon = EditorGUIUtility.IconContent("d_TreeEditor.Trash");

            // Draw a subtle red tint behind the icon only on hover
            if (removeRect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(removeRect, new Color(0.7f, 0.15f, 0.15f, 0.35f));

            if (GUI.Button(removeRect, trashIcon, RemoveStyle))
                _pendingRemoveIndex = index;

            y += lh + ROW_PAD;

            // ── Foldout content: UnityEvent only ─────────────────────────────
            if (isOpen)
            {
                var onChangedProp = elemProp.FindPropertyRelative("_onChanged");
                if (onChangedProp != null)
                {
                    float eh = EditorGUI.GetPropertyHeight(onChangedProp, true);
                    float contentX = r.x + DRAG_W + FOLD_W;
                    EditorGUI.PropertyField(new Rect(contentX, y, r.xMax - contentX, eh), onChangedProp, true);
                }
            }
        }

        // ─── Element height ───────────────────────────────────────────────────────

        private float GetElementHeight(int index)
        {
            float lh = EditorGUIUtility.singleLineHeight;
            if (index >= _variablesProp.arraySize) return lh + ROW_PAD * 2f;

            var elemProp = _variablesProp.GetArrayElementAtIndex(index);
            var variable = elemProp.managedReferenceValue as BlackboardVar;
            if (variable == null) return lh + ROW_PAD * 2f;

            float height = lh + ROW_PAD * 2f;

            string key = FoldoutKey(variable, index);
            if (_foldouts.TryGetValue(key, out bool open) && open)
            {
                var onChangedProp = elemProp.FindPropertyRelative("_onChanged");
                if (onChangedProp != null)
                    height += EditorGUI.GetPropertyHeight(onChangedProp, true) + ROW_PAD;
            }

            return height;
        }

        // ─── Background ──────────────────────────────────────────────────────────

        private void DrawBackground(Rect r, int index, bool isActive, bool isFocused)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color c = isActive ? C_ROW_ACTIVE : (index % 2 == 0 ? C_ROW_EVEN : C_ROW_ODD);
            EditorGUI.DrawRect(r, c);
        }

        // ─── Value field ─────────────────────────────────────────────────────────

        private const float TYPE_BTN_W = 100f;  // inline type-picker dropdown width

        private void DrawValueField(SerializedProperty elemProp, BlackboardVar variable, Rect r)
        {


            if (variable is BlackObject objVar)
            {
                // Split: [ObjectField | COL_GAP | TypeDropdown]
                float objW = r.width - COL_GAP - TYPE_BTN_W;
                var objRect = new Rect(r.x, r.y, objW, r.height);
                var btnRect = new Rect(r.x + objW + COL_GAP, r.y, TYPE_BTN_W, r.height);

                if (Application.isPlaying)
                {
                    EditorGUI.BeginChangeCheck();
                    var obj = EditorGUI.ObjectField(objRect, objVar.Get(), objVar.ObjectType, true);
                    if (EditorGUI.EndChangeCheck()) objVar.Set(obj);
                }
                else
                {
                    var valueProp = elemProp.FindPropertyRelative("_value");
                    if (valueProp != null)
                        EditorGUI.ObjectField(objRect, valueProp, objVar.ObjectType, GUIContent.none);
                }
                using (new EditorGUI.DisabledGroupScope(Application.isPlaying))
                {
                    DrawTypeDropdownButton(elemProp, objVar, btnRect);
                }
                return;
            }

            if (Application.isPlaying)
                DrawValueRuntime(variable, r);
            else
            {
                var valueProp = elemProp.FindPropertyRelative("_value");
                if (valueProp != null)
                    EditorGUI.PropertyField(r, valueProp, GUIContent.none);
            }

        }

        private void DrawTypeDropdownButton(SerializedProperty elemProp, BlackObject objVar, Rect btnRect)
        {
            var typeProp = elemProp.FindPropertyRelative("_objectTypeName");

            string currentFull = typeProp.stringValue;
            string displayName = string.IsNullOrEmpty(currentFull)
                ? "Select Type..."
                : (currentFull.Contains('.') ? currentFull[(currentFull.LastIndexOf('.') + 1)..] : currentFull);

            if (EditorGUI.DropdownButton(btnRect, new GUIContent(displayName), FocusType.Keyboard, EditorStyles.popup))
            {
                var capturedProp = typeProp;
                var capturedVar = objVar;
                var capturedSO = serializedObject;

                PopupWindow.Show(btnRect, new TypePickerPopup(currentFull, chosen =>
                {
                    capturedSO.Update();
                    capturedProp.stringValue = chosen.FullName ?? chosen.Name;
                    capturedVar.ObjectTypeName = capturedProp.stringValue;
                    capturedSO.ApplyModifiedProperties();
                }));
            }
        }

        private void DrawValueRuntime(BlackboardVar variable, Rect r)
        {
            switch (variable)
            {
                case BlackboardVar<float> v:
                    {
                        EditorGUI.BeginChangeCheck();
                        float val = EditorGUI.FloatField(r, v.Get());
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackboardVar<int> v:
                    {
                        EditorGUI.BeginChangeCheck();
                        int val = EditorGUI.IntField(r, v.Get());
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackboardVar<bool> v:
                    {
                        EditorGUI.BeginChangeCheck();
                        bool val = EditorGUI.Toggle(r, v.Get());
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackboardVar<string> v:
                    {
                        EditorGUI.BeginChangeCheck();
                        string val = EditorGUI.TextField(r, v.Get());
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackObject v:
                    {
                        EditorGUI.BeginChangeCheck();
                        var obj = EditorGUI.ObjectField(r, v.Get(), v.ObjectType, true);
                        if (EditorGUI.EndChangeCheck()) v.Set(obj);
                        break;
                    }
                case BlackboardVar<Vector3> v:
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 val = EditorGUI.Vector3Field(r, GUIContent.none, v.Get());
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackboardVar<Vector2> v:
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector2 val = EditorGUI.Vector2Field(r, GUIContent.none, v.Get());
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackboardVar<Color> v:
                    {
                        EditorGUI.BeginChangeCheck();
                        Color val = EditorGUI.ColorField(r, v.Get());
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackIntVar v:
                    {
                        EditorGUI.BeginChangeCheck();
                        var val = (MalbersAnimations.Scriptables.IntVar)EditorGUI.ObjectField(r, v.Get(), typeof(MalbersAnimations.Scriptables.IntVar), false);
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackFloatVar v:
                    {
                        EditorGUI.BeginChangeCheck();
                        var val = (MalbersAnimations.Scriptables.FloatVar)EditorGUI.ObjectField(r, v.Get(), typeof(MalbersAnimations.Scriptables.FloatVar), false);
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackBoolVar v:
                    {
                        EditorGUI.BeginChangeCheck();
                        var val = (MalbersAnimations.Scriptables.BoolVar)EditorGUI.ObjectField(r, v.Get(), typeof(MalbersAnimations.Scriptables.BoolVar), false);
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
                case BlackStringVar v:
                    {
                        EditorGUI.BeginChangeCheck();
                        var val = (MalbersAnimations.Scriptables.StringVar)EditorGUI.ObjectField(r, v.Get(), typeof(MalbersAnimations.Scriptables.StringVar), false);
                        if (EditorGUI.EndChangeCheck()) v.Set(val);
                        break;
                    }
            }
        }

        // ─── Type badge ──────────────────────────────────────────────────────────

        private void DrawBadge(Rect r, BlackboardVar v)
        {
            (Color bg, string label) badge = v switch
            {
                BlackFloat => (C_FLOAT, "F"),
                BlackInt => (C_INT, "I"),
                BlackBool => (C_BOOL, "B"),
                BlackString => (C_STRING, "S"),
                BlackObject => (C_OBJECT, "O"),
                BlackVector3 => (C_VEC3, "V3"),
                BlackVector2 => (C_VEC2, "V2"),
                BlackColor => (C_COLOR, "Co"),
                BlackIntVar => (C_INTVAR, "IV"),
                BlackFloatVar => (C_FLOATVAR, "FV"),
                BlackBoolVar => (C_BOOLVAR, "BV"),
                BlackStringVar => (C_STRVAR, "SV"),
                _ => (Color.gray, "?")
            };

            EditorGUI.DrawRect(r, badge.bg);
            GUI.Label(r, badge.label, BadgeStyle);
        }

        // ─── Add menu ────────────────────────────────────────────────────────────

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Float"), false, () => CreateVariable(typeof(BlackFloat)));
            menu.AddItem(new GUIContent("Int"), false, () => CreateVariable(typeof(BlackInt)));
            menu.AddItem(new GUIContent("Bool"), false, () => CreateVariable(typeof(BlackBool)));
            menu.AddItem(new GUIContent("String"), false, () => CreateVariable(typeof(BlackString)));
            menu.AddItem(new GUIContent("Object"), false, () => CreateVariable(typeof(BlackObject)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Vector3"), false, () => CreateVariable(typeof(BlackVector3)));
            menu.AddItem(new GUIContent("Vector2"), false, () => CreateVariable(typeof(BlackVector2)));
            menu.AddItem(new GUIContent("Color"), false, () => CreateVariable(typeof(BlackColor)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Var/IntVar"), false, () => CreateVariable(typeof(BlackIntVar)));
            menu.AddItem(new GUIContent("Var/FloatVar"), false, () => CreateVariable(typeof(BlackFloatVar)));
            menu.AddItem(new GUIContent("Var/BoolVar"), false, () => CreateVariable(typeof(BlackBoolVar)));
            menu.AddItem(new GUIContent("Var/StringVar"), false, () => CreateVariable(typeof(BlackStringVar)));
            menu.ShowAsContext();
        }

        private void CreateVariable(Type varType)
        {
            serializedObject.Update();

            string baseName = varType.Name.Replace("Variable", string.Empty);
            string uniqueName = UniqueName($"new{baseName}");

            int newId = _nextIdProp.intValue;
            _nextIdProp.intValue = newId + 1;

            int newIndex = _variablesProp.arraySize;
            _variablesProp.arraySize++;
            var elem = _variablesProp.GetArrayElementAtIndex(newIndex);

            var instance = (BlackboardVar)Activator.CreateInstance(varType);
            instance.Name = uniqueName;
            instance.Id = newId;
            elem.managedReferenceValue = instance;

            serializedObject.ApplyModifiedProperties();
            ReassignAllIds();
            serializedObject.ApplyModifiedProperties();
            _list.index = newIndex;
            RebuildIfPlaying();
        }

        // ─── Main GUI ────────────────────────────────────────────────────────────

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (HasDuplicateNames())
                EditorGUILayout.HelpBox("Duplicate variable names detected – only the last one will be reachable by name.", MessageType.Warning);

            EditorGUILayout.Space(2f);

            // Snapshot managed references NOW (pre-draw) so OnReorder can fix
            // Unity's [SerializeReference] reorder bug where MoveArrayElement
            // moves serialised field data but leaves managedReferenceValue pointers stale.
            _snapshot.Clear();
            for (int i = 0; i < _variablesProp.arraySize; i++)
                _snapshot.Add(_variablesProp.GetArrayElementAtIndex(i).managedReferenceValue as BlackboardVar);

            _list.DoLayoutList();

            serializedObject.ApplyModifiedProperties();

            // Deferred remove – processed AFTER ApplyModifiedProperties to avoid mutation mid-draw
            if (_pendingRemoveIndex >= 0)
            {
                serializedObject.Update();
                var toRemove = _variablesProp.GetArrayElementAtIndex(_pendingRemoveIndex).managedReferenceValue as BlackboardVar;
                if (toRemove != null) _foldouts.Remove(FoldoutKey(toRemove, _pendingRemoveIndex));

                _variablesProp.DeleteArrayElementAtIndex(_pendingRemoveIndex);
                _pendingRemoveIndex = -1;
                ReassignAllIds();
                serializedObject.ApplyModifiedProperties();
                RebuildIfPlaying();
            }

            if (_needsRepaint) { _needsRepaint = false; Repaint(); }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private string FoldoutKey(BlackboardVar v, int fallback)
            => string.IsNullOrEmpty(v?.Name) ? $"__idx_{fallback}" : v.Name;

        private bool HasDuplicateNames()
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < _variablesProp.arraySize; i++)
            {
                var n = _variablesProp.GetArrayElementAtIndex(i).FindPropertyRelative("_name");
                if (n != null && !seen.Add(n.stringValue)) return true;
            }
            return false;
        }

        private string UniqueName(string baseName)
        {
            var existing = new HashSet<string>();
            for (int i = 0; i < _variablesProp.arraySize; i++)
            {
                var n = _variablesProp.GetArrayElementAtIndex(i).FindPropertyRelative("_name");
                if (n != null) existing.Add(n.stringValue);
            }
            string name = baseName;
            int cnt = 1;
            while (existing.Contains(name)) name = $"{baseName}_{cnt++}";
            return name;
        }

        private void OnReorder(ReorderableList list, int oldIndex, int newIndex)
        {
            // Unity's MoveArrayElement already moved the serialised field data (name, id, value text)
            // but left managedReferenceValue pointers stale.  We fix this by applying the same
            // move to our pre-draw snapshot, then reassigning every slot's managed reference.
            serializedObject.Update();

            var item = _snapshot[oldIndex];
            _snapshot.RemoveAt(oldIndex);
            _snapshot.Insert(newIndex, item);

            for (int i = 0; i < _variablesProp.arraySize; i++)
                _variablesProp.GetArrayElementAtIndex(i).managedReferenceValue = _snapshot[i];

            ReassignAllIds();
            serializedObject.ApplyModifiedProperties();
            RebuildIfPlaying();
        }

        private void ReassignAllIds()
        {
            for (int i = 0; i < _variablesProp.arraySize; i++)
            {
                var idProp = _variablesProp.GetArrayElementAtIndex(i).FindPropertyRelative("_id");
                if (idProp != null) idProp.intValue = i;
            }
        }

        private void RebuildIfPlaying()
        {
            if (Application.isPlaying) ((MLocalVarsBlackboard)target).RebuildRegistry();
        }
    }

    // ─── Type Picker Popup ───────────────────────────────────────────────────────

    /// <summary>
    /// Searchable popup that lists every UnityEngine.Object-derived type
    /// found in the loaded assemblies, grouped by origin (Engine / Editor / Project).
    /// </summary>
    public class TypePickerPopup : PopupWindowContent
    {
        // ─── Type cache (built once per Editor session) ──────────────────────────

        private static List<(string group, string fullName, string shortName)> _cachedTypes;

        private static List<(string group, string fullName, string shortName)> CachedTypes
        {
            get
            {
                if (_cachedTypes != null) return _cachedTypes;

                _cachedTypes = new(512);
                var uoType = typeof(UnityEngine.Object);

                foreach (var asm in MalbersReflection.GetLoadedAssemblies())
                {
                    string asmName = asm.GetName().Name ?? string.Empty;

                    // Skip assemblies that never contain useful component types
                    if (asmName.StartsWith("System", StringComparison.Ordinal) ||
                        asmName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                        asmName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                        asmName.StartsWith("netstandard", StringComparison.Ordinal) ||
                        asmName.StartsWith("Mono.", StringComparison.Ordinal))
                        continue;

                    string group = asmName.StartsWith("UnityEditor", StringComparison.Ordinal) ? "Unity Editor"
                                 : asmName.StartsWith("Unity", StringComparison.Ordinal) ? "Unity Engine"
                                 : "Project";

                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (!t.IsPublic) continue;
                        if (!uoType.IsAssignableFrom(t)) continue;
                        if (t.IsGenericTypeDefinition) continue;

                        string shortName = t.Name;
                        string fullName = t.FullName ?? shortName;
                        _cachedTypes.Add((group, fullName, shortName));
                    }
                }

                // Sort: Engine first, Editor second, Project third, then alphabetically within group
                _cachedTypes.Sort((a, b) =>
                {
                    int ga = GroupOrder(a.group), gb = GroupOrder(b.group);
                    return ga != gb ? ga.CompareTo(gb) : string.Compare(a.shortName, b.shortName, StringComparison.OrdinalIgnoreCase);
                });

                return _cachedTypes;
            }
        }

        private static int GroupOrder(string g) => g switch
        {
            "Unity Engine" => 0,
            "Unity Editor" => 1,
            _ => 2
        };

        // ─── Instance state ──────────────────────────────────────────────────────

        private readonly string _currentFullName;
        private readonly Action<Type> _onSelected;

        private string _search = string.Empty;
        private Vector2 _scroll;
        private int _hoveredIndex = -1;

        private List<(string group, string fullName, string shortName)> _filtered;

        // ─── Styles ──────────────────────────────────────────────────────────────

        private GUIStyle _sRow;
        private GUIStyle _sRowSelected;
        private GUIStyle _sGroupHeader;
        private GUIStyle _sShortName;
        private GUIStyle _sFullName;

        private const float WIN_W = 320f;
        private const float WIN_H = 380f;
        private const float SEARCH_H = 22f;
        private const float ROW_H = 34f;
        private const float GROUP_H = 18f;

        private static readonly Color C_GROUP_BG = new(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color C_ROW_SEL = new(0.18f, 0.37f, 0.73f, 0.55f);
        private static readonly Color C_ROW_HOVER = new(1f, 1f, 1f, 0.06f);
        private static readonly Color C_FULL_NAME = new(0.55f, 0.55f, 0.55f, 1f);

        // ─── Constructor ─────────────────────────────────────────────────────────

        public TypePickerPopup(string currentFullName, Action<Type> onSelected)
        {
            _currentFullName = currentFullName;
            _onSelected = onSelected;
            RebuildFilter();
        }

        // ─── PopupWindowContent overrides ────────────────────────────────────────

        public override Vector2 GetWindowSize() => new(WIN_W, WIN_H);

        public override void OnGUI(Rect rect)
        {
            InitStyles();
            HandleKeyboard();

            // ── Search bar ──────────────────────────────────────────────────────
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("TypeSearch");
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField,
                          GUILayout.Height(SEARCH_H));
            if (EditorGUI.EndChangeCheck()) { _hoveredIndex = -1; RebuildFilter(); }

            EditorGUI.FocusTextInControl("TypeSearch");

            // ── Count label ─────────────────────────────────────────────────────
            EditorGUILayout.LabelField($"{_filtered.Count} type{(_filtered.Count != 1 ? "s" : "")}", EditorStyles.centeredGreyMiniLabel);

            // ── Scrollable list ─────────────────────────────────────────────────
            float listHeight = rect.height - SEARCH_H - EditorGUIUtility.singleLineHeight - 6f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listHeight));

            string lastGroup = null;
            int flatIndex = 0;

            foreach (var entry in _filtered)
            {
                // Group header
                if (entry.group != lastGroup)
                {
                    lastGroup = entry.group;
                    var ghRect = EditorGUILayout.GetControlRect(GUILayout.Height(GROUP_H));
                    EditorGUI.DrawRect(ghRect, C_GROUP_BG);
                    EditorGUI.LabelField(ghRect, entry.group, _sGroupHeader);
                }

                // Row
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));
                bool isCurrent = entry.fullName == _currentFullName;
                bool isHovered = flatIndex == _hoveredIndex;

                if (isCurrent) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHovered) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                // Short name
                var shortRect = new Rect(rowRect.x + 8f, rowRect.y + 3f, rowRect.width - 8f, 16f);
                EditorGUI.LabelField(shortRect, entry.shortName, isCurrent ? _sRowSelected : _sRow);

                // Full name (dim, smaller)
                var fullRect = new Rect(rowRect.x + 8f, rowRect.y + 18f, rowRect.width - 8f, 13f);
                EditorGUI.LabelField(fullRect, entry.fullName, _sFullName);

                // Interaction
                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                {
                    _hoveredIndex = flatIndex;
                    editorWindow.Repaint();
                }
                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                    SelectEntry(entry.fullName);

                flatIndex++;
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private void RebuildFilter()
        {
            if (string.IsNullOrWhiteSpace(_search))
            {
                _filtered = CachedTypes;
                return;
            }

            string q = _search.Trim();
            _filtered = new List<(string, string, string)>(64);
            foreach (var e in CachedTypes)
            {
                if (e.shortName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.fullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    _filtered.Add(e);
            }
        }

        private void SelectEntry(string fullName)
        {
            var type = TypeResolver.Resolve(fullName);
            if (type != null) _onSelected?.Invoke(type);
            editorWindow.Close();
        }

        private void HandleKeyboard()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;

            if (ev.keyCode == KeyCode.DownArrow) { _hoveredIndex = Mathf.Min(_hoveredIndex + 1, _filtered.Count - 1); ev.Use(); }
            if (ev.keyCode == KeyCode.UpArrow) { _hoveredIndex = Mathf.Max(_hoveredIndex - 1, 0); ev.Use(); }
            if (ev.keyCode == KeyCode.Return && _hoveredIndex >= 0 && _hoveredIndex < _filtered.Count)
            {
                SelectEntry(_filtered[_hoveredIndex].fullName);
                ev.Use();
            }
            if (ev.keyCode == KeyCode.Escape) { editorWindow.Close(); ev.Use(); }
        }

        private void InitStyles()
        {
            _sRow ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Color.white }
            };

            _sRowSelected ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _sGroupHeader ??= new(EditorStyles.boldLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 0, 0, 0),
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            _sFullName ??= new(EditorStyles.label)
            {
                fontSize = 9,
                normal = { textColor = C_FULL_NAME }
            };
        }
    }
}
#endif