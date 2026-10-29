// Last Change: 9/17/2026 5:33pm
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [Serializable]
    public class MemberValueSetter
    {
        /// <summary>Component type the member is written to. Always resolved from the target GameObject.</summary>
        [TypeFilter(typeof(Component))]
        public SerializableType targetType;

        public string memberName;
        public float setValue;
        public int setValueInt;
        public bool setValueBool;
        public string setValueString; // MWC – string value storage
        public Object setValueObject; // MWC – UnityEngine.Object reference (Transform, GameObject, ScriptableObject, …)

        // ─── Runtime cache (not serialized) ──────────────────────────────────────

        private Component _resolvedTarget; // component the member is written to
        private Object _targetObject;      // last target passed to SetTarget
        private PropertyInfo _cachedProperty;
        private FieldInfo _cachedField;
        private MethodInfo _cachedMethod;
        private string _cachedMemberName;
        private bool _cachedIsBool;
        private bool _cachedIsInt;
        private bool _cachedIsString; // MWC – string cache flag
        private bool _cachedIsObject; // MWC – UnityEngine.Object cache flag
        private bool _cachedIsVoid;   // MWC – parameterless void method (action trigger) cache flag

        // ─── Public API ───────────────────────────────────────────────────────────

        public void SetTarget(Object target)
        {
            _resolvedTarget = null;
            _cachedProperty = null;
            _cachedField = null;
            _cachedMethod = null;
            _cachedMemberName = null;

            _targetObject = target;
            Resolve();
        }

        /// <summary>Finds the Target Type component on the target GameObject (self → parent → children) and rebuilds the cache.</summary>
        private void Resolve()
        {
            _resolvedTarget = null;
            var type = targetType?.Type;

            if (type != null)
            {
                var go = (_targetObject as GameObject) ?? (_targetObject as Component)?.gameObject;
                if (go != null) _resolvedTarget = go.FindComponent(type);
            }

            RebuildMemberCache();
        }

        public void Apply()
        {
            if (_resolvedTarget == null) return;

            if (_cachedMemberName != memberName) RebuildMemberCache();

            if (_cachedProperty == null && _cachedField == null && _cachedMethod == null) return;

            // MWC – parameterless void method: just invoke it, there is no value to set
            if (_cachedIsVoid) { _cachedMethod.Invoke(_resolvedTarget, null); return; }

            object value = _cachedIsBool ? setValueBool
                         : _cachedIsInt ? setValueInt
                         : _cachedIsString ? setValueString // MWC – string branch
                         : _cachedIsObject ? setValueObject         // MWC – Object branch
                         : Convert.ChangeType(setValue, GetTargetType());

            if (_cachedProperty != null) _cachedProperty.SetValue(_resolvedTarget, value);
            else if (_cachedField != null) _cachedField.SetValue(_resolvedTarget, value);
            else _cachedMethod.Invoke(_resolvedTarget, new[] { value });
        }

        public void Apply(Object target)
        {
            SetTarget(target);
            Apply();
        }

        public string Description()
        {
            string typeName = targetType?.Type?.Name ?? "?";
            string memberText = string.IsNullOrEmpty(memberName) ? "?" : memberName;

            if (IsBoolMember()) return $"{typeName}.{memberText} = {setValueBool}";
            if (IsIntMember()) return $"{typeName}.{memberText} = {setValueInt}";
            if (IsStringMember()) return $"{typeName}.{memberText} = \"{setValueString}\""; // MWC – string description
            if (IsObjectMember()) return $"{typeName}.{memberText} = {(setValueObject != null ? setValueObject.name : "null")}"; // MWC – Object description
            if (IsVoidMember()) return $"{typeName}.{memberText}()"; // MWC – parameterless void method description
            return $"{typeName}.{memberText} = {setValue}";
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private void RebuildMemberCache()
        {
            _cachedProperty = null;
            _cachedField = null;
            _cachedMethod = null;
            _cachedMemberName = memberName;
            _cachedIsBool = false;
            _cachedIsInt = false;
            _cachedIsString = false; // MWC – reset string flag
            _cachedIsObject = false; // MWC – reset Object flag
            _cachedIsVoid = false;   // MWC – reset void flag

            if (_resolvedTarget == null || string.IsNullOrEmpty(memberName) || targetType?.Type == null) return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite && (MemberValueCompare.IsSupported(prop.PropertyType) || MemberValueCompare.IsObjectType(prop.PropertyType))) // MWC – include Object types
            {
                _cachedProperty = prop;
                _cachedIsBool = prop.PropertyType == typeof(bool);
                _cachedIsInt = IsIntType(prop.PropertyType);
                _cachedIsString = prop.PropertyType == typeof(string);
                _cachedIsObject = MemberValueCompare.IsObjectType(prop.PropertyType);
                return;
            }

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly && !field.IsLiteral && (MemberValueCompare.IsSupported(field.FieldType) || MemberValueCompare.IsObjectType(field.FieldType))) // MWC – include Object types
            {
                _cachedField = field;
                _cachedIsBool = field.FieldType == typeof(bool);
                _cachedIsInt = IsIntType(field.FieldType);
                _cachedIsString = field.FieldType == typeof(string);
                _cachedIsObject = MemberValueCompare.IsObjectType(field.FieldType);
                return;
            }

            foreach (var method in type.GetMethods(flags))
            {
                if (method.IsSpecialName) continue;
                if (method.Name != memberName) continue;
                if (method.ReturnType != typeof(void)) continue;
                var parms = method.GetParameters();
                if (parms.Length != 1) continue;
                if (!MemberValueCompare.IsSupported(parms[0].ParameterType) && !MemberValueCompare.IsObjectType(parms[0].ParameterType)) continue; // MWC – include Object types
                _cachedMethod = method;
                _cachedIsBool = parms[0].ParameterType == typeof(bool);
                _cachedIsInt = IsIntType(parms[0].ParameterType);
                _cachedIsString = parms[0].ParameterType == typeof(string);
                _cachedIsObject = MemberValueCompare.IsObjectType(parms[0].ParameterType);
                return;
            }

            // MWC – fall back to a parameterless void method (action trigger). Value-bearing
            // members above take priority, so existing setters keep their behaviour.
            var voidMethod = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            if (voidMethod != null && !voidMethod.IsSpecialName && voidMethod.ReturnType == typeof(void))
            {
                _cachedMethod = voidMethod;
                _cachedIsVoid = true;
            }
        }

        private Type GetTargetType()
        {
            if (_cachedProperty != null) return _cachedProperty.PropertyType;
            if (_cachedField != null) return _cachedField.FieldType;
            if (_cachedMethod != null) return _cachedMethod.GetParameters()[0].ParameterType;
            return typeof(float);
        }

        private bool IsBoolMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite) return prop.PropertyType == typeof(bool);

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly) return field.FieldType == typeof(bool);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && MemberValueCompare.IsSupported(p[0].ParameterType))
                    return p[0].ParameterType == typeof(bool);
            }

            return false;
        }

        private static bool IsIntType(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) ||
            t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
            t == typeof(sbyte) || t == typeof(ushort);

        private bool IsIntMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite) return IsIntType(prop.PropertyType);

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly) return IsIntType(field.FieldType);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && MemberValueCompare.IsSupported(p[0].ParameterType))
                    return IsIntType(p[0].ParameterType);
            }

            return false;
        }

        // MWC – detects whether the selected member accepts a string
        private bool IsStringMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite) return prop.PropertyType == typeof(string);

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly) return field.FieldType == typeof(string);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && MemberValueCompare.IsSupported(p[0].ParameterType))
                    return p[0].ParameterType == typeof(string);
            }

            return false;
        }

        // MWC – detects whether the selected member accepts a UnityEngine.Object subtype
        private bool IsObjectMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite) return MemberValueCompare.IsObjectType(prop.PropertyType);

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly) return MemberValueCompare.IsObjectType(field.FieldType);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && (MemberValueCompare.IsSupported(p[0].ParameterType) || MemberValueCompare.IsObjectType(p[0].ParameterType)))
                    return MemberValueCompare.IsObjectType(p[0].ParameterType);
            }

            return false;
        }

        // MWC – detects whether the selected member is a parameterless void method (action trigger)
        private bool IsVoidMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            // Value-bearing members take priority and are reported by the other Is*Member helpers.
            if (type.GetProperty(memberName, flags) != null) return false;
            if (type.GetField(memberName, flags) != null) return false;

            var m = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            return m != null && !m.IsSpecialName && m.ReturnType == typeof(void);
        }
    }


