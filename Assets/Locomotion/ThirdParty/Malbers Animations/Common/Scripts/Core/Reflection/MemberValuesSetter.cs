using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

// MWC – New, general-purpose member setter (Made With Claude).
// Generalizes MemberValueSetter: targets any writable property/field or any method
// (any return type, including void) taking up to 4 arguments, with strongly-typed,
// properly-cast values. Reflection is resolved and the argument values pre-boxed once
// into a reusable buffer so Apply() performs zero per-call allocation.
namespace MalbersAnimations
{
    // ─── Argument value kinds ────────────────────────────────────────────────────
    // Drives which serialized field is active and how the value is cast to the
    // parameter's CLR type. Numeric types collapse into Int/Float; every enum maps
    // to Enum; every UnityEngine.Object subtype maps to Object.
    public enum MArgKind
    {
        Float, Int, Bool, String, Object, Vector2, Vector3, Vector4, Color, Quaternion, Enum
    }

    /// <summary>
    /// One serialized argument value. A single compact record backs every supported
    /// type: a <see cref="double"/> covers all numerics and enums, a <see cref="Vector4"/>
    /// covers Vector2/3/4, Color (rgba) and Quaternion (xyzw).
    /// </summary>
    [Serializable]
    public class MArgument
    {
        public MArgKind kind;            // active value field + cast strategy
        public string typeName;          // assembly-qualified CLR type of the target parameter
        public string label;             // parameter name (display only)

        public double numberValue;       // numerics + enum (stored as its integer value)
        public bool boolValue;
        public string stringValue;
        public Object objectValue;       // UnityEngine.Object reference
        public Vector4 vectorValue;      // Vector2/3/4 + Color (rgba) + Quaternion (xyzw)

        /// <summary>Boxes this argument as the exact value the target parameter expects.</summary>
        public object ToValue(Type paramType)
        {
            switch (kind)
            {
                case MArgKind.Bool: return boolValue;
                case MArgKind.String: return stringValue;
                case MArgKind.Object:
                    // MWC – Collapse Unity "fake null"/missing references to a real C# null and
                    // never hand Invoke a reference the parameter can't accept – either case
                    // would otherwise throw inside reflection's argument conversion.
                    if (objectValue == null) return null;
                    if (paramType != null && !paramType.IsInstanceOfType(objectValue)) return null;
                    return objectValue;
                case MArgKind.Vector2: return new Vector2(vectorValue.x, vectorValue.y);
                case MArgKind.Vector3: return new Vector3(vectorValue.x, vectorValue.y, vectorValue.z);
                case MArgKind.Vector4: return vectorValue;
                case MArgKind.Color: return new Color(vectorValue.x, vectorValue.y, vectorValue.z, vectorValue.w);
                case MArgKind.Quaternion: return new Quaternion(vectorValue.x, vectorValue.y, vectorValue.z, vectorValue.w);
                case MArgKind.Enum:
                    return paramType != null && paramType.IsEnum
                        ? Enum.ToObject(paramType, (long)numberValue)
                        : (int)numberValue;
                default: // Int / Float and any numeric parameter
                    if (paramType == null) return numberValue;
                    try { return Convert.ChangeType(numberValue, paramType, CultureInfo.InvariantCulture); }
                    catch { return numberValue; }
            }
        }

        /// <summary>Human-readable value for <see cref="MemberValuesSetter.Description"/>.</summary>
        public string Describe() => kind switch
        {
            MArgKind.Bool => boolValue ? "true" : "false",
            MArgKind.String => $"\"{stringValue}\"",
            MArgKind.Object => objectValue != null ? objectValue.name : "null",
            MArgKind.Vector2 => $"({vectorValue.x},{vectorValue.y})",
            MArgKind.Vector3 => $"({vectorValue.x},{vectorValue.y},{vectorValue.z})",
            MArgKind.Vector4 => vectorValue.ToString(),
            MArgKind.Color => new Color(vectorValue.x, vectorValue.y, vectorValue.z, vectorValue.w).ToString(),
            MArgKind.Quaternion => new Quaternion(vectorValue.x, vectorValue.y, vectorValue.z, vectorValue.w).eulerAngles.ToString(),
            MArgKind.Enum => ResolveEnumName(),
            MArgKind.Int => ((long)numberValue).ToString(),
            _ => numberValue.ToString("0.###", CultureInfo.InvariantCulture),
        };

