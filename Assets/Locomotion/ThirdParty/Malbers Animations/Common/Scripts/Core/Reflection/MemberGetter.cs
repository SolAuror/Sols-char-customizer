// Last Change: 9/17/2026 6:05pm
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

// MWC — Lean read-only member getter used by the ReferenceVar "Use Reference" mode (FloatReference,
// IntReference, …). Unlike MemberValueReader it resolves no Component type and carries no events: the
// instance to read from is a direct serialized Object reference, so the serialized footprint stays
// small enough to live inside every Var Reference. Reuses MGetArg for method arguments and the
// MGV_MemberPickerPopup for member selection.
namespace MalbersAnimations
{
    /// <summary>Reads a value from a member (field / property / value-returning method) on a directly referenced Object.</summary>
    [Serializable]
    public class MemberGetter : ISerializationCallbackReceiver
    {
        /// <summary>Component, ScriptableObject or asset the member is read from.</summary>
        public Object target;
        /// <summary>Name of the member to read.</summary>
        public string memberName;
        /// <summary>Whether the member is a Property, Field or Method.</summary>
        public MMemberKind memberKind = MMemberKind.None;
        /// <summary>Constant arguments for a method member. Empty for properties, fields and zero-arg methods.</summary>
        public MGetArg[] args = Array.Empty<MGetArg>();
        /// <summary>Display-only parameter signature of the picked method ("int, float").</summary>
        public string paramSignature;
        /// <summary>Returns the logical NOT of the member. Applies to bool reads only; the fallback is never inverted.</summary>
        public bool invert;

        // ─── Runtime cache (not serialized) ──────────────────────────────────────

        private static readonly object[] NoArgs = Array.Empty<object>();

        private MethodInfo _getter;     // property getter or value-returning method
        private FieldInfo _field;
        private object[] _args = NoArgs;
        private Object _cachedTarget;
        private bool _cacheBuilt;
        private bool _valid;
        private string _cachedMemberName;
        private MMemberKind _cachedKind;

        // MWC — Fast path: a closed Func<T> bound to the target, built once for parameterless members.
        // Calling it costs about as much as the direct C# call and, unlike MethodInfo.Invoke, boxes nothing.
        // Null whenever no fast path applies (fields, methods with arguments, exotic return types, AOT refusal),
        // in which case the reflection path below runs unchanged.
        private Delegate _fast;

        /// <summary>True when a target and member are configured (says nothing about whether the read will succeed).</summary>
        public bool IsConfigured => target != null && !string.IsNullOrEmpty(memberName) && memberKind != MMemberKind.None;

        /// <summary>True when the member can be read right now; rebuilds the reflection cache if it went stale.</summary>
        private bool Ready()
        {
            if (target == null) return false;

            // Cheap invalidation check (reference + string + enum compare), matching MemberValueReader.
            if (!_cacheBuilt || !ReferenceEquals(target, _cachedTarget) ||
                _cachedMemberName != memberName || _cachedKind != memberKind)
                BuildCache();

            return _valid;
        }

        /// <summary>Reflection read; assumes <see cref="Ready"/> already returned true.</summary>
        private object ReadRaw()
        {
            if (_field != null) return _field.GetValue(target);

            return _getter.Invoke(target, _args);
        }

        /// <summary>Drops the cached reflection data so the next read re-resolves the member.</summary>
        public void Invalidate() => _cacheBuilt = false;

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        /// <summary>
        /// Arguments are baked into the bound delegate when the cache is built, so an inspector edit
        /// has to drop that cache to take effect. Unity re-deserializes the owner after every edit, which
        /// makes this the cheap way to stay live during play without re-reading the arguments per call.
        /// </summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize() => _cacheBuilt = false;

        /// <summary>Reads the member on the referenced target. False when nothing readable is configured.</summary>
        public bool TryRead(out object raw)
        {
            raw = null;
            if (!Ready()) return false;

            raw = ReadRaw();
            return true;
        }

        // ─── Typed reads (fallback returned when the member cannot be read) ──────
        //
        // MWC — each one tries the matching closed delegate first. The `is Func<T>` tests are ordinary type
        // checks, and every Func<T> used here is written out explicitly so the AOT compiler generates it.

        /// <summary>Reads the member as a float, or returns the fallback.</summary>
        public float GetFloat(float fallback)
        {
            if (!Ready()) return fallback;

            if (_fast is Func<float> f) return f();
            if (_fast is Func<int> i) return i();

            return MemberValueReader.ToFloat(ReadRaw());
        }