#if UNITY_EDITOR

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    [CustomPropertyDrawer(typeof(MemberValueSetter))]
    public class MemberValueSetterDrawer : PropertyDrawer
    {
        private const float ROW_H = 18f;
        private const float PAD = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int rows = IsVoidMember(property) ? 1 : 2; // [Target Type][Member] (+ Value); void methods need no value row
            return (ROW_H + PAD) * rows;
        }

        // MWC – static resolver for the drawer: is the selected member a parameterless void method?
        private static bool IsVoidMember(SerializedProperty property)
        {
            var asmQnProp = property.FindPropertyRelative("targetType").FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);
            if (type == null) return false;

            string name = property.FindPropertyRelative("memberName").stringValue;
            if (string.IsNullOrEmpty(name)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            if (type.GetProperty(name, flags) != null) return false;
            if (type.GetField(name, flags) != null) return false;

            var m = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
            return m != null && !m.IsSpecialName && m.ReturnType == typeof(void);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect Row(int i) => new(position.x, position.y + i * (ROW_H + PAD), position.width, ROW_H);
            int row = 0;

            // Row – [Target Type ▼][Member ▼] (Components only; the member is always written on the target GameObject)
            MemberDrawerUtility.SplitRow(Row(row++), out var typeRect, out var memberRect);
            MemberDrawerUtility.DrawTargetTypeButton(typeRect, property);

            var memberProp = property.FindPropertyRelative("memberName");
            (bool isBool, bool isInt, bool isString, bool isObject, bool isVoid) = DrawMemberPicker(memberRect, property, memberProp);

            // MWC – parameterless void method: no value row to draw
            if (isVoid) return;

            // Row – Set value
            var valueRow = Row(row);

            if (isBool)
            {
                // standard labelled toggle, so it sits on the same column as the Member button and the other value kinds
                EditorGUI.PropertyField(valueRow, property.FindPropertyRelative("setValueBool"), new GUIContent("Value"));
            }
            else if (isInt)
            {
                EditorGUI.PropertyField(valueRow, property.FindPropertyRelative("setValueInt"), new GUIContent("Value"));
            }
            else if (isString)
            {
                EditorGUI.PropertyField(valueRow, property.FindPropertyRelative("setValueString"), new GUIContent("Value")); // MWC – string field
            }
            else if (isObject) // MWC – Object field with proper type constraint
            {
                Type objType = ResolveObjectMemberType(property, memberProp.stringValue);
                var objProp = property.FindPropertyRelative("setValueObject");
                EditorGUI.BeginChangeCheck();
                var picked = EditorGUI.ObjectField(valueRow, new GUIContent("Value"), objProp.objectReferenceValue, objType ?? typeof(Object), true);
                if (EditorGUI.EndChangeCheck()) objProp.objectReferenceValue = picked;
            }
            else
            {
                EditorGUI.PropertyField(valueRow, property.FindPropertyRelative("setValue"), new GUIContent("Value"));
            }
        }

        /// <summary>Draws the member picker button (no label) and returns the kind of value the selected member takes.</summary>
        private static (bool isBool, bool isInt, bool isString, bool isObject, bool isVoid) DrawMemberPicker(Rect buttonRect, SerializedProperty parentProp, SerializedProperty memberProp)
        {
            var asmQnProp = parentProp.FindPropertyRelative("targetType")
                                       .FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);

            bool isBool = false, isInt = false, isString = false, isObject = false, isVoid = false;
            if (type != null && !string.IsNullOrEmpty(memberProp.stringValue))
                (isBool, isInt, isString, isObject, isVoid) = ResolveKind(type, memberProp.stringValue);

            string current = memberProp.stringValue;
            string display = string.IsNullOrEmpty(current) ? "Select Member…" : current;

            using (new EditorGUI.DisabledGroupScope(type == null))
            {
                if (EditorGUI.DropdownButton(buttonRect, new GUIContent(display), FocusType.Keyboard, EditorStyles.popup))
                {
                    if (type == null) return (isBool, isInt, isString, isObject, isVoid);

                    var capturedProp = memberProp;
                    var capturedSO = parentProp.serializedObject;

                    PopupWindow.Show(buttonRect, new MemberSetterPickerPopup(type, current, chosen =>
                    {
                        capturedSO.Update();
                        capturedProp.stringValue = chosen;
                        capturedSO.ApplyModifiedProperties();
                    }));
                }
            }

            return (isBool, isInt, isString, isObject, isVoid);
        }

        // MWC – resolves the exact UnityEngine.Object subtype of the selected member for the ObjectField constraint
        private static Type ResolveObjectMemberType(SerializedProperty property, string memberName)
        {
            var asmQnProp = property.FindPropertyRelative("targetType").FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);
            if (type == null || string.IsNullOrEmpty(memberName)) return typeof(Object);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite && MemberValueCompare.IsObjectType(prop.PropertyType)) return prop.PropertyType;

            var field = type.GetField(memberName, flags);
            if (field != null && MemberValueCompare.IsObjectType(field.FieldType)) return field.FieldType;

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName || m.ReturnType != typeof(void)) continue;
                var parms = m.GetParameters();
                if (parms.Length == 1 && MemberValueCompare.IsObjectType(parms[0].ParameterType)) return parms[0].ParameterType;
            }

            return typeof(Object);
        }

        // MWC – added isObject and isVoid return values
        private static (bool isBool, bool isInt, bool isString, bool isObject, bool isVoid) ResolveKind(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            var prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanWrite)
                return (prop.PropertyType == typeof(bool), IsIntType(prop.PropertyType), prop.PropertyType == typeof(string), MemberValueCompare.IsObjectType(prop.PropertyType), false);

            var field = type.GetField(name, flags);
            if (field != null && !field.IsInitOnly)
                return (field.FieldType == typeof(bool), IsIntType(field.FieldType), field.FieldType == typeof(string), MemberValueCompare.IsObjectType(field.FieldType), false);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != name) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && (MemberValueCompare.IsSupported(p[0].ParameterType) || MemberValueCompare.IsObjectType(p[0].ParameterType)))
                    return (p[0].ParameterType == typeof(bool), IsIntType(p[0].ParameterType), p[0].ParameterType == typeof(string), MemberValueCompare.IsObjectType(p[0].ParameterType), false);
            }

            // MWC – parameterless void method (action trigger)
            var voidMethod = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
            if (voidMethod != null && !voidMethod.IsSpecialName && voidMethod.ReturnType == typeof(void))
                return (false, false, false, false, true);

            return (false, false, false, false, false);
        }

        private static bool IsIntType(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) ||
            t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
            t == typeof(sbyte) || t == typeof(ushort);
    }

    // ─── Member Setter Picker Popup ───────────────────────────────────────────────
    //
    //  MWC – single flat list grouped under type title bars, matching the other pickers.
    //  No drill-down; search filters across all groups.

    public class MemberSetterPickerPopup : PopupWindowContent
    {
        // ─── Layout ──────────────────────────────────────────────────────────────

        private const float WIN_W = 280f;
        private const float WIN_H = 320f;
        private const float HEADER_H = 26f;
        private const float SEARCH_H = 22f;
        private const float ROW_H = 26f;
        private const float COUNT_H = 16f;
        private const float GROUP_H = 18f; // MWC – group title bar

        private static readonly Color C_HEADER_BG = new(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color C_GROUP_BG = new(0.10f, 0.10f, 0.10f, 1f);
        private static readonly Color C_ROW_SEL = new(0.18f, 0.37f, 0.73f, 0.55f);
        private static readonly Color C_ROW_HOVER = new(1f, 1f, 1f, 0.06f);

        // ─── Type → group ─────────────────────────────────────────────────────────

        private static string TypeGroup(Type t)
        {
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string"; // MWC – string group
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) ||
                t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
                t == typeof(sbyte) || t == typeof(ushort)) return "int";
            if (MemberValueCompare.IsObjectType(t)) return "Object"; // MWC – UnityEngine.Object group
            return "float";
        }

        // MWC – display order of the group title bars
        private static int GroupOrder(string group) => group switch
        {
            "bool" => 0,
            "int" => 1,
            "float" => 2,
            "string" => 3,
            "Object" => 4,
            "void" => 5,
            _ => 6,
        };

        // ─── State ───────────────────────────────────────────────────────────────

        private readonly string _current;
        private readonly Action<string> _onSelected;
        private readonly List<MemberEntry> _all;

        private string _search = string.Empty;
        private List<MemberEntry> _filtered;
        private int _hoveredIndex = -1;
        private Vector2 _scroll;

        private GUIStyle _sRow;
        private GUIStyle _sRowSel;
        private GUIStyle _sHeader;
        private GUIStyle _sCount;
        private GUIStyle _sGroup; // MWC – group title bar label

        // ─── Constructor ─────────────────────────────────────────────────────────

        public MemberSetterPickerPopup(Type type, string current, Action<string> onSelected)
        {
            _current = current;
            _onSelected = onSelected;
            _all = BuildAll(type);
            RebuildFilter();
        }

        // ─── PopupWindowContent ───────────────────────────────────────────────────

        public override Vector2 GetWindowSize() => new(WIN_W, WIN_H);

        public override void OnGUI(Rect rect)
        {
            InitStyles();

            DrawHeader("Set Member");
            DrawSearchBar();
            HandleKeyboard();
            DrawNameList();
        }

        // ─── List renderer (group title bars interleaved) ─────────────────────────

        // MWC – flat list; each group gets a title bar instead of its own drill-down level.
        private void DrawNameList()
        {
            EditorGUILayout.LabelField(
                $"{_filtered.Count} member{(_filtered.Count != 1 ? "s" : "")}",
                _sCount, GUILayout.Height(COUNT_H));

            float listH = WIN_H - HEADER_H - SEARCH_H - COUNT_H - 4f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listH));

            string lastGroup = null;
            int flatIndex = 0;

            foreach (var entry in _filtered)
            {
                // MWC – group title bar whenever the group changes
                if (entry.Group != lastGroup)
                {
                    lastGroup = entry.Group;
                    var groupRect = EditorGUILayout.GetControlRect(GUILayout.Height(GROUP_H));
                    EditorGUI.DrawRect(groupRect, C_GROUP_BG);
                    EditorGUI.LabelField(new Rect(groupRect.x + 8f, groupRect.y, groupRect.width - 8f, groupRect.height),
                                         lastGroup, _sGroup);
                }

                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));
                bool isCurrent = entry.Name == _current;
                bool isHovered = flatIndex == _hoveredIndex;

                if (isCurrent) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHovered) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                var nameRect = new Rect(rowRect.x + 10f, rowRect.y + 4f, rowRect.width - 10f, ROW_H - 4f);
                EditorGUI.LabelField(nameRect, entry.Name, isCurrent ? _sRowSel : _sRow);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIndex; editorWindow.Repaint(); }
                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                    Confirm(entry.Name);

                flatIndex++;
            }
            EditorGUILayout.EndScrollView();
        }

        // ─── Header ───────────────────────────────────────────────────────────────

        private void DrawHeader(string title)
        {
            var headerRect = EditorGUILayout.GetControlRect(GUILayout.Height(HEADER_H));
            EditorGUI.DrawRect(headerRect, C_HEADER_BG);

            var titleRect = new Rect(headerRect.x + 8f, headerRect.y, headerRect.width - 8f, headerRect.height);
            EditorGUI.LabelField(titleRect, title, _sHeader);
        }

        // ─── Search bar ───────────────────────────────────────────────────────────

        private void DrawSearchBar()
        {
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("MemberSetterSearch");
            var searchRect = EditorGUILayout.GetControlRect(GUILayout.Height(SEARCH_H));
            _search = EditorGUI.TextField(searchRect, _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) { _hoveredIndex = -1; RebuildFilter(); }
            EditorGUI.FocusTextInControl("MemberSetterSearch");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // MWC – single flat source; search filters across every group
        private void RebuildFilter()
        {
            if (string.IsNullOrWhiteSpace(_search)) { _filtered = _all; return; }

            string q = _search.Trim();
            _filtered = _all.FindAll(e => e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Confirm(string name) { _onSelected?.Invoke(name); editorWindow.Close(); }

        // MWC – no drill-down levels left, so navigation is a plain flat-list walk
        private void HandleKeyboard()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;

            if (ev.keyCode == KeyCode.DownArrow)
            { _hoveredIndex = Mathf.Min(_hoveredIndex + 1, _filtered.Count - 1); ev.Use(); }
            else if (ev.keyCode == KeyCode.UpArrow)
            { _hoveredIndex = Mathf.Max(_hoveredIndex - 1, 0); ev.Use(); }
            else if (ev.keyCode == KeyCode.Return && _hoveredIndex >= 0 && _hoveredIndex < _filtered.Count)
            { Confirm(_filtered[_hoveredIndex].Name); ev.Use(); }
            else if (ev.keyCode == KeyCode.Escape)
            { editorWindow.Close(); ev.Use(); }
        }

        // ─── Data builder ─────────────────────────────────────────────────────────

        private static List<MemberEntry> BuildAll(Type type)
        {
            var list = new List<MemberEntry>(32);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var prop in type.GetProperties(flags))
            {
                if (!prop.CanWrite) continue;
                if (!MemberValueCompare.IsSupported(prop.PropertyType) && !MemberValueCompare.IsObjectType(prop.PropertyType)) continue; // MWC – include Object types
                list.Add(new(TypeGroup(prop.PropertyType), prop.Name));
            }

            foreach (var field in type.GetFields(flags))
            {
                if (field.IsInitOnly || field.IsLiteral) continue;
                if (!MemberValueCompare.IsSupported(field.FieldType) && !MemberValueCompare.IsObjectType(field.FieldType)) continue; // MWC – include Object types
                list.Add(new(TypeGroup(field.FieldType), field.Name));
            }

            foreach (var method in type.GetMethods(flags))
            {
                if (method.IsSpecialName) continue;
                if (method.ReturnType != typeof(void)) continue;
                var parms = method.GetParameters();
                if (parms.Length == 0) // MWC – parameterless void method (action trigger)
                {
                    list.Add(new("void", method.Name));
                    continue;
                }
                if (parms.Length != 1) continue;
                if (!MemberValueCompare.IsSupported(parms[0].ParameterType) && !MemberValueCompare.IsObjectType(parms[0].ParameterType)) continue; // MWC – include Object types
                list.Add(new(TypeGroup(parms[0].ParameterType), method.Name));
            }

            // MWC – group first, then alphabetical inside each group, so the title bars
            // come out in a stable order.
            list.Sort((a, b) =>
            {
                int ga = GroupOrder(a.Group), gb = GroupOrder(b.Group);
                if (ga != gb) return ga.CompareTo(gb);
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
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
            _sCount ??= new(EditorStyles.centeredGreyMiniLabel);
            // MWC – group title bar label, same look as the Member Set picker
            _sGroup ??= new(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = new Color(0.6f, 0.8f, 1f) }
            };
        }
    }

#endif
}