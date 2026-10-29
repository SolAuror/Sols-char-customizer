using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    public class DoublePropertyAttribute : PropertyAttribute
    {
        public bool ContentNone1;
        public bool ContentNone2;
        public float leftPadding = 0f;

        public float padding = 0f;
        public DoublePropertyAttribute(bool ContentNone1 = true, bool ContentNone2 = true, float leftPadding = 0, float padding = 10)
        {
            this.padding = padding;
            this.leftPadding = leftPadding;
            this.ContentNone1 = ContentNone1;
            this.ContentNone2 = ContentNone2;
        }
    }

#if UNITY_EDITOR

    [CustomPropertyDrawer(typeof(DoublePropertyAttribute))]
    public class DoublePropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var indent = EditorGUI.indentLevel; // Store the current indent level
            EditorGUI.indentLevel = 0; // Reset the indent level to 0

            EditorGUI.BeginProperty(position, label, property);

            var doubleAtt = attribute as DoublePropertyAttribute;

            var children = property.FindChildrenProperties().ToArray();

            var prop1 = children[0];
            var prop2 = children[1];


            var GUIContent1 = doubleAtt.ContentNone1 ? GUIContent.none : new GUIContent(prop1.displayName, prop1.tooltip);
            var GUIContent2 = doubleAtt.ContentNone2 ? GUIContent.none : new GUIContent(prop2.displayName, prop2.tooltip);


            float width = (position.width) * 0.5f;
            Rect prop1Rect = new(position.x + doubleAtt.leftPadding, position.y, width - doubleAtt.padding - doubleAtt.leftPadding, position.height);
            Rect prop2Rect = new(position.x + width + doubleAtt.padding, position.y, width - doubleAtt.padding, position.height);


            EditorGUI.PropertyField(prop1Rect, prop1, GUIContent1);
            EditorGUI.PropertyField(prop2Rect, prop2, GUIContent2);

            EditorGUI.EndProperty();
            EditorGUI.indentLevel = indent; // Restore the original indent level
        }
    }
#endif

}
