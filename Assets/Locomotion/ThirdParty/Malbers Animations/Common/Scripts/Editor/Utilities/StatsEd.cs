// Last Change: 9/24/2026 1:16pm
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MalbersAnimations
{
    [CustomEditor(typeof(Stats))]
    public class StatsEd : Editor
    {
        protected ReorderableList list;
        protected Stats M;
        protected SerializedProperty statList, Selected_StatIndex;

        protected string[] Tabs1 = new string[] { "General", "Events" };

        protected virtual void OnEnable()
        {
            M = (Stats)target;

            statList = serializedObject.FindProperty("stats");
            Selected_StatIndex = serializedObject.FindProperty("Selected_StatIndex");
            //Set = serializedObject.FindProperty("Set");


            list = new(serializedObject, statList, true, true, true, true)
            {
                drawElementCallback = DrawElementCallback,
                drawHeaderCallback = HeaderCallbackDelegate,
                onAddCallback = OnAddCallBack,
                onSelectCallback = (list) => { Selected_StatIndex.intValue = list.index; },
                index = Selected_StatIndex.intValue
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Stats Manager");

            //  EditorGUILayout.BeginVertical(MalbersEditor.StyleGray);
            {

                if (Application.isPlaying)
                {

                    using (new EditorGUI.DisabledGroupScope(true))
                    {
                        if (M.PinnedStat != null)
                            EditorGUILayout.ObjectField("Pin Stat: ", (StatID)M.PinnedStat.ID, typeof(StatID), false);
                        else
                            EditorGUILayout.LabelField("Pin Stat: NULL ");
                    }

                }

                list.DoLayoutList();

                if (list.index >= 0 && list.index < statList.arraySize)
                {
                    var element = statList.GetArrayElementAtIndex(list.index);

                    var ID = element.FindPropertyRelative("ID").objectReferenceValue;
                    var statName = ID != null ? ID.name : "";

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            var dC = GUI.color;
                            GUI.color = selected;

                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(element, new GUIContent($"[{statName} Properties]"), false);
                            EditorGUI.indentLevel--;
                            GUI.color = dC;
                            var debug = element.FindPropertyRelative("debug");
                            MalbersEditor.DrawDebugIcon(debug);

                        }

                        if (element.isExpanded)
                        {
                            var EditorTabs = element.FindPropertyRelative("EditorTabs");

                            EditorTabs.intValue = GUILayout.Toolbar(EditorTabs.intValue, Tabs1);

                            if (EditorTabs.intValue == 0) DrawGeneral(element);
                            else DrawEvents(element);
                        }
                    }
                }
            }
            //EditorGUILayout.PropertyField(Set,new GUIContent("Runtime Set")); 
            //    EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Draws the General tab of the selected Stat: values, regeneration, degeneration and options</summary>
        protected virtual void DrawGeneral(SerializedProperty element)
        {
            var DisableOnEmpty = element.FindPropertyRelative("DisableOnEmpty");
            var InvokeEventsOnEnable = element.FindPropertyRelative("InvokeEventsOnEnable");
            var Value = element.FindPropertyRelative("value");
            var MaxValue = element.FindPropertyRelative("maxValue");
            var MinValue = element.FindPropertyRelative("minValue");

            var resetOnEnable = element.FindPropertyRelative("resetOnEnable"); //MWC — replaces 'resetTo' + 'ResetOnEnable'
            var ImmuneTime = element.FindPropertyRelative("ImmuneTime");
            var immune = element.FindPropertyRelative("immune");

            var Regenerate = element.FindPropertyRelative("regenerate");

            var can_Regenerate = element.FindPropertyRelative("can_Regenerate");
            var can_Degenerate = element.FindPropertyRelative("can_Degenerate");

            var RegenRate = element.FindPropertyRelative("RegenRate");
            var RegenWaitTime = element.FindPropertyRelative("RegenWaitTime");

            var Degenerate = element.FindPropertyRelative("degenerate");
            var DegenRate = element.FindPropertyRelative("DegenRate");
            var DegenWaitTime = element.FindPropertyRelative("DegenWaitTime");
            var multiplier = element.FindPropertyRelative("multiplier");
            var Round = element.FindPropertyRelative("Round");

            var labelWidth = 50f;

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                //MWC — one change check around Value/Min/Max: editing any of the three re-clamps the Value into the range
                EditorGUI.BeginChangeCheck();

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = labelWidth;
                    EditorGUILayout.PropertyField(Value);
                    EditorGUILayout.PropertyField(multiplier, new GUIContent("  Mult", "Stat Multiplier to be used when the value is modified"));
                    EditorGUIUtility.labelWidth = 0;
                }

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = labelWidth;
                    EditorGUILayout.PropertyField(MinValue, new GUIContent("Min"));
                    EditorGUILayout.PropertyField(MaxValue, new GUIContent("  Max"));
                    EditorGUIUtility.labelWidth = 0;
                }

                if (EditorGUI.EndChangeCheck()) ClampValueToRange(element);
            }

            //MWC — Regeneration is a foldout now. The open/closed state rides on [can_Regenerate].isExpanded,
            //so nothing new has to be serialized on the Stat (the Reference drawers never use isExpanded)
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (can_Regenerate.isExpanded = MalbersEditor.Foldout(can_Regenerate.isExpanded, "Regeneration"))
                {
                    EditorGUILayout.PropertyField(can_Regenerate, new GUIContent("Can Regenerate", "Enable Disable the Regeneration Logic"));
                    EditorGUILayout.PropertyField(Regenerate, new GUIContent("Regenerate", "Regenerate over time"));

                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUIUtility.labelWidth = labelWidth;
                        EditorGUILayout.PropertyField(RegenRate, new GUIContent("Rate", "Regeneration Rate, how fast/Slow the Stat will regenerate"));
                        EditorGUILayout.PropertyField(RegenWaitTime, new GUIContent("  Wait", "After the Stat is modified, the time to wait to start regenerating"));
                        EditorGUIUtility.labelWidth = 0;
                    }
                }
            }

            //MWC — Degeneration foldout, state on [can_Degenerate].isExpanded
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (can_Degenerate.isExpanded = MalbersEditor.Foldout(can_Degenerate.isExpanded, "Degeneration"))
                {
                    EditorGUILayout.PropertyField(can_Degenerate, new GUIContent("Can Degenerate", "Enable Disable the Degeneration Logic"));
                    EditorGUILayout.PropertyField(Degenerate, new GUIContent("Degenerate", "Degenerate over time"));

                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUIUtility.labelWidth = labelWidth;
                        EditorGUILayout.PropertyField(DegenRate, new GUIContent("Rate", "Degeneration Rate, how fast/Slow the Stat will Degenerate"));
                        EditorGUILayout.PropertyField(DegenWaitTime, new GUIContent("  Wait", "After the Stat is modified, the time to wait to start degenerating"));
                        EditorGUIUtility.labelWidth = 0;
                    }
                }
            }

            //MWC — [Disable On Empty] and [Round] moved in here, next to Reset On Enable / Immune.
            //One foldout for all of them, state on [resetOnEnable].isExpanded
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (resetOnEnable.isExpanded = MalbersEditor.Foldout(resetOnEnable.isExpanded, "Options"))
                {
                    EditorGUILayout.PropertyField(DisableOnEmpty);
                    EditorGUILayout.PropertyField(InvokeEventsOnEnable, new GUIContent("Events On Enable", InvokeEventsOnEnable.tooltip));
                    EditorGUILayout.PropertyField(Round);

                    //MWC — one field now: it says whether the Stat resets on Enable AND which end it resets to.
                    //It is also the target used by Restart(), Restore All and the [Reset] Stat Option (None → Max Value).
                    EditorGUILayout.PropertyField(resetOnEnable, new GUIContent("Reset On Enable",
                        "None: keep the serialized Value when the Stat is enabled.\n" +
                        "Reset To Min/Max Value: set the Value on enable.\n" +
                        "Also the target used by Restart(), Restore All and the [Reset] Stat Option (None → Max Value)"));

                    EditorGUILayout.PropertyField(ImmuneTime, new GUIContent("Immune Time", "If greater than zero, the Stat cannot be modify until the inmune time have passed"));
                    EditorGUILayout.PropertyField(immune);

                    //MWC — Parent Link: how each number follows the same Stat on the closest Stats ABOVE this one in the
                    //hierarchy (E.g. a weapon [Attack] adding the character [Attack] while equipped or holstered)
                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField(new GUIContent("Parent Link (On Parent Change)",
                        "How this Stat follows the same Stat on the closest [Stats] component above it in the hierarchy.\n" +
                        "E.g. a sword [Attack 20] equipped or holstered by a character with [Attack 30] reads 50 with [Additive], and goes back to 20 when dropped.\n" +
                        "The link is live: the Stat follows the parent Stat while it is linked"), EditorStyles.boldLabel);

                    using (new EditorGUI.IndentLevelScope())
                    {
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("parentValue"), new GUIContent("Value"));
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("parentMax"), new GUIContent("Max Value"));
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("parentMultiplier"), new GUIContent("Multiplier"));
                    }
                }
            }
        }

        protected virtual void DrawEvents(SerializedProperty element)
        {
            var id = element.FindPropertyRelative("ID");
            var BelowValue = element.FindPropertyRelative("Below");
            var AboveValue = element.FindPropertyRelative("Above");

            var OnValueChange = element.FindPropertyRelative("OnValueChange");
            var OnValueChangeNormalized = element.FindPropertyRelative("OnValueChangeNormalized");
            var OnStatFull = element.FindPropertyRelative("OnStatFull");
            var OnStatEmpty = element.FindPropertyRelative("OnStatEmpty");
            var OnRegenerate = element.FindPropertyRelative("OnRegenerate");
            var OnDegenerate = element.FindPropertyRelative("OnDegenerate");
            var OnStatBelow = element.FindPropertyRelative("OnStatBelow");
            var OnStatAbove = element.FindPropertyRelative("OnStatAbove");
            var OnMaxValueChange = element.FindPropertyRelative("OnMaxValueChange");
            var OnActive = element.FindPropertyRelative("OnActive");
            var isPercent = element.FindPropertyRelative("isPercent");


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string name = "Stat";

                if (id.objectReferenceValue != null)
                {
                    name = id.objectReferenceValue.name;
                }

                EditorGUILayout.PropertyField(OnValueChange, new GUIContent($"On [{name}] change"));
                EditorGUILayout.PropertyField(OnValueChangeNormalized, new GUIContent($"On [{name}] change normalized"));
                EditorGUILayout.PropertyField(OnMaxValueChange, new GUIContent($"On [{name}] Max Value change"));
                MalbersEditor.DrawSplitter();
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(OnActive, new GUIContent($"On [{name}] Active "));
                EditorGUILayout.PropertyField(OnStatFull, new GUIContent($"On [{name}] full "));
                EditorGUILayout.PropertyField(OnStatEmpty, new GUIContent($"On [{name}] empty "));
                MalbersEditor.DrawSplitter();
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(OnRegenerate, new GUIContent($"On [{name}] Regenerate "));
                EditorGUILayout.PropertyField(OnDegenerate, new GUIContent($"On [{name}] Degenerate "));

                MalbersEditor.DrawSplitter();
                EditorGUILayout.Space();

                using (new GUILayout.HorizontalScope())

                {
                    EditorGUIUtility.labelWidth = 55;
                    EditorGUILayout.PropertyField(BelowValue, new GUIContent("Below", "Used to Check when the Stat is below this value"));
                    EditorGUILayout.PropertyField(AboveValue, new GUIContent("Above", "Used to Check when the Stat is Above this value"));
                    isPercent.boolValue = GUILayout.Toggle(isPercent.boolValue, new GUIContent("%", "Check below/above using percent instead of static values"), EditorStyles.miniButton, GUILayout.Width(25));
                    EditorGUIUtility.labelWidth = 0;
                }
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(OnStatBelow, new GUIContent($"On [{name}] Below {BelowValue.floatValue}"));
                EditorGUILayout.PropertyField(OnStatAbove, new GUIContent($"On [{name}] Above {AboveValue.floatValue}"));
            }

        }

        private const float IDNumberW = 34f; // MWC — width of the numeric Stat ID column

        //MWC — single source for the row columns, so the header labels and the element fields cannot drift apart.
        //The 40px before the Stat ID hold the [active] toggle and the space the ID drawer paints its [⋮] button into.
        private static void RowRects(Rect rect, out Rect active, out Rect id, out Rect idNumber, out Rect value)
        {
            rect.x += 5;
            rect.width -= 15;

            var line = EditorGUIUtility.singleLineHeight;
            var half = rect.width / 2;

            active = new Rect(rect.x - 5, rect.y, 15, line);
            id = new Rect(rect.x + 30, rect.y, half - IDNumberW, line);
            value = new Rect(rect.x + 20 + half, rect.y, rect.width - half - 30, line);
            idNumber = new Rect(value.xMax + 4, rect.y, IDNumberW, line); //MWC — moved here, right after the Stat ID
        }

        protected void HeaderCallbackDelegate(Rect rect)
        {
            RowRects(rect, out _, out var R_ID, out var R_IDNumber, out var R_Value);

            EditorGUI.LabelField(R_ID, "     ID/Name", EditorStyles.miniLabel);
            EditorGUI.LabelField(R_IDNumber, "ID", EditorStyles.miniLabel); // MWC — over the ID number column
            EditorGUI.LabelField(R_Value, "Value", EditorStyles.centeredGreyMiniLabel);
        }

        private static readonly Color selected = new(1.5f, 1f, 0);

        protected void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = statList.GetArrayElementAtIndex(index);
            var ID = element.FindPropertyRelative("ID");
            var active = element.FindPropertyRelative("active");
            var Value = element.FindPropertyRelative("value");

            rect.y += 2;

            RowRects(rect, out var R_0, out var R_1, out var R_3, out var R_2); // MWC — shared with the header

            var dC = GUI.color;
            if (index == Selected_StatIndex.intValue) GUI.color = selected;

            EditorGUI.PropertyField(R_0, active, new GUIContent("", "Is the Stat Enabled? when Disable no modification can be done"));

            EditorGUI.PropertyField(R_1, ID, GUIContent.none);

            if (ID.objectReferenceValue != null)
            {
                var od = ID.objectReferenceValue as StatID;
                EditorGUI.LabelField(R_3, od.ID.ToString(), EditorStyles.boldLabel); // MWC — now right after the Stat ID
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(R_2, Value, GUIContent.none);

            //MWC — was raising Max up to the typed Value. Now the Value is clamped into the [Min - Max] range instead
            if (EditorGUI.EndChangeCheck()) ClampValueToRange(element);

            GUI.color = dC;
        }

        //MWC — keep a Stat's Value inside its [Min - Max] range. Used by both Value property fields
        //(the list row and the [General] tab) so neither can push the Value out of range.
        private static void ClampValueToRange(SerializedProperty element)
        {
            var value = element.FindPropertyRelative("value");
            var minValue = element.FindPropertyRelative("minValue");
            var maxValue = element.FindPropertyRelative("maxValue");

            //Only the constants are editable here: when a field is driven by a FloatVar asset its ConstantValue is not the live value
            if (!UsesConstant(value) || !UsesConstant(minValue) || !UsesConstant(maxValue)) return;

            var Constant = value.FindPropertyRelative("ConstantValue");
            var ConstantMIN = minValue.FindPropertyRelative("ConstantValue");
            var ConstantMAX = maxValue.FindPropertyRelative("ConstantValue");

            //MWC — Min and Max both zero means the Stat has no range (see Stat.HasNoRange): leave the Value alone
            if (ConstantMIN.floatValue == 0 && ConstantMAX.floatValue == 0) return;

            if (ConstantMAX.floatValue < ConstantMIN.floatValue) ConstantMAX.floatValue = ConstantMIN.floatValue; //Keep the range valid first

            Constant.floatValue = Mathf.Clamp(Constant.floatValue, ConstantMIN.floatValue, ConstantMAX.floatValue);
        }

        //MWC — mirrors FloatReference.Value: the constant is the live value when UseConstant is on or no Variable is assigned
        private static bool UsesConstant(SerializedProperty reference) =>
            reference.FindPropertyRelative("UseConstant").boolValue ||
            reference.FindPropertyRelative("Variable").objectReferenceValue == null;


        protected virtual void OnAddCallBack(ReorderableList list)
        {
            M.stats ??= new List<Stat>();
            M.stats.Add(new Stat());
        }
    }
}

#endif