        private string ResolveEnumName()
        {
            var t = string.IsNullOrEmpty(typeName) ? null : Type.GetType(typeName);
            if (t != null && t.IsEnum) return Enum.ToObject(t, (long)numberValue).ToString();
            return ((long)numberValue).ToString();
        }
    }

    // ─── The kind of member being driven ─────────────────────────────────────────
    public enum MMemberKind { None, Property, Field, Method }

    [Serializable]
    public class MemberValuesSetter
    {
        /// <summary>Component type the member is written to. Always resolved from the target GameObject.</summary>
        [TypeFilter(typeof(Component))]
        public SerializableType targetType;

        public string memberName;
        public MMemberKind memberKind = MMemberKind.None;

        // One entry per parameter (0..4). For a property/field this holds a single
        // entry typed to the member's value type.
        public MArgument[] arguments = Array.Empty<MArgument>();

        // ─── Runtime cache (not serialized) ──────────────────────────────────────

        private static readonly object[] NoArgs = Array.Empty<object>();

        private Component _resolvedTarget; // component the member is written to
        private Object _targetObject;      // last target passed to SetTarget
        private PropertyInfo _property;
        private FieldInfo _field;
        private MethodInfo _method;
        private Type[] _paramTypes;
        private object[] _boxedArgs;          // reused every Apply – no per-call allocation
        private bool _cacheBuilt;
        private bool _valid;
        private string _cachedMemberName;
        private MMemberKind _cachedKind;

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>Resolves the target instance from a GameObject/Component and rebuilds the cache.</summary>
        public void SetTarget(Object target)
        {
            _resolvedTarget = null;
            _cacheBuilt = false;
            _valid = false;

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

            BuildCache();
        }

        /// <summary>Applies the configured member assignment / method invocation on the cached target.</summary>
        public void Apply()
        {
            if (_resolvedTarget == null) return;

            // Cheap invalidation check (ref + enum compare, no allocation).
            if (!_cacheBuilt || _cachedMemberName != memberName || _cachedKind != memberKind)
                BuildCache();

            if (!_valid) return;

            switch (memberKind)
            {
                case MMemberKind.Property: _property.SetValue(_resolvedTarget, _boxedArgs[0]); break;
                case MMemberKind.Field: _field.SetValue(_resolvedTarget, _boxedArgs[0]); break;
                case MMemberKind.Method: _method.Invoke(_resolvedTarget, _boxedArgs); break;
            }
        }

        /// <summary>Resolves the target then applies in one call.</summary>
        public void Apply(Object target)
        {
            SetTarget(target);
            Apply();
        }

        /// <summary>
        /// Re-boxes the argument values. Call this only if you mutate the serialized
        /// argument values at runtime – values are otherwise boxed once for performance.
        /// </summary>
        public void RefreshValues()
        {
            if (!_valid || _paramTypes == null) return;
            BuildBoxedArgs();
        }

        public string Description()
        {
            string typeName = targetType?.Type?.Name ?? "?";
            string member = string.IsNullOrEmpty(memberName) ? "?" : memberName;

            if (memberKind == MMemberKind.Method)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(arguments[i].Describe());
                }
                return $"{typeName}.{member}({sb})";
            }

