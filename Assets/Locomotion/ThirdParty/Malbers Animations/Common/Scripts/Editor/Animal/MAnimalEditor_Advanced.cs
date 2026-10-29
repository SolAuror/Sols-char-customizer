// Last Change: 9/24/2026 1:16pm
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    // MWC - partial: Advanced, Speeds, and Events tabs
    public partial class MAnimalEditor
    {
        private void ShowAdvanced()
        {

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Anim.isExpanded = MalbersEditor.Foldout(Anim.isExpanded, "References"))
                {
                    EditorGUILayout.PropertyField(Anim, new GUIContent("Animator"));
                    EditorGUILayout.PropertyField(RB, new GUIContent("RigidBody"));
                    EditorGUILayout.PropertyField(UseMainCameraDirection);

                    EditorGUILayout.PropertyField(MainCamera, new GUIContent("Main Camera"));
                    EditorGUILayout.PropertyField(Aimer);
                    EditorGUILayout.PropertyField(DefaultPlatform);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (Rotator.isExpanded = MalbersEditor.Foldout(Rotator.isExpanded, "Free Movement"))
                {
                    EditorGUILayout.PropertyField(Rotator, G_Rotator);
                    EditorGUILayout.PropertyField(RootBone, G_RootBone);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (sleep.isExpanded = MalbersEditor.Foldout(sleep.isExpanded, "Lock Inputs"))
                {
                    EditorGUILayout.PropertyField(sleep, new GUIContent("Sleep", "Disable internally the Controller without disabling the component"));
                    EditorGUILayout.PropertyField(lockInput);
                    EditorGUILayout.PropertyField(lockMovement);
                    EditorGUILayout.PropertyField(LockForwardMovement, new GUIContent("Lock Forward"));
                    EditorGUILayout.PropertyField(LockHorizontalMovement, new GUIContent("Lock Horizontal"));
                    EditorGUILayout.PropertyField(LockUpDownMovement, new GUIContent("Lock UpDown"));
                }
            }
            ShowAnimParam();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                animalType.isExpanded = MalbersEditor.Foldout(animalType.isExpanded, "Extras");

                if (animalType.isExpanded)
                {
                    // EditorGUILayout.PropertyField(NoParent);
                    EditorGUILayout.PropertyField(animalType, G_animalType);
                    EditorGUILayout.PropertyField(kinematicTimeline);
                    EditorGUILayout.PropertyField(AnimalMaterial);
                }
            }
        }

        /// <summary> Draws the Required and Optional Animator Parameter names and checks them against the Animator </summary>
        private void ShowAnimParam()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var v_float = UnityEngine.AnimatorControllerParameterType.Float;
                var v_int = UnityEngine.AnimatorControllerParameterType.Int;
                var v_bool = UnityEngine.AnimatorControllerParameterType.Bool;
                var v_trigger = UnityEngine.AnimatorControllerParameterType.Trigger;
                var anim = m.Anim;

                using (new GUILayout.HorizontalScope())
                {

                    m_Vertical.isExpanded = MalbersEditor.Foldout(m_Vertical.isExpanded, "Required Animator Parameters");

                    if (m_Vertical.isExpanded)
                    {
                        if (GUILayout.Button(new GUIContent("*", "Check Required Parameters"), GUILayout.Width(20), GUILayout.Height(20)))
                        {
                            MalbersEditor.CheckAnimParameter(anim, m_StateOn.stringValue, v_trigger);
                            MalbersEditor.CheckAnimParameter(anim, m_ModeOn.stringValue, v_trigger);
                            MalbersEditor.CheckAnimParameter(anim, m_Vertical.stringValue, v_float);

                            MalbersEditor.CheckAnimParameter(anim, m_Horizontal.stringValue, v_float);
                            MalbersEditor.CheckAnimParameter(anim, m_State.stringValue, v_int);
                            MalbersEditor.CheckAnimParameter(anim, m_LastState.stringValue, v_int);
                            MalbersEditor.CheckAnimParameter(anim, m_StateStatus.stringValue, v_int);
                            MalbersEditor.CheckAnimParameter(anim, m_StateFloat.stringValue, v_float);
                            MalbersEditor.CheckAnimParameter(anim, m_Mode.stringValue, v_int);
                            MalbersEditor.CheckAnimParameter(anim, m_ModeStatus.stringValue, v_int);
                            MalbersEditor.CheckAnimParameter(anim, m_Grounded.stringValue, v_bool);
                            MalbersEditor.CheckAnimParameter(anim, m_Movement.stringValue, v_bool);
                        }
                    }
                }

                if (m_Vertical.isExpanded)
                {
                    MalbersEditor.DisplayParam(anim, m_StateOn, v_trigger);
                    MalbersEditor.DisplayParam(anim, m_ModeOn, v_trigger);
                    EditorGUILayout.Space();
                    MalbersEditor.DisplayParam(anim, m_Vertical, v_float);
                    MalbersEditor.DisplayParam(anim, m_Horizontal, v_float);
                    EditorGUILayout.Space();
                    MalbersEditor.DisplayParam(anim, m_State, v_int);
                    MalbersEditor.DisplayParam(anim, m_LastState, v_int);
                    MalbersEditor.DisplayParam(anim, m_StateStatus, v_int);
                    MalbersEditor.DisplayParam(anim, m_StateFloat, v_float);
                    EditorGUILayout.Space();
                    MalbersEditor.DisplayParam(anim, m_Mode, v_int);
                    MalbersEditor.DisplayParam(anim, m_ModeStatus, v_int);
                    EditorGUILayout.Space();
                    MalbersEditor.DisplayParam(anim, m_Grounded, v_bool);
                    MalbersEditor.DisplayParam(anim, m_Movement, v_bool);
                }

                m_UpDown.isExpanded = MalbersEditor.Foldout(m_UpDown.isExpanded, "Optional Animator Parameters");

                if (m_UpDown.isExpanded)
                {
                    MalbersEditor.DisplayParam(anim, m_SpeedMultiplier, v_float);
                    MalbersEditor.DisplayParam(anim, m_UpDown, v_float);
                    MalbersEditor.DisplayParam(anim, m_DeltaUpDown, v_float);
                    MalbersEditor.DisplayParam(anim, m_VerticalRaw, v_float);
                    MalbersEditor.DisplayParam(anim, m_TargetAngle, v_float);
                    MalbersEditor.DisplayParam(anim, m_Sprint, v_bool);
                    EditorGUILayout.Space();

                    MalbersEditor.DisplayParam(anim, m_StateProfile, v_int);
                    MalbersEditor.DisplayParam(anim, m_StateExitStatus, v_int);
                    MalbersEditor.DisplayParam(anim, m_StateTime, v_float);
                    EditorGUILayout.Space();

                    MalbersEditor.DisplayParam(anim, m_Stance, v_int);
                    MalbersEditor.DisplayParam(anim, m_LastStance, v_int);

                    EditorGUILayout.Space();

                    MalbersEditor.DisplayParam(anim, m_ModePower, v_float);
                    MalbersEditor.DisplayParam(anim, m_ModeLayer, v_int);
                    EditorGUILayout.Space();

                    MalbersEditor.DisplayParam(anim, m_Slope, v_float);
                    MalbersEditor.DisplayParam(anim, m_Random, v_int);
                    MalbersEditor.DisplayParam(anim, m_StrafeAnim, v_bool);
                    //   DisplayParam(m_TargetHorizontal, v_float);
                    EditorGUILayout.Space();
                    MalbersEditor.DisplayParam(anim, m_Type, v_int);
                }
            }
        }

        private void ShowEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);

                Editor_EventTabs.intValue = GUILayout.Toolbar(Editor_EventTabs.intValue,
                    new string[] { "Movement", "State", "Stance", "Modes", "Speeds", "Extras" }, EditorStyles.toolbarButton);



                switch (Editor_EventTabs.intValue)
                {
                    case 0: //Movement

                        using (var X = new GUILayout.ScrollViewScope(ScrollEvents, GUILayout.MaxHeight(500)))
                        {
                            ScrollEvents = X.scrollPosition;

                            EditorGUILayout.PropertyField(OnSprintEnabled, new GUIContent("On Sprint"));
                            EditorGUILayout.PropertyField(OnMovementDetected);
                            EditorGUILayout.PropertyField(OnFreeMovement);
                            EditorGUILayout.PropertyField(OnGrounded);
                            EditorGUILayout.Space();

                            if (m.CanStrafe)
                            {
                                EditorGUILayout.PropertyField(OnStrafe);
                            }
                            //EditorGUILayout.PropertyField(OnMaxSlopeReached);
                            EditorGUILayout.PropertyField(OnPreTeleport);
                            EditorGUILayout.PropertyField(OnTeleport);
                            EditorGUILayout.PropertyField(OnGroundChangesGravity);
                            EditorGUILayout.Space();
                        }
                        break;
                    case 1: //States
                        EditorGUILayout.PropertyField(OnStateChange);
                        EditorGUILayout.PropertyField(OnStateProfile);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(OnEnterExitStates);
                        EditorGUI.indentLevel--;
                        break;
                    case 2: //Stances
                        EditorGUILayout.PropertyField(OnStanceChange);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(OnEnterExitStances);
                        EditorGUI.indentLevel--;
                        break;
                    case 3: //Modes
                        EditorGUILayout.PropertyField(OnModeStart);
                        EditorGUILayout.PropertyField(OnModeEnd);
                        EditorGUILayout.Space();

                        for (int i = 0; i < S_Mode_List.arraySize; i++)
                        {
                            var SelectedMode = S_Mode_List.GetArrayElementAtIndex(i);

                            var ID = SelectedMode.FindPropertyRelative("ID").objectReferenceValue;
                            var ModeName = ID != null ? ID.name : "";


                            var expanded = SelectedMode.FindPropertyRelative("allowRotation");

                            expanded.isExpanded = MalbersEditor.Foldout(expanded.isExpanded, $"Mode [{ModeName}] Events");

                            if (expanded.isExpanded)
                            {
                                var OnEnterMode = SelectedMode.FindPropertyRelative("OnEnterMode");
                                var OnExitMode = SelectedMode.FindPropertyRelative("OnExitMode");
                                EditorGUILayout.PropertyField(OnEnterMode, new GUIContent($"On [{ModeName}] Enter "));
                                EditorGUILayout.PropertyField(OnExitMode, new GUIContent($"On [{ModeName}] Exit"));
                                EditorGUILayout.PropertyField(expanded, new GUIContent($"On [{ModeName}] Active Ability Index changed"));
                            }
                        }
                        break;
                    case 4:
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(OnEnterExitSpeeds);
                        EditorGUI.indentLevel--;
                        EditorGUILayout.PropertyField(OnSpeedChange);
                        break;

                    case 5:
                        EditorGUILayout.PropertyField(OnMovementLocked);
                        EditorGUILayout.PropertyField(OnInputLocked);
                        EditorGUILayout.Space();

                        EditorGUILayout.PropertyField(OnAnimationChange);
                        break;
                    default:
                        break;
                }
                EditorGUI.indentLevel--;
            }
        }

        private void ShowSpeeds()
        {
            int speedTabs = SpeedTabs.intValue;
            MSpeedEditor.ShowSpeeds(Reo_List_Speeds, m.speedSets, SelectedSpeed.intValue, ref speedTabs);
            SpeedTabs.intValue = speedTabs;

            DisplayActiveSpeed();
        }

        private void DisplayActiveSpeed()
        {
            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.LabelField("Active Speed Modifier", EditorStyles.boldLabel);
                    EditorGUILayout.IntField("Current Index", m.CurrentSpeedIndex);
                    EditorGUILayout.Toggle("Locked Speed", m.CurrentSpeedSet.LockSpeed);
                    EditorGUILayout.Toggle("Using Custom Speed", m.CustomSpeed);

                    EditorGUILayout.LabelField($"Current Speed Modifier: [{m.CurrentSpeedModifier.Name}]");
                    var cpM = serializedObject.FindProperty("currentSpeedModifier");
                    var cSprintSpeed = serializedObject.FindProperty("SprintSpeed");
                    cpM.isExpanded = true;
                    cSprintSpeed.isExpanded = true;

                    if (m.Sprint && !m.CustomSpeed)
                        EditorGUILayout.PropertyField(cSprintSpeed, true);
                    else
                        EditorGUILayout.PropertyField(cpM, true);

                    // EditorGUILayout.LabelField($"SprintSpeed: {m.SprintSpeed.name}", EditorStyles.boldLabel);
                }


            }
        }

        #region Draw Speeds

        private void Draw_Header_Speed(Rect rect)
        {
            var height = EditorGUIUtility.singleLineHeight;
            var nameRec = new Rect(rect.x + 30, rect.y, rect.width / 2, height);

            EditorGUI.LabelField(nameRec, "Speed Sets");

            Rect R_1 = new(rect.width + 5, rect.y, 20, EditorGUIUtility.singleLineHeight);

            if (GUI.Button(R_1, "?"))
                Application.OpenURL("https://malbersanimations.gitbook.io/animal-controller/main-components/manimal-controller/speeds");

        }

        private void OnRemoveCallback_Speeds(ReorderableList list)
        {
            S_Speed_List.DeleteArrayElementAtIndex(list.index);
            list.index = -1;
            SelectedSpeed.intValue = -1;
            EditorUtility.SetDirty(m);
        }

        private void OnAddCallback_Speeds(ReorderableList reo_List_Speeds)
        {
            if (m.speedSets == null) m.speedSets = new List<MSpeedSet>();

            m.speedSets.Add(new MSpeedSet());

            EditorUtility.SetDirty(m);
        }

        private void Draw_Element_Speed(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (S_Speed_List.arraySize <= index) return;

            var nameRect = new Rect(rect);
            var IndexRect = new Rect(rect);

            nameRect.y += 1;
            nameRect.x += 25;
            nameRect.height = EditorGUIUtility.singleLineHeight;
            IndexRect.height = EditorGUIUtility.singleLineHeight;
            IndexRect.width = 20f;

            Rect activeRect = new(nameRect);

            var speedSet = S_Speed_List.GetArrayElementAtIndex(index);
            var nameSpeedSet = speedSet.FindPropertyRelative("name");
            nameRect.width /= 2;
            nameRect.width += 10;
            EditorGUI.LabelField(IndexRect, $"[{index}]");
            EditorGUI.PropertyField(nameRect, nameSpeedSet, GUIContent.none);

            activeRect.x = rect.width / 2 + 80;
            activeRect.width = rect.width / 2 - 40;

            if (Application.isPlaying)
            {
                if (m.speedSets[index] == m.CurrentSpeedSet)
                {
                    EditorGUI.LabelField(activeRect, "  (" + m.CurrentSpeedModifier.name + ")", EditorStyles.boldLabel);
                }
            }
        }
        #endregion
    }
}
#endif
