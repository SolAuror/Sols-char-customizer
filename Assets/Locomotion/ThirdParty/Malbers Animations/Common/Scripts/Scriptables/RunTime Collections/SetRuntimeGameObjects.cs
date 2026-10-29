using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    [AddComponentMenu("Malbers/Runtime Vars/Set Runtime GameObjects")]
    public class SetRuntimeGameObjects : MonoBehaviour
    {
        [CreateScriptableAsset] public List<RuntimeGameObjects> Collections;

        private void OnEnable()
        {
            if (Collections == null) Collections = new();

            foreach (var item in Collections)
            {
                item?.Item_Add(gameObject);
            }
        }

        private void OnDisable()
        {
            foreach (var item in Collections)
            {
                item?.Item_Remove(gameObject);
            }
        }

        public virtual void RemoveSelf()
        {
            foreach (var item in Collections)
            {
                item?.Item_Remove(gameObject);
            }
        }

        public virtual void AddSelf()
        {
            Collections ??= new();
            foreach (var item in Collections)
            {
                item?.Item_Add(gameObject);
            }
        }
    }


#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(SetRuntimeGameObjects)), UnityEditor.CanEditMultipleObjects]
    public class SetRuntimeGameObjectsEditor : UnityEditor.Editor
    {
        public static GUIStyle StyleBlue => MTools.Style(new Color(0, 0.5f, 1f, 0.3f));
        SetRuntimeGameObjects M;

        private void OnEnable() => M = (SetRuntimeGameObjects)target;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var Collection = serializedObject.FindProperty("Collections");
            UnityEditor.EditorGUILayout.PropertyField(Collection);
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}