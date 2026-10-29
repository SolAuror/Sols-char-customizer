// Last Change: 9/18/2026 11:00am
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations.Scriptables
{
    public abstract class ScriptableVar : ScriptableObject
    {
#if UNITY_EDITOR
        [TextArea(3, 20)]
        public string Description = "";
#endif
        public bool debug = false;

        //MWC — Text serialization API. Each variable knows how to store and restore its own value, so
        //persistence systems (PlayerPrefs, save files) no longer need a per-type is/as dispatch chain.
        //Default is "not serializable" so reference-holding vars (Transform, GameObject, Material, Sprite)
        //and computed vars (ranges, formulas) opt in explicitly instead of being persisted by accident.

        /// <summary>True when this variable's value can be stored as text (PlayerPrefs, save files).</summary>
        public virtual bool CanSerialize => false;

        /// <summary>Writes the current value as a culture-invariant string.</summary>
        public virtual string GetSerializedValue() => string.Empty;

        /// <summary>Restores the value from [GetSerializedValue]; returns false when the data cannot be parsed.</summary>
        public virtual bool SetSerializedValue(string data) => false;

        /// <summary>Parses JSON into a struct value, returning false instead of throwing when the data is malformed.</summary>
        protected static bool TryFromJson<T>(string data, out T result)
        {
            result = default;
            if (string.IsNullOrEmpty(data)) return false;

            try { result = JsonUtility.FromJson<T>(data); }
            catch { return false; }

            return true;
        }
    }

    /// <summary> Base for all Local Scriptable Reference Variables </summary>
    [System.Serializable]
    public abstract class ReferenceVar
    {
        /// <summary> Use Local Value instead of Global Value (Scriptable Var)  </summary>
        public bool UseConstant = true;

        /// <summary>Use Reference mode: read the value from a member on another component/object (read-only). Overrides UseConstant when true.</summary>
        public bool UseMember = false;

        /// <summary>Member getter used when [UseMember] is enabled (target object + field/property/method).</summary>
        public MemberGetter Member = new();

#if UNITY_EDITOR
        //MWC — a write inside Update would otherwise log every frame, and a log call is far more expensive
        //than the write it is reporting, so each reference warns only the first time.
        [System.NonSerialized] private bool m_memberWriteLogged;
#endif

        /// <summary>True when writes must be ignored because the value comes from a member reference (warns once in the Editor).</summary>
        protected bool BlockedByMember()
        {
            if (!UseMember) return false;
#if UNITY_EDITOR
            if (!m_memberWriteLogged)
            {
                m_memberWriteLogged = true;
                Debug.LogWarning($"[{GetType().Name}] uses a Member Reference [{Member.Description()}]. The value is read-only and the write was ignored. (further writes are silent)");
            }
#endif
            return true;
        }
    }


#if UNITY_EDITOR
    /// <summary> Editor only. With [Enter Play Mode Options: Domain Reload OFF] a Scriptable Var asset keeps its runtime value after Play Mode, so the Transform/GameObject vars still point at (dead or reloaded) scene objects in Edit Mode and the UI that follows them lights up on the next Play. Cleared on the way back to Edit Mode </summary>
    [InitializeOnLoad]
    public static class ScriptableVarPlayModeReset
    {
        static ScriptableVarPlayModeReset() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        /// <summary> Clears every scene reference left on the Transform and GameObject var assets once Edit Mode is back </summary>
        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;

            foreach (var tv in Resources.FindObjectsOfTypeAll<TransformVar>())
                if (EditorUtility.IsPersistent(tv) && IsRuntimeReference(tv.Value)) tv.SetNull();

            foreach (var gv in Resources.FindObjectsOfTypeAll<GameObjectVar>())
                if (EditorUtility.IsPersistent(gv) && IsRuntimeReference(gv.Value)) gv.Value = null;
        }

        /// <summary> True for a destroyed object or a scene object: anything an asset cannot legitimately hold in Edit Mode </summary>
        private static bool IsRuntimeReference(Object o) => (object)o != null && (o == null || !EditorUtility.IsPersistent(o));
    }

    [CustomEditor(typeof(ScriptableVar), true)]
    public class VariableEditor : Editor
    {
        public static GUIStyle StyleBlue => MTools.Style(new Color(0, 0.5f, 1f, 0.3f));

        protected SerializedProperty value, Description, debug;

        private void OnEnable()
        {
            value = serializedObject.FindProperty("value");
            Description = serializedObject.FindProperty("Description");
            debug = serializedObject.FindProperty("debug");
        }

        public virtual void PaintInspectorGUI(string title)
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription(title);


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(value, new GUIContent("Value", "The current value"));
                    MalbersEditor.DrawDebugIcon(debug);
                }

                ExtraValues();

                EditorGUILayout.PropertyField(Description, GUIContent.none);
            }
            serializedObject.ApplyModifiedProperties();
        }

        public virtual void ExtraValues() { }
    }
#endif
}