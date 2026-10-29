// Last Change: 9/17/2026 6:05pm
using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor; //MWC — was imported unguarded, which breaks player builds since UnityEditor does not exist there
#endif

namespace MalbersAnimations.Scriptables
{
    ///<summary>  Prefab Scriptable Variable. Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Variables/Transform Offset Var", order = 3000)]
    public class TransformOffsetVar : ScriptableVar
    {
        [SerializeField] private TransformOffset value;

        /// <summary>Invoked when the value changes </summary>
        public Action<TransformOffset> OnValueChanged;

        /// <summary> Value of the Bool variable</summary>
        public virtual TransformOffset Value
        {
            get => value;
            set
            {
                //MWC — guard was inverted: it only assigned when the new value already matched, so every real
                //write (SetValue, SetPosition, SetRotation, SetScale) was silently discarded.
                if (!value.Equal(this.value)) // Avoid Stack Overflow
                {
                    this.value = value;
                    OnValueChanged?.Invoke(value);         //If we are using OnChange event Invoked
#if UNITY_EDITOR
                    if (debug) Debug.Log($"<B>{name} -> [<color=white> {value} </color>] </B>", this);
#endif
                }
            }
        }

        public virtual void SetValue(TransformVar var) => Value = new TransformOffset(var.Value);

        /// <summary>Transform offsets can be stored as text.</summary>
        public override bool CanSerialize => true;

        /// <summary>Writes position, rotation and scale as JSON, which is culture invariant by specification.</summary>
        public override string GetSerializedValue() => JsonUtility.ToJson(value);

        /// <summary>Restores the offset from JSON; assigns through [Value] so listeners are notified.</summary>
        public override bool SetSerializedValue(string data)
        {
            if (!TryFromJson<TransformOffset>(data, out var result)) return false;
            Value = result;
            return true;
        }
    }

    [Serializable]
    public class TransformOffsetReference : ReferenceVar
    {
        public TransformOffset ConstantValue;
        [RequiredField] public TransformOffsetVar Variable;

        public TransformOffsetReference()
        {
            UseConstant = true;
            ConstantValue = new TransformOffset(0);
        }

        public TransformOffsetReference(Transform value) => Value = new TransformOffset(value);

        public TransformOffsetReference(TransformOffsetVar value)
        {
            Variable.Value = value.Value;
            UseConstant = false;
        }

        public TransformOffset Value
        {
            get
            {
                return UseConstant || Variable != null ? ConstantValue : Variable.Value;
            }
            set
            {
                if (UseConstant || Variable == null)
                {
                    UseConstant = true;
                    ConstantValue = value;
                }
                else
                    Variable.Value = value;
            }
        }
        public Vector3 Position => Value.Position;
        public Quaternion Rotation => Quaternion.Euler(Value.Rotation);

        public Vector3 RotationEuler => Value.Rotation;

        public Vector3 Scale => Value.Scale;

        internal void SetPosition(Vector3 value)
        {
            var offset = Value;
            offset.Position = value;
            Value = offset;
        }

        internal void SetRotation(Vector3 value)
        {
            var offset = Value;
            offset.Rotation = value;
            Value = offset;
        }

        internal void SetScale(Vector3 value)
        {
            var offset = Value;
            offset.Scale = value;
            Value = offset;
        }
    }

#if UNITY_EDITOR

    [CustomPropertyDrawer(typeof(TransformOffsetReference), true)]
    public class TransformValueVarReferenceDrawer : PropertyDrawer
    {
        /// <summary>  Options to display in the popup to select constant or variable. </summary>
        private readonly string[] popupOptions = { "Use Local", "Use Global" };

        /// <summary> Cached style to use to draw the popup button. </summary>
        private GUIStyle popupStyle;
        private GUIStyle AddStyle;
        private GUIContent plus;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            popupStyle ??= new GUIStyle(GUI.skin.GetStyle("PaneOptions")) { imagePosition = ImagePosition.ImageOnly };
            AddStyle ??= new GUIStyle(GUI.skin.GetStyle("PaneOptions")) { imagePosition = ImagePosition.ImageOnly };
            plus ??= EditorGUIUtility.IconContent("d_Toolbar Plus");