            string value = arguments.Length > 0 ? arguments[0].Describe() : "?";
            return $"{typeName}.{member} = {value}";
        }

        // ─── Cache building ─────────────────────────────────────────────────────────

        private void BuildCache()
        {
            _cacheBuilt = true;
            _valid = false;
            _cachedMemberName = memberName;
            _cachedKind = memberKind;
            _property = null;
            _field = null;
            _method = null;
            _paramTypes = null;
            _boxedArgs = null;

            if (_resolvedTarget == null || targetType?.Type == null ||
                string.IsNullOrEmpty(memberName) || memberKind == MMemberKind.None)
                return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            switch (memberKind)
            {
                case MMemberKind.Property:
                    _property = type.GetProperty(memberName, flags);
                    if (_property == null || !_property.CanWrite) return;
                    _paramTypes = new[] { _property.PropertyType };
                    break;

                case MMemberKind.Field:
                    _field = type.GetField(memberName, flags);
                    if (_field == null || _field.IsInitOnly || _field.IsLiteral) return;
                    _paramTypes = new[] { _field.FieldType };
                    break;

                case MMemberKind.Method:
                    // Resolve the exact overload from the stored parameter types.
                    Type[] sig = ResolveParamTypes();
                    if (sig == null) return;
                    _method = type.GetMethod(memberName, flags, null, sig, null);
                    if (_method == null) return;
                    _paramTypes = sig;
                    break;
            }

            BuildBoxedArgs();
            _valid = true;
        }

        // Pre-boxes every argument value once, into a reusable buffer.
        private void BuildBoxedArgs()
        {
            int count = _paramTypes.Length;
            if (count == 0) { _boxedArgs = NoArgs; return; }

            if (_boxedArgs == null || _boxedArgs.Length != count)
                _boxedArgs = new object[count];

            for (int i = 0; i < count; i++)
            {
                var arg = i < arguments.Length ? arguments[i] : null;
                _boxedArgs[i] = arg != null ? arg.ToValue(_paramTypes[i]) : GetDefault(_paramTypes[i]);
            }
        }

        // Resolves the CLR parameter types from the serialized argument metadata.
        private Type[] ResolveParamTypes()
        {
            if (arguments.Length == 0) return Type.EmptyTypes;

            var sig = new Type[arguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                if (string.IsNullOrEmpty(arguments[i].typeName)) return null;
                var t = Type.GetType(arguments[i].typeName);
                if (t == null) return null;
                sig[i] = t;
            }
            return sig;
        }

        private static object GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;
    }


