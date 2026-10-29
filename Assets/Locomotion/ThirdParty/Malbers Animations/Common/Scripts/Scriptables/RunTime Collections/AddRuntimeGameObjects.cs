using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    [AddComponentMenu("Malbers/Runtime Vars/Add Runtime GameObjects")]
    public class AddRuntimeGameObjects : MonoBehaviour
    {
        [CreateScriptableAsset] public RuntimeGameObjects Collection;

        protected virtual void OnEnable() => Collection?.Item_Add(gameObject);

        private void OnDisable() => Collection?.Item_Remove(gameObject);

        public virtual void RemoveSelf() => Collection?.Item_Remove(gameObject);
        public virtual void AddSelf() => Collection?.Item_Add(gameObject);
    }


#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(AddRuntimeGameObjects)), UnityEditor.CanEditMultipleObjects]
    public class AddRuntimeGameObjectsEditor : UnityEditor.Editor
    {
        public static GUIStyle StyleBlue => MTools.Style(new Color(0, 0.5f, 1f, 0.3f));
        AddRuntimeGameObjects M;
        UnityEditor.SerializedProperty Collection;

        protected virtual void OnEnable()
        {
            M = (AddRuntimeGameObjects)target;
            Collection = serializedObject.FindProperty("Collection");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (M.Collection && !string.IsNullOrEmpty(M.Collection.Description))
                MalbersEditor.DrawDescription(M.Collection.Description);

            UnityEditor.EditorGUILayout.PropertyField(Collection);

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}