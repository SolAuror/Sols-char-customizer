using UnityEngine;
using System;
using MalbersAnimations.Scriptables;


#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Enum |
        AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]

    //[AttributeUsage(AttributeTargets.All  ,  AllowMultiple = true, Inherited = true)]
    public sealed class HideAttribute : PropertyAttribute
    {
        public string Variable = "";
        public bool inverse = false;
        public bool hide = true;
        public int[] EnumValue;
        public bool flag = false;


        public HideAttribute(string conditionalSourceField)
        {
            this.Variable = conditionalSourceField;
            this.inverse = false;
            this.hide = true;
            flag = false;
        }

        public HideAttribute(string conditionalSourceField, bool inverse)
        {
            this.Variable = conditionalSourceField;
            this.inverse = inverse;
            this.hide = true;
            flag = false;
        }

        public HideAttribute(string conditionalSourceField, bool inverse, bool hide)
        {
            this.Variable = conditionalSourceField;
            this.inverse = inverse;
            this.hide = hide;
            flag = false;
        }

        public HideAttribute(string conditionalSourceField, bool inverse, params int[] EnumValue)
        {
            this.Variable = conditionalSourceField;
            this.inverse = inverse;
            this.EnumValue = EnumValue;
            this.hide = true;
            flag = false;
        }

        public HideAttribute(string conditionalSourceField, bool inverse, bool hide, params int[] EnumValue)
        {
            this.Variable = conditionalSourceField;
            this.inverse = inverse;
            this.EnumValue = EnumValue;
            this.hide = hide;
        }

        public HideAttribute(string conditionalSourceField, params int[] EnumValue)
        {
            this.Variable = conditionalSourceField;
            this.inverse = false;
            this.EnumValue = EnumValue;
            this.hide = true;
            flag = false;
        }

        public HideAttribute(string conditionalSourceField, bool inverse, bool hide, bool flag, params int[] EnumValue)
        {
            this.Variable = conditionalSourceField;
            this.inverse = inverse;
            this.EnumValue = EnumValue;
            this.hide = hide;
            this.flag = flag;
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(HideAttribute))]
    public class HidePropertyDrawer : PropertyDrawer
    {
        private bool enabled;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            HideAttribute condHAtt = (HideAttribute)attribute;

            enabled = GetConditionalHideAttributeResult(condHAtt, property);
            //  CachePropertyDrawer(property);

            bool wasEnabled = GUI.enabled;
            GUI.enabled = enabled;

            if (!condHAtt.hide || enabled)
            {
                EditorGUI.PropertyField(position, property, label, true);
            }

            GUI.enabled = wasEnabled;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            HideAttribute condHAtt = (HideAttribute)attribute;
            bool enabled = GetConditionalHideAttributeResult(condHAtt, property);

            if (enabled || !condHAtt.hide)
            {
                return EditorGUI.GetPropertyHeight(property, label);
            }
            else
            {
                return -EditorGUIUtility.standardVerticalSpacing;
            }
        }

        private bool GetConditionalHideAttributeResult(HideAttribute condHAtt, SerializedProperty property)
        {
            bool enabled = true;

            //Handle primary property
            SerializedProperty sourcePropertyValue;

            //Get the full relative property path of the source field so we can have nested hiding.Use old method when dealing with arrays
            //  if (!property.isArray)
            {
                //returns the property path of the property we want to apply the attribute to
                string propertyPath = property.propertyPath;


                //changes the path to the conditional source property path
                // MWC — replace only the LAST path segment. String.Replace() replaced every occurrence,
                // so a parent field whose name contains the field name (e.g. "ConditionsOnTarget" holding
                // a field named "Target") produced a corrupted path and the hiding silently stopped working.
                int lastDot = propertyPath.LastIndexOf('.');
                string conditionPath = lastDot >= 0
                    ? propertyPath[..(lastDot + 1)] + condHAtt.Variable
                    : condHAtt.Variable;


                sourcePropertyValue = property.serializedObject.FindProperty(conditionPath);

                //if the find failed->fall back to the old system
                if (sourcePropertyValue == null)
                {
                    //original implementation (doesn't work with nested serializedObjects)
                    sourcePropertyValue = property.serializedObject.FindProperty(condHAtt.Variable);
                }
            }
            //else
            //{
            //    //original implementation (doesn't work with nested serializedObjects)
            //    sourcePropertyValue = property.serializedObject.FindProperty(condHAtt.Variable);
            //}


            if (sourcePropertyValue != null)
            {
                enabled = CheckPropertyType(sourcePropertyValue, condHAtt);
            }

            //wrap it all up
            if (condHAtt.inverse) enabled = !enabled;
            return enabled;
        }

        private bool CheckPropertyType(SerializedProperty sourcePropertyValue, HideAttribute condHAtt)
        {


            //Note: add others for custom handling if desired
            switch (sourcePropertyValue.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return sourcePropertyValue.boolValue;
                case SerializedPropertyType.Float:
                    return sourcePropertyValue.floatValue == 0;
                case SerializedPropertyType.Integer:
                    return sourcePropertyValue.intValue == 0;
                case SerializedPropertyType.ObjectReference:
                    return sourcePropertyValue.objectReferenceValue != null;
                case SerializedPropertyType.ManagedReference:
                    return sourcePropertyValue.objectReferenceValue != null;
                case SerializedPropertyType.ArraySize:
                    return sourcePropertyValue.arraySize == 0;
                case SerializedPropertyType.Enum:
                    if (!condHAtt.flag)
                    {
                        for (int i = 0; i < condHAtt.EnumValue.Length; i++)
                        {
                            if (sourcePropertyValue.enumValueIndex == condHAtt.EnumValue[i])
                                return true;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < condHAtt.EnumValue.Length; i++)
                        {
                            if ((sourcePropertyValue.intValue & condHAtt.EnumValue[i]) == condHAtt.EnumValue[i])
                                return true;
                        }
                    }
                    return false;

                case SerializedPropertyType.Generic:

                    if (sourcePropertyValue.GetValue() is BoolReference boolVar)
                    {
                        return boolVar.Value; //BoolReference is a ScriptableObject that holds a bool value
                    }
                    else if (sourcePropertyValue.GetValue() is FloatReference floatVar)
                    {
                        return floatVar.Value == 0; //FloatReference is a ScriptableObject that holds a float value
                    }
                    else if (sourcePropertyValue.GetValue() is IntReference intVar)
                    {
                        return intVar.Value == 0; //IntReference is a ScriptableObject that holds an int value
                    }

                    return true; //Generic is used for custom classes, so we assume it is always valid

                default:
                    Debug.LogError("Data type of the property used for conditional hiding [" + sourcePropertyValue.propertyType + "] is currently not supported");
                    return true;
            }
        }
    }
#endif
}