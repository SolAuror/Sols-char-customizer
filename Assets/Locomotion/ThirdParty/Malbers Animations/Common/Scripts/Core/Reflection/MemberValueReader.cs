// Last Change: 9/17/2026 6:05pm
using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

// Reads a value from a member (property / field / method) on a Component found on a target GameObject.
// The instance is never serialized: only the Component TYPE is, and the instance is resolved at runtime
// from whatever target is passed in (self → parent → children), which is what makes it usable on prefabs
// and on ScriptableObject-authored Reactions, States and Tasks.
//
// This is the lean half: it reads and hands the value back through TryRead. MemberValueGetter derives from
// it and adds the typed UnityEvents that push the value out. Owners that consume the value themselves
// (MEventReaction) use this one, so they do not carry twelve unused events.
namespace MalbersAnimations
{
    /// <summary>
    /// The value kind of the member being read. Each kind maps 1:1 to a UnityEvent type in
    /// <see cref="MalbersAnimations.Events"/>, which is also what makes a member selectable: members whose
    /// type has no matching event are not listed in the picker.
    /// </summary>
    public enum MGetKind
    {
        None, Float, Int, Bool, String, GameObject, Transform, Component, Sprite, Object, Vector2, Vector3, Color
    }

    /// <summary>
    /// A constant argument for a member method that takes parameters. The parameter's CLR type is fixed
    /// when the method is picked; <see cref="kind"/> selects which value field carries it (and which field the
    /// drawer shows), while <see cref="typeName"/> keeps the real type so the value is boxed back correctly and
    /// the right overload is resolved.
    /// </summary>
    [Serializable]
    public class MGetArg
    {
        public string name;         // parameter name, used as the inspector label
        public string typeName;     // assembly-qualified CLR type of the parameter
        public MGetKind kind = MGetKind.None;

        public float floatValue;
        public int intValue;
        public bool boolValue;
        public string stringValue;
        public Object objectValue;
        public Vector2 vector2Value;
        public Vector3 vector3Value;
        public Color colorValue = Color.white;

        [NonSerialized] private Type _type;
        [NonSerialized] private string _cachedTypeName;

        /// <summary>Resolved CLR type of the parameter. Re-resolved only when <see cref="typeName"/> changes.</summary>
        public Type Type
        {
            get
            {
                if (_cachedTypeName != typeName)
                {
                    _cachedTypeName = typeName;
                    _type = string.IsNullOrEmpty(typeName) ? null : Type.GetType(typeName);
                }
                return _type;
            }
        }

        /// <summary>Boxes the stored value as the parameter's real CLR type, ready for Invoke.</summary>
        public object Value()
        {
            var t = Type;
            if (t == null) return null;

            if (t.IsEnum) return Enum.ToObject(t, intValue);

            if (t == typeof(float)) return floatValue;
            if (t == typeof(int)) return intValue;
            if (t == typeof(bool)) return boolValue;
            if (t == typeof(string)) return stringValue ?? string.Empty;

            if (t == typeof(double)) return (double)floatValue;
            if (t == typeof(decimal)) return (decimal)floatValue;
            if (t == typeof(long)) return (long)intValue;
            if (t == typeof(short)) return (short)intValue;
            if (t == typeof(byte)) return (byte)intValue;
            if (t == typeof(sbyte)) return (sbyte)intValue;
            if (t == typeof(uint)) return (uint)intValue;
            if (t == typeof(ulong)) return (ulong)intValue;
            if (t == typeof(ushort)) return (ushort)intValue;

            if (t == typeof(Vector2)) return vector2Value;
            if (t == typeof(Vector3)) return vector3Value;
            if (t == typeof(Quaternion)) return Quaternion.Euler(vector3Value);
            if (t == typeof(Color)) return colorValue;

            // MWC — a null reference is a valid argument; a mismatched one is not, so it is passed as null.
            if (typeof(Object).IsAssignableFrom(t))
                return objectValue != null && t.IsInstanceOfType(objectValue) ? objectValue : null;

            return null;
        }
    }

    /// <summary> Reads a value from a member on a Component resolved at runtime from the target GameObject. </summary>
    [Serializable]
    public class MemberValueReader
    {
        /// <summary>Component type the member is read from. Always resolved from the target GameObject.</summary>
        [TypeFilter(typeof(Component))]
        public SerializableType targetType;

