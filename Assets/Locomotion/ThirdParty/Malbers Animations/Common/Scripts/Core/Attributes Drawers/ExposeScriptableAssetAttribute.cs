using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    public class ExposeScriptableAssetAttribute : PropertyAttribute
    { }



#if UNITY_EDITOR
    [UnityEditor.CustomPropertyDrawer(typeof(ExposeScriptableAssetAttribute), true)]
    public class ExposeScriptableAssetDrawer : PropertyDrawer
    {
        private Editor editor = null;
        private GUIContent plus;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var hasProperty = property.objectReferenceValue != null;

            if (hasProperty)
            {
                DrawFoldoutField(position, property, label);

                if (property.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (!editor || editor.target != property.objectReferenceValue)
                            Editor.CreateCachedEditor(property.objectReferenceValue, null, ref editor);

                        DrawAssetInspector();
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                position.width -= 22;
                EditorGUI.PropertyField(position, property, label, true);


                if (plus == null)
                {
                    plus = UnityEditor.EditorGUIUtility.IconContent("d_Toolbar Plus");
                    plus.tooltip = "Create";
                }

                var AddButtonRect = new Rect(position) { x = position.width + position.x + 4, width = 20 };

                if (GUI.Button(AddButtonRect, plus, UnityEditor.EditorStyles.helpBox))
                {
                    MTools.CreateScriptableAsset(property, MalbersEditor.GetSelectedPathOrFallback());
                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>Draws the foldout arrow glued to the label at the current indent level. In the Inspector (hierarchy mode) the arrow sits in the left gutter like Unity's own foldouts so the label stays aligned with the other rows</summary>
        private static void DrawFoldoutField(Rect position, SerializedProperty property, GUIContent label)
        {
            var line = new Rect(position) { height = EditorGUIUtility.singleLineHeight };
            var labelStart = EditorGUI.IndentedRect(line).x;   //Where the label begins at the current indent

            //GUI.Toggle with the foldout style draws the arrow exactly at the rect. EditorGUI.Foldout would shift it into the gutter on its own in hierarchy mode
            var arrowRect = new Rect(line) { x = labelStart - ArrowWidth, width = ArrowWidth };

            if (!EditorGUIUtility.hierarchyMode)
            {
                //No gutter outside the rect: push the label right of the arrow and shrink the label width so the field column keeps its alignment
                arrowRect.x = labelStart;
                var offset = ArrowWidth;
                var fieldRect = new Rect(line) { x = line.x + offset, width = line.width - offset };

                var labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Max(labelWidth - offset, 1f);

                property.isExpanded = GUI.Toggle(arrowRect, property.isExpanded, GUIContent.none, EditorStyles.foldout);
                EditorGUI.PropertyField(fieldRect, property, label, true);

                EditorGUIUtility.labelWidth = labelWidth;
            }
            else
            {
                property.isExpanded = GUI.Toggle(arrowRect, property.isExpanded, GUIContent.none, EditorStyles.foldout);
                EditorGUI.PropertyField(line, property, label, true);
            }
        }

        /// <summary>Draws the cached inspector of the asset. The default inspector skips the Script row; custom inspectors draw as they are</summary>
        private void DrawAssetInspector()
        {
            if (editor.GetType().Name == "GenericInspector")
            {
                var so = editor.serializedObject;
                so.Update();

                var prop = so.GetIterator();
                var enterChildren = true;

                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (prop.name == "m_Script") continue;
                    EditorGUILayout.PropertyField(prop, true);
                }

                so.ApplyModifiedProperties();
            }
            else
            {
                editor.OnInspectorGUI();
            }
        }

        /// <summary>Width reserved for the foldout arrow</summary>
        private const float ArrowWidth = 15f;
    }
#endif
}