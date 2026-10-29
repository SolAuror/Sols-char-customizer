using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary>
    /// Per-GameObject Blackboard.  Variables are looked up by string name or integer ID.
    /// Variables can be defined at edit-time in the Inspector or added/removed at runtime.
    /// </summary>
    [AddComponentMenu("Malbers/Runtime Vars/Local Variables 2 [BlackBoard]")]
    [DisallowMultipleComponent]
    public class MLocalVarsBlackboard : MonoBehaviour, ILocalVars
    {
        [SerializeReference, SerializeField]
        private List<BlackboardVar> _variables = new();

        /// <summary>Counter used to hand out unique IDs. Serialised so edit-time IDs persist.</summary>
        [SerializeField] private int _nextId = 0;

        private Dictionary<string, BlackboardVar> _byName;
        private Dictionary<int, BlackboardVar> _byId;

        // ─── Lifecycle ───────────────────────────────────────────────────────────

        private void Awake()
        {
            RebuildRegistry();
        }

        private void OnEnable()
        {
            // Broadcast every variable's current value through its Unity event
            // so any Inspector-wired listeners receive the initial state.
            foreach (var v in _variables)
                v?.InvokeEvents();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Keep the registry consistent whenever the Inspector changes in Edit Mode.
            RebuildRegistry();
        }
#endif

        // ─── Registry ────────────────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds both lookup dictionaries from the serialised variable list.
        /// Call this after any programmatic rename, add, or remove.
        /// </summary>
        public void RebuildRegistry()
        {
            _byName ??= new(_variables.Count);
            _byId ??= new(_variables.Count);
            _byName.Clear();
            _byId.Clear();

            foreach (var v in _variables)
            {
                if (v == null) continue;
                if (!string.IsNullOrEmpty(v.Name)) _byName[v.Name] = v;
                if (v.Id >= 0) _byId[v.Id] = v;
            }
        }

        // ─── Get ─────────────────────────────────────────────────────────────────

        /// <summary>Returns true and the typed value if the variable exists and matches <typeparamref name="T"/>.</summary>
        public bool TryGet<T>(string name, out T value)
        {
            if (_byName != null && _byName.TryGetValue(name, out var v) && v is BlackboardVar<T> t)
            {
                value = t.Get();
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>Returns true and the typed value if the variable exists and matches <typeparamref name="T"/>.</summary>
        public bool TryGet<T>(int id, out T value)
        {
            if (_byId != null && _byId.TryGetValue(id, out var v) && v is BlackboardVar<T> t)
            {
                value = t.Get();
                return true;
            }
            value = default;
            return false;
        }



        /// <summary>Returns the value, or <c>default</c> if not found.</summary>
        public T Get<T>(string name) { TryGet(name, out T v); return v; }

        public T GetVar<T>(string name) => Get<T>(name);

        /// <summary>Returns the value, or <c>default</c> if not found.</summary>
        public T Get<T>(int id) { TryGet(id, out T v); return v; }

        // ─── Easy Basic Gets ─────────────────────────────────────────────────────────────────

        public int GetInt(string name) => Get<int>(name);
        public float GetFloat(string name) => Get<float>(name);
        public bool GetBool(string name) => Get<bool>(name);
        public string GetString(string name) => Get<string>(name);

        public void SetInt(string name, int value) => Set(name, value);
        public void SetFloat(string name, float value) => Set(name, value);
        public void SetBool(string name, bool value) => Set(name, value);

        public void SetString(string name, string value) => Set(name, value);

        // MWC v2: Easy 1-argument bool setters (mirror MLocalVars.Var_Set_True/False) for UnityEvent wiring.
        public void Var_Set_True(string name) => Set(name, true);
        public void Var_Set_False(string name) => Set(name, false);

        public void SetVar(ILocalVar var)
        {
            if (var == null) return;
            if (var is BlackboardVar v)
            {
                if (HasVariable(v.Name))
                    Set(v.Name, v.GetValueRaw());
                else
                    Debug.LogWarning($"[Blackboard] No variable named '{v.Name}' found on '{gameObject.name}' to set from '{var}'.", this);
            }
            else
            {
                Debug.LogWarning($"[Blackboard] Variable '{var}' is not a BlackboardVar and cannot be set on '{gameObject.name}'.", this);
            }
        }



        // ─── Set ─────────────────────────────────────────────────────────────────

        public bool SetVar<T>(string name, T value) => Set(name, value);

        /// <summary>Sets the value by name. Change events fire only if the value actually changes.</summary>
        public bool Set<T>(string name, T value)
        {
            if (_byName != null && _byName.TryGetValue(name, out var v) && v is BlackboardVar<T> t)
            {
                t.Set(value);
                return true;
            }
            return false;
        }

        /// <summary>Sets the value by ID. Change events fire only if the value actually changes.</summary>
        public bool Set<T>(int id, T value)
        {
            if (_byId != null && _byId.TryGetValue(id, out var v) && v is BlackboardVar<T> t)
            {
                t.Set(value);
                return true;
            }
            return false;
        }


        public void ClearVar(string name)
        {
            if (_byName != null && _byName.TryGetValue(name, out var v))
            {
                if (v is BlackObject o) o.Set(null);
            }
        }

        public void SetNull(string name) => ClearVar(name);



        // ─── Listen / StopListening ──────────────────────────────────────────────

        //  Typed C# Action<T> ──────────────────────────────────────────────────

        public bool Listen<T>(string name, Action<T> cb) => ToggleTyped(name, cb, true);
        public bool StopListening<T>(string name, Action<T> cb) => ToggleTyped(name, cb, false);

        public bool Listen<T>(int id, Action<T> cb) => ToggleTypedById(id, cb, true);
        public bool StopListening<T>(int id, Action<T> cb) => ToggleTypedById(id, cb, false);

        //  Untyped Action<object> ─────────────────────────────────────────────

        public bool Listen(string name, Action<object> cb)
        {
            if (_byName != null && _byName.TryGetValue(name, out var v))
            { v.OnValueChangedRaw += cb; return true; }
            return false;
        }

        public bool StopListening(string name, Action<object> cb)
        {
            if (_byName != null && _byName.TryGetValue(name, out var v))
            { v.OnValueChangedRaw -= cb; return true; }
            return false;
        }

        /// <summary>
        /// Adds or removes the callback from the variable's typed change event, if the variable exists and matches <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T"> The expected variable type. The callback is added/removed only if the variable exists and is a BlackboardVariable&lt;T&gt;.</typeparam>
        /// <param name="name"> The variable name. Lookup fails if not found.</param>
        /// <param name="cb"> The callback to add or remove. Should match the variable type <typeparamref name="T"/>. Added to the variable's OnValueChanged event, if found and typed correctly.</param>
        /// <param name="add"> If true, adds the callback; if false, removes it.</param>
        /// <returns></returns>
        private bool ToggleTyped<T>(string name, Action<T> cb, bool add)
        {
            if (_byName != null && _byName.TryGetValue(name, out var v) && v is BlackboardVar<T> t)
            { if (add) t.OnValueChanged += cb; else t.OnValueChanged -= cb; return true; }
            return false;
        }

        private bool ToggleTypedById<T>(int id, Action<T> cb, bool add)
        {
            if (_byId != null && _byId.TryGetValue(id, out var v) && v is BlackboardVar<T> t)
            { if (add) t.OnValueChanged += cb; else t.OnValueChanged -= cb; return true; }
            return false;
        }

        // ─── Direct Variable Access ──────────────────────────────────────────────

        public BlackboardVar GetVariable(string name)
        {
            BlackboardVar v = null;
            _byName?.TryGetValue(name, out v);
            return v;
        }

        public BlackboardVar GetVariable(int id)
        {
            BlackboardVar v = null;
            _byId?.TryGetValue(id, out v);
            return v;
        }
        public BlackboardVar<T> GetVariable<T>(string name) => GetVariable(name) as BlackboardVar<T>;
        public BlackboardVar<T> GetVariable<T>(int id) => GetVariable(id) as BlackboardVar<T>;

        public bool HasVariable(string name) => _byName != null && _byName.ContainsKey(name);
        public bool HasVariable(int id) => _byId != null && _byId.ContainsKey(id);

        /// <summary>Read-only view of the full variable list in order.</summary>
        public IReadOnlyList<BlackboardVar> GetAllVariables() => _variables;

        // ─── Runtime Add ─────────────────────────────────────────────────────────

        public BlackFloat AddFloat(string name, float value = 0f) => Add<BlackFloat, float>(name, value);
        public BlackInt AddInt(string name, int value = 0) => Add<BlackInt, int>(name, value);
        public BlackBool AddBool(string name, bool value = false) => Add<BlackBool, bool>(name, value);
        public BlackString AddString(string name, string value = "") => Add<BlackString, string>(name, value);

        /// <param name="objectType">Optional runtime type used to filter object assignment.</param>
        public BlackObject AddObject(string name, UnityEngine.Object value = null, Type objectType = null)
        {
            if (!GuardAdd(name)) return null;

            var variable = new BlackObject();
            InitVariable(variable, name);
            if (objectType != null) variable.ObjectTypeName = objectType.FullName ?? objectType.Name;
            if (value != null) variable.Set(value);

            Register(variable);
            return variable;
        }

        private TVar Add<TVar, TVal>(string name, TVal value) where TVar : BlackboardVar<TVal>, new()
        {
            if (!GuardAdd(name)) return null;

            var variable = new TVar();
            InitVariable(variable, name);

            // Set without event noise – nothing is subscribed yet on a brand-new variable.
            variable.Set(value);

            Register(variable);
            return variable;
        }

        // ─── Runtime Remove ──────────────────────────────────────────────────────

        /// <summary>Removes a variable by name. Returns false if not found.</summary>
        public bool RemoveVariable(string name)
        {
            if (_byName == null || !_byName.TryGetValue(name, out var v)) return false;
            Unregister(v);
            return true;
        }

        /// <summary>Removes a variable by ID. Returns false if not found.</summary>
        public bool RemoveVariable(int id)
        {
            if (_byId == null || !_byId.TryGetValue(id, out var v)) return false;
            Unregister(v);
            return true;
        }

        // ─── Rename ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Renames a variable and updates the name registry atomically.
        /// Returns false if <paramref name="currentName"/> is not found or <paramref name="newName"/> is already taken.
        /// </summary>
        public bool RenameVariable(string currentName, string newName)
        {
            if (_byName == null || !_byName.TryGetValue(currentName, out var v)) return false;
            if (_byName.ContainsKey(newName)) return false;

            _byName.Remove(currentName);
            v.Name = newName;
            _byName[newName] = v;
            return true;
        }

        // ─── Debug ───────────────────────────────────────────────────────────────

        [ContextMenu("Blackboard/Print All Variables")]
        private void DebugPrint()
        {
            foreach (var v in _variables)
            {
                if (v != null)
                    Debug.Log($"[Blackboard] [{v.Id:D3}] {v.Name} ({v.ValueType.Name}) = {v.GetValueRaw()}", this);
            }
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private bool GuardAdd(string name)
        {
            if (HasVariable(name))
            {
                Debug.LogWarning($"[Blackboard] Variable '{name}' already exists on '{gameObject.name}'.", this);
                return false;
            }
            return true;
        }

        private void InitVariable(BlackboardVar v, string name)
        {
            v.Name = name;
            v.Id = _nextId++;
        }

        private void Register(BlackboardVar v)
        {
            _variables.Add(v);
            _byName ??= new();
            _byId ??= new();
            _byName[v.Name] = v;
            if (v.Id >= 0) _byId[v.Id] = v;
        }

        private void Unregister(BlackboardVar v)
        {
            _variables.Remove(v);
            if (!string.IsNullOrEmpty(v.Name)) _byName?.Remove(v.Name);
            if (v.Id >= 0) _byId?.Remove(v.Id);
        }

        public T GetVarEditor<T>(string name)
        {
#if UNITY_EDITOR
            if (_variables != null && _variables.Find(v => v.Name == name) is BlackboardVar<T> variable)
            {
                return variable.Get();
            }
#endif
            return default;
        }
    }
}