        public string memberName;
        public MMemberKind memberKind = MMemberKind.None;

        // MWC — resolved when the member is picked; drives the runtime cast and which event fires.
        public MGetKind valueKind = MGetKind.None;
        // MWC — assembly-qualified CLR type of the member's value (display only).
        public string valueTypeName;

        // MWC — constant arguments for a method member. Empty for properties, fields and zero-arg methods.
        public MGetArg[] args = Array.Empty<MGetArg>();
        // MWC — display-only parameter signature of the picked method ("int, float").
        public string paramSignature;

        // ─── Runtime cache (not serialized) ──────────────────────────────────────

        private static readonly object[] NoArgs = Array.Empty<object>();

        private Component _resolvedTarget;                             // component the member is read from
        private Object _targetObject;                                  // last target passed to SetTarget
        private MethodInfo _getter;                                    // property getter or value-returning method
        private FieldInfo _field;
        private object[] _args = NoArgs;                               // MWC — boxed arguments reused per call
        private bool _cacheBuilt;
        private bool _valid;
        private string _cachedMemberName;
        private MMemberKind _cachedKind;

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>The value kind resolved when the member was picked.</summary>
        public MGetKind ValueKind => valueKind;

        /// <summary>True when a member has been picked (says nothing about whether the read will succeed).</summary>
        public bool IsConfigured => targetType?.Type != null && !string.IsNullOrEmpty(memberName) && memberKind != MMemberKind.None;

