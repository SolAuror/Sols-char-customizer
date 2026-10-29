// Last Change: 9/17/2026 5:33pm
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace MalbersAnimations.Reactions
{

    [CustomPropertyDrawer(typeof(Reaction))]
    public class ReactionCoreDrawer : PropertyDrawer
    {
        private const int k_MaxTypePopupLineCount = 8;
        private static readonly Type k_UnityObjectType = typeof(UnityEngine.Object);

        // MWC — header row metrics. The foldout arrow is drawn at position.x, so the Active checkbox starts
        // exactly where the arrow ends and the label right after the checkbox. Nudge these two if the row
        // ever feels tight/loose — everything to their right is anchored from the opposite edge.
        private const float FoldoutW = 5f;
        private const float ToggleW = 14f;

        private readonly Dictionary<string, TypePopupCache> m_TypePopups = new();
        private readonly Dictionary<string, GUIContent> m_TypeNameCaches = new();

        private static GUIStyle s_toolbarBold; // MWC — cached to avoid new GUIStyle() allocation every OnGUI

        private SerializedProperty m_TargetProperty;

        private static GUIContent Icon_Delete;
        private static GUIContent Icon_Edit;
        private static GUIContent Icon_Global;
        private static GUIContent Icon_Local;
        private static GUIContent Icon_Delay;
        private static GUIContent Icon_Condition; // MWC — "Use Condition" toggle icon
        private static GUIContent Icon_Help;      // MWC — documentation button icon

        // MWC — width of the Help button. The Delete button is anchored to the row's right edge and the
        // Help button sits immediately to its left; everything else chains off it.
        private const float HelpW = 20f;

        // MWC — Documentation URL per reaction type, read from its [HelpURL]. Cached because resolving it is
        // reflection and this runs on every repaint of every reaction row. A null result is cached too, so a
        // reaction without the attribute is never probed twice.
        private static readonly Dictionary<Type, string> s_HelpURLs = new();

        private static string GetHelpURL(Reaction reaction)
        {
            if (reaction == null) return null;

            var type = reaction.GetType();

            if (!s_HelpURLs.TryGetValue(type, out var url))
            {
                url = type.GetCustomAttribute<HelpURLAttribute>(true)?.URL;
                s_HelpURLs[type] = url;
            }
            return url;
        }

        private static bool s_iconsInitialized; // MWC — single flag replaces per-frame null checks
        private static GUIContent debugCont;

        public static GUIContent DebugCont
        {
            get
            {
                debugCont ??= new GUIContent(EditorGUIUtility.IconContent("d_debug"));
                return debugCont;
            }
        }

        public readonly GUIContent DelayContent = new("☼", "Delay before the reaction is executed");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!s_iconsInitialized) FindIcons(); // MWC — only call once, not every repaint

            var isNull = property.managedReferenceValue == null;
            var Active = property.FindPropertyRelative("Active");
            var delay = property.FindPropertyRelative("delay");
            var desc = property.FindPropertyRelative("desc");
            var useLocal = property.FindPropertyRelative("useLocal");
            var localTarget = property.FindPropertyRelative("localTarget");
            var useCondition = property.FindPropertyRelative("useCondition");

            string targetName = label.text;

            if (!isNull)
            {
                var targetType = property.managedReferenceValue.GetType();
                targetName = $"{(property.managedReferenceValue as Reaction).DynamicName}";

                var att = targetType.GetCustomAttribute<MDescriptionAttribute>(false); //Find the correct name

                if (!string.IsNullOrEmpty(desc.stringValue))
                {
                    targetName = $" {desc.stringValue}";
                }
                else if (att != null)
                {
                    desc.stringValue = att.Description;
                    targetName = $" {att.Description}";
                }

                label.text = "       " + targetName; //Add the space for the active option

                // if (desc.isExpanded) //Clear the label when the description is being edited
                label.text = string.Empty;  //Make the label to be empty, I'll draw it later on
            }
            else
            {
                label.text = targetName.Replace("Element", "Reaction");
            }

            // targetName = Regex.Replace(targetName, "(?<!^)([A-Z])", " $1");
            // targetName = $"[{targetName}]";


            EditorGUIUtility.labelWidth = 0;



            label = EditorGUI.BeginProperty(position, label, property);
            {
                var width = 25;
                var popupPosition = new Rect(position);
                popupPosition.width -= EditorGUIUtility.labelWidth;
                popupPosition.x += EditorGUIUtility.labelWidth;
                popupPosition.height = EditorGUIUtility.singleLineHeight;

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

                    // MWC — Help button, between the description toggle and the delete button
                    var HelpRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y + 1,
                        width = HelpW,
                        x = RemoveRect.x - HelpW
                    };

                    var DescripRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width,
                        x = HelpRect.x - width // MWC — was RemoveRect.x - width; now anchored to the Help button
                    };


                    var DelayRect = new Rect(position)
                    {
                        height = Height + 1,
                        y = position.y + 1,
                        width = width + 5,
                        x = DescripRect.x - width - 2 - 5
                    };

                    //----------
                    var UseLocalTargetRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width + 2,
                        x = DelayRect.x - 3 * width // MWC — shifted left to make room for the Condition toggle on its right
                    };

                    // MWC — toggle that shows/hides the per-reaction condition, placed AFTER (right of) the Global/Local toggle
                    var ConditionRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y,
                        width = width,
                        x = UseLocalTargetRect.x + width + 4
                    };

                    var DelayIconRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y + 2,
                        width = width + 2,
                        x = DelayRect.x - width + 5
                    };

                    var TargetRect2 = new Rect(position)
                    {
                        height = Height + 2,
                        y = position.y + 1,
                        width = position.width - EditorGUIUtility.labelWidth - 165 - HelpW, // MWC — reserve room so the Local Target field stops before the Global/Local + Condition toggles (+ Help)
                        x = position.x + EditorGUIUtility.labelWidth + 2,
                    };

                    var actRect = new Rect(position)
                    {
                        height = Height,
                        y = position.y + 2,
                        width = ToggleW, // MWC — clamp click area to the visible checkbox
                        x = position.x + FoldoutW, // MWC — sits right after the far-left foldout arrow, no gap
                    };

                    var TextRect = new Rect(position)
                    {
                        height = Height + 2,
                        y = position.y + 1,
                        width = !useLocal.boolValue ? position.width - 187 - HelpW : EditorGUIUtility.labelWidth - 22, // MWC — narrowed to make room for the Condition toggle (and the Help button)
                        x = actRect.xMax + 2, // MWC — label sits right after the Active checkbox
                    };

                    s_toolbarBold ??= new GUIStyle(EditorStyles.toolbarButton) { fontStyle = FontStyle.Bold };
                    var style = s_toolbarBold;
                    #endregion

                    if (desc.isExpanded)
                        desc.stringValue = EditorGUI.TextField(TextRect, desc.stringValue);
                    else
                        GUI.Label(TextRect, targetName);

                    // MWC — Draw the Use Condition toggle (green tint when enabled)
                    var cColor = GUI.color;
                    GUI.color = useCondition.boolValue ? Color.green + Color.white : cColor;
                    useCondition.boolValue = GUI.Toggle(ConditionRect, useCondition.boolValue, Icon_Condition, style);
                    GUI.color = cColor;

                    // if (Target != null) //Draw the Target Global/Local
                    useLocal.boolValue = GUI.Toggle(UseLocalTargetRect, useLocal.boolValue, useLocal.boolValue ? Icon_Local : Icon_Global, style);

                    Active.boolValue = GUI.Toggle(actRect, Active.boolValue, GUIContent.none);

                    GUI.Label(DelayIconRect, Icon_Delay, EditorStyles.iconButton);

                    delay.floatValue = EditorGUI.FloatField(DelayRect, GUIContent.none, delay.floatValue);


                    if (useLocal.boolValue)
                    {
                        if (property.managedReferenceValue == null)
                        {
                            EditorGUI.ObjectField(TargetRect2, null, typeof(Component), GUIContent.none); //Draw the Local Target and cast it as the Reaction Type
                        }
                        else
                        {
                            var typ = (property.managedReferenceValue as Reaction).ReactionType;
                            EditorGUI.ObjectField(TargetRect2, localTarget, typ, GUIContent.none); //Draw the Local Target and cast it as the Reaction Type
                        }
                    }

                    var dC = GUI.color;
                    GUI.color = desc.isExpanded ? Color.blue + Color.white : dC;
                    desc.isExpanded = GUI.Toggle(DescripRect, desc.isExpanded, Icon_Edit, style);
                    GUI.color = dC;


                    // MWC — Help button. Greyed out (rather than hidden) when the reaction has no [HelpURL],
                    // so every row keeps the same layout and a missing page is visible instead of silent.
                    var helpURL = GetHelpURL(property.managedReferenceValue as Reaction);

                    using (new EditorGUI.DisabledScope(helpURL == null))
                    {
                        Icon_Help.tooltip = helpURL != null
                            ? $"Open the documentation for this Reaction\n{helpURL}"
                            : "This Reaction has no documentation page.\nAdd a [HelpURL] attribute to its class to link one";

                        if (GUI.Button(HelpRect, Icon_Help, EditorStyles.iconButton))
                            Application.OpenURL(helpURL);
                    }

                    if (GUI.Button(RemoveRect, Icon_Delete, style))
                    {
                        property.managedReferenceValue = null;
                        property.serializedObject.ApplyModifiedProperties();
                    }
                }

                if (isNull)
                {
                    var propertyName = TypePopupCache.GetTypeName(property, m_TypeNameCaches);

                    //  var guiColor = GUI.color;
                    // GUI.color = Color.red + Color.white;

                    if (EditorGUI.DropdownButton(popupPosition, propertyName, FocusType.Keyboard))
                    {
                        TypePopupCache popup = GetTypePopup(property);
                        m_TargetProperty = property;
                        popup.TypePopup.Show(popupPosition);
                    }
                    //   GUI.color = guiColor;
                }
                // Draw the managed reference property.
                if (isNull)
                {
                    EditorGUI.PropertyField(position, property, label, true);
                }
                else
                {
                    // MWC — draw the header row, then the reaction's own fields. The Condition is a normal
                    //       visible child now (gated by [Hide(useCondition)] on Reaction.condition), so the
                    //       child loop below draws it - no manual placement needed.
                    var line = EditorGUIUtility.singleLineHeight;
                    var spacing = EditorGUIUtility.standardVerticalSpacing;

                    bool bodyExists = property.hasVisibleChildren;

                    if (bodyExists)
                    {
                        // MWC — draw the foldout arrow in its canonical far-left slot with the ambient indent
                        //       neutralized (MReactions does indentLevel++). The Active checkbox lives to its
                        //       right (actRect.x = position.x + 18), so the arrow can never paint under it.
                        var indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        var foldRect = new Rect(position) { height = line, x = position.x, width = position.width };
                        property.isExpanded = EditorGUI.Foldout(foldRect, property.isExpanded, GUIContent.none, true);
                        EditorGUI.indentLevel = indent;
                    }

                    if (bodyExists && property.isExpanded)
                    {
                        var y = position.y + line + spacing;
                        //The reaction's visible fields (the Condition is one of them)
                        var end = property.GetEndProperty();
                        var child = property.Copy();
                        bool enter = true;

                        while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                        {
                            enter = false;
                            var h = EditorGUI.GetPropertyHeight(child, true);
                            EditorGUI.PropertyField(new Rect(position) { y = y, height = h }, child, true);
                            y += h + spacing;
                        }
                    }
                }
            }
            EditorGUI.EndProperty();
            // EditorGUI.indentLevel = indent;
        }

        // MWC — initialize all icons in one pass, guarded by s_iconsInitialized flag
        private static void FindIcons()
        {
            s_iconsInitialized = true;

            Icon_Delete = EditorGUIUtility.IconContent("d_TreeEditor.Trash");
            Icon_Delete.tooltip = "Clear the Condition";

            Icon_Global = EditorGUIUtility.IconContent("d_ToolHandleGlobal@2x");
            Icon_Global.tooltip = "Dynamic Target. The target for the reaction will be found dynamically";

            Icon_Local = EditorGUIUtility.IconContent("d_ToolHandleLocal@2x");
            Icon_Local.tooltip = "Local Target. The target for the reaction will be set in the Editor and it will not be changed";

            Icon_Delay = EditorGUIUtility.IconContent("d_UnityEditor.AnimationWindow@2x");
            Icon_Delay.tooltip = "Delay the Reaction for x Seconds";

            Icon_Condition = new GUIContent(EditorGUIUtility.IconContent("Animator Icon"))
            {
                tooltip = "Use Condition. The reaction will only execute if the condition is valid"
            };

            Icon_Edit = MalbersEditor.Icon_Edit;
            Icon_Edit.tooltip = "Edit the reaction Description";

            // MWC — cloned, not shared: the tooltip is rewritten per row (documented vs. undocumented reaction),
            // and IconContent returns a cached instance that other drawers also use.
            Icon_Help = new GUIContent(EditorGUIUtility.IconContent("d_Help@2x").image);
        }

        private TypePopupCache GetTypePopup(SerializedProperty property)
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
            // MWC — the Condition is a visible child now, so it is already included here. HidePropertyDrawer
            //       collapses it to -standardVerticalSpacing when 'useCondition' is off.
            return EditorGUI.GetPropertyHeight(property, true) + 2;
        }
    }
}
#endif
