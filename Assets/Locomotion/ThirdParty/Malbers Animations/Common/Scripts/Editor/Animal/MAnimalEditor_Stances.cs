#if UNITY_EDITOR
using MalbersAnimations.Scriptables;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    // MWC - partial: Stances tab — reorderable list setup and ShowStances inspector
    public partial class MAnimalEditor
    {
        private void Reordable_Stances()
        {
            Reo_List_Stances = new ReorderableList(serializedObject, Stances_List, true, true, true, true)
            {
                drawHeaderCallback = (rect) =>
                {
                    var r = new Rect(rect);
                    r.x += 40;
                    r.width = 90;
                    EditorGUI.LabelField(r, new GUIContent("Stances", "Stances allowed in this Controller"));

                    var activeRect = rect;
                    activeRect.width -= 20;
                    activeRect.x += 20;
                    var IDRect = new Rect(activeRect.width + 35, activeRect.y, 35, activeRect.height);

                    EditorGUI.LabelField(IDRect, new GUIContent("ID", "Mode ID:\n Numerical ID value for the Mode"));
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;
                    if (Stances_List.arraySize <= index) return;

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        var ModeProperty = Stances_List.GetArrayElementAtIndex(index);
                        var ID = ModeProperty.FindPropertyRelative("ID");

                        var Active = ModeProperty.FindPropertyRelative("enabled");
                        var ConstValue = Active.FindPropertyRelative("ConstantValue");
                        var VarValue = Active.FindPropertyRelative("Variable");
                        var useConstant = Active.FindPropertyRelative("UseConstant").boolValue;
                        BoolVar variable = VarValue.objectReferenceValue as BoolVar;

                        var rect_an = new Rect(rect);
                        rect_an.width -= 20;
                        rect_an.x += 20;
                        rect_an.y -= 2;

                        var ActiveRect = new Rect(rect.x, rect.y - 2, 20, rect.height);
                        var IDRect = new Rect(rect.x + 40, rect.y, rect.width - 70, EditorGUIUtility.singleLineHeight);
                        var Rect_Label = new Rect(rect.width - 80, rect.y, 60, EditorGUIUtility.singleLineHeight);

                        if (Application.isPlaying) IDRect.width -= 100f;

                        if (useConstant)
                        {
                            ConstValue.boolValue = EditorGUI.Toggle(ActiveRect, GUIContent.none, ConstValue.boolValue);

                            if (variable != null)
                            {
                                variable.Value = ConstValue.boolValue;
                                EditorUtility.SetDirty(variable);
                            }
                        }
                        else
                        {
                            if (variable != null)
                            {
                                variable.Value = EditorGUI.Toggle(ActiveRect, GUIContent.none, variable.Value);
                                ConstValue.boolValue = variable.Value;
                            }
                            else
                            {
                                ConstValue.boolValue = EditorGUI.Toggle(ActiveRect, GUIContent.none, ConstValue.boolValue);
                            }
                        }

                        var oldColor = GUI.contentColor;

                        if (Application.isPlaying)
                        {
                            if (m.Stances[index].Active) GUI.contentColor = Color.yellow;
                            else if (m.Stances[index].Persistent) GUI.contentColor = Color.red + Color.white;
                            else if (m.Stances[index].DisableTemp) GUI.contentColor = Color.white;
                        }


                        var st_label = "";

                        var stanceElement = m.Stances[index];

                        if (Application.isPlaying)
                        {
                            if (stanceElement.Active) st_label = "[Active]";
                            if (stanceElement.Persistent) st_label = "[Persis]";
                            if (stanceElement.Queued) st_label = "[Queued]";
                            if (stanceElement.DisableTemp) st_label = "[Disabled]";
                        }

                        if (Application.isPlaying)
                        {
                            var TestBRect = new Rect(rect.width - 20, rect.y, 38, EditorGUIUtility.singleLineHeight);

                            var color = GUI.color;
                            GUI.color = MTools.MGreen * 1.5f;
                            if (GUI.Button(TestBRect, "Test"))
                            {
                                m.Stance_Activate(ID.objectReferenceValue as StanceID);
                            }
                            GUI.color = color;
                        }



                        var dC = GUI.contentColor;

                        if (!Application.isPlaying && isFocused) GUI.contentColor = new Color(3f, 0.7f, 0.5f);


                        var dbC = GUI.backgroundColor;
                        GUI.backgroundColor = isActive ? MTools.MOrange : dbC;

                        EditorGUI.PropertyField(IDRect, ID, GUIContent.none);

                        EditorGUI.LabelField(Rect_Label, st_label);

                        GUI.contentColor = Application.isPlaying ? oldColor : dC;
                        GUI.backgroundColor = dbC;

                        var style = new GUIStyle(EditorStyles.boldLabel)
                        { alignment = TextAnchor.UpperRight };

                        if (stanceElement.ID != null)
                        {
                            var IDVal = new Rect(rect.width + 10, rect_an.y + 3, 35, rect_an.height);
                            EditorGUI.LabelField(IDVal, stanceElement.ID.ID.ToString(), style);
                        }

                        if (cc.changed)
                        {
                            Undo.RecordObject(target, "MAnimal Inspector");
                            EditorUtility.SetDirty(target);
                        }
                    }
                },

                onAddCallback = (list) =>
                {
                    if (m.Stances == null) m.Stances = new System.Collections.Generic.List<Stance>();

                    var newStance = new Stance();
                    m.Stances.Add(newStance);
                    EditorUtility.SetDirty(m);

                },

                onSelectCallback = (list) =>
                { SelectedStance.intValue = list.index; }
            };
        }

        private void ShowStances()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Stances", EditorStyles.boldLabel);
                    MalbersEditor.DrawDebugIcon(DebugStances, MTools.MOrange);
                }
                EditorGUILayout.PropertyField(defaultStance, new GUIContent("Default Stance", "Default Stance ID to reset to when the animal exit an Stance"));
                EditorGUILayout.PropertyField(currentStance, new GUIContent("Current Stance", "Current Stance ID the animal is On"));

                Reo_List_Stances.index = SelectedStance.intValue;

                Reo_List_Stances.DoLayoutList();

            }
            var StanceIndex = Reo_List_Stances.index;
            if (StanceIndex != -1 && Stances_List.arraySize > 0 && StanceIndex < Stances_List.arraySize)
            {
                //EditorGUILayout.Space(-16);
                var SelectedStanceProp = Stances_List.GetArrayElementAtIndex(StanceIndex);

                var ID = SelectedStanceProp.FindPropertyRelative("ID").objectReferenceValue;
                var n = ID != null ? ID.name : "";

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))

                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(SelectedStanceProp, new GUIContent($"Stance [{n}]"), false);
                    EditorGUI.indentLevel--;

                    if (SelectedStanceProp != null && SelectedStanceProp.isExpanded)
                    {
                        var Active = SelectedStanceProp.FindPropertyRelative("enabled");
                        var Input = SelectedStanceProp.FindPropertyRelative("Input");
                        var persistent = SelectedStanceProp.FindPropertyRelative("persistent");
                        var CoolDown = SelectedStanceProp.FindPropertyRelative("CoolDown");
                        var ExitAfter = SelectedStanceProp.FindPropertyRelative("ExitAfter");
                        var CanStrafe = SelectedStanceProp.FindPropertyRelative("CanStrafe");
                        var states = SelectedStanceProp.FindPropertyRelative("States");
                        var StateQueue = SelectedStanceProp.FindPropertyRelative("stateQueue");
                        // var Include = SelectedStanceProp.FindPropertyRelative("Include");
                        var DisableStances = SelectedStanceProp.FindPropertyRelative("disableStances");
                        var activeOnly = SelectedStanceProp.FindPropertyRelative("activeOnly");
                        var OverrideCapsule = SelectedStanceProp.FindPropertyRelative("OverrideCapsule");
                        var newCapsule = SelectedStanceProp.FindPropertyRelative("newCapsule");
                        var MovementStrafe = SelectedStanceProp.FindPropertyRelative("MovementStrafe");
                        var IdleStrafe = SelectedStanceProp.FindPropertyRelative("IdleStrafe");


                        EditorGUILayout.PropertyField(Active);
                        EditorGUILayout.PropertyField(Input);
                        EditorGUILayout.PropertyField(CoolDown);
                        EditorGUILayout.PropertyField(ExitAfter);
                        EditorGUILayout.PropertyField(persistent);
                        EditorGUILayout.PropertyField(activeOnly);

                        using (new EditorGUI.DisabledGroupScope(!MainCollider.objectReferenceValue))
                        {
                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUILayout.PropertyField(OverrideCapsule);
                                if (OverrideCapsule.boolValue && GUILayout.Button(new GUIContent("C", "Copy Main Capsule values"), GUILayout.Width(25)))
                                {
                                    m.Stances[StanceIndex].newCapsule = new(m.MainCollider)
                                    {
                                        modify = CapsuleModifier.height | CapsuleModifier.radius | CapsuleModifier.center
                                    };
                                    EditorUtility.SetDirty(m);
                                }
                            }

                            if (OverrideCapsule.boolValue)
                                EditorGUILayout.PropertyField(newCapsule);
                        }

                        var stance = m.Stances[StanceIndex];
                        var StanceName = stance.ID != null ? stance.ID.name : "-EMPTY-";

                        EditorGUILayout.PropertyField(CanStrafe);

                        if (stance.CanStrafe.Value)
                        {
                            EditorGUILayout.PropertyField(IdleStrafe);
                            EditorGUILayout.PropertyField(MovementStrafe);
                        }


                        EditorGUILayout.PropertyField(states);


                        EditorGUILayout.PropertyField(StateQueue);

                        if (!stance.ActiveOnly)
                        {
                            EditorGUILayout.PropertyField(DisableStances);
                        }

                        if (Application.isPlaying && m.HasStances)
                        {
                            using (new EditorGUI.DisabledGroupScope(true))
                            {
                                EditorGUILayout.IntField("Temp Disable", stance.DisableValue);
                                EditorGUILayout.Toggle("Active", stance.Active);
                                EditorGUILayout.Toggle("Queued", stance.Queued);
                                EditorGUILayout.Toggle("CanExit", stance.CanExit);
                                Repaint();
                            }
                        }
                    }
                }
            }
        }
    }
}
#endif
