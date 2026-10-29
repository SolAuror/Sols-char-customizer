using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    [DefaultExecutionOrder(-1001)]
    [AddComponentMenu("Malbers/Runtime Vars/GameObject Hook")]

    public class GameObjectHook : MonoBehaviour
    {
        [RequiredField, Tooltip("Scriptable Asset to Store this GameObject as a reference to avoid Scene Dependencies")]
        public GameObjectVar Hook;

        [Tooltip("Transform that it will be saved on the Transform var asset")]
        public GameObject Reference;

        private void OnEnable() => UpdateHook();

        private void OnDisable()
        {
            DisableHook(); //Disable it only when is not this gameobject
        }

        private void OnValidate()
        {
            if (Reference == null) Reference = gameObject; //If the Reference is null, set it to this GameObject
        }

        public virtual void UpdateHook()
        {
            if (Reference == null) Reference = gameObject;

            if (Hook) Hook.Value = Reference;
        }

        public virtual void DisableHook()
        {
            if (Hook && Hook.Value == gameObject) Hook.Value = null;
        }
    }


#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(GameObjectHook)), UnityEditor.CanEditMultipleObjects]
    public class GameObjectHookEditor : UnityEditor.Editor
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