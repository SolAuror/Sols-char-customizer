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
    public enum ComparerBoolValue { True = 0, False = 1 }

    // MWC – argument type for single-arg method overloads
    public enum MethodArgType { None, String, Bool, Int, Float }

    [Serializable]
    public class MemberValueCompare
    {
        /// <summary>Component type the member is read from. Always resolved from the target GameObject.</summary>
        [TypeFilter(typeof(Component))]
        public SerializableType targetType;

        public string memberName;
        public ComparerNumber comparer;
        public ComparerBoolValue comparerBool;
        public float compareValue;

        // MWC – serialized fields for single-argument method support
        public MethodArgType methodArgType;
        public string methodArgString;
        public bool methodArgBool;
        public int methodArgInt;
        public float methodArgFloat;

        // ─── Runtime cache (not serialized) ──────────────────────────────────────

        private Component _resolvedTarget; // component the member is read from
        private Object _targetObject;      // last target passed to SetTarget
        private PropertyInfo _cachedProperty;
        private FieldInfo _cachedField;
        private MethodInfo _cachedMethod;
        private string _cachedMemberName;
        private bool _cachedIsBool;
        private MethodArgType _cachedArgType; // MWC – track arg type to invalidate cache when it changes

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

        /// <summary>Finds the Target Type component on the target GameObject itself (plain GetComponent) and rebuilds the cache.</summary>
        private void Resolve()
        {
            _resolvedTarget = null;
            var type = targetType?.Type;

            if (type != null)
            {
                var go = (_targetObject as GameObject) ?? (_targetObject as Component)?.gameObject;
                if (go != null) _resolvedTarget = go.GetComponent(type);
            }

            RebuildMemberCache();
        }

        public bool Evaluate()
        {
            if (_resolvedTarget == null) return false;

            // MWC – also invalidate when the arg type changes (e.g. user edits in inspector)
            if (_cachedMemberName != memberName || _cachedArgType != methodArgType) RebuildMemberCache();

            if (_cachedProperty == null && _cachedField == null && _cachedMethod == null) return false;

            object raw = _cachedProperty != null
                ? _cachedProperty.GetValue(_resolvedTarget)
                : _cachedField != null
                    ? _cachedField.GetValue(_resolvedTarget)
                    : _cachedMethod.Invoke(_resolvedTarget, BuildMethodArgs()); // MWC – pass args for 1-arg methods

            if (_cachedIsBool)
            {
                bool val = (bool)raw;
                return comparerBool switch
                {
                    ComparerBoolValue.True => val,
                    ComparerBoolValue.False => !val,
                    _ => false
                };
            }
            else
            {
                float val = Convert.ToSingle(raw);
                return comparer switch
                {
                    ComparerNumber.Equal => Mathf.Approximately(val, compareValue),
                    ComparerNumber.NotEqual => !Mathf.Approximately(val, compareValue),
                    ComparerNumber.Greater => val > compareValue,
                    ComparerNumber.Less => val < compareValue,
                    ComparerNumber.GreaterEqual => val >= compareValue,
                    ComparerNumber.LessEqual => val <= compareValue,
                    _ => false
                };
            }
        }

        public string Description()
        {
            string typeName = targetType?.Type?.Name ?? "?";
            string memberText = string.IsNullOrEmpty(memberName) ? "?" : memberName;
            // MWC – show the argument value in the description when a 1-arg method is used
            string argText = methodArgType != MethodArgType.None ? $"({DescribeArg()})" : "";

            if (IsBoolType())
                return $"{typeName}.{memberText}{argText} is {comparerBool}";

            return $"{typeName}.{memberText}{argText} {ComparerSymbol(comparer)} {compareValue}";
        }

        private static string ComparerSymbol(ComparerNumber c) => c switch
        {
            ComparerNumber.Equal => "==",
            ComparerNumber.NotEqual => "!=",
            ComparerNumber.Greater => ">",
            ComparerNumber.Less => "<",
            ComparerNumber.GreaterEqual => ">=",
            ComparerNumber.LessEqual => "<=",
            _ => c.ToString()
        };

        /// <summary>Resolves whether the serialized member is a bool without relying on the runtime cache.</summary>
        private bool IsBoolType()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null) return prop.PropertyType == typeof(bool);

            var field = type.GetField(memberName, flags);
            if (field != null) return field.FieldType == typeof(bool);

            var method = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            if (method != null) return method.ReturnType == typeof(bool);

            // MWC – also check 1-arg method overload
            Type argT = GetArgClrType();
            if (argT != null)
            {
                var m1 = type.GetMethod(memberName, flags, null, new[] { argT }, null);
                if (m1 != null) return m1.ReturnType == typeof(bool);
            }
            return false;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private void RebuildMemberCache()
        {
            _cachedProperty = null;
            _cachedField = null;
            _cachedMethod = null;
            _cachedMemberName = memberName;
            _cachedArgType = methodArgType; // MWC – record arg type at cache time
            _cachedIsBool = false;

            if (_resolvedTarget == null || string.IsNullOrEmpty(memberName) || targetType?.Type == null) return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanRead && IsSupported(prop.PropertyType) && prop.GetIndexParameters().Length == 0)
            {
                _cachedProperty = prop;
                _cachedIsBool = prop.PropertyType == typeof(bool);
                return;
            }

            var field = type.GetField(memberName, flags);
            if (field != null && IsSupported(field.FieldType))
            {
                _cachedField = field;
                _cachedIsBool = field.FieldType == typeof(bool);
                return;
            }

            // zero-arg method
            var method = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            if (method != null && IsSupported(method.ReturnType))
            {
                _cachedMethod = method;
                _cachedIsBool = method.ReturnType == typeof(bool);
                return;
            }

            // MWC – 1-arg method: look for overload matching the stored argument type
            Type argT = GetArgClrType();
            if (argT != null)
            {
                var method1 = type.GetMethod(memberName, flags, null, new[] { argT }, null);
                if (method1 != null && IsSupported(method1.ReturnType))
                {
                    _cachedMethod = method1;
                    _cachedIsBool = method1.ReturnType == typeof(bool);
                }
            }
        }

        // MWC – returns the CLR Type for the stored MethodArgType, or null for None
        private Type GetArgClrType() => methodArgType switch
        {
            MethodArgType.String => typeof(string),
            MethodArgType.Bool => typeof(bool),
            MethodArgType.Int => typeof(int),
            MethodArgType.Float => typeof(float),
            _ => null
        };

        // MWC – builds the invocation argument array for 1-arg methods
        private object[] BuildMethodArgs() => methodArgType switch
        {
            MethodArgType.String => new object[] { methodArgString },
            MethodArgType.Bool => new object[] { methodArgBool },
            MethodArgType.Int => new object[] { methodArgInt },
            MethodArgType.Float => new object[] { methodArgFloat },
            _ => null
        };

        // MWC – human-readable argument value for Description()
        private string DescribeArg() => methodArgType switch
        {
            MethodArgType.String => $"\"{methodArgString}\"",
            MethodArgType.Bool => methodArgBool.ToString().ToLower(),
            MethodArgType.Int => methodArgInt.ToString(),
            MethodArgType.Float => methodArgFloat.ToString("F2"),
            _ => ""
        };

        internal static bool IsNumeric(Type t) =>
            t == typeof(int) || t == typeof(float) || t == typeof(double) ||
            t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
            t == typeof(uint) || t == typeof(ulong) || t == typeof(sbyte) ||
            t == typeof(ushort);

        internal static bool IsSupported(Type t) => t == typeof(bool) || t == typeof(string) || IsNumeric(t); // MWC – added string support

        // MWC – true for any UnityEngine.Object subtype (Transform, GameObject, ScriptableObject, …)
        internal static bool IsObjectType(Type t) => t != null && typeof(Object).IsAssignableFrom(t);

        // MWC – types accepted as a single method argument
        internal static bool IsArgSupported(Type t) =>
            t == typeof(string) || t == typeof(bool) || t == typeof(int) || t == typeof(float);

    }


