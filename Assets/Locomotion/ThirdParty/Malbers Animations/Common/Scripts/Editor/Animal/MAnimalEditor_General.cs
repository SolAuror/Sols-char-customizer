#if UNITY_EDITOR
using MalbersAnimations.Utilities;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    // MWC - partial: General tab and Strafing section
    public partial class MAnimalEditor
    {
        private void ShowGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(S_PivotsList, true);

                if (S_PivotsList.isExpanded)
                {
                    //Draw Height
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(Height);

                        if (GUILayout.Button(new GUIContent("C", "Calculate Height and Animal Center"), GUILayout.Width(26)))
                        {
                            m.SetPivots();
                            m.CalculateCenter(true);
                        }
                    }

                    EditorGUILayout.PropertyField(pivotMultiplier);

                }
                EditorGUI.indentLevel--;


#if UNITY_EDITOR && !MALBERS_DEBUG
                EditorGUILayout.HelpBox("Go to the Menu Tools/Malbers Animations/Debug Gizmos [ON] to visualize the Pivots", MessageType.Warning);
#endif
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MainCollider.isExpanded = MalbersEditor.Foldout(MainCollider.isExpanded, "Colliders");

                if (MainCollider.isExpanded)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(MainCollider);
                        EditorGUILayout.PropertyField(MainColliderColor, GUIContent.none, GUILayout.Width(50));

                    }
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(colliders, new GUIContent("Internal Colliders"), true);
                    EditorGUI.indentLevel--;
                }
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                UseCameraInput.isExpanded = MalbersEditor.Foldout(UseCameraInput.isExpanded, "Movement");

                if (UseCameraInput.isExpanded)
                {
                    EditorGUILayout.PropertyField(UseCameraInput, new GUIContent("Camera Input", "The Animal uses the Camera Forward Direction to Move"));
                    EditorGUI.BeginChangeCheck();
                    {
                        EditorGUILayout.PropertyField(alwaysForward,
                            new GUIContent("Always Forward", "If true the animal will always go forward. useful for infinite runners"));
                    }
                    if (EditorGUI.EndChangeCheck() && Application.isPlaying && Application.isEditor)
                        m.AlwaysForward = m.AlwaysForward; //Update Always Forward Property on the Editor


                    EditorGUILayout.PropertyField(useCameraUp, new GUIContent("Use Camera Up", "Uses the Camera Up Vector to move UP or Down while flying or Swimming UnderWater. if this is false the Animal will need an UPDOWN Input to move higher or lower"));
                    EditorGUILayout.PropertyField(SmoothVertical, G_SmoothVertical);
                    EditorGUILayout.PropertyField(useSprintGlobal, G_useSprintGlobal);
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(TurnMultiplier);
                    EditorGUILayout.PropertyField(InPlaceDamp);
                    EditorGUILayout.PropertyField(TurnLimit);
                    EditorGUILayout.PropertyField(GlobalRootMotion);
                    EditorGUILayout.PropertyField(AnimatorSpeed);
                    EditorGUILayout.PropertyField(m_TimeMultiplier);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GroundLayer.isExpanded = MalbersEditor.Foldout(GroundLayer.isExpanded, "Ground");

                if (GroundLayer.isExpanded)
                {
                    EditorGUILayout.PropertyField(GroundLayer, G_GroundLayer);
                    EditorGUILayout.PropertyField(OrientToGround);
                    EditorGUILayout.PropertyField(DebreeTag);




                    //EditorGUILayout.PropertyField(TerrainSlopeLimit);
                    EditorGUILayout.PropertyField(SlopeLimit);
                    EditorGUILayout.PropertyField(SlideThreshold);
                    EditorGUILayout.PropertyField(SlideAmount);
                    EditorGUILayout.PropertyField(SlideDamp);
                    //EditorGUILayout.PropertyField(maxAngleSlope);
                    //EditorGUILayout.PropertyField(deepSlope);

                    Height.isExpanded = MalbersEditor.Foldout(Height.isExpanded, "Ground Alignment");
                    if (Height.isExpanded)
                    {
                        EditorGUILayout.PropertyField(AlignPosLerp, G_AlignPosLerp);
                        EditorGUILayout.PropertyField(AlignPosDelta, G_AlignPosDelta);
                        EditorGUILayout.PropertyField(AlignRotLerp, G_AlignRotLerp);
                        EditorGUILayout.PropertyField(AlignRotDelta, G_AlignRotDelta);
                        EditorGUILayout.PropertyField(RayCastRadius, G_RayCastRadius);
                        EditorGUILayout.PropertyField(useSphereCastAlign, new GUIContent("Use SphereCast", "Use a SphereCast (with the RayCast Radius) instead of a thin Raycast for the Ground Alignment. More robust on ledge lips, grates and small gaps")); //MWC: Tier3 opt-in
                        EditorGUILayout.PropertyField(AlignCycle);
                    }
                }
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                m_gravity.isExpanded = MalbersEditor.Foldout(m_gravity.isExpanded, "Gravity");

                if (m_gravity.isExpanded)
                {
                    //  EditorGUILayout.LabelField("Gravity", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(m_gravity, G_gravityDirection);
                    EditorGUILayout.PropertyField(m_gravityPower, G_GravityForce);
                    EditorGUILayout.PropertyField(m_gravityTime, G_GravityCycle);
                    EditorGUILayout.PropertyField(m_ClampGravitySpeed);
                    EditorGUILayout.PropertyField(ground_Changes_Gravity, new GUIContent("Ground Changes Gravity", "The Ground will change the gravity direction, allowing the animals to move in any surface"));
                }
            }
            ShowStrafingVars();
        }

        private void ShowStrafingVars()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m_CanStrafe.isExpanded = MalbersEditor.Foldout(m_CanStrafe.isExpanded, "Strafing"))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(m_CanStrafe, G_CanStrafe);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (m.Aimer == null)
                            {
                                m.Aimer = m.FindComponent<Aim>();
                                if (m.Aimer == null)
                                {
                                    m.Aimer = m.gameObject.AddComponent<Aim>();
                                }
                                EditorUtility.SetDirty(m);
                            }
                        }

                        if (GUILayout.Button("?", GUILayout.Width(20)))
                        {
                            Application.OpenURL("https://malbersanimations.gitbook.io/animal-controller/strafing");
                        }
                    }

                    if (m.CanStrafe)
                    {
                        EditorGUILayout.PropertyField(m_strafe, G_Strafe);
                        EditorGUILayout.PropertyField(m_StrafeNormalize, G_StrafeNormalize);
                        EditorGUILayout.PropertyField(m_StrafeLerp, G_StrafeLerp);
                    }
                }
            }
        }
    }
}
#endif