        /// <summary>
        /// Resolves the instance to read from. Cheap no-op when the same target is passed again,
        /// so repeated reactions on one target never rebuild the reflection cache.
        /// </summary>
        public void SetTarget(Object target)
        {
            if (_cacheBuilt && ReferenceEquals(target, _targetObject)) return;

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

        /// <summary>Reads the member on the already-resolved target. False when nothing readable is configured.</summary>
        public bool TryRead(out object raw)
        {
            raw = null;

            if (_resolvedTarget == null) return false;

            // Cheap invalidation check (string ref + enum compare), matching MemberValuesSetter.
            if (!_cacheBuilt || _cachedMemberName != memberName || _cachedKind != memberKind) BuildCache();
            if (!_valid) return false;

            if (_field != null)
            {
                raw = _field.GetValue(_resolvedTarget);
                return true;
            }

            // MWC — argument values are refilled per call, so tweaking them in the inspector applies while playing.
            for (int i = 0; i < _args.Length; i++) _args[i] = args[i].Value();

            raw = _getter.Invoke(_resolvedTarget, _args);
            return true;
        }

        /// <summary>Resolves the target then reads in one call.</summary>
        public bool TryRead(Object target, out object raw)
        {
            SetTarget(target);
            return TryRead(out raw);
        }

        /// <summary>"Type.Member(args)" of the picked member, without any output suffix.</summary>
        protected string MemberSignature
        {
            get
            {
                string typeName = targetType?.Type?.Name ?? "?";
                string member = string.IsNullOrEmpty(memberName) ? "?" : memberName;
                string call = memberKind == MMemberKind.Method ? $"({paramSignature})" : string.Empty; // MWC — show the arg signature

                return $"{typeName}.{member}{call}";
            }
        }

        /// <summary>Human readable "Type.Member(args): Kind" description of the configured member.</summary>
        public virtual string Description() => $"{MemberSignature}: {valueKind}";

        // ─── Value conversion ─────────────────────────────────────────────────────
        // MWC — the member's CLR type is fixed at edit time, so these only ever hit their first branch
        // for the common cases; the Convert fallbacks cover the exotic numeric types and enums.

        public static float ToFloat(object raw) => raw switch
        {
            float f => f,
            int i => i,
            double d => (float)d,
            null => 0f,
            _ => Convert.ToSingle(raw, CultureInfo.InvariantCulture)
        };

        public static int ToInt(object raw) => raw switch
        {
            int i => i,
            float f => (int)f,
            double d => (int)d,
            null => 0,
            _ => Convert.ToInt32(raw, CultureInfo.InvariantCulture)
        };

        public static Vector2 ToVector2(object raw) => raw switch
        {
            Vector2 v => v,
            Vector3 v => v,
            _ => default
        };

        // MWC — a Quaternion member is sent as its euler angles, which is what a Vector3 listener wants.
        public static Vector3 ToVector3(object raw) => raw switch
        {
            Vector3 v => v,
            Vector2 v => v,
            Quaternion q => q.eulerAngles,
            _ => default
        };

        // ─── Kind mapping ─────────────────────────────────────────────────────────

        /// <summary>
        /// Maps a member's CLR type to the event kind that carries it, or null when no
        /// UnityEvent type can carry it (such a member is not offered in the picker).
        /// </summary>
        public static MGetKind? TryKindOf(Type t)
        {
            if (t == null || t == typeof(void) || t.IsByRef || t.IsPointer) return null;

            if (t.IsEnum) return MGetKind.Int;
            if (t == typeof(bool)) return MGetKind.Bool;
            if (t == typeof(string)) return MGetKind.String;
            if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return MGetKind.Float;
            if (IsInteger(t)) return MGetKind.Int;
            if (t == typeof(Vector2)) return MGetKind.Vector2;
            if (t == typeof(Vector3) || t == typeof(Quaternion)) return MGetKind.Vector3;
            if (t == typeof(Color)) return MGetKind.Color;

            // Order matters: Transform is a Component, and Sprite/GameObject are not Components.
            if (typeof(GameObject).IsAssignableFrom(t)) return MGetKind.GameObject;
            if (typeof(Transform).IsAssignableFrom(t)) return MGetKind.Transform;
            if (typeof(Sprite).IsAssignableFrom(t)) return MGetKind.Sprite;
            if (typeof(Component).IsAssignableFrom(t)) return MGetKind.Component;
            if (typeof(Object).IsAssignableFrom(t)) return MGetKind.Object;

            return null;
        }

        public static bool IsSupported(Type t) => TryKindOf(t).HasValue;

        private static bool IsInteger(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
            t == typeof(uint) || t == typeof(ulong) || t == typeof(sbyte) || t == typeof(ushort);

        // ─── Cache building ───────────────────────────────────────────────────────

        private void BuildCache()
        {
            _cacheBuilt = true;
            _valid = false;
            _cachedMemberName = memberName;
            _cachedKind = memberKind;
            _getter = null;
            _field = null;
            _args = NoArgs;

            if (_resolvedTarget == null || targetType?.Type == null ||
                string.IsNullOrEmpty(memberName) || memberKind == MMemberKind.None)
                return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            switch (memberKind)
            {
                case MMemberKind.Property:
                    var prop = type.GetProperty(memberName, flags);
                    if (prop == null || !prop.CanRead || prop.GetIndexParameters().Length > 0) return;
                    // MWC — cache the getter MethodInfo directly, skipping PropertyInfo's per-call plumbing.
                    _getter = prop.GetGetMethod();
                    if (_getter == null) return;
                    break;

                case MMemberKind.Field:
                    _field = type.GetField(memberName, flags);
                    if (_field == null) return;
                    break;

                case MMemberKind.Method:
                    // MWC — the stored argument types both resolve the overload and size the invoke array.
                    var argTypes = ArgTypes();
                    if (argTypes == null) return;

                    _getter = type.GetMethod(memberName, flags, null, argTypes, null);
                    if (_getter == null || _getter.ReturnType == typeof(void)) return;

                    if (argTypes.Length > 0) _args = new object[argTypes.Length];
                    break;
            }

            _valid = true;
        }

        // MWC — CLR types of the serialized arguments; null when one of them cannot be resolved (stale type),
        // which keeps the member invalid instead of invoking the wrong overload.
        private Type[] ArgTypes()
        {
            if (args == null || args.Length == 0) return Type.EmptyTypes;

            var types = new Type[args.Length];

            for (int i = 0; i < args.Length; i++)
            {
                types[i] = args[i]?.Type;
                if (types[i] == null) return null;
            }
            return types;
        }
    }


#if UNITY_EDITOR

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    /// <summary> Draws [Target Type][Member] plus one row per constant argument. MemberValueGetterDrawer adds the output event. </summary>
    [CustomPropertyDrawer(typeof(MemberValueReader))]
    public class MemberValueReaderDrawer : PropertyDrawer
    {
        protected const float ROW_H = 18f;
        protected const float PAD = 2f;

        /// <summary>Rows the member itself needs: the picker row plus one per constant argument.</summary>
        protected static int RowCount(SerializedProperty property)
        {
            int rows = 1; // [Target Type][Member]

            // MWC — one row per constant argument of the picked method
            var argsProp = property.FindPropertyRelative("args");
            if (argsProp != null) rows += argsProp.arraySize;

            return rows;
        }

        /// <summary>Height of the member rows alone, without any output drawn under them.</summary>
        protected static float RowsHeight(SerializedProperty property) => (ROW_H + PAD) * RowCount(property);

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => RowsHeight(property);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) => DrawRows(position, property);

        /// <summary>Draws the picker row and the argument rows, and returns how many rows were used.</summary>
        protected static int DrawRows(Rect position, SerializedProperty property)
        {
            Rect Row(int i) => new(position.x, position.y + i * (ROW_H + PAD), position.width, ROW_H);
            int row = 0;

            // Row – [Target Type ▼][Member ▼] (Components only; the member is always read from the target GameObject)
            MemberDrawerUtility.SplitRow(Row(row++), out var typeRect, out var memberRect);
            MemberDrawerUtility.DrawTargetTypeButton(typeRect, property);
            DrawMemberPicker(memberRect, property);

            // Rows – constant arguments of the picked method (none for properties / fields / zero-arg methods)
            var argsProp = property.FindPropertyRelative("args");

            if (argsProp != null && argsProp.arraySize > 0)
            {
                EditorGUI.indentLevel++; // MWC — arguments read as children of the Member row
                for (int i = 0; i < argsProp.arraySize; i++)
                    DrawArg(Row(row++), argsProp.GetArrayElementAtIndex(i));
                EditorGUI.indentLevel--;
            }

            return row;
        }

        // ─── Argument rows ────────────────────────────────────────────────────────

        // MWC — Type.GetType is not free and these are hit every repaint, so resolved argument types are cached.
        private static readonly Dictionary<string, Type> s_ArgTypes = new();

        private static Type ArgType(SerializedProperty arg)
        {
            string tn = arg.FindPropertyRelative("typeName").stringValue;
            if (string.IsNullOrEmpty(tn)) return null;

            if (!s_ArgTypes.TryGetValue(tn, out var t)) s_ArgTypes[tn] = t = Type.GetType(tn);
            return t;
        }

        // MWC — draws the one value field that matches the parameter's type.
        // (internal: reused by VariableReferenceDrawer for the Var Reference member mode)
        internal static void DrawArg(Rect r, SerializedProperty arg)
        {
            var kind = (MGetKind)arg.FindPropertyRelative("kind").enumValueIndex;
            var label = new GUIContent(arg.FindPropertyRelative("name").stringValue);
            Type t = ArgType(arg);

            switch (kind)
            {
                case MGetKind.Float: EditorGUI.PropertyField(r, arg.FindPropertyRelative("floatValue"), label); break;
                case MGetKind.Bool: EditorGUI.PropertyField(r, arg.FindPropertyRelative("boolValue"), label); break;
                case MGetKind.String: EditorGUI.PropertyField(r, arg.FindPropertyRelative("stringValue"), label); break;
                case MGetKind.Vector2: EditorGUI.PropertyField(r, arg.FindPropertyRelative("vector2Value"), label); break;
                case MGetKind.Vector3: EditorGUI.PropertyField(r, arg.FindPropertyRelative("vector3Value"), label); break;
                case MGetKind.Color: EditorGUI.PropertyField(r, arg.FindPropertyRelative("colorValue"), label); break;

                case MGetKind.Int:
                    var intProp = arg.FindPropertyRelative("intValue");

                    if (t != null && t.IsEnum) DrawEnumArg(r, label, intProp, t); // MWC — enum params get a real popup
                    else EditorGUI.PropertyField(r, intProp, label);
                    break;

                // Every Object-ish kind stores the same reference; only the ObjectField filter differs.
                case MGetKind.GameObject:
                case MGetKind.Transform:
                case MGetKind.Component:
                case MGetKind.Sprite:
                case MGetKind.Object:
                    var objProp = arg.FindPropertyRelative("objectValue");

                    EditorGUI.BeginChangeCheck();
                    var o = EditorGUI.ObjectField(r, label, objProp.objectReferenceValue, t ?? typeof(Object), true);
                    if (EditorGUI.EndChangeCheck()) objProp.objectReferenceValue = o;
                    break;
            }
        }

        private static void DrawEnumArg(Rect r, GUIContent label, SerializedProperty intProp, Type enumType)
        {
            var current = (Enum)Enum.ToObject(enumType, intProp.intValue);
            bool flags = enumType.IsDefined(typeof(FlagsAttribute), false);

            EditorGUI.BeginChangeCheck();
            var picked = flags ? EditorGUI.EnumFlagsField(r, label, current) : EditorGUI.EnumPopup(r, label, current);
            if (EditorGUI.EndChangeCheck()) intProp.intValue = Convert.ToInt32(picked, CultureInfo.InvariantCulture);
        }

        // ─── Member picker ────────────────────────────────────────────────────────

        /// <summary>Draws the member picker button (no label) in the given rect.</summary>
        private static void DrawMemberPicker(Rect buttonRect, SerializedProperty property)
        {
            var asmQnProp = property.FindPropertyRelative("targetType").FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);

            var memberProp = property.FindPropertyRelative("memberName");
            var sigProp = property.FindPropertyRelative("paramSignature");
            bool isMethod = property.FindPropertyRelative("memberKind").enumValueIndex == (int)MMemberKind.Method;

            // MWC — a method shows its signature so overloads are told apart on the button itself.
            string display = string.IsNullOrEmpty(memberProp.stringValue) ? "Select Member…"
                           : isMethod ? $"{memberProp.stringValue}({sigProp.stringValue})"
                           : memberProp.stringValue;

            using (new EditorGUI.DisabledGroupScope(type == null))
            {
                if (EditorGUI.DropdownButton(buttonRect, new GUIContent(display), FocusType.Keyboard, EditorStyles.popup))
                {
                    if (type == null) return;

                    var so = property.serializedObject;
                    var path = property.propertyPath;

                    PopupWindow.Show(buttonRect, new MGV_MemberPickerPopup(type, memberProp.stringValue, sigProp.stringValue, sig =>
                    {
                        so.Update();
                        var p = so.FindProperty(path);
                        ApplySelection(p, sig);
                        so.ApplyModifiedProperties();
                    }));
                }
            }
        }

