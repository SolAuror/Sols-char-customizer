// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary>
    /// Non-generic base for all Blackboard variables.
    /// Holds the name, integer ID, and the untyped C# Action for callers
    /// that don't know T at compile time.  The serializable typed UnityEvent
    /// lives in each concrete subclass so it carries the value as its argument.
    /// </summary>
    [Serializable]
    public abstract class BlackboardVar
    {
        [SerializeField] private string _name = "NewVariable";
        [SerializeField] private int _id = -1;

        /// <summary>Display / lookup name.  After renaming call Blackboard.RebuildRegistry().</summary>
        public string Name { get => _name; set => _name = value; }

        /// <summary>Stable integer ID assigned by the Blackboard or the custom inspector.</summary>
        public int Id { get => _id; set => _id = value; }

        public abstract Type ValueType { get; }
        public abstract object GetValueRaw();
        public abstract bool SetValueRaw(object value);

        /// <summary>Invokes the serialised Unity event with the current value. Called on OnEnable.</summary>
        public abstract void InvokeEvents();

        // ── Untyped C# Action (fires with the new value boxed as object) ──────────

        [NonSerialized] private Action<object> _onValueChangedRaw;

        /// <summary>Untyped C# event – fires with the new value boxed as object.</summary>
        public event Action<object> OnValueChangedRaw
        {
            add => _onValueChangedRaw += value;
            remove => _onValueChangedRaw -= value;
        }

        protected void NotifyChangedRaw(object newValue) => _onValueChangedRaw?.Invoke(newValue);
    }

    // ─── Generic typed base ──────────────────────────────────────────────────────

    /// <summary>
    /// Strongly-typed Blackboard variable.
    /// Provides the typed C# Action and calls InvokeUnityEvent() on every change
    /// so each concrete subclass can fire its own Malbers typed event with the value.
    /// </summary>
    [Serializable]
    public abstract class BlackboardVar<T> : BlackboardVar, ILocalVar
    {
        [SerializeField] protected T _value;

        [NonSerialized] private Action<T> _onValueChanged;

        /// <summary>Typed C# Action – fires with the new value on every change.</summary>
        public event Action<T> OnValueChanged
        {
            add => _onValueChanged += value;
            remove => _onValueChanged -= value;
        }

        public override Type ValueType => typeof(T);
        public override object GetValueRaw() => _value;

        public override bool SetValueRaw(object value)
        {
            if (value is T typed) { Set(typed); return true; }
            return false;
        }

        /// <summary>Returns the current value.</summary>
        public T Get() => _value;

        /// <summary>
        /// Sets the value.  All events fire ONLY when the value actually changes.
        /// </summary>
        public void Set(T value)
        {
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            _value = value;
            _onValueChanged?.Invoke(value);
            NotifyChangedRaw(value);
            InvokeUnityEvent(value);
        }

        /// <summary>
        /// Implemented by each concrete class to invoke its serialized Malbers typed event.
        /// </summary>
        protected abstract void InvokeUnityEvent(T value);

        /// <inheritdoc/>
        public override void InvokeEvents() => InvokeUnityEvent(_value);
    }

    // ─── Concrete sealed types ───────────────────────────────────────────────────

    [Serializable]
    public sealed class BlackFloat : BlackboardVar<float>
    {
        [SerializeField] private FloatEvent _onChanged = new();
        public FloatEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(float value) => _onChanged?.Invoke(value);
    }

    [Serializable]
    public sealed class BlackInt : BlackboardVar<int>
    {
        [SerializeField] private IntEvent _onChanged = new();
        public IntEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(int value) => _onChanged?.Invoke(value);
    }

    [Serializable]
    public sealed class BlackBool : BlackboardVar<bool>
    {
        [SerializeField] private BoolEvent _onChanged = new();
        public BoolEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(bool value) => _onChanged?.Invoke(value);
    }

    [Serializable]
    public sealed class BlackString : BlackboardVar<string>
    {
        [SerializeField] private StringEvent _onChanged = new();
        public StringEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(string value) => _onChanged?.Invoke(value);
    }

    [Serializable]
    public sealed class BlackVector3 : BlackboardVar<Vector3>
    {
        [SerializeField] private Vector3Event _onChanged = new();
        public Vector3Event OnChanged => _onChanged;
        protected override void InvokeUnityEvent(Vector3 value) => _onChanged?.Invoke(value);
    }

    [Serializable]
    public sealed class BlackVector2 : BlackboardVar<Vector2>
    {
        [SerializeField] private Vector2Event _onChanged = new();
        public Vector2Event OnChanged => _onChanged;
        protected override void InvokeUnityEvent(Vector2 value) => _onChanged?.Invoke(value);
    }

    [Serializable]
    public sealed class BlackColor : BlackboardVar<Color>
    {
        [SerializeField] private ColorEvent _onChanged = new();
        public ColorEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(Color value) => _onChanged?.Invoke(value);
    }

    /// <summary>Holds an IntVar ScriptableObject reference. Fires IntEvent with its .Value when the reference changes.</summary>
    [Serializable]
    public sealed class BlackIntVar : BlackboardVar<IntVar>
    {
        [SerializeField] private IntEvent _onChanged = new();
        public IntEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(IntVar value)
            => _onChanged?.Invoke(value != null ? value.Value : 0);
    }

    /// <summary>Holds a FloatVar ScriptableObject reference. Fires FloatEvent with its .Value when the reference changes.</summary>
    [Serializable]
    public sealed class BlackFloatVar : BlackboardVar<FloatVar>
    {
        [SerializeField] private FloatEvent _onChanged = new();
        public FloatEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(FloatVar value)
            => _onChanged?.Invoke(value != null ? value.Value : 0f);
    }

    /// <summary>Holds a BoolVar ScriptableObject reference. Fires BoolEvent with its .Value when the reference changes.</summary>
    [Serializable]
    public sealed class BlackBoolVar : BlackboardVar<BoolVar>
    {
        [SerializeField] private BoolEvent _onChanged = new();
        public BoolEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(BoolVar value)
            => _onChanged?.Invoke(value != null && value.Value);
    }

    /// <summary>Holds a StringVar ScriptableObject reference. Fires StringEvent with its .Value when the reference changes.</summary>
    [Serializable]
    public sealed class BlackStringVar : BlackboardVar<StringVar>
    {
        [SerializeField] private StringEvent _onChanged = new();
        public StringEvent OnChanged => _onChanged;
        protected override void InvokeUnityEvent(StringVar value)
            => _onChanged?.Invoke(value != null ? value.Value : string.Empty);
    }

    /// <summary>
    /// A UnityEngine.Object variable whose ObjectField is filtered by a configurable subtype.
    /// Set <see cref="ObjectTypeName"/> to any type name resolvable at runtime,
    /// e.g. "Animator", "AudioClip", "MyScriptableObject".
    /// </summary>
    [Serializable]
    public sealed class BlackObject : BlackboardVar<UnityEngine.Object>
    {
        /// <summary>Inspector-hookable event – fires with the new object value.</summary>
        [SerializeField] private UnityObjectEvent _onChanged = new();
        public UnityObjectEvent OnChanged => _onChanged;

        protected override void InvokeUnityEvent(UnityEngine.Object value) => _onChanged?.Invoke(value);

        // ── Subtype filtering ─────────────────────────────────────────────────────

        [SerializeField] private string _objectTypeName = "UnityEngine.Object";

        [NonSerialized] private Type _resolvedType;

        public void SetNull() => Set(null);

        /// <summary>The resolved Type used to filter the ObjectField in the Inspector.</summary>
        public Type ObjectType => _resolvedType ??= TypeResolver.Resolve(_objectTypeName);

        /// <summary>Short or fully-qualified type name, e.g. "Animator" or "UnityEngine.AudioClip".</summary>
        public string ObjectTypeName
        {
            get => _objectTypeName;
            set
            {
                if (_objectTypeName == value) return;
                _objectTypeName = value;
                _resolvedType = null;   // invalidate resolved-type cache
            }
        }
    }

    // ─── Type resolver ───────────────────────────────────────────────────────────

    /// <summary>Resolves a type name across all loaded assemblies.</summary>
    public static class TypeResolver
    {
        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return typeof(UnityEngine.Object);

            var type = Type.GetType(typeName);
            if (type != null) return type;

            foreach (var asm in MalbersReflection.GetLoadedAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) return type;
            }

            return typeof(UnityEngine.Object);
        }
    }
}