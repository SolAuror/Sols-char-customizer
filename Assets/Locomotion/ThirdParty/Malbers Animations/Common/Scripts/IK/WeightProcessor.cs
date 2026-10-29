using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
#endif

namespace MalbersAnimations.IK
{
    [Serializable]
    public abstract class WeightProcessor
    {
        public virtual string DynamicName => $"{GetType().Name}";


        [HideInInspector] public bool Active = true;


        [Tooltip("Invert the weight of the entry. If the weight is 1, it will return 0, and if the weight is 0, it will return 1.")]
        [HideInInspector] public bool Invert = false;

        /// <summary>  Process the weight given some extra parameters to check and it multiplies to the entry weight</summary>
        /// <param name="weight">Entry weight to modify</param>
        /// <param name="set"> IK Set sending the weight</param>
        /// <returns>returns the processed weight</returns>
        public abstract float Process(IKSet set, Animator Anim, float weight);

        /// <summary> Call when the IK Set is enabled </summary>
        public virtual void OnEnable(IKSet set, Animator anim) { }

        /// <summary> Call when the IK Set is Disabled </summary>
        public virtual void OnDisable(IKSet set, Animator anim) { }

        public virtual void OnDrawGizmos(IKSet set, Animator anim) { }
        public virtual void OnHandlesGizmos(IKSet set, Animator anim) { }
    }

#if UNITY_EDITOR

    [CustomPropertyDrawer(typeof(WeightProcessor))]
    public class WeightProcessorDrawer : PropertyDrawer
    {
        const int k_MaxTypePopupLineCount = 8;
        static readonly Type k_UnityObjectType = typeof(UnityEngine.Object);

        readonly Dictionary<string, TypePopupCache> m_TypePopups = new();
        readonly Dictionary<string, GUIContent> m_TypeNameCaches = new();

        SerializedProperty m_TargetProperty;

        private static GUIContent Icon_Delete;

        private static GUIContent Icon_InvertOff;
        private static GUIContent Icon_InvertOn;

        //private bool editName;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            FindIcons();

            var isNull = property.managedReferenceValue == null;
            var Active = property.FindPropertyRelative("Active");
            var invert = property.FindPropertyRelative("Invert");


            string targetName = label.text;

            if (!isNull)
            {
                var targetType = property.managedReferenceValue.GetType();
                targetName = $"{(property.managedReferenceValue as WeightProcessor).DynamicName}";

                label.text = "       " + targetName; //Add the space for the active option

                if (invert.boolValue) label.text += " (Inverted)";
            }
            else
            {
                label.text = targetName.Replace("Element", "Weight");
            }


            EditorGUIUtility.labelWidth = 0;

            label = EditorGUI.BeginProperty(position, label, property);
            {
                var width = 25;
                var popupPosition = new Rect(position);
                popupPosition.width -= EditorGUIUtility.labelWidth;
                popupPosition.x += EditorGUIUtility.labelWidth;
                popupPosition.height = EditorGUIUtility.singleLineHeight;

                var rectBox = new Rect(position);

                GUI.Box(rectBox, GUIContent.none, EditorStyles.toolbar); // Draw a box around the property

                if (!isNull)
                {
                    var first = position.x + popupPosition.width + EditorGUIUtility.labelWidth - width;

                    #region Rects

                    var Height = EditorGUIUtility.singleLineHeight - 2;

                    var RemoveRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width,
                        x = first
                    };

                    var Invert_Rect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width,
                        x = RemoveRect.x - width
                    };


                    var actRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y + 2,
                        width = width - 4,
                        x = position.x + 4,
                    };


                    var style = new GUIStyle(EditorStyles.toolbarButton)
                    {
                        fontStyle = FontStyle.Bold
                    };
                    #endregion

                    Active.boolValue = GUI.Toggle(actRect, Active.boolValue, GUIContent.none);

                    var dC = GUI.color;
                    GUI.color = invert.boolValue ? Color.red : dC;
                    invert.boolValue = GUI.Toggle(Invert_Rect, invert.boolValue, invert.boolValue ? Icon_InvertOn : Icon_InvertOff, style);
                    GUI.color = dC;


                    if (GUI.Button(RemoveRect, Icon_Delete, style))
                    {
                        property.managedReferenceValue = null;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }

                if (isNull)
                {
                    var propertyName = TypePopupCache.GetTypeName(property, m_TypeNameCaches);

                    if (EditorGUI.DropdownButton(popupPosition, propertyName, FocusType.Keyboard))
                    {
                        TypePopupCache popup = GetTypePopup(property);
                        m_TargetProperty = property;
                        popup.TypePopup.Show(popupPosition);
                    }
                }
                // Draw the managed reference property.
                position.y += 2;

                EditorGUI.PropertyField(position, property, label, true);
            }
            EditorGUI.EndProperty();
            EditorGUI.indentLevel = indent;
        }

        private static void FindIcons()
        {
            if (Icon_Delete == null)
            {
                Icon_Delete = EditorGUIUtility.IconContent("d_TreeEditor.Trash");
                Icon_Delete.tooltip = "Clear the Condition";
            }

            if (Icon_InvertOff == null)
            {
                Icon_InvertOff = EditorGUIUtility.IconContent("console.erroricon.inactive.sml@2x");
                Icon_InvertOff.tooltip = "Invert Weight. The result will be the opposite";
            }
            if (Icon_InvertOn == null)
            {
                Icon_InvertOn = EditorGUIUtility.IconContent("console.erroricon.sml@2x");
                Icon_InvertOn.tooltip = "Invert Weight. The result will be the opposite";
            }
        }

        TypePopupCache GetTypePopup(SerializedProperty property)
        {
            // Cache this string. This property internally call Assembly.GetName, which result in a large allocation.
            string managedReferenceFieldTypeName = property.managedReferenceFieldTypename;

            if (!m_TypePopups.TryGetValue(managedReferenceFieldTypeName, out TypePopupCache result))
            {
                var state = new AdvancedDropdownState();

                Type baseType = MSerializedTools.GetType(managedReferenceFieldTypeName);
                var popup = new AdvancedTypePopup(
                    TypeCache.GetTypesDerivedFrom(baseType).Append(baseType).Where(p =>
                        (p.IsPublic || p.IsNestedPublic) &&
                        !p.IsAbstract &&
                        !p.IsGenericType &&
                        !k_UnityObjectType.IsAssignableFrom(p) &&
                        Attribute.IsDefined(p, typeof(SerializableAttribute))
                    ),
                    k_MaxTypePopupLineCount, state);

                popup.OnItemSelected += item =>
                {
                    Type type = item.Type;
                    object obj = m_TargetProperty.SetManagedReference(type);
                    m_TargetProperty.isExpanded = (obj != null);
                    m_TargetProperty.serializedObject.ApplyModifiedProperties();
                    m_TargetProperty.serializedObject.Update();
                };

                result = new TypePopupCache(popup, state);
                m_TypePopups.Add(managedReferenceFieldTypeName, result);
            }
            return result;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, true) + 2;
        }
    }
#endif
}