        // MWC — writes the picked member plus the value kind that selects its output event.
        private static void ApplySelection(SerializedProperty property, MGV_MemberSig sig)
        {
            property.FindPropertyRelative("memberName").stringValue = sig.Name;
            property.FindPropertyRelative("memberKind").enumValueIndex = (int)sig.Kind;
            property.FindPropertyRelative("valueKind").enumValueIndex = (int)sig.Get;
            property.FindPropertyRelative("valueTypeName").stringValue = sig.ValueType?.AssemblyQualifiedName ?? string.Empty;
            property.FindPropertyRelative("paramSignature").stringValue = sig.ParamSignature;

            // MWC — rebuild the constant-argument list for the picked method (empty for properties / fields).
            var argsProp = property.FindPropertyRelative("args");
            int count = sig.Params?.Length ?? 0;
            argsProp.arraySize = count;

            for (int i = 0; i < count; i++)
            {
                var p = sig.Params[i];
                var a = argsProp.GetArrayElementAtIndex(i);

                a.FindPropertyRelative("name").stringValue = ObjectNames.NicifyVariableName(p.Name);
                a.FindPropertyRelative("typeName").stringValue = p.ParameterType.AssemblyQualifiedName;

                var kind = MemberValueReader.TryKindOf(p.ParameterType) ?? MGetKind.None;
                a.FindPropertyRelative("kind").enumValueIndex = (int)kind;

                ClearArgValues(a);
                ApplyArgDefault(a, p, kind);
            }
        }

