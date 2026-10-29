// Last Change: 9/18/2026 8:55pm
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;
using MalbersAnimations.Conditions;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace MalbersAnimations
{
    [CustomPropertyDrawer(typeof(ConditionCore))]
    public class ConditionCoreDrawer : PropertyDrawer
    {
        const int k_MaxTypePopupLineCount = 8;
        // MWC — Active toggle geometry copied from ItemProcessorDrawer, so Conditions and Item Processors
        // line up: the foldout arrow keeps its place and the label is padded with spaces to clear the toggle.
        const float ActiveX = 4f;
        const float ActiveWidth = 21f;
        const string ActiveLabelPad = "       "; //room for the foldout arrow + the Active toggle
        static readonly Type k_UnityObjectType = typeof(UnityEngine.Object);
        // static readonly GUIContent k_NullDisplayName = new(TypeMenuUtility.k_NullDisplayName);

        readonly Dictionary<string, TypePopupCache> m_TypePopups = new();
        readonly Dictionary<string, GUIContent> m_TypeNameCaches = new();

        SerializedProperty m_TargetProperty;

        private static GUIContent Icon_Delete;
        private static GUIContent Icon_Edit;
        private static GUIContent Icon_Global;
        private static GUIContent Icon_Local;
        private static GUIContent Icon_InvertOff;
        private static GUIContent Icon_InvertOn;

        private static bool s_iconsInitialized; // MWC — single flag replaces per-frame null checks
        private static GUIStyle s_toolbarBold;  // MWC — cached to avoid new GUIStyle() every OnGUI
        private static GUIContent debugCont;
        // The label Unity hands to OnGUI is a shared temp GUIContent. The Local Target PropertyField drawn on the
        // header row reuses it and blanks the text, so the foldout is drawn from this private copy instead.
        private static readonly GUIContent s_FoldLabel = new();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            if (!s_iconsInitialized) FindIcons(); // MWC — only call once, not every repaint

            var blueColor = MTools.MBlue * 2;
            var greenColor = MTools.MGreen * 2;

            var isNull = property.managedReferenceValue == null;
            var active = property.FindPropertyRelative("active"); // MWC — per condition On/Off switch
            var invert = property.FindPropertyRelative("invert");
            var desc = property.FindPropertyRelative("desc");
            //var Condition = property.FindPropertyRelative("Condition");
            var Target = property.FindPropertyRelative("Target");
            var LocalTarget = property.FindPropertyRelative("LocalTarget");
            var debug = property.FindPropertyRelative("debug");

            // Local Target goes on the header row (like Reactions) and is skipped from the body below
            var drawTargetInHeader = DrawTargetInHeader(property, out _);

            var rectBox = new Rect(position)
            {
                x = position.x - 13,
            };


            GUI.Box(rectBox, GUIContent.none, EditorStyles.toolbar); // Draw a box around the property

            if (!isNull)
            {
                var type = property.managedReferenceValue.GetType();
                var att = type.GetCustomAttribute<MDescriptionAttribute>(false); //Find the correct name

                //  string targetName = label.text;

                if (desc.stringValue != string.Empty)
                {
                    label.text = $" {desc.stringValue}";
                }
                else if (att != null)
                {
                    desc.stringValue = att.Description;
                }
                else
                {
                    label.text = $"{(property.managedReferenceValue as ConditionCore).DynamicName}";

                    //if (Target != null && Target.objectReferenceValue != null && LocalTarget.boolValue)
                    //{
                    //    targetName = Target.objectReferenceValue.name;
                    //    label.text += $" on [{targetName}]";
                    //}
                }

                if (desc.isExpanded) label.text = string.Empty;
                if (invert.boolValue) label.text = "[NOT] " + label.text;

                // MWC — flag the muted conditions on the header, the row is also greyed out below
                if (!active.boolValue && !desc.isExpanded) label.text += " (Disabled)";

                // MWC — same padding ItemProcessorDrawer uses, so the label clears the Active toggle
                if (!desc.isExpanded) label.text = ActiveLabelPad + label.text;
            }

            label = EditorGUI.BeginProperty(position, label, property);
            {
                // Snapshot the label before any nested PropertyField can recycle the shared instance
                s_FoldLabel.text = label.text;
                s_FoldLabel.tooltip = label.tooltip;
                s_FoldLabel.image = label.image;

                var width = 25;
                var popupPosition = new Rect(position);
                popupPosition.width -= EditorGUIUtility.labelWidth;
                popupPosition.x += EditorGUIUtility.labelWidth;
                popupPosition.height = EditorGUIUtility.singleLineHeight;

                if (!isNull)
                {
                    var OrAnd = property.FindPropertyRelative("OrAnd");

                    var dC = GUI.color;

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

                    var DebugRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width,
                        x = RemoveRect.x - width
                    };


                    var EditRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width,
                        x = DebugRect.x - width
                    };

                    //----------
                    var LocalTargetRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width,
                        x = EditRect.x - width
                    };

                    var invRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width,
                        x = LocalTargetRect.x - width
                    };

                    var andOrRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width + 10,
                        x = invRect.x - width - 10
                    };

                    // MWC — On/Off checkbox, same rect ItemProcessorDrawer uses for its Active toggle
                    var ActiveRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y + 2,
                        width = ActiveWidth,
                        x = position.x + ActiveX
                    };

                    var TextRect = new Rect(position)
                    {
                        height = Height + 3,
                        y = position.y,
                        width = position.width - 240,
                        x = 1 + (invert.boolValue ? position.x + 50 : position.x + 26) // MWC — starts after the Active toggle
                    };

                    // Local Target field on the header row (same slot Reactions use): it starts where the label
                    // column ends and stops before the leftmost toolbar toggle (AND/OR).
                    var TargetRect = new Rect(position)
                    {
                        height = Height + 2,
                        y = position.y + 1,
                        x = position.x + EditorGUIUtility.labelWidth + 2,
                    };
                    TargetRect.width = andOrRect.x - TargetRect.x - 4;

                    // The description text field must not run under the Local Target field
                    if (drawTargetInHeader) TextRect.width = TargetRect.x - TextRect.x - 4;

                    #endregion

                    s_toolbarBold ??= new GUIStyle(EditorStyles.toolbarButton) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                    var style = s_toolbarBold;

                    // MWC — drawn before everything else so the checkbox owns the click on the gutter
                    active.boolValue = EditorGUI.Toggle(ActiveRect, active.boolValue);

                    if (desc.isExpanded)
                        desc.stringValue = EditorGUI.TextField(TextRect, desc.stringValue);

                    if (Target != null) //Draw the Target Global/Local
                    {
                        var bgc = GUI.backgroundColor;
                        GUI.backgroundColor = LocalTarget.boolValue ? blueColor : GUI.backgroundColor;
                        LocalTarget.boolValue = GUI.Toggle(LocalTargetRect, LocalTarget.boolValue, LocalTarget.boolValue ? Icon_Local : Icon_Global, style);
                        GUI.backgroundColor = bgc;
                    }

                    var orAndContent = new GUIContent(OrAnd.boolValue ? "OR" : "AND",
                        OrAnd.boolValue ? "OR. First Condition will be ignored" : "AND. First Condition will be ignored");


                    if (!property.propertyPath.EndsWith("data[0]")) //Do not  AND- OR if if this is the first condition on the list
                    {
                        GUI.color = OrAnd.boolValue ? blueColor : greenColor;
                        OrAnd.boolValue = GUI.Toggle(andOrRect, OrAnd.boolValue, orAndContent, style);
                        GUI.color = dC;
                    }

                    GUI.color = invert.boolValue ? Color.red : dC;
                    invert.boolValue = GUI.Toggle(invRect, invert.boolValue, invert.boolValue ? Icon_InvertOn : Icon_InvertOff, style);
                    GUI.color = dC;

                    GUI.color = debug.boolValue ? Color.red + Color.white : dC;
                    debug.boolValue = GUI.Toggle(DebugRect, debug.boolValue, DebugCont, style);
                    GUI.color = dC;

                    GUI.color = desc.isExpanded ? blueColor : dC;
                    desc.isExpanded = GUI.Toggle(EditRect, desc.isExpanded, Icon_Edit, style);
                    GUI.color = dC;

                    position.y += 2;

                    if (GUI.Button(RemoveRect, Icon_Delete, style))
                    {
                        property.managedReferenceValue = null;
                        property.serializedObject.ApplyModifiedProperties();
                    }

                    // Local Target on the header row. Drawn through PropertyField so the [RequiredField] tint and the
                    // per-type filter of the ObjectField are kept; greyed out with the body while the condition is off.
                    if (drawTargetInHeader && property.managedReferenceValue != null)
                    {
                        using (new EditorGUI.DisabledGroupScope(!active.boolValue))
                            EditorGUI.PropertyField(TargetRect, Target, GUIContent.none);
                    }
                }

                var propertyName = TypePopupCache.GetTypeName(property, m_TypeNameCaches);

                if (isNull)
                {
                    var guiColor = GUI.color;
                    GUI.color = MTools.MRed;

                    if (EditorGUI.DropdownButton(popupPosition, propertyName, FocusType.Keyboard))
                    {
                        TypePopupCache popup = GetTypePopup(property);
                        m_TargetProperty = property;
                        popup.TypePopup.Show(popupPosition);
                    }

                    GUI.color = guiColor;
                }

                // Draw the managed reference property.
                // MWC — drawn at the original rect so the foldout arrow keeps the Item Processor alignment;
                // the label is padded instead. Greyed out while the condition is disabled: the toolbar icons
                // and the Active toggle are drawn above, outside the scope, so they stay usable.
                using (new EditorGUI.DisabledGroupScope(active != null && !active.boolValue))
                {
                    if (!drawTargetInHeader)
                    {
                        EditorGUI.PropertyField(position, property, label, true);
                    }
                    else
                    {
                        // Same layout PropertyField produces (foldout row + indented children), but the Target child
                        // is skipped because it is already on the header row. The foldout is clipped to the label
                        // column so the description never paints under the Local Target field.
                        var line = EditorGUIUtility.singleLineHeight;
                        var spacing = EditorGUIUtility.standardVerticalSpacing;

                        var foldRect = new Rect(position) { height = line, width = EditorGUIUtility.labelWidth };
                        property.isExpanded = EditorGUI.Foldout(foldRect, property.isExpanded, s_FoldLabel, true);

                        if (property.isExpanded)
                        {
                            EditorGUI.indentLevel++;
                            var y = position.y + line + spacing;
                            var end = property.GetEndProperty();
                            var child = property.Copy();
                            bool enter = true;

                            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                            {
                                enter = false;
                                if (child.name == "Target") continue; //Already drawn on the header row

                                var h = EditorGUI.GetPropertyHeight(child, true);
                                EditorGUI.PropertyField(new Rect(position) { y = y, height = h }, child, true);
                                y += h + spacing;
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }
            EditorGUI.EndProperty();
            EditorGUI.indentLevel = indent;
        }

        /// <summary>True when the condition has a Local Target of an Object type, which is drawn on the header row instead of the body</summary>
        private static bool DrawTargetInHeader(SerializedProperty property, out SerializedProperty Target)
        {
            Target = null;
            if (property.managedReferenceValue == null) return false;

            var LocalTarget = property.FindPropertyRelative("LocalTarget");
            if (LocalTarget == null || !LocalTarget.boolValue) return false;

            // Only plain Object references fit the single header line. Other targets (e.g. GameObjectReference on
            // the GameObject condition) keep their place in the body.
            Target = property.FindPropertyRelative("Target");
            return Target != null && Target.propertyType == SerializedPropertyType.ObjectReference;
        }


        public static GUIContent DebugCont
        {
            get
            {
                debugCont ??= new GUIContent(EditorGUIUtility.IconContent("d_debug"));
                return debugCont;
            }
        }

        // MWC — initialize all icons in one pass, guarded by s_iconsInitialized flag
        private static void FindIcons()
        {
            s_iconsInitialized = true;

            Icon_Delete = EditorGUIUtility.IconContent("d_TreeEditor.Trash");
            Icon_Delete.tooltip = "Clear the Condition";

            Icon_InvertOff = EditorGUIUtility.IconContent("console.erroricon.inactive.sml@2x");
            Icon_InvertOff.tooltip = "Invert Condition. The result will be the opposite";

            Icon_InvertOn = EditorGUIUtility.IconContent("console.erroricon.sml@2x");
            Icon_InvertOn.tooltip = "Invert Condition. The result will be the opposite";

            Icon_Global = EditorGUIUtility.IconContent("d_ToolHandleGlobal@2x");
            Icon_Global.tooltip = "Dynamic Target. The target for the condition will be found dynamically";

            Icon_Local = EditorGUIUtility.IconContent("d_ToolHandleLocal@2x");
            Icon_Local.tooltip = "Local Target. The target for the condition will be set in the Editor and it will not be changed";

            Icon_Edit = MalbersEditor.Icon_Edit;
            Icon_Edit.tooltip = "Edit the Condition Description";
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
            var height = EditorGUI.GetPropertyHeight(property, true);

            // The Target lives on the header row, so its body line is not part of the height anymore
            if (property.isExpanded && DrawTargetInHeader(property, out var Target))
                height -= EditorGUI.GetPropertyHeight(Target, true) + EditorGUIUtility.standardVerticalSpacing;

            return height;
        }
    }
}
#endif
