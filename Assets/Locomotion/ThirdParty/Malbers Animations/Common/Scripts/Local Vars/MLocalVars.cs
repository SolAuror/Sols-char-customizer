using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MalbersAnimations
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/scriptable-architecture/mlocal-variables")]
    [AddComponentMenu("Malbers/Runtime Vars/Local Variables [BlackBoard]")]
    public class MLocalVars : MonoBehaviour, ILocalVars
    {
        public LocalVars localVars = new();

        /// <summary> Direct access to the serialized variable list. </summary>
        public List<LocalVar> variables => localVars.variables;

        /// <summary> Direct access to the runtime dictionary. </summary>
        public Dictionary<string, object> vars => localVars.vars;

        public void Start() => localVars.Initialize(gameObject.name);

        // ── Subscriptions ────────────────────────────────────────────────────
        public void Subscribe(string varName, Action<string> callback) => localVars.Subscribe(varName, callback);
        public void Unsubscribe(string varName, Action<string> callback) => localVars.Unsubscribe(varName, callback);
        public void ClearListeners(string varName) => localVars.ClearListeners(varName);
        internal void FireChanged(string varName) => localVars.FireChanged(varName);

        // ── Set / Get ─────────────────────────────────────────────────────────


        //   public LocalVar GetVar(string varName) => localVars.GetVar(varName);
        public bool SetVar<T>(string name, T value) => localVars.SetVar(name, value);
        public T GetVar<T>(string name) => localVars.GetVar<T>(name);

        public virtual bool HasVar(string name) => localVars.HasVar(name);
        public virtual bool HasVar(LocalVar var) => localVars.HasVar(var);

        // ── Pin ──────────────────────────────────────────────────────────────
        public void Pin_Var(string name) => localVars.Pin_Var(name);
        public void Var_Set_True(string name) => localVars.Var_Set_True(name);
        public void Var_Set_False(string name) => localVars.Var_Set_False(name);

        public virtual void Pin_SetValue(int value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(float value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(bool value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(string value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(Vector2 value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(Vector3 value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(GameObject value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(Transform value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(Material value) => localVars.Pin_SetValue(value);
        public virtual void Pin_SetValue(Object value) => localVars.Pin_SetValue(value);

        // ── Compare ───────────────────────────────────────────────────────────
        public bool Compare(LocalVar value, ComparerNumber compare = ComparerNumber.Equal) => localVars.Compare(value, compare);

        public int GetInt(string name) => GetVar<int>(name);
        public float GetFloat(string name) => GetVar<float>(name);
        public bool GetBool(string name) => GetVar<bool>(name);
        public string GetString(string name) => GetVar<string>(name);

        public void SetInt(string name, int value) => SetVar(name, value);
        public void SetFloat(string name, float value) => SetVar(name, value);
        public void SetBool(string name, bool value) => SetVar(name, value);
        public void SetString(string name, string value) => SetVar(name, value);

        public void SetVar(ILocalVar var)
        {
            if (var == null) return;

            var name = var.Name;


            if (!localVars.vars.ContainsKey(name))
            {
                Debug.LogWarning($"Variable '{name}' does not exist in {gameObject.name}'s Local Variables.");
                return;
            }

            SetVar(name, var.GetValueRaw());
        }

        public T GetVarEditor<T>(string name) => localVars.GetVarEditor<T>(name);
    }


#if UNITY_EDITOR

    [CustomEditor(typeof(MLocalVars))]
    public class MLocalVarsEditor : Editor
    {
        private SerializedProperty variables;
        private MLocalVars M;

        private void OnEnable()
        {
            variables = serializedObject.FindProperty("localVars.variables");
            M = target as MLocalVars;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                EditorGUILayout.PropertyField(variables);

                if (cc.changed && Application.isPlaying)
                {
                    serializedObject.ApplyModifiedProperties();

                    foreach (var item in M.variables)
                    {
                        if (!M.vars.ContainsKey(item.name)) continue;

                        var oldValue = M.vars[item.name];
                        var newValue = item.GetValueRaw();

                        if (Equals(oldValue, newValue)) continue;

                        M.vars[item.name] = newValue;
                        item.InvokeEvent(newValue);
                        M.FireChanged(item.name);
                    }
                    return;
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}