        // MWC — a grown array copies the previous element, and a re-picked member reuses old slots, so every
        // value field is reset before the parameter's own default is written.
        // (internal: reused by MemberGetterEditor for the Var Reference member mode)
        internal static void ClearArgValues(SerializedProperty a)
        {
            a.FindPropertyRelative("floatValue").floatValue = 0f;
            a.FindPropertyRelative("intValue").intValue = 0;
            a.FindPropertyRelative("boolValue").boolValue = false;
            a.FindPropertyRelative("stringValue").stringValue = string.Empty;
            a.FindPropertyRelative("objectValue").objectReferenceValue = null;
            a.FindPropertyRelative("vector2Value").vector2Value = Vector2.zero;
            a.FindPropertyRelative("vector3Value").vector3Value = Vector3.zero;
            a.FindPropertyRelative("colorValue").colorValue = Color.white;
        }

        // MWC — seeds an optional parameter's declared default so the row starts at the method's own value.
        // (internal: reused by MemberGetterEditor for the Var Reference member mode)
        internal static void ApplyArgDefault(SerializedProperty a, ParameterInfo p, MGetKind kind)
        {
            if (!p.HasDefaultValue || p.DefaultValue == null) return;

            switch (kind)
            {
                case MGetKind.Float: a.FindPropertyRelative("floatValue").floatValue = MemberValueReader.ToFloat(p.DefaultValue); break;
                case MGetKind.Int: a.FindPropertyRelative("intValue").intValue = MemberValueReader.ToInt(p.DefaultValue); break;
                case MGetKind.Bool: a.FindPropertyRelative("boolValue").boolValue = p.DefaultValue is bool b && b; break;
                case MGetKind.String: a.FindPropertyRelative("stringValue").stringValue = p.DefaultValue as string ?? string.Empty; break;
            }
        }
    }

