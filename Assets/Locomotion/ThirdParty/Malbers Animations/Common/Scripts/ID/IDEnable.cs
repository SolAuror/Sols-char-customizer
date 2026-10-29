using MalbersAnimations.Scriptables;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations
{
    [System.Serializable]
    public class IDString<T> where T : IDs
    {
        public bool active;
        public T ID;
        public StringReference value;
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(IDString<>), true)]
    public class IDValueDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect pos, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(pos, label, property);

            if (label.text.Contains("Element"))
            {
                pos.x += 12;
                pos.width -= 12;
            }
            else
                pos = EditorGUI.PrefixLabel(pos, label);

            EditorGUI.BeginChangeCheck();

            pos.height = EditorGUIUtility.singleLineHeight;

            var IDRect = new Rect(pos) { x = pos.x + 20, width = pos.width * 0.5f - 10 };
            var valueRect = new Rect(pos) { x = IDRect.x + IDRect.width + 20, width = pos.width - 40 };
            valueRect.width = valueRect.width * 0.5f - 10;
            var activeRect = new Rect(pos) { x = IDRect.x - 40, width = 20 };

            var ID = property.FindPropertyRelative("ID");
            var active = property.FindPropertyRelative("active");
            var value = property.FindPropertyRelative("value");

            EditorGUI.PropertyField(IDRect, ID, GUIContent.none, false);
            EditorGUI.PropertyField(valueRect, value, GUIContent.none, false);
            EditorGUI.PropertyField(activeRect, active, GUIContent.none, false);

            if (EditorGUI.EndChangeCheck())
                property.serializedObject.ApplyModifiedProperties();

            EditorGUI.EndProperty();
        }
    }
#endif
}