        /// <summary>Reads the member as an int, or returns the fallback.</summary>
        public int GetInt(int fallback)
        {
            if (!Ready()) return fallback;

            if (_fast is Func<int> i) return i();
            if (_fast is Func<float> f) return (int)f();

            return MemberValueReader.ToInt(ReadRaw());
        }

        /// <summary>Reads the member as a bool (negated when <see cref="invert"/> is set), or returns the fallback.</summary>
        public bool GetBool(bool fallback)
        {
            //MWC — the fallback is the authored constant, so it is returned as-is; only a real read is inverted.
            if (!Ready()) return fallback;

            if (_fast is Func<bool> b) return b() ^ invert;

            return (ReadRaw() is bool v && v) ^ invert;
        }

        /// <summary>Reads the member as a string (ToString for non-string members), or returns the fallback.</summary>
        public string GetString(string fallback)
        {
            if (!Ready()) return fallback;

            if (_fast is Func<string> s) return s() ?? string.Empty;

            return ReadRaw()?.ToString() ?? string.Empty;
        }

        /// <summary>Reads the member as a Vector2, or returns the fallback.</summary>
        public Vector2 GetVector2(Vector2 fallback)
        {
            if (!Ready()) return fallback;

            if (_fast is Func<Vector2> v2) return v2();
            if (_fast is Func<Vector3> v3) return v3();

            return MemberValueReader.ToVector2(ReadRaw());
        }

        /// <summary>Reads the member as a Vector3, or returns the fallback.</summary>
        public Vector3 GetVector3(Vector3 fallback)
        {
            if (!Ready()) return fallback;

            if (_fast is Func<Vector3> v3) return v3();
            if (_fast is Func<Vector2> v2) return v2();
            if (_fast is Func<Quaternion> q) return q().eulerAngles;

            return MemberValueReader.ToVector3(ReadRaw());
        }

        /// <summary>Reads the member as a Color, or returns the fallback.</summary>
        public Color GetColor(Color fallback)
        {
            if (!Ready()) return fallback;

            if (_fast is Func<Color> c) return c();

            return ReadRaw() is Color v ? v : fallback;
        }

        /// <summary>Reads the member as a Transform, coercing GameObject / Component members to their transform.</summary>
        public Transform GetTransform(Transform fallback)
        {
            if (!Ready()) return fallback;

            return ToTransform(_fast is Func<Object> o ? o() : ReadRaw());
        }

        /// <summary>Reads the member as a GameObject, coercing Component members to their gameObject.</summary>
        public GameObject GetGameObject(GameObject fallback)
        {
            if (!Ready()) return fallback;

            return ToGameObject(_fast is Func<Object> o ? o() : ReadRaw());
        }

        /// <summary>Reads the member as an Object of type T, or returns the fallback when the read fails.</summary>
        public T GetObject<T>(T fallback) where T : Object
        {
            if (!Ready()) return fallback;

            return (_fast is Func<Object> o ? o() : ReadRaw()) as T;
        }

        private static Transform ToTransform(object raw) => raw switch
        {
            Transform t => t,
            GameObject go => go.transform,
            Component c => c.transform,
            _ => null
        };

        private static GameObject ToGameObject(object raw) => raw switch
        {
            GameObject go => go,
            Component c => c.gameObject,
            _ => null
        };

        /// <summary>Human readable "Type.Member(args)" description of the configured member.</summary>
        public string Description()
        {
            string typeName = target != null ? target.GetType().Name : "?";
            string member = string.IsNullOrEmpty(memberName) ? "?" : memberName;
            string call = memberKind == MMemberKind.Method ? $"({paramSignature})" : string.Empty;
            string not = invert ? "!" : string.Empty;

            return $"{not}{typeName}.{member}{call}";
        }

        // ─── Cache building ──────────────────────────────────────────────────────

        // MWC — same shape as MemberValueReader.BuildCache, but the type comes from the target instance itself.
        private void BuildCache()
        {
            _cacheBuilt = true;
            _valid = false;
            _cachedTarget = target;
            _cachedMemberName = memberName;
            _cachedKind = memberKind;
            _getter = null;
            _field = null;
            _args = NoArgs;
            _fast = null;

            if (target == null || string.IsNullOrEmpty(memberName) || memberKind == MMemberKind.None)
                return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = target.GetType();

            switch (memberKind)
            {
                case MMemberKind.Property:
                    var prop = type.GetProperty(memberName, flags);
                    if (prop == null || !prop.CanRead || prop.GetIndexParameters().Length > 0) return;
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

                    if (argTypes.Length > 0)
                    {
                        // MWC — arguments are serialized constants, so they are boxed once here. An inspector
                        // edit re-deserializes the owner, which drops this cache (see OnAfterDeserialize).
                        _args = new object[argTypes.Length];
                        for (int i = 0; i < _args.Length; i++) _args[i] = args[i].Value();
                    }
                    break;
            }

            // MWC — bind the fastest form available; a null result simply leaves the Invoke path in place.
            if (_getter != null)
                _fast = _args.Length == 0 ? TryBindFast(_getter, target) : TryBindFastArgs(_getter, target);

            _valid = true;
        }