    // ─── Member signature carried from the popup back to the drawer ───────────────

    public readonly struct MGV_MemberSig
    {
        public readonly MMemberKind Kind;        // Property / Field / Method
        public readonly string Name;
        public readonly Type ValueType;          // CLR type of the value read
        public readonly MGetKind Get;            // event kind that carries it
        public readonly string Display;          // list label
        public readonly ParameterInfo[] Params;  // MWC — parameters the method needs; empty for property / field
        public readonly string ParamSignature;   // MWC — "int, float" display of Params

        public MGV_MemberSig(MMemberKind kind, string name, Type valueType, MGetKind get, string display,
                             ParameterInfo[] parameters = null, string paramSignature = null)
        {
            Kind = kind;
            Name = name;
            ValueType = valueType;
            Get = get;
            Display = display;
            Params = parameters ?? Array.Empty<ParameterInfo>();
            ParamSignature = paramSignature ?? string.Empty;
        }
    }

    // ─── Member picker popup (searchable, grouped by Property / Field / Method) ────
    //
    //  MWC — same look and behaviour as MVS_MemberPickerPopup, but lists readable members whose
    //  type maps to a UnityEvent kind (see MemberValueReader.TryKindOf).

    public class MGV_MemberPickerPopup : PopupWindowContent
    {
        private const float WIN_W = 320f;
        private const float WIN_H = 340f;
        private const float HEADER_H = 22f;
        private const float ROW_H = 22f;

        private static readonly Color C_HEADER = new(0.16f, 0.16f, 0.16f, 1f);
        private static readonly Color C_GROUP = new(0.10f, 0.10f, 0.10f, 1f);
        private static readonly Color C_SEL = new(0.18f, 0.37f, 0.73f, 0.55f);
        private static readonly Color C_HOVER = new(1f, 1f, 1f, 0.06f);

        // MWC — maximum parameters a method may take to stay listed; more than this stops being a usable inspector row.
        private const int MAX_ARGS = 4;

        private readonly string _current;
        private readonly string _currentSig;  // MWC — tells overloads of the picked method apart
        private readonly Action<MGV_MemberSig> _onSelected;
        private readonly List<MGV_MemberSig> _all;

        private string _search = string.Empty;
        private List<MGV_MemberSig> _filtered;
        private Vector2 _scroll;
        private int _hover = -1;

        private GUIStyle _row, _rowSel, _group;