#if UNITY_EDITOR

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    [CustomPropertyDrawer(typeof(MemberValuesSetter))]
    public class MemberValuesSetterDrawer : PropertyDrawer
    {
        private const float ROW_H = 18f;
        private const float PAD = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // [Target Type][Member] row, plus one row per argument.
            int argCount = property.FindPropertyRelative("arguments").arraySize;
            int rows = 1 + argCount;
            return (ROW_H + PAD) * rows;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect Row(int i) => new(position.x, position.y + i * (ROW_H + PAD), position.width, ROW_H);
            int row = 0;

            // Row – [Target Type ▼][Member ▼] (Components only; the member is always written on the target GameObject)
            MemberDrawerUtility.SplitRow(Row(row++), out var typeRect, out var memberRect);
            MemberDrawerUtility.DrawTargetTypeButton(typeRect, property);
            DrawMemberPicker(memberRect, property);

            // Remaining rows – one value field per argument
            var argsProp = property.FindPropertyRelative("arguments");
            for (int i = 0; i < argsProp.arraySize; i++)
                DrawArgument(Row(row + i), argsProp.GetArrayElementAtIndex(i));
        }

        // ─── Member picker ────────────────────────────────────────────────────────

        /// <summary>Draws the member picker button (no label) in the given rect.</summary>
        private static void DrawMemberPicker(Rect buttonRect, SerializedProperty property)
        {
            var asmQnProp = property.FindPropertyRelative("targetType").FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);

            var memberProp = property.FindPropertyRelative("memberName");
            string display = string.IsNullOrEmpty(memberProp.stringValue) ? "Select Member…" : memberProp.stringValue;

            using (new EditorGUI.DisabledGroupScope(type == null))
            {
                if (EditorGUI.DropdownButton(buttonRect, new GUIContent(display), FocusType.Keyboard, EditorStyles.popup))
                {
                    if (type == null) return;
                    var so = property.serializedObject;
                    var path = property.propertyPath;

                    PopupWindow.Show(buttonRect, new MVS_MemberPickerPopup(type, memberProp.stringValue, sig =>
                    {
                        so.Update();
                        var p = so.FindProperty(path);
                        ApplySelection(p, sig);
                        so.ApplyModifiedProperties();
                    }));
                }
            }
        }

        // Writes the chosen member + rebuilds the argument array to match its signature.
        private static void ApplySelection(SerializedProperty property, MVS_MemberSig sig)
        {
            property.FindPropertyRelative("memberName").stringValue = sig.Name;
            property.FindPropertyRelative("memberKind").enumValueIndex = (int)sig.Kind;

            var argsProp = property.FindPropertyRelative("arguments");
            argsProp.arraySize = sig.Params.Length;

            for (int i = 0; i < sig.Params.Length; i++)
            {
                var argProp = argsProp.GetArrayElementAtIndex(i);
                Type pt = sig.Params[i];
                MArgKind kind = MVS_Types.KindOf(pt);

                argProp.FindPropertyRelative("kind").enumValueIndex = (int)kind;
                argProp.FindPropertyRelative("typeName").stringValue = pt.AssemblyQualifiedName;
                // Method params show their real name; a property/field value field falls back to "Value".
                argProp.FindPropertyRelative("label").stringValue = sig.ParamNames != null ? sig.ParamNames[i] : string.Empty;

                // Sensible defaults (identity quaternion, white color, otherwise zero/empty/null).
                argProp.FindPropertyRelative("numberValue").doubleValue = 0;
                argProp.FindPropertyRelative("boolValue").boolValue = false;
                argProp.FindPropertyRelative("stringValue").stringValue = string.Empty;
                argProp.FindPropertyRelative("objectValue").objectReferenceValue = null;
                argProp.FindPropertyRelative("vectorValue").vector4Value =
                    kind == MArgKind.Quaternion ? new Vector4(0, 0, 0, 1) :
                    kind == MArgKind.Color ? new Vector4(1, 1, 1, 1) : Vector4.zero;
            }
        }

        // ─── Argument value field ───────────────────────────────────────────────────

        private static void DrawArgument(Rect r, SerializedProperty argProp)
        {
            var kind = (MArgKind)argProp.FindPropertyRelative("kind").enumValueIndex;
            string label = argProp.FindPropertyRelative("label").stringValue;
            if (string.IsNullOrEmpty(label)) label = "Value";
            var lbl = new GUIContent(label);

            var numberProp = argProp.FindPropertyRelative("numberValue");
            var vectorProp = argProp.FindPropertyRelative("vectorValue");

            switch (kind)
            {
                case MArgKind.Bool:
                    EditorGUI.PropertyField(r, argProp.FindPropertyRelative("boolValue"), lbl);
                    break;

                case MArgKind.String:
                    EditorGUI.PropertyField(r, argProp.FindPropertyRelative("stringValue"), lbl);
                    break;

                case MArgKind.Int:
                    numberProp.doubleValue = EditorGUI.LongField(r, lbl, (long)numberProp.doubleValue);
                    break;

                case MArgKind.Float:
                    numberProp.doubleValue = EditorGUI.DoubleField(r, lbl, numberProp.doubleValue);
                    break;

                case MArgKind.Enum:
                    DrawEnum(r, lbl, argProp, numberProp);
                    break;

                case MArgKind.Object:
                    DrawObject(r, lbl, argProp);
                    break;

                case MArgKind.Vector2:
                {
                    Vector4 cur = vectorProp.vector4Value;
                    Vector2 v = EditorGUI.Vector2Field(r, lbl, new Vector2(cur.x, cur.y));
                    vectorProp.vector4Value = new Vector4(v.x, v.y, 0, 0);
                    break;
                }
                case MArgKind.Vector3:
                {
                    Vector4 cur = vectorProp.vector4Value;
                    Vector3 v = EditorGUI.Vector3Field(r, lbl, new Vector3(cur.x, cur.y, cur.z));
                    vectorProp.vector4Value = new Vector4(v.x, v.y, v.z, 0);
                    break;
                }
                case MArgKind.Vector4:
                    vectorProp.vector4Value = EditorGUI.Vector4Field(r, label, vectorProp.vector4Value);
                    break;

                case MArgKind.Color:
                {
                    Vector4 c = vectorProp.vector4Value;
                    Color picked = EditorGUI.ColorField(r, lbl, new Color(c.x, c.y, c.z, c.w));
                    vectorProp.vector4Value = new Vector4(picked.r, picked.g, picked.b, picked.a);
                    break;
                }
                case MArgKind.Quaternion:
                {
                    // Edited as Euler angles for usability; stored as raw xyzw.
                    Vector4 q = vectorProp.vector4Value;
                    Vector3 euler = new Quaternion(q.x, q.y, q.z, q.w).eulerAngles;
                    euler = EditorGUI.Vector3Field(r, new GUIContent($"{label} (Euler)"), euler);
                    Quaternion nq = Quaternion.Euler(euler);
                    vectorProp.vector4Value = new Vector4(nq.x, nq.y, nq.z, nq.w);
                    break;
                }
            }
        }

        private static void DrawEnum(Rect r, GUIContent lbl, SerializedProperty argProp, SerializedProperty numberProp)
        {
            Type enumType = Type.GetType(argProp.FindPropertyRelative("typeName").stringValue);
            if (enumType != null && enumType.IsEnum)
            {
                var current = (Enum)Enum.ToObject(enumType, (long)numberProp.doubleValue);
                Enum picked = EditorGUI.EnumPopup(r, lbl, current);
                numberProp.doubleValue = Convert.ToInt64(picked);
            }
            else // type unavailable – fall back to a raw integer
            {
                numberProp.doubleValue = EditorGUI.LongField(r, lbl, (long)numberProp.doubleValue);
            }
        }

        private static void DrawObject(Rect r, GUIContent lbl, SerializedProperty argProp)
        {
            Type objType = Type.GetType(argProp.FindPropertyRelative("typeName").stringValue) ?? typeof(Object);
            var objProp = argProp.FindPropertyRelative("objectValue");
            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.ObjectField(r, lbl, objProp.objectReferenceValue, objType, true);
            if (EditorGUI.EndChangeCheck()) objProp.objectReferenceValue = picked;
        }
    }

    // ─── Type helpers (shared editor + selection logic) ───────────────────────────

    internal static class MVS_Types
    {
        /// <summary>Maps a CLR type to the serialized value kind, or null if unsupported.</summary>
        public static MArgKind? TryKindOf(Type t)
        {
            if (t == null) return null;
            if (t.IsByRef || t.IsPointer) return null;            // out/ref/pointer params unsupported
            if (t.IsEnum) return MArgKind.Enum;
            if (t == typeof(bool)) return MArgKind.Bool;
            if (t == typeof(string)) return MArgKind.String;
            if (t == typeof(Vector2)) return MArgKind.Vector2;
            if (t == typeof(Vector3)) return MArgKind.Vector3;
            if (t == typeof(Vector4)) return MArgKind.Vector4;
            if (t == typeof(Color)) return MArgKind.Color;
            if (t == typeof(Quaternion)) return MArgKind.Quaternion;
            if (typeof(Object).IsAssignableFrom(t)) return MArgKind.Object;
            if (IsInteger(t)) return MArgKind.Int;
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return MArgKind.Float;
            return null;
        }

        public static MArgKind KindOf(Type t) => TryKindOf(t) ?? MArgKind.Float;

        public static bool IsSupported(Type t) => TryKindOf(t).HasValue;

        private static bool IsInteger(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
            t == typeof(uint) || t == typeof(ulong) || t == typeof(sbyte) || t == typeof(ushort);
    }

    // ─── Member signature carried from the popup back to the drawer ───────────────

    public readonly struct MVS_MemberSig
    {
        public readonly MMemberKind Kind;
        public readonly string Name;
        public readonly Type[] Params;       // property/field → [valueType]; method → 0..4 params
        public readonly string[] ParamNames; // null for property/field
        public readonly string Display;      // list label

        public MVS_MemberSig(MMemberKind kind, string name, Type[] parms, string[] paramNames, string display)
        {
            Kind = kind;
            Name = name;
            Params = parms;
            ParamNames = paramNames;
            Display = display;
        }
    }

    // ─── Member picker popup (searchable, grouped by Property / Field / Method) ────

    public class MVS_MemberPickerPopup : PopupWindowContent
    {
        private const float WIN_W = 320f;
        private const float WIN_H = 340f;
        private const float HEADER_H = 22f;
        private const float ROW_H = 22f;

        private static readonly Color C_HEADER = new(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color C_GROUP = new(0.10f, 0.10f, 0.10f, 1f);
        private static readonly Color C_SEL = new(0.18f, 0.37f, 0.73f, 0.55f);
        private static readonly Color C_HOVER = new(1f, 1f, 1f, 0.06f);

        private readonly string _current;
        private readonly Action<MVS_MemberSig> _onSelected;
        private readonly List<MVS_MemberSig> _all;

        private string _search = string.Empty;
        private List<MVS_MemberSig> _filtered;
        private Vector2 _scroll;
        private int _hover = -1;

        private GUIStyle _row, _rowSel, _group;

        public MVS_MemberPickerPopup(Type type, string current, Action<MVS_MemberSig> onSelected)
        {
            _current = current;
            _onSelected = onSelected;
            _all = BuildAll(type);
            _filtered = _all;
        }

        public override Vector2 GetWindowSize() => new(WIN_W, WIN_H);

        public override void OnGUI(Rect rect)
        {
            InitStyles();
            DrawSearch();
            HandleKeyboard();
            DrawList();
        }

        // ─── Search bar ─────────────────────────────────────────────────────────

        private void DrawSearch()
        {
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("MVS_Search");
            var rr = EditorGUILayout.GetControlRect(GUILayout.Height(HEADER_H));
            EditorGUI.DrawRect(rr, C_HEADER);
            _search = EditorGUI.TextField(new Rect(rr.x + 3, rr.y + 2, rr.width - 6, rr.height - 4),
                                          _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) { _hover = -1; RebuildFilter(); }
            EditorGUI.FocusTextInControl("MVS_Search");
        }

        // ─── List (group headers interleaved) ────────────────────────────────────

        private void DrawList()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            MMemberKind lastGroup = MMemberKind.None;
            int index = 0;

            foreach (var sig in _filtered)
            {
                if (sig.Kind != lastGroup)
                {
                    lastGroup = sig.Kind;
                    var gr = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H - 4));
                    EditorGUI.DrawRect(gr, C_GROUP);
                    EditorGUI.LabelField(new Rect(gr.x + 8, gr.y, gr.width - 8, gr.height), $"{lastGroup}s", _group);
                }

                var rr = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));
                bool isCurrent = sig.Name == _current && sig.Kind != MMemberKind.None;
                bool isHover = index == _hover;

                if (isCurrent) EditorGUI.DrawRect(rr, C_SEL);
                else if (isHover) EditorGUI.DrawRect(rr, C_HOVER);

                EditorGUI.LabelField(new Rect(rr.x + 12, rr.y, rr.width - 12, rr.height), sig.Display,
                                     isCurrent ? _rowSel : _row);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rr.Contains(ev.mousePosition)) { _hover = index; editorWindow.Repaint(); }
                if (ev.type == EventType.MouseDown && rr.Contains(ev.mousePosition)) Confirm(sig);

                index++;
            }
            EditorGUILayout.EndScrollView();
        }

        private void HandleKeyboard()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;

            if (ev.keyCode == KeyCode.DownArrow) { _hover = Mathf.Min(_hover + 1, _filtered.Count - 1); ev.Use(); }
            else if (ev.keyCode == KeyCode.UpArrow) { _hover = Mathf.Max(_hover - 1, 0); ev.Use(); }
            else if (ev.keyCode == KeyCode.Return && _hover >= 0 && _hover < _filtered.Count) { Confirm(_filtered[_hover]); ev.Use(); }
            else if (ev.keyCode == KeyCode.Escape) { editorWindow.Close(); ev.Use(); }
        }

        private void Confirm(MVS_MemberSig sig) { _onSelected?.Invoke(sig); editorWindow.Close(); }

        private void RebuildFilter()
        {
            if (string.IsNullOrWhiteSpace(_search)) { _filtered = _all; return; }
            string q = _search.Trim();
            _filtered = _all.FindAll(s => s.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ─── Reflection harvest ───────────────────────────────────────────────────

        private static List<MVS_MemberSig> BuildAll(Type type)
        {
            var props = new List<MVS_MemberSig>();
            var fields = new List<MVS_MemberSig>();
            var methods = new List<MVS_MemberSig>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var p in type.GetProperties(flags))
            {
                if (!p.CanWrite || p.GetIndexParameters().Length > 0) continue;
                if (!MVS_Types.IsSupported(p.PropertyType)) continue;
                props.Add(new(MMemberKind.Property, p.Name, new[] { p.PropertyType }, null,
                              $"{p.Name}   :  {Pretty(p.PropertyType)}"));
            }

            foreach (var f in type.GetFields(flags))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                if (!MVS_Types.IsSupported(f.FieldType)) continue;
                fields.Add(new(MMemberKind.Field, f.Name, new[] { f.FieldType }, null,
                               $"{f.Name}   :  {Pretty(f.FieldType)}"));
            }

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.IsGenericMethod) continue;       // skip get_/set_/op_/generics
                var parms = m.GetParameters();
                if (parms.Length > 4) continue;                           // up to 4 arguments

                bool ok = true;
                var types = new Type[parms.Length];
                var names = new string[parms.Length];
                for (int i = 0; i < parms.Length; i++)
                {
                    if (parms[i].IsOut || !MVS_Types.IsSupported(parms[i].ParameterType)) { ok = false; break; }
                    types[i] = parms[i].ParameterType;
                    names[i] = parms[i].Name;
                }
                if (!ok) continue;

                methods.Add(new(MMemberKind.Method, m.Name, types, names, $"{m.Name}({Signature(parms)})"));
            }

            props.Sort(ByName); fields.Sort(ByName); methods.Sort(ByName);

            var all = new List<MVS_MemberSig>(props.Count + fields.Count + methods.Count);
            all.AddRange(props); all.AddRange(fields); all.AddRange(methods);
            return all;
        }

        private static int ByName(MVS_MemberSig a, MVS_MemberSig b) =>
            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

        private static string Signature(ParameterInfo[] parms)
        {
            if (parms.Length == 0) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parms.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Pretty(parms[i].ParameterType));
            }
            return sb.ToString();
        }

        // Short, friendly type names for the picker labels.
        private static string Pretty(Type t)
        {
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            return t.Name;
        }

        // ─── Styles ───────────────────────────────────────────────────────────────

        private void InitStyles()
        {
            _row ??= new(EditorStyles.label) { fontSize = 12, normal = { textColor = Color.white } };
            _rowSel ??= new(EditorStyles.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _group ??= new(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(0.6f, 0.8f, 1f) } };
        }
    }

#endif
}
