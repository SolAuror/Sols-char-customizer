#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace MalbersAnimations
{
    [CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
    public class SubclassSelectorDrawer : PropertyDrawer
    {
        // const int k_MaxTypePopupLineCount = 13;
        static readonly Type k_UnityObjectType = typeof(UnityEngine.Object);
        // static readonly GUIContent k_NullDisplayName = new(TypeMenuUtility.k_NullDisplayName);
        static readonly GUIContent k_IsNotManagedReferenceLabel = new("The property type is not manage reference.");

        readonly Dictionary<string, TypePopupCache> m_TypePopups = new();
        readonly Dictionary<string, GUIContent> m_TypeNameCaches = new();

        SerializedProperty m_TargetProperty;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var boxRect = new Rect(position);

            boxRect.width -= EditorGUIUtility.labelWidth;
            boxRect.height += -3;
            boxRect.y += 2;
            boxRect.x -= 12;

            position.y += 2;

            GUIStyle d = new(EditorStyles.label)
            {
                imagePosition = ImagePosition.TextOnly
            };

            GUI.Box(boxRect, GUIContent.none, d);

            label = EditorGUI.BeginProperty(position, label, property);
            {
                if (property.propertyType == SerializedPropertyType.ManagedReference)
                {
                    //if (label.text.Contains("Element"))
                    //{ label.text = label.text.Replace("Element", property.name); } //????

                    // Draw the subclass selector popup.
                    Rect popupPosition = new(position);
                    popupPosition.width -= EditorGUIUtility.labelWidth;
                    popupPosition.x += EditorGUIUtility.labelWidth;
                    popupPosition.height = EditorGUIUtility.singleLineHeight;

                    var indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0;

                    var hasActive = property.FindPropertyRelative("Active");
                    var delay = property.FindPropertyRelative("delay");


                    if (hasActive != null) //Draw the Active Toggle if it has the active property
                    {
                        var pos = EditorGUI.PrefixLabel(position, new GUIContent(" "));
                        Rect buttonRect = new(pos)
                        {
                            width = 20f,
                            height = EditorGUIUtility.singleLineHeight,
                            x = pos.x - 20f,
                        };

                        hasActive.boolValue = EditorGUI.Toggle(buttonRect, GUIContent.none, hasActive.boolValue);
                    }

                    if (delay != null)
                    {
                        var w = 50f;

                        Rect DelayRect = new(popupPosition)
                        {
                            width = w,
                            x = popupPosition.x + popupPosition.width - w,
                        };

                        EditorGUIUtility.labelWidth = 10;
                        EditorGUI.PropertyField(DelayRect, delay, new GUIContent("D", "Delay the Reaction for this amount of seconds"));
                        EditorGUIUtility.labelWidth = 0;

                        popupPosition.width -= (w + 3);
                    }

                    if (EditorGUI.DropdownButton(popupPosition, TypePopupCache.GetTypeName(property, m_TypeNameCaches), FocusType.Keyboard))
                    {
                        TypePopupCache popup = GetTypePopup(property);
                        m_TargetProperty = property;
                        popup.TypePopup.Show(popupPosition);
                    }

                    // Draw the managed reference property.
                    EditorGUI.PropertyField(position, property, label, true);

                    EditorGUI.indentLevel = indent;
                }
                else
                {
                    EditorGUI.LabelField(position, label, k_IsNotManagedReferenceLabel);
                }

                //CustomPatch: removed unnecessary and faulty ApplyModifiedProperties call here that can mess up Unity's default properties handling system (in a custom property drawer method you should never need to manually call ApplyModifiedProperties on a the actual SerializedProperty being inspected (the one sent as parameter by Unity))
                // property.serializedObject.ApplyModifiedProperties();
            }
            EditorGUI.EndProperty();
        }

        TypePopupCache GetTypePopup(SerializedProperty property)
        {
            // Cache this string. This property internally call Assembly.GetName, which result in a large allocation.
            string managedReferenceFieldTypename = property.managedReferenceFieldTypename;

            if (!m_TypePopups.TryGetValue(managedReferenceFieldTypename, out TypePopupCache result))
            {
                var state = new AdvancedDropdownState();

                Type baseType = MSerializedTools.GetType(managedReferenceFieldTypename);
                var popup = new AdvancedTypePopup(
                    TypeCache.GetTypesDerivedFrom(baseType).Append(baseType).Where(p =>
                        (p.IsPublic || p.IsNestedPublic) &&
                        !p.IsAbstract &&
                        !p.IsGenericType &&
                        !k_UnityObjectType.IsAssignableFrom(p) &&
                        Attribute.IsDefined(p, typeof(SerializableAttribute))
                    ), 13, state);

                popup.OnItemSelected += item =>
                {
                    Type type = item.Type;
                    object obj = m_TargetProperty.SetManagedReference(type);
                    m_TargetProperty.isExpanded = (obj != null);
                    m_TargetProperty.serializedObject.ApplyModifiedProperties();
                    m_TargetProperty.serializedObject.Update();
                };

                result = new TypePopupCache(popup, state);
                m_TypePopups.Add(managedReferenceFieldTypename, result);
            }
            return result;
        }



        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, true) + 8;
        }
    }
}
#endif