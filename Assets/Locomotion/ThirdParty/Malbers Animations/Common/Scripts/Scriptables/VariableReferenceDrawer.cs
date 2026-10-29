// Last Change: 9/20/2026 12:05pm
#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    [CustomPropertyDrawer(typeof(ReferenceVar), true)]
    public class VariableReferenceDrawer : PropertyDrawer
    {
        /// <summary>  Options to display in the popup to select constant or variable. </summary>
        private static readonly string[] popupOptions = { "Use Local", "Use Global" };

        //MWC — third mode: read the value from a member on another component/object (read-only)
        private static readonly string[] popupOptionsMember = { "Use Local", "Use Global", "Use Reference" };

        //MWC — width of the [!] invert toggle shown on bool references in member mode
        private const float INVERT_W = 22f;

        private static readonly GUIContent s_Invert = new("!", "Invert: use the logical NOT of the value read from the member");

        /// <summary> Cached style to use to draw the popup button. </summary>
        private static GUIStyle popupStyle;
        private GUIStyle AddStyle;
        private GUIContent plus;

        /// <summary>Lazily built style of the mode popup (shared with external drawers that paint the constant themselves)</summary>
        private static GUIStyle PopupStyle => popupStyle ??= new GUIStyle(GUI.skin.GetStyle("PaneOptions")) { imagePosition = ImagePosition.ImageOnly };

        /// <summary>True when the reference reads its Local constant (not a Global var nor a Member); [valueType] is the ConstantValue type</summary>
        public static bool IsLocalMode(SerializedProperty property, Type valueType)
        {
            var useConstant = property.FindPropertyRelative("UseConstant");
            var useMember = property.FindPropertyRelative("UseMember");
            bool memberSupported = valueType != null && FilterFor(valueType) != null && useMember != null;
            return useConstant.boolValue && !(memberSupported && useMember.boolValue);
        }

        /// <summary>Draws only the Local/Global/Reference popup, 20px left of [fieldRect], for a drawer that paints the constant field itself</summary>
        public static void DrawModePopup(Rect fieldRect, SerializedProperty property, Type valueType)
        {
            var useConstant = property.FindPropertyRelative("UseConstant");
            var useMember = property.FindPropertyRelative("UseMember");
            bool memberSupported = valueType != null && FilterFor(valueType) != null && useMember != null;
            DrawModePopup(fieldRect, useConstant, useMember, memberSupported, out _);
        }

        /// <summary>Draws the mode popup 20px left of [fieldRect] and applies the pick; returns true while the reference is in Member (Use Reference) mode</summary>
        private static bool DrawModePopup(Rect fieldRect, SerializedProperty useConstant, SerializedProperty useMember, bool memberSupported, out Rect buttonRect)
        {
            buttonRect = new Rect(fieldRect);
            buttonRect.yMin += PopupStyle.margin.top;
            buttonRect.width = PopupStyle.fixedWidth + PopupStyle.margin.right;
            buttonRect.x -= 20;
            buttonRect.height = EditorGUIUtility.singleLineHeight;

            //CustomPatch: Ensures multi-object selection properly propagates changes without losing data (useConstant was resetting when multi-selecting objects).
            //MWC — popup maps 0:Local 1:Global 2:Reference (2 only when the type supports it)
            bool memberMode = memberSupported && useMember.boolValue;
            int currentIndex = memberMode ? 2 : (useConstant.boolValue ? 0 : 1);

            EditorGUI.BeginChangeCheck();
            int result = EditorGUI.Popup(buttonRect, currentIndex, memberSupported ? popupOptionsMember : popupOptions, PopupStyle);
            if (EditorGUI.EndChangeCheck())
            {
                useConstant.boolValue = (result == 0);
                if (useMember != null) useMember.boolValue = (result == 2);
                memberMode = memberSupported && result == 2;
            }
            return memberMode;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (AddStyle == null)
                AddStyle = new GUIStyle(GUI.skin.GetStyle("PaneOptions")) { imagePosition = ImagePosition.ImageOnly };

            if (plus == null) plus = UnityEditor.EditorGUIUtility.IconContent("d_Toolbar Plus");

            position.y -= 0;

            label = EditorGUI.BeginProperty(position, label, property);
            {
                Rect variableRect = new Rect(position); //MWC — full inspector row (label column included); argument rows are drawn on it
                position = EditorGUI.PrefixLabel(position, label);


                float height = EditorGUIUtility.singleLineHeight;

                // Get properties
                SerializedProperty useConstant = property.FindPropertyRelative("UseConstant");
                SerializedProperty constantValue = property.FindPropertyRelative("ConstantValue");
                SerializedProperty variable = property.FindPropertyRelative("Variable");
                SerializedProperty useMember = property.FindPropertyRelative("UseMember");

                //MWC — member mode is offered only when the reference's value type has a wired conversion
                var valueType = ValueTypeOf(property);
                var memberFilter = valueType == null ? null : FilterFor(valueType);
                bool memberSupported = memberFilter != null && useMember != null;

                Rect propRect = new Rect(position) { height = height };

                var AddButtonRect = new Rect(propRect) { x = propRect.width + propRect.x - 18, width = 20 };
                var ValueRect = new Rect(AddButtonRect);

                // Store old indent level and set it to 0, the PrefixLabel takes care of it
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;

                //Mode popup hangs 20px left of the field; shared with drawers that paint the constant themselves
                bool memberMode = DrawModePopup(position, useConstant, useMember, memberSupported, out Rect buttonRect);
                position.xMin = buttonRect.xMax;

                if (memberMode)
                {
                    //MWC — [Target Object | Member] row + arg rows; only bool references get the [!] toggle
                    DrawMemberMode(propRect, variableRect, indent, property, memberFilter, valueType == typeof(bool));
                }
                else
                {
                    bool varIsEmpty = variable.objectReferenceValue == null;

                    if (!useConstant.boolValue)
                    {
                        if (varIsEmpty)
                        {
                            propRect.width -= 20;
                        }
                        else
                        {
                            if (ValidObject(variable.objectReferenceValue))   //Do not Paint other than Int float and Strings
                            {
                                //propRect.width -= 30;
                                ValueRect.width = (propRect.width / 2 * 0.25f) + 9;
                                propRect.width = (propRect.width / 2 * 1.75f) - 13;
                                //  ValueRect.x -= 8;
                                ValueRect.x = position.x + propRect.width + 8;
                            }
                        }
                    }


                    EditorGUIUtility.labelWidth = 0.1f;
                    EditorGUI.PropertyField(propRect, useConstant.boolValue ? constantValue : variable, GUIContent.none, false);
                    EditorGUIUtility.labelWidth = 0;

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
                            ShowScriptVar(ValueRect, variable);
                        }
                    }
                }
                EditorGUI.indentLevel = indent;
            }
            EditorGUI.EndProperty();
        }

        // ─── Member (Use Reference) mode ─────────────────────────────────────────
        //MWC — draws [Target Object | Member picker] on the main row and one row per method argument below.

        private static void DrawMemberMode(Rect propRect, Rect rowRect, int indent, SerializedProperty property,
                                           Func<Type, bool> memberFilter, bool allowInvert)
        {
            var getterProp = property.FindPropertyRelative("Member");
            var targetProp = getterProp.FindPropertyRelative("target");

            var target = targetProp.objectReferenceValue;

            // Row: [target object | member button | !]. Target takes the full row while unassigned.
            Rect targetRect = new Rect(propRect);
            Rect memberRect = default;
            Rect invertRect = default;

            if (target != null)
            {
                targetRect.width = propRect.width * 0.5f - 2;
                memberRect = new Rect(propRect) { x = targetRect.xMax + 4, width = propRect.width * 0.5f - 2 };

                //MWC — bool references get a [!] toggle carved out of the right end of the member column
                if (allowInvert)
                {
                    memberRect.width -= INVERT_W + 2;
                    invertRect = new Rect(propRect) { x = memberRect.xMax + 2, width = INVERT_W };
                }
            }

            EditorGUI.BeginChangeCheck();
            var picked = EditorGUI.ObjectField(targetRect, GUIContent.none, target, typeof(UnityEngine.Object), true);
            if (EditorGUI.EndChangeCheck())
            {
                //MWC — a stale member from a different type would silently fail at runtime, so it is cleared
                if (picked == null || target == null || picked.GetType() != target.GetType())
                    MemberGetterEditor.ClearSelection(getterProp);

                targetProp.objectReferenceValue = picked;

                //MWC — a dropped GameObject usually means "one of its components": offer them right away
                if (picked is GameObject go) ShowComponentMenu(getterProp, go);
            }

            if (target != null)
            {
                var invertProp = allowInvert ? getterProp.FindPropertyRelative("invert") : null;
                DrawMemberButton(memberRect, getterProp, target, memberFilter, invertProp != null && invertProp.boolValue);

                //MWC — logical NOT of the read value; the member button shows the "!" so it reads as one expression
                if (invertProp != null)
                {
                    EditorGUI.BeginChangeCheck();
                    bool inv = GUI.Toggle(invertRect, invertProp.boolValue, s_Invert, EditorStyles.miniButton);
                    if (EditorGUI.EndChangeCheck()) invertProp.boolValue = inv;
                }
            }

            // Rows below: constant arguments of the picked method.
            //MWC — drawn on the FULL inspector row (label column included) with the indent restored, so the
            //argument name lands in the normal label column and its field spans the normal field column.
            //Drawing them on the post-PrefixLabel rect instead squeezes the field against the right edge,
            //because labelWidth is measured from the row's left edge and would apply twice.
            var argsProp = getterProp.FindPropertyRelative("args");
            if (argsProp.arraySize == 0) return;

            float rowH = EditorGUIUtility.singleLineHeight;
            float step = rowH + EditorGUIUtility.standardVerticalSpacing;

            EditorGUI.indentLevel = indent + 1; //nested one step under the reference itself
            for (int i = 0; i < argsProp.arraySize; i++)
            {
                var argRect = new Rect(rowRect.x, propRect.y + (i + 1) * step, rowRect.width, rowH);
                MemberValueReaderDrawer.DrawArg(argRect, argsProp.GetArrayElementAtIndex(i));
            }
            EditorGUI.indentLevel = 0; //the caller restores the real indent when the property ends
        }

        //MWC — member dropdown: opens the shared member picker filtered to types this reference can carry.
        private static void DrawMemberButton(Rect r, SerializedProperty getterProp, UnityEngine.Object target,
                                             Func<Type, bool> memberFilter, bool inverted)
        {
            var memberProp = getterProp.FindPropertyRelative("memberName");
            var sigProp = getterProp.FindPropertyRelative("paramSignature");
            bool isMethod = getterProp.FindPropertyRelative("memberKind").enumValueIndex == (int)MMemberKind.Method;

            string not = inverted ? "!" : string.Empty; //MWC — the negation is part of what the value reads as
            string display = string.IsNullOrEmpty(memberProp.stringValue) ? "Member…"
                           : isMethod ? $"{not}{memberProp.stringValue}({sigProp.stringValue})"
                           : $"{not}{memberProp.stringValue}";

            if (EditorGUI.DropdownButton(r, new GUIContent(display, "Field, Property or Method to read the value from"),
                                         FocusType.Keyboard, EditorStyles.popup))
            {
                var so = getterProp.serializedObject;
                var path = getterProp.propertyPath;

                PopupWindow.Show(r, new MGV_MemberPickerPopup(target.GetType(), memberProp.stringValue, sigProp.stringValue, sig =>
                {
                    so.Update();
                    MemberGetterEditor.ApplySelection(so.FindProperty(path), sig);
                    so.ApplyModifiedProperties();
                }, memberFilter));
            }
        }

        //MWC — dropped GameObject: choose the GameObject itself or one of its components as the read target.
        private static void ShowComponentMenu(SerializedProperty getterProp, GameObject go)
        {
            var so = getterProp.serializedObject;
            var path = getterProp.propertyPath;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"GameObject ({go.name})"), false, () => AssignTarget(so, path, go));

            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue; //missing scripts
                var comp = c;
                menu.AddItem(new GUIContent(comp.GetType().Name), false, () => AssignTarget(so, path, comp));
            }
            menu.ShowAsContext();
        }

        //MWC — deferred assignment from the component menu (runs outside the GUI pass).
        private static void AssignTarget(SerializedObject so, string getterPath, UnityEngine.Object obj)
        {
            so.Update();
            var getterProp = so.FindProperty(getterPath);
            var targetProp = getterProp.FindPropertyRelative("target");

            var old = targetProp.objectReferenceValue;
            if (old == null || obj == null || old.GetType() != obj.GetType())
                MemberGetterEditor.ClearSelection(getterProp); //MWC — member belongs to the previous type

            targetProp.objectReferenceValue = obj;
            so.ApplyModifiedProperties();
        }

        // ─── Member-mode support per reference type ──────────────────────────────

        //MWC — the value type this reference carries, taken from its ConstantValue field. Null when unresolvable.
        private Type ValueTypeOf(SerializedProperty property)
        {
            var refType = fieldInfo != null ? ElementType(fieldInfo.FieldType) : null;
            return refType?.GetField("ConstantValue")?.FieldType;
        }

        //MWC — unwraps arrays and List<T> so references inside collections resolve their element type.
        private static Type ElementType(Type t)
        {
            if (t.IsArray) return t.GetElementType();
            if (t.IsGenericType && t.GetGenericArguments().Length == 1 &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) return t.GetGenericArguments()[0];
            return t;
        }

        //MWC — which member value types each reference type accepts (must stay in sync with the wired Get* conversions).
        private static Func<Type, bool> FilterFor(Type valueType)
        {
            if (valueType == typeof(float)) return IsNumeric;
            if (valueType == typeof(int)) return t => t.IsEnum || IsNumeric(t);
            if (valueType == typeof(bool)) return t => t == typeof(bool);
            if (valueType == typeof(string)) return t => t == typeof(string);
            if (valueType == typeof(Vector2)) return t => t == typeof(Vector2) || t == typeof(Vector3);
            if (valueType == typeof(Vector3)) return t => t == typeof(Vector3) || t == typeof(Vector2) || t == typeof(Quaternion);
            if (valueType == typeof(Color)) return t => t == typeof(Color);
            if (valueType == typeof(Transform)) return t => typeof(Transform).IsAssignableFrom(t) || t == typeof(GameObject) || typeof(Component).IsAssignableFrom(t);
            if (valueType == typeof(GameObject)) return t => t == typeof(GameObject) || typeof(Component).IsAssignableFrom(t);
            if (valueType == typeof(Sprite)) return t => typeof(Sprite).IsAssignableFrom(t);

            return null; //unsupported reference type → member mode hidden
        }

        //MWC — numeric member types the float/int conversions understand.
        private static bool IsNumeric(Type t) =>
            t == typeof(float) || t == typeof(double) || t == typeof(decimal) ||
            t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) ||
            t == typeof(sbyte) || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort);

        private static void ShowScriptVar(Rect variableRect, SerializedProperty variable)
        {
            if (variable.objectReferenceValue != null)
            {
                if (!ValidObject(variable.objectReferenceValue)) return; //Do not Paint vectors

                SerializedObject objs = new(variable.objectReferenceValue);

                var Var = objs.FindProperty("value");

                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(variableRect, Var, GUIContent.none);
                if (EditorGUI.EndChangeCheck())
                {
                    objs.ApplyModifiedProperties();
                    EditorUtility.SetDirty(variable.objectReferenceValue);
                }
            }
        }


        private static bool ValidObject(UnityEngine.Object val) => (val is IntVar) || (val is FloatVar && val is not FloatRangeVar) || (val is BoolVar) /*|| (val is StringVar)*/;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            //MWC — member mode: main row plus one row per method argument
            var useMember = property.FindPropertyRelative("UseMember");
            if (useMember != null && useMember.boolValue && ValueTypeOf(property) is Type vt && FilterFor(vt) != null)
            {
                float h = EditorGUIUtility.singleLineHeight;
                var argsProp = property.FindPropertyRelative("Member").FindPropertyRelative("args");
                if (argsProp != null)
                    h += argsProp.arraySize * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
                return h;
            }

            // Pick which sub-property we are drawing
            var useConstant = property.FindPropertyRelative("UseConstant");
            var targetProp = useConstant.boolValue
                ? property.FindPropertyRelative("ConstantValue")
                : property.FindPropertyRelative("Variable");

            // Ask Unity for the height of that sub-property
            return EditorGUI.GetPropertyHeight(targetProp, label, true);
        }
    }
}
#endif
