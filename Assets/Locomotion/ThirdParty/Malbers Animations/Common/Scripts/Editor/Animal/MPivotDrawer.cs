
#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;


namespace MalbersAnimations.Controller
{
    [CustomPropertyDrawer(typeof(MPivots))]
    public class MPivotDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var height = EditorGUIUtility.singleLineHeight;

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var with = position.width + 55;
            var PosX = position.x;


            var nameRect = new Rect(PosX - 10, position.y, with / 2 - 105, height);
            var vectorRect = new Rect(with / 2 - 45, position.y, with / 2 - 20, height);
            var multiplierRect = new Rect(with - 55, position.y, 50, height);
            var button = new Rect(with, position.y, 18, height);


            EditorGUI.BeginProperty(position, label, property);
            var name = property.FindPropertyRelative("name");
            var vectorPos = property.FindPropertyRelative("position");
            var modifyGizmo = property.FindPropertyRelative("EditorModify");
            var PivotColor = property.FindPropertyRelative("PivotColor");
            var EditorDisplay = property.FindPropertyRelative("EditorDisplay");

            //EditorGUI.HelpBox(buttonRect, "SD", MessageType.None);


            EditorGUI.PropertyField(nameRect, name, GUIContent.none);

            if (EditorDisplay.intValue == 0)
            {
                EditorGUI.PropertyField(vectorRect, vectorPos, GUIContent.none);
            }
            else if (EditorDisplay.intValue == 1)
            {
                EditorGUI.PropertyField(vectorRect, PivotColor, GUIContent.none);
            }

            EditorGUI.PropertyField(multiplierRect, PivotColor, GUIContent.none);
            modifyGizmo.boolValue = GUI.Toggle(button, modifyGizmo.boolValue, new GUIContent("•", "Edit on the Scene"), EditorStyles.miniButton);

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();

        }
    }
}
#endif