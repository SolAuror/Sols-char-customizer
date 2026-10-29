using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("Malbers/Runtime Vars/Transform Hook")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/global-components/scriptables/transform-hook")]
    public class TransformHook : MonoBehaviour
    {
        [Tooltip("Transform that it will be saved on the Transform var asset")]
        public Transform Reference;

        [Tooltip("Transform Scriptable var that will store at runtime a transform")]
        [CreateScriptableAsset] public TransformVar Hook;

        private void OnEnable()
        {
            if (Reference == null) Reference = transform;

            UpdateHook();
        }

        private void OnDisable()
        {
            if (Hook != null && Hook.Value == Reference) DisableHook();  //Disable it only when is not this transform
        }

        private void OnValidate()
        {
            if (Reference == null) Reference = transform;
        }

        public virtual void UpdateHook()
        {
            if (Hook != null) Hook.Value = Reference;
        }

        public virtual void SetByName(string name)
        {
            var findCore = this.FindInterface<IObjectCore>();

            if (findCore != null)
                Hook.Value = findCore.transform.FindGrandChild(name);
        }

        public virtual void DisableHook() => Hook.Value = null;
        public virtual void RemoveHook() => Hook.Value = null;
        public virtual void RemoveHook(Transform val)
        {
            if (Hook.Value == val) Hook.Value = null;
        }
    }


#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(TransformHook)), UnityEditor.CanEditMultipleObjects]
    public class TransformHookEditor : UnityEditor.Editor
    {
        UnityEditor.SerializedProperty Hook, Reference;

        private void OnEnable()
        {
            Hook = serializedObject.FindProperty("Hook");
            Reference = serializedObject.FindProperty("Reference");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            UnityEditor.EditorGUILayout.Space();

            using (new GUILayout.HorizontalScope())
            {
                EditorGUIUtility.labelWidth = 40;
                UnityEditor.EditorGUILayout.PropertyField(Hook, new GUIContent("Hook", "Scriptable Asset to store the Reference Transform. Used to avoid scene dependencies"));
                EditorGUIUtility.labelWidth = 50;
                UnityEditor.EditorGUILayout.PropertyField(Reference, new GUIContent("    Value"));
                EditorGUIUtility.labelWidth = 0;
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}