            var OldPos = new Rect(position);

            label = EditorGUI.BeginProperty(position, label, property);
            {
                Rect variableRect = new(position);
                position = EditorGUI.PrefixLabel(position, label);


                float height = EditorGUIUtility.singleLineHeight;

                // Get properties
                SerializedProperty useConstant = property.FindPropertyRelative("UseConstant");
                SerializedProperty constantValue = property.FindPropertyRelative("ConstantValue");
                SerializedProperty variable = property.FindPropertyRelative("Variable");

                Rect propRect = new(OldPos) { height = height };

                // Calculate rect for configuration button
                Rect buttonRect = new(position);
                buttonRect.yMin += popupStyle.margin.top;
                buttonRect.width = popupStyle.fixedWidth + popupStyle.margin.right;
                buttonRect.x -= 20;
                buttonRect.height = height;

                position.xMin = buttonRect.xMax;


                var AddButtonRect = new Rect(propRect) { x = propRect.width + propRect.x - 18, width = 20 };
                var ValueRect = new Rect(AddButtonRect);

                // Store old indent level and set it to 0, the PrefixLabel takes care of it
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;

                //CustomPatch: Ensures multi-object selection properly propagates changes without losing data (useConstant was resetting when multi-selecting objects).
                EditorGUI.BeginChangeCheck();
                int result = EditorGUI.Popup(buttonRect, useConstant.boolValue ? 0 : 1, popupOptions, popupStyle);
                if (EditorGUI.EndChangeCheck())
                    useConstant.boolValue = (result == 0);

                bool varIsEmpty = variable.objectReferenceValue == null;

                if (!useConstant.boolValue)
                {
                    if (varIsEmpty)
                    {
                        propRect.width -= 20;
                    }
                }


                if (useConstant.boolValue)
                {
                    EditorGUI.PropertyField(propRect, constantValue, GUIContent.none, true);
                    // constantValue.isExpanded = true; // Force the constant value to be expanded when using it
                }
                else
                {
                    EditorGUI.PropertyField(propRect, variable, new GUIContent(" "));
                }

                if (!useConstant.boolValue)
                {
                    if (varIsEmpty)
                    {
                        if (GUI.Button(AddButtonRect, plus, UnityEditor.EditorStyles.helpBox))
                        {
                            MTools.CreateScriptableAsset(variable, MalbersEditor.GetSelectedPathOrFallback());
                            GUIUtility.ExitGUI(); //Unity Bug!
                        }
                    }
                    else
                    {
                        ShowScriptVar(propRect, variable);
                    }
                }
                EditorGUI.indentLevel = indent;
            }
            EditorGUI.EndProperty();
        }

        private static void ShowScriptVar(Rect variableRect, SerializedProperty variable)
        {
            if (variable.objectReferenceValue != null)
            {
                if (variable.objectReferenceValue is not TransformOffsetVar) return; //Do not Paint vectors

                SerializedObject objs = new(variable.objectReferenceValue);

                var Var = objs.FindProperty("value");

                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(variableRect, Var, GUIContent.none, true);
                Var.isExpanded = true;
                if (EditorGUI.EndChangeCheck())
                {
                    objs.ApplyModifiedProperties();
                    EditorUtility.SetDirty(variable.objectReferenceValue);
                }
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Pick which sub-property we are drawing
            var useConstant = property.FindPropertyRelative("UseConstant");
            var targetProp = useConstant.boolValue
                ? property.FindPropertyRelative("ConstantValue")
                : property.FindPropertyRelative("Variable");

            if (!useConstant.boolValue)
            {
                if (targetProp.objectReferenceValue == null)

                    return EditorGUIUtility.singleLineHeight;
                else
                    return 4 * EditorGUIUtility.singleLineHeight + 6;
            }
            // Ask Unity for the height of that sub-property
            return EditorGUI.GetPropertyHeight(targetProp, label, true);
        }
    }
#endif
}