        /// <summary>
        /// Binds a parameterless getter to the target as a closed <c>Func&lt;T&gt;</c>, or null when the
        /// return type has no fast path. Delegate binding can be refused on AOT platforms, so a failure is
        /// swallowed and simply leaves the reflection path in place.
        /// </summary>
        private static Delegate TryBindFast(MethodInfo m, Object instance)
        {
            Type t = m.ReturnType;

            try
            {
                if (t == typeof(float)) return m.CreateDelegate(typeof(Func<float>), instance);
                if (t == typeof(int)) return m.CreateDelegate(typeof(Func<int>), instance);
                if (t == typeof(bool)) return m.CreateDelegate(typeof(Func<bool>), instance);
                if (t == typeof(string)) return m.CreateDelegate(typeof(Func<string>), instance);
                if (t == typeof(Vector2)) return m.CreateDelegate(typeof(Func<Vector2>), instance);
                if (t == typeof(Vector3)) return m.CreateDelegate(typeof(Func<Vector3>), instance);
                if (t == typeof(Quaternion)) return m.CreateDelegate(typeof(Func<Quaternion>), instance);
                if (t == typeof(Color)) return m.CreateDelegate(typeof(Func<Color>), instance);

                // MWC — every Object subtype shares one delegate shape (covariant return binding).
                if (typeof(Object).IsAssignableFrom(t)) return m.CreateDelegate(typeof(Func<Object>), instance);
            }
            catch (Exception)
            {
                return null;
            }

            return null; // enums and the exotic numeric types keep the reflection path
        }

        // ─── Fast path for methods that take arguments ───────────────────────────
        //
        // MWC — a method with parameters cannot be bound to a bare Func<T>, because the parameters are part
        // of the delegate's signature. The arguments are serialized constants though, so they are captured
        // into a closure once: Bind1..Bind4 build the typed Func<T0…,TRet>, pin the argument values, and
        // hand back the same plain Func<TRet> shape the parameterless path produces. Every typed getter then
        // matches it with no extra code, no per-call boxing and no Invoke.
        //
        // The generic instantiations are created at runtime, which an AOT backend may refuse (see AOTHints
        // for the ones that are pre-generated). A refusal throws, is caught, and leaves the Invoke path.

        /// <summary>Builds the closed <c>Func&lt;TRet&gt;</c> for a method with 1-4 constant arguments, or null.</summary>
        private Delegate TryBindFastArgs(MethodInfo m, Object instance)
        {
            if (!IsFastReturn(m.ReturnType)) return null;
            if (_args.Length is < 1 or > 4) return null;

            try
            {
                string binder = _args.Length switch
                {
                    1 => nameof(Bind1),
                    2 => nameof(Bind2),
                    3 => nameof(Bind3),
                    _ => nameof(Bind4)
                };

                var parameters = m.GetParameters();

                // Generic arguments are the parameter types followed by the return type.
                var typeArgs = new Type[_args.Length + 1];
                for (int i = 0; i < _args.Length; i++) typeArgs[i] = parameters[i].ParameterType;
                typeArgs[_args.Length] = m.ReturnType;

                var built = typeof(MemberGetter)
                            .GetMethod(binder, BindingFlags.NonPublic | BindingFlags.Static)
                            .MakeGenericMethod(typeArgs);

                // Call arguments are (method, instance, a0…aN).
                var callArgs = new object[_args.Length + 2];
                callArgs[0] = m;
                callArgs[1] = instance;
                for (int i = 0; i < _args.Length; i++) callArgs[i + 2] = _args[i];

                return (Delegate)built.Invoke(null, callArgs);
            }
            catch (Exception)
            {
                return null; // AOT refused the instantiation — the Invoke path still works
            }
        }

        /// <summary>True for the return types the typed getters know how to pick up from a Func&lt;T&gt;.</summary>
        private static bool IsFastReturn(Type t) =>
            t == typeof(float) || t == typeof(int) || t == typeof(bool) || t == typeof(string) ||
            t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Quaternion) || t == typeof(Color) ||
            typeof(Object).IsAssignableFrom(t);

