using MalbersAnimations.Conditions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations.Controller
{
    [CreateAssetMenu(menuName = "Malbers Animations/Modifier/Mode/Mode Index by Condition")]
    public class ModifierByCondition : ModeModifier
    {
        [System.Serializable]
        public struct IndexByConditions
        {
            public Conditions2 condition;
            public bool UseModifier;

            [Hide(nameof(UseModifier), true)]
            public int Index;

            [Hide(nameof(UseModifier), false)]
            public ModeModifier modifier;


            public IndexByConditions(int index)
            {
                condition = new Conditions2();
                UseModifier = false;
                Index = index;
                modifier = null;
            }
        }

        [Tooltip("Changes the Active Index of the Mode by a condition")]
        public IndexByConditions[] conditions;

        public override void OnModeEnter(Mode mode)
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].condition.Evaluate(mode.Animal))
                {
                    if (conditions[i].UseModifier)
                    {
                        conditions[i].modifier.OnModeEnter(mode);
                    }
                    else
                    {
                        mode.AbilityIndex = conditions[i].Index; //Set the index of the mode
                    }
                    return; // Exit the loop if a condition is met
                }
            }
        }

        public override void OnModeExit(Mode mode)
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i].condition.Evaluate(mode.Animal))
                {
                    //Apply Mode Exit on the first condition that is true
                    if (conditions[i].UseModifier) conditions[i].modifier.OnModeExit(mode);
                    return; // Exit the loop if a condition is met
                }
            }
        }

    }


#if UNITY_EDITOR

    //[CustomEditor(typeof(ModifierByCondition))]
    public class ModifierByConditionEditor : Editor
    {
        private ReorderableList conditionsList;

        protected virtual void OnEnable()
        {
            SerializedProperty conditionsProp = serializedObject.FindProperty("conditions");

            conditionsList = new ReorderableList(serializedObject, conditionsProp, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    EditorGUI.LabelField(rect, "Conditions");
                },
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    SerializedProperty element = conditionsProp.GetArrayElementAtIndex(index);
                    float y = rect.y + 2;
                    float lineHeight = EditorGUIUtility.singleLineHeight;
                    float spacing = 2f;

                    // Draw Condition field
                    EditorGUI.PropertyField(
                        new Rect(rect.x, y, rect.width, lineHeight),
                        element.FindPropertyRelative("condition"),
                        new GUIContent("Condition")
                    );
                    y += lineHeight + spacing;

                    // Draw UseModifier toggle
                    SerializedProperty useModifierProp = element.FindPropertyRelative("UseModifier");
                    EditorGUI.PropertyField(
                        new Rect(rect.x, y, rect.width, lineHeight),
                        useModifierProp,
                        new GUIContent("Use Modifier")
                    );
                    y += lineHeight + spacing;

                    // Draw Index or Modifier depending on UseModifier
                    if (useModifierProp.boolValue)
                    {
                        EditorGUI.PropertyField(
                            new Rect(rect.x, y, rect.width, lineHeight),
                            element.FindPropertyRelative("modifier"),
                            new GUIContent("Modifier")
                        );
                    }
                    else
                    {
                        EditorGUI.PropertyField(
                            new Rect(rect.x, y, rect.width, lineHeight),
                            element.FindPropertyRelative("Index"),
                            new GUIContent("Index")
                        );
                    }
                },
                elementHeightCallback = index =>
                {
                    SerializedProperty element = conditionsProp.GetArrayElementAtIndex(index);

                    return EditorGUI.GetPropertyHeight(element);
                },

                onAddCallback = list =>
                {
                    int index = list.count;
                    list.serializedProperty.InsertArrayElementAtIndex(index);
                    SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
                    element.objectReferenceValue = null;
                },

            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.Space();
            conditionsList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}