        // MWC — valueFilter (optional) narrows the listed members to those whose value type passes it.
        public MGV_MemberPickerPopup(Type type, string current, string currentSignature, Action<MGV_MemberSig> onSelected,
                                     Func<Type, bool> valueFilter = null)
        {
            _current = current;
            _currentSig = currentSignature ?? string.Empty;
            _onSelected = onSelected;
            _all = BuildAll(type, valueFilter);
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
            GUI.SetNextControlName("MGV_Search");
            var rr = EditorGUILayout.GetControlRect(GUILayout.Height(HEADER_H));
            EditorGUI.DrawRect(rr, C_HEADER);
            _search = EditorGUI.TextField(new Rect(rr.x + 3, rr.y + 2, rr.width - 6, rr.height - 4),
                                          _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) { _hover = -1; RebuildFilter(); }
            EditorGUI.FocusTextInControl("MGV_Search");
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
                bool isCurrent = sig.Name == _current && sig.ParamSignature == _currentSig; // MWC — overload aware
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

        private void Confirm(MGV_MemberSig sig) { _onSelected?.Invoke(sig); editorWindow.Close(); }

        private void RebuildFilter()
        {
            if (string.IsNullOrWhiteSpace(_search)) { _filtered = _all; return; }
            string q = _search.Trim();
            _filtered = _all.FindAll(s => s.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ─── Reflection harvest ───────────────────────────────────────────────────

        // MWC — valueFilter narrows the list to members whose value type passes it (null = list all supported).
        private static List<MGV_MemberSig> BuildAll(Type type, Func<Type, bool> valueFilter = null)
        {
            var props = new List<MGV_MemberSig>();
            var fields = new List<MGV_MemberSig>();
            var methods = new List<MGV_MemberSig>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var p in type.GetProperties(flags))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                var kind = MemberValueReader.TryKindOf(p.PropertyType);
                if (!kind.HasValue) continue;
                if (valueFilter != null && !valueFilter(p.PropertyType)) continue;
                props.Add(new(MMemberKind.Property, p.Name, p.PropertyType, kind.Value,
                              $"{p.Name}   :  {Pretty(p.PropertyType)}"));
            }

            foreach (var f in type.GetFields(flags))
            {
                var kind = MemberValueReader.TryKindOf(f.FieldType);
                if (!kind.HasValue) continue;
                if (valueFilter != null && !valueFilter(f.FieldType)) continue;
                fields.Add(new(MMemberKind.Field, f.Name, f.FieldType, kind.Value,
                               $"{f.Name}   :  {Pretty(f.FieldType)}"));
            }

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.IsGenericMethod) continue;   // skip get_/set_/op_/generics
                var kind = MemberValueReader.TryKindOf(m.ReturnType);
                if (!kind.HasValue) continue;
                if (valueFilter != null && !valueFilter(m.ReturnType)) continue;

                // MWC — methods with parameters are listed too, as long as every parameter is a type that can
                // be stored as a constant in the inspector.
                var ps = m.GetParameters();
                if (ps.Length > MAX_ARGS || !ArgsSupported(ps)) continue;

                string sig = Signature(ps);
                methods.Add(new(MMemberKind.Method, m.Name, m.ReturnType, kind.Value,
                                $"{m.Name}({sig})   :  {Pretty(m.ReturnType)}", ps, sig));
            }

            props.Sort(ByName); fields.Sort(ByName); methods.Sort(ByName);

            var all = new List<MGV_MemberSig>(props.Count + fields.Count + methods.Count);
            all.AddRange(props); all.AddRange(fields); all.AddRange(methods);
            return all;
        }

        // MWC — every parameter must map to a storable constant; out/ref parameters are never usable.
        private static bool ArgsSupported(ParameterInfo[] ps)
        {
            foreach (var p in ps)
            {
                if (p.IsOut || p.ParameterType.IsByRef) return false;
                if (!MemberValueReader.IsSupported(p.ParameterType)) return false;
            }
            return true;
        }

        // MWC — "int, float" for the list label and the picker button.
        private static string Signature(ParameterInfo[] ps)
        {
            if (ps.Length == 0) return string.Empty;

            var parts = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++) parts[i] = Pretty(ps[i].ParameterType);

            return string.Join(", ", parts);
        }

        // MWC — overloads share a name, so the signature breaks the tie and keeps their order stable.
        private static int ByName(MGV_MemberSig a, MGV_MemberSig b)
        {
            int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.ParamSignature, b.ParamSignature, StringComparison.OrdinalIgnoreCase);
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