        private static Func<TRet> Bind1<T0, TRet>(MethodInfo m, Object inst, object a0)
        {
            var f = (Func<T0, TRet>)m.CreateDelegate(typeof(Func<T0, TRet>), inst);
            T0 v0 = (T0)a0;
            return () => f(v0);
        }

        private static Func<TRet> Bind2<T0, T1, TRet>(MethodInfo m, Object inst, object a0, object a1)
        {
            var f = (Func<T0, T1, TRet>)m.CreateDelegate(typeof(Func<T0, T1, TRet>), inst);
            T0 v0 = (T0)a0; T1 v1 = (T1)a1;
            return () => f(v0, v1);
        }

        private static Func<TRet> Bind3<T0, T1, T2, TRet>(MethodInfo m, Object inst, object a0, object a1, object a2)
        {
            var f = (Func<T0, T1, T2, TRet>)m.CreateDelegate(typeof(Func<T0, T1, T2, TRet>), inst);
            T0 v0 = (T0)a0; T1 v1 = (T1)a1; T2 v2 = (T2)a2;
            return () => f(v0, v1, v2);
        }

        private static Func<TRet> Bind4<T0, T1, T2, T3, TRet>(MethodInfo m, Object inst, object a0, object a1, object a2, object a3)
        {
            var f = (Func<T0, T1, T2, T3, TRet>)m.CreateDelegate(typeof(Func<T0, T1, T2, T3, TRet>), inst);
            T0 v0 = (T0)a0; T1 v1 = (T1)a1; T2 v2 = (T2)a2; T3 v3 = (T3)a3;
            return () => f(v0, v1, v2, v3);
        }

        // MWC — never runs. It exists so an AOT build compiles these instantiations ahead of time, which is
        // what MakeGenericMethod needs at runtime. Reference-type arguments share one implementation, so the
        // Object rows also cover StatID, EquipPointID and every other Object-derived argument.
#pragma warning disable CS0649 // assigned nowhere on purpose — see above
        private static bool s_never; // not readonly: a constant would let the body be optimised away
#pragma warning restore CS0649

        [UnityEngine.Scripting.Preserve]
        private static void AOTHints()
        {
            if (!s_never) return;

            Bind1<Object, float>(null, null, null);
            Bind1<Object, int>(null, null, null);
            Bind1<Object, bool>(null, null, null);
            Bind1<Object, string>(null, null, null);
            Bind1<Object, Vector3>(null, null, null);
            Bind1<Object, Object>(null, null, null);

            Bind1<string, float>(null, null, null);
            Bind1<string, int>(null, null, null);
            Bind1<string, bool>(null, null, null);
            Bind1<int, float>(null, null, null);
            Bind1<int, bool>(null, null, null);
            Bind1<float, float>(null, null, null);
            Bind1<bool, bool>(null, null, null);

            Bind2<Object, Object, float>(null, null, null, null);
            Bind2<Object, int, float>(null, null, null, null);
            Bind2<Object, bool, float>(null, null, null, null);
        }

        // MWC — CLR types of the serialized arguments; null when one cannot be resolved (stale type),
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

    // ─── Editor helpers ──────────────────────────────────────────────────────────

    /// <summary>Shared editor plumbing for MemberGetter properties (used by VariableReferenceDrawer).</summary>
    public static class MemberGetterEditor
    {
        /// <summary>Writes a picked member signature into a serialized MemberGetter (name, kind, signature and rebuilt args).</summary>
        public static void ApplySelection(SerializedProperty getterProp, MGV_MemberSig sig)
        {
            getterProp.FindPropertyRelative("memberName").stringValue = sig.Name;
            getterProp.FindPropertyRelative("memberKind").enumValueIndex = (int)sig.Kind;
            getterProp.FindPropertyRelative("paramSignature").stringValue = sig.ParamSignature;

            var argsProp = getterProp.FindPropertyRelative("args");
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

                MemberValueReaderDrawer.ClearArgValues(a);
                MemberValueReaderDrawer.ApplyArgDefault(a, p, kind);
            }
        }

        /// <summary>Clears the picked member (target changed to an incompatible type).</summary>
        public static void ClearSelection(SerializedProperty getterProp)
        {
            getterProp.FindPropertyRelative("memberName").stringValue = string.Empty;
            getterProp.FindPropertyRelative("memberKind").enumValueIndex = (int)MMemberKind.None;
            getterProp.FindPropertyRelative("paramSignature").stringValue = string.Empty;
            getterProp.FindPropertyRelative("args").arraySize = 0;
        }
    }

#endif
}