#if UNITY_EDITOR

    // ─── Shared drawer helpers ───────────────────────────────────────────────────

    // Shared between the MemberValueCompare / MemberValueSetter / MemberValuesSetter / MemberValueGetter
    // drawers so the [Target Type ▼][Member ▼] row behaves identically in all of them.
    internal static class MemberDrawerUtility
    {
        private const float GAP = 4f;

        /// <summary>Splits a row at the inspector label width, so the right part lines up with the value fields drawn below it.</summary>
        public static void SplitRow(Rect r, out Rect left, out Rect right)
        {
            float lw = Mathf.Min(EditorGUIUtility.labelWidth, r.width * 0.5f);
            left = new Rect(r.x, r.y, lw - GAP, r.height);
            right = new Rect(r.x + lw, r.y, r.width - lw, r.height);
        }

        /// <summary>Target Type dropdown (no label): a searchable picker listing concrete Component types.</summary>
        public static void DrawTargetTypeButton(Rect buttonRect, SerializedProperty parentProp)
        {
            var aqnProp = parentProp.FindPropertyRelative("targetType").FindPropertyRelative("assemblyQualifiedName");

            string currentAQN = aqnProp.stringValue;
            string display;

            if (string.IsNullOrEmpty(currentAQN))
            {
                display = "Select Type…";
            }
            else
            {
                var resolved = Type.GetType(currentAQN);
                display = resolved != null ? resolved.Name : currentAQN.Split(',')[0].Trim();
            }

            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(display), FocusType.Keyboard, EditorStyles.popup))
            {
                var capturedProp = aqnProp;
                var capturedSO = parentProp.serializedObject;

                PopupWindow.Show(buttonRect, new SerializableTypePickerPopup(currentAQN, ComponentFilter, chosen =>
                {
                    capturedSO.Update();
                    capturedProp.stringValue = chosen.AssemblyQualifiedName ?? chosen.FullName ?? chosen.Name;
                    capturedSO.ApplyModifiedProperties();
                }));
            }
        }

        /// <summary>Only concrete, non-generic Component types are selectable.</summary>
        private static readonly Func<Type, bool> ComponentFilter =
            t => !t.IsAbstract && !t.IsInterface && !t.IsGenericType && typeof(Component).IsAssignableFrom(t);
    }

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    [CustomPropertyDrawer(typeof(MemberValueCompare))]
    public class MemberValueCompareDrawer : PropertyDrawer
    {
        private const float ROW_H = 18f;
        private const float PAD = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int rows = 2; // [Target Type][Member] + [Comparer][Value]
            // extra row for the argument value when a 1-arg method is selected
            if (HasMethodArg(property)) rows++;
            return (ROW_H + PAD) * rows;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect Row(int i) => new(position.x, position.y + i * (ROW_H + PAD), position.width, ROW_H);
            int row = 0;

            // Row – [Target Type ▼][Member ▼]
            MemberDrawerUtility.SplitRow(Row(row++), out var typeRect, out var memberRect);
            MemberDrawerUtility.DrawTargetTypeButton(typeRect, property);

            var memberProp = property.FindPropertyRelative("memberName");
            bool isBool = DrawMemberPicker(memberRect, property, memberProp);

            // Row – argument value (only when a 1-arg method is selected)
            if (HasMethodArg(property))
                DrawArgValueRow(Row(row++), property);

            // Row – [Comparer ▼][Value]; a bool member has no separate value, so its True/False comparer
            // is drawn as a labelled "Value" field, aligned with the Argument row above it.
            if (isBool)
            {
                EditorGUI.PropertyField(Row(row), property.FindPropertyRelative("comparerBool"), new GUIContent("Value"));
            }
            else
            {
                MemberDrawerUtility.SplitRow(Row(row), out var comparerRect, out var valueRect);
                EditorGUI.PropertyField(comparerRect, property.FindPropertyRelative("comparer"), GUIContent.none);
                EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("compareValue"), GUIContent.none);
            }
        }

        // MWC – returns true when methodArgType != None (i.e. a 1-arg method is stored)
        private static bool HasMethodArg(SerializedProperty property)
        {
            var p = property.FindPropertyRelative("methodArgType");
            return p != null && p.enumValueIndex != (int)MethodArgType.None;
        }

        // MWC – draws the correct argument field depending on methodArgType, labelled with the parameter's own name
        private static void DrawArgValueRow(Rect r, SerializedProperty property)
        {
            var argTypeProp = property.FindPropertyRelative("methodArgType");
            var lbl = new GUIContent(ArgParameterName(property) ?? "Argument");
            switch ((MethodArgType)argTypeProp.enumValueIndex)
            {
                case MethodArgType.String: EditorGUI.PropertyField(r, property.FindPropertyRelative("methodArgString"), lbl); break;
                case MethodArgType.Bool: EditorGUI.PropertyField(r, property.FindPropertyRelative("methodArgBool"), lbl); break;
                case MethodArgType.Int: EditorGUI.PropertyField(r, property.FindPropertyRelative("methodArgInt"), lbl); break;
                case MethodArgType.Float: EditorGUI.PropertyField(r, property.FindPropertyRelative("methodArgFloat"), lbl); break;
            }
        }

        /// <summary>Nicified name of the single parameter of the selected 1-arg method, or null when it cannot be resolved.</summary>
        private static string ArgParameterName(SerializedProperty property)
        {
            Type argT = GetArgClrTypeFromProp(property);
            if (argT == null) return null;

            var aqn = property.FindPropertyRelative("targetType").FindPropertyRelative("assemblyQualifiedName").stringValue;
            Type type = Type.GetType(aqn);
            string name = property.FindPropertyRelative("memberName").stringValue;
            if (type == null || string.IsNullOrEmpty(name)) return null;

            var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, new[] { argT }, null);
            var ps = m?.GetParameters();
            return ps == null || ps.Length == 0 ? null : ObjectNames.NicifyVariableName(ps[0].Name);
        }

        // MWC – reads methodArgType from a SerializedProperty and returns the matching CLR Type
        private static Type GetArgClrTypeFromProp(SerializedProperty property)
        {
            var p = property.FindPropertyRelative("methodArgType");
            if (p == null) return null;
            return (MethodArgType)p.enumValueIndex switch
            {
                MethodArgType.String => typeof(string),
                MethodArgType.Bool => typeof(bool),
                MethodArgType.Int => typeof(int),
                MethodArgType.Float => typeof(float),
                _ => null
            };
        }

        /// <summary>Draws the member picker button (no label) and returns true if the selected member is a bool.</summary>
        private static bool DrawMemberPicker(Rect buttonRect, SerializedProperty parentProp, SerializedProperty memberProp)
        {
            var asmQnProp = parentProp.FindPropertyRelative("targetType")
                                       .FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);

            // MWC – resolve isBool for all member kinds (property, field, 0-arg and 1-arg methods)
            bool isBool = false;
            if (type != null && !string.IsNullOrEmpty(memberProp.stringValue))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
                var prop = type.GetProperty(memberProp.stringValue, flags);
                if (prop != null) isBool = prop.PropertyType == typeof(bool);
                else
                {
                    var field = type.GetField(memberProp.stringValue, flags);
                    if (field != null) isBool = field.FieldType == typeof(bool);
                }
                if (!isBool)
                {
                    var mZero = type.GetMethod(memberProp.stringValue, flags, null, Type.EmptyTypes, null);
                    if (mZero != null) isBool = mZero.ReturnType == typeof(bool);
                }
                if (!isBool)
                {
                    Type argT = GetArgClrTypeFromProp(parentProp);
                    if (argT != null)
                    {
                        var m1 = type.GetMethod(memberProp.stringValue, flags, null, new[] { argT }, null);
                        if (m1 != null) isBool = m1.ReturnType == typeof(bool);
                    }
                }
            }

            string current = memberProp.stringValue;
            string display = string.IsNullOrEmpty(current) ? "Select Member…" : current;

            using (new EditorGUI.DisabledGroupScope(type == null))
            {
                if (EditorGUI.DropdownButton(buttonRect, new GUIContent(display), FocusType.Keyboard, EditorStyles.popup))
                {
                    if (type == null) return isBool;

                    var capturedMemberProp = memberProp;
                    var capturedParentProp = parentProp;
                    var capturedSO = parentProp.serializedObject;

                    // MWC – callback now receives the full MemberEntry so we can also set methodArgType
                    PopupWindow.Show(buttonRect, new MemberPickerPopup(type, current, chosen =>
                    {
                        capturedSO.Update();
                        capturedMemberProp.stringValue = chosen.Name;

                        var argTypeProp = capturedParentProp.FindPropertyRelative("methodArgType");
                        if (chosen.ParamType == null)
                            argTypeProp.intValue = (int)MethodArgType.None;
                        else if (chosen.ParamType == typeof(string))
                            argTypeProp.intValue = (int)MethodArgType.String;
                        else if (chosen.ParamType == typeof(bool))
                            argTypeProp.intValue = (int)MethodArgType.Bool;
                        else if (chosen.ParamType == typeof(int))
                            argTypeProp.intValue = (int)MethodArgType.Int;
                        else
                            argTypeProp.intValue = (int)MethodArgType.Float;

                        capturedSO.ApplyModifiedProperties();
                    }));
                }
            }

            return isBool;
        }
    }

    // ─── Member entry ─────────────────────────────────────────────────────────────

    // MWC – made public so Action<MemberEntry> is accessible from the public MemberPickerPopup constructor
    public readonly struct MemberEntry
    {
        public readonly string Group;
        public readonly string Name;
        // MWC – non-null for 1-arg methods; null for properties, fields, and zero-arg methods
        public readonly Type ParamType;

        public MemberEntry(string group, string name, Type paramType = null)
        {
            Group = group;
            Name = name;
            ParamType = paramType;
        }

        // MWC – display label includes the argument type in parentheses for 1-arg methods
        public string DisplayName => ParamType == null ? Name : $"{Name}  ({ShortArgTypeName(ParamType)})";

        private static string ShortArgTypeName(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            return t.Name;
        }
    }

    // ─── Member Picker Popup ──────────────────────────────────────────────────────
    //
    //  MWC – single flat list grouped under [bool] / [int] / [float] title bars,
    //  matching the Member Set picker. No drill-down; search filters across all groups.

    public class MemberPickerPopup : PopupWindowContent
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

        // ─── Type → group mapping ─────────────────────────────────────────────────

        internal static string TypeGroup(Type t)
        {
            if (t == typeof(bool)) return "bool";
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) ||
                t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
                t == typeof(sbyte) || t == typeof(ushort)) return "int";
            // float + double → "float"
            return "float";
        }

        // MWC – display order of the group title bars: bool, then int, then float
        internal static int GroupOrder(string group) => group switch
        {
            "bool" => 0,
            "int" => 1,
            "float" => 2,
            _ => 3,
        };

        // ─── State ───────────────────────────────────────────────────────────────

        private readonly string _current;
        // MWC – callback receives the full MemberEntry (name + optional param type)
        private readonly Action<MemberEntry> _onSelected;
        private readonly List<MemberEntry> _all;

        // Search
        private string _search = string.Empty;
        private List<MemberEntry> _filtered;
        private int _hoveredIndex = -1;
        private Vector2 _scroll;

        // Styles
        private GUIStyle _sRow;
        private GUIStyle _sRowSel;
        private GUIStyle _sHeader;
        private GUIStyle _sCount;
        private GUIStyle _sGroup; // MWC – group title bar label

        // ─── Constructor ─────────────────────────────────────────────────────────

        // MWC – callback is now Action<MemberEntry> to carry the param type alongside the name
        public MemberPickerPopup(Type type, string current, Action<MemberEntry> onSelected)
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

            DrawHeader("Member");
            DrawSearchBar();
            HandleKeyboard();
            DrawNameList();
        }

        // ─── List renderer (group title bars interleaved) ─────────────────────────

        // MWC – flat list; each group gets a [bool] / [int] / [float] title bar instead
        // of its own drill-down level.
        private void DrawNameList()
        {
            EditorGUILayout.LabelField(
                $"{_filtered.Count} member{(_filtered.Count != 1 ? "s" : "")}",
                _sCount, GUILayout.Height(COUNT_H));

            float used = HEADER_H + SEARCH_H + COUNT_H + 4f;
            float listH = WIN_H - used;
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
                // MWC – show DisplayName so 1-arg methods show their argument type
                EditorGUI.LabelField(nameRect, entry.DisplayName, isCurrent ? _sRowSel : _sRow);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIndex; editorWindow.Repaint(); }
                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                    Confirm(entry); // MWC – pass full entry

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
            GUI.SetNextControlName("MemberSearch");
            var searchRect = EditorGUILayout.GetControlRect(GUILayout.Height(SEARCH_H));
            _search = EditorGUI.TextField(searchRect, _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) { _hoveredIndex = -1; RebuildFilter(); }
            EditorGUI.FocusTextInControl("MemberSearch");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // MWC – single flat source; search filters across every group
        private void RebuildFilter()
        {
            if (string.IsNullOrWhiteSpace(_search))
            {
                _filtered = _all;
                return;
            }

            string q = _search.Trim();
            // MWC – search against DisplayName so arg type suffix is also searchable
            _filtered = _all.FindAll(e => e.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // MWC – pass full MemberEntry to callback
        private void Confirm(MemberEntry entry) { _onSelected?.Invoke(entry); editorWindow.Close(); }

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
            { Confirm(_filtered[_hoveredIndex]); ev.Use(); } // MWC – pass entry
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
                if (!prop.CanRead) continue;
                if (!MemberValueCompare.IsSupported(prop.PropertyType)) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                list.Add(new(TypeGroup(prop.PropertyType), prop.Name));
            }

            foreach (var field in type.GetFields(flags))
            {
                if (!MemberValueCompare.IsSupported(field.FieldType)) continue;
                list.Add(new(TypeGroup(field.FieldType), field.Name));
            }

            foreach (var method in type.GetMethods(flags))
            {
                if (method.IsSpecialName) continue;
                if (!MemberValueCompare.IsSupported(method.ReturnType)) continue;

                var parms = method.GetParameters();

                if (parms.Length == 0)
                {
                    list.Add(new(TypeGroup(method.ReturnType), method.Name));
                }
                // MWC – include methods with exactly 1 argument of a supported type
                else if (parms.Length == 1 && MemberValueCompare.IsArgSupported(parms[0].ParameterType))
                {
                    list.Add(new(TypeGroup(method.ReturnType), method.Name, parms[0].ParameterType));
                }
            }

            // MWC – group first (bool → int → float), then alphabetical inside each group,
            // so the title bars come out in a stable order.
            list.Sort((a, b) =>
            {
                int ga = GroupOrder(a.Group), gb = GroupOrder(b.Group);
                if (ga != gb) return ga.CompareTo(gb);
                return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
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
