#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    // MWC - partial: Debug tab — runtime data display methods
    public partial class MAnimalEditor
    {
        public static void DrawDebugButton(SerializedProperty property, GUIContent name, Color Highlight)
        {
            var currentGUIColor = GUI.color;
            GUI.color = property.boolValue ? Highlight : currentGUIColor;
            property.boolValue = GUILayout.Toggle(property.boolValue, name, EditorStyles.miniButton);
            GUI.color = currentGUIColor;
        }

        #region Debug Stuff

        private void ShowDebug()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var Deb = serializedObject.FindProperty("debugStates");
                var DebG = serializedObject.FindProperty("debugGizmos");

                using (new GUILayout.HorizontalScope())
                {
                    var DebColor = Color.red + Color.white;

                    DrawDebugButton(Deb, new GUIContent(" States", "Activate debbuging on the States"), DebColor);
                    DrawDebugButton(DebugModes, new GUIContent(" Modes", "Activate debbuging on the Modes"), DebColor);
                    DrawDebugButton(DebugStances, new GUIContent(" Stances", "Activate debbuging on the Stances"), DebColor);
                    DrawDebugButton(DebG, new GUIContent(" Gizmos", "Show States and Modes Gizmos"), DebColor);
                    DrawDebugButton(showPivots, new GUIContent(" Pivots", "Show Animal Pivos"), DebColor);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("RUNTIME DATA", EditorStyles.boldLabel);

                    if (Application.isPlaying)
                    {

                        EditorGUIUtility.labelWidth = 120;
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("ShowOnGUIData"));
                        EditorGUIUtility.labelWidth = 00;
                    }
                }

                Runtime_Tabs1.intValue = GUILayout.Toolbar(Runtime_Tabs1.intValue, DebugTab1, EditorStyles.toolbarButton);
                if (Runtime_Tabs1.intValue != 4) Runtime_Tabs2.intValue = 4;

                Runtime_Tabs2.intValue = GUILayout.Toolbar(Runtime_Tabs2.intValue, DebugTab2, EditorStyles.toolbarButton);
                if (Runtime_Tabs2.intValue != 4) Runtime_Tabs1.intValue = 4;

            }


            if (Application.isPlaying)
            {

                using (new EditorGUI.DisabledGroupScope(true))
                {

                    switch (Runtime_Tabs1.intValue)
                    {
                        case 0: DrawDebugData(); break;
                        case 1: DrawStateData(); break; //State
                        case 2: DebugGroundData(); break; //Ground
                        case 3: DrawSpeeds(); break; //Speed
                        default: break;
                    }

                    switch (Runtime_Tabs2.intValue)
                    {
                        case 0: DebugInputData(); break; //Input
                        case 1: DebugModeData(); break; //Mode
                        case 2: DebugForcesData(); break;//Forces
                        case 3: DebugMoveData(); break; //Movement
                        default: break;
                    }
                    Repaint();
                }
            }
        }

        private void DrawSpeeds()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"RigidBody Horizontal Speed: [{m.HorizontalSpeed:F4}]", EditorStyles.boldLabel);
            }
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m.CurrentSpeedSet != null)
                    EditorGUILayout.LabelField
                        ($"Set: [{m.CurrentSpeedSet.name}] -  Speed: [{m.CurrentSpeedModifier.name}]. " +
                        $"Current Index: [{m.CurrentSpeedIndex}]", EditorStyles.boldLabel);
                DisplayActiveSpeed();
            }
        }

        private void DrawStateData()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField($"Active State: [{m.ActiveState.ID.name}] ({m.ActiveState.ID.ID})", m.ActiveState, typeof(State), false);

                var M = m.ActiveState;
                EditorGUILayout.ToggleLeft("Is Active State", M.IsActiveState);
                StateData(M);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (m.LastState)
                {
                    EditorGUILayout.ObjectField($"Last State: [{m.LastState.ID.name}] ({m.LastState.ID.ID})", m.LastState, typeof(State), false);
                    StateData(m.LastState);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField("Stance", m.Stance, typeof(StanceID), false);
                if (m.ActiveStance != null) EditorGUILayout.ToggleLeft("Stance Input", m.ActiveStance.InputValue);
            }
        }

        private void StateData(State M)
        {
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUIUtility.labelWidth = 50;
                using (new GUILayout.VerticalScope())
                {
                    EditorGUILayout.ToggleLeft("Enabled", M.Active);
                    EditorGUILayout.ToggleLeft("In Core Animation", M.InCoreAnimation);
                    EditorGUILayout.ToggleLeft("Can Exit", M.CanExit);
                    EditorGUILayout.ToggleLeft("OnQueue", M.OnQueue);
                    EditorGUILayout.ToggleLeft("On Active Queue", M.OnActiveQueue);
                    EditorGUILayout.ToggleLeft("Pending", M.IsPending);
                    EditorGUILayout.ToggleLeft("Always Forward", M.AlwaysForward);
                }
                using (new GUILayout.VerticalScope())
                {
                    EditorGUILayout.ToggleLeft("Sleep From State", M.IsSleepFromState);
                    EditorGUILayout.ToggleLeft("Sleep From Mode", M.IsSleepFromMode);
                    EditorGUILayout.ToggleLeft("Sleep From Stance", M.IsSleepFromStance);
                    EditorGUILayout.ToggleLeft("Ignore Lower States", M.IgnoreLowerStates);
                    EditorGUILayout.ToggleLeft("Is Persistent", M.IsPersistent);
                    EditorGUILayout.ToggleLeft("On Hold by Reset", M.OnHoldByReset);
                    EditorGUILayout.ToggleLeft("Input Value", M.InputValue);
                }
                EditorGUIUtility.labelWidth = 0;
            }
        }

        private void DebugMoveData()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.FloatField("HorizontalSpeed ", m.HorizontalSpeed);
                EditorGUILayout.Vector3Field("Horizontal Velocity ", m.HorizontalVelocity.Round(3) * m.DeltaTime);
                EditorGUILayout.Vector3Field("Inertia ", m.Inertia.Round(3));
                EditorGUILayout.Vector3Field("Inertia Speed ", m.InertiaPositionSpeed.Round(3));
                EditorGUILayout.Vector3Field("Target Speed ", m.TargetSpeed.Round(3));
                EditorGUILayout.Vector3Field("Pitch Direction", m.PitchDirection.Round(3));
                EditorGUILayout.Vector3Field("Delta Pos ", m.DeltaPos.Round(3));
                EditorGUILayout.Vector3Field("Delta RM ", m.DeltaRootMotion.Round(3));
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.Vector3Field("Raw Input Axis", m.RawInputAxis.Round(3));
                EditorGUILayout.Vector3Field("Movement Direction", m.Move_Direction.Round(3));
                EditorGUILayout.Vector3Field("Movement Axis Raw", m.MovementAxisRaw.Round(3));
                EditorGUILayout.Vector3Field("Movement Axis", m.MovementAxis.Round(3));
                EditorGUILayout.Vector3Field("Movement Smooth", m.MovementAxisSmoothed.Round(3));
                EditorGUILayout.Toggle("Disable Position", m.DisablePosition);
                EditorGUILayout.Toggle("Disable Rotation", m.DisableRotation);
            }
        }

        private void DebugInputData()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.FloatField("Delta Angle", m.DeltaAngle);
                EditorGUILayout.FloatField("Pitch Angle", m.PitchAngle);
                EditorGUILayout.FloatField("Bank", m.Bank);

                EditorGUILayout.Toggle("Rotate at Direction", m.Rotate_at_Direction);
                EditorGUILayout.Toggle("Move with Direction", m.UsingMoveWithDirection);
                EditorGUILayout.Toggle("Use Raw Input", m.UseRawInput);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.Vector3Field("Raw Input Axis", m.RawInputAxis.Round(3));
                EditorGUILayout.Vector3Field("Raw Rotate Axis", m.RawRotateDirAxis.Round(3));
                EditorGUILayout.Vector3Field("Movement Direction", m.Move_Direction.Round(3));
                EditorGUILayout.Vector3Field("Movement Axis Raw", m.MovementAxisRaw.Round(3));
                EditorGUILayout.Vector3Field("Movement Axis", m.MovementAxis.Round(3));
                EditorGUILayout.Vector3Field("Movement Smooth", m.MovementAxisSmoothed.Round(3));

            }
        }

        private void DebugGroundData()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField("Platform", m.platform, typeof(Transform), false);
                EditorGUILayout.ObjectField("Ground Changer", m.GroundChanger, typeof(GroundSpeedChanger), false);
                EditorGUILayout.FloatField("Terrain Slope", m.TerrainSlope);
                EditorGUILayout.FloatField("Main Pivot Slope", m.MainPivotSlope);
                EditorGUILayout.FloatField("Slope Normalized", m.SlopeNormalized);
                EditorGUILayout.FloatField("Slope Dir Angle", m.SlopeDirectionAngle);
                EditorGUILayout.FloatField("Slope Limit", m.SlopeLimit);
                EditorGUILayout.FloatField("Slope  Angle Difference", m.SlopeAngleDifference);
                EditorGUILayout.ToggleLeft("Deep Slope", m.DeepSlope);
                EditorGUILayout.ToggleLeft("Use Orient To Ground", m.UseOrientToGround);
                EditorGUILayout.Vector3Field("Slope Direction", m.SlopeDirection);
                EditorGUILayout.Vector3Field("Slope Direction Sm", m.SlopeDirectionSmooth);
                EditorGUILayout.Vector3Field("Surface Normal", m.SurfaceNormal);
            }
        }

        private void DebugForcesData()
        {
            EditorGUIUtility.labelWidth = 80;
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.FloatField("Gravity Time", m.GravityTime, GUILayout.MinWidth(50));
                    EditorGUILayout.FloatField("Gravity Mult", m.GravityMultiplier, GUILayout.MinWidth(50));
                }
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.FloatField("Delta Time", m.DeltaTime, GUILayout.MinWidth(50));
                    EditorGUILayout.FloatField("Time Scale", Time.timeScale, GUILayout.MinWidth(50));
                }
                EditorGUILayout.Space();

                EditorGUIUtility.labelWidth = 120;
                EditorGUILayout.Vector3Field("Gravity Velocity", m.GravityStoredVelocity);
                EditorGUILayout.Vector3Field("Gravity Offset", m.GravityOffset);
                EditorGUILayout.FloatField("Gravity ExPower", m.GravityExtraPower, GUILayout.MinWidth(50));
            }

            EditorGUIUtility.labelWidth = 0;

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.Vector3Field("External Force", m.CurrentExternalForce);
                EditorGUILayout.Vector3Field("External Force Max", m.ExternalForce);
                EditorGUILayout.FloatField("External Force Acel", m.ExternalForceAcel);
                EditorGUILayout.ToggleLeft("Force Air Control ?", m.ExternalForceAirControl);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.Vector3Field("Slope Direction", m.SlopeDirection);
                EditorGUILayout.Vector3Field("Slope Dir Smooth", m.SlopeDirectionSmooth);
                EditorGUILayout.FloatField("SlopeAngle", m.SlopeDirectionAngle);
            }
        }

        private void DebugModeData()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {

                    EditorGUILayout.ObjectField("Active Mode: ", m.IsPlayingMode ? m.ActiveMode.ID : null, typeof(ModeID), false);
                    EditorGUILayout.ObjectField("In Zone: ", m.InZone ? m.Zone.transform : null, typeof(Transform), false);


                    EditorGUILayout.TextField("Ability: ", (m.ActiveMode != null && m.ActiveMode.ActiveAbility != null) ?
                        "[" + m.ActiveMode.ActiveAbility.Index.Value + "]" + m.ActiveMode.ActiveAbility.Name : "");

                    EditorGUILayout.ToggleLeft("Ability Input Value  ", m.IsPlayingMode && m.ActiveMode.ActiveAbility.InputValue);

                }

                EditorGUIUtility.labelWidth = 70;
                using (new GUILayout.HorizontalScope())
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ToggleLeft("Input Value  ", m.IsPlayingMode && m.ActiveMode.InputValue);
                        EditorGUILayout.ToggleLeft("Playing Mode", m.IsPlayingMode);
                    }
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ToggleLeft("Preparing Mode", m.IsPreparingMode);
                        EditorGUILayout.ToggleLeft("Mode In Transition", m.ActiveMode != null && m.ActiveMode.IsInTransition);
                    }
                }

                EditorGUIUtility.labelWidth = 90;
                using (new GUILayout.HorizontalScope())
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (m.LastMode != null) EditorGUILayout.IntField("Last Mode ID", m.LastMode.ID);
                        EditorGUILayout.IntField("ModeID-Ability", m.ModeAbility);
                    }

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (m.LastMode != null) EditorGUILayout.IntField("Last Mode Ability", m.LastMode.AbilityIndex);
                        EditorGUILayout.FloatField("Mode Time", m.ModeTime);
                    }
                }
            }
            EditorGUIUtility.labelWidth = 0;
        }

        private void DrawDebugData()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ToggleLeft("In Timeline", m.InTimeline);
                EditorGUILayout.ToggleLeft("Using Camera Input", m.UseCameraInput);
                EditorGUILayout.ObjectField("In Zone: ", m.InZone ? m.Zone.transform : null, typeof(Transform), false);
                EditorGUILayout.IntField("Current Anim Tag", m.AnimStateTag);
                EditorGUILayout.FloatField("Strafe Delta", m.StrafeDeltaValue);
            }


            EditorGUIUtility.labelWidth = 70;
            using (new GUILayout.HorizontalScope())
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ToggleLeft("++ [Pos]", m.UseAdditivePos);
                    EditorGUILayout.ToggleLeft("RootMotion", m.RootMotion);
                    EditorGUILayout.ToggleLeft("RootMotion Rot", m.RootMotionRotation);
                    EditorGUILayout.ToggleLeft("Orient To Ground", m.UseOrientToGround);
                    EditorGUILayout.ToggleLeft("Chest Ray", m.FrontRay);
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ToggleLeft("++ [Rotation]", m.UseAdditiveRot);
                    EditorGUILayout.ToggleLeft("Grounded", m.Grounded);
                    EditorGUILayout.ToggleLeft("Use Custom Rot", m.UseCustomRotation);
                    EditorGUILayout.ToggleLeft("Strafe", m.Strafe);
                    EditorGUILayout.ToggleLeft("Hip Ray", m.MainRay);
                }
            }


            using (new GUILayout.HorizontalScope())
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ToggleLeft("Move with Direction", m.UsingMoveWithDirection);
                    EditorGUILayout.ToggleLeft("Raw Input", m.UseRawInput);
                    EditorGUILayout.ToggleLeft("Use Sprint", m.UseSprint);
                    EditorGUILayout.ToggleLeft("Input Locked", m.LockInput);
                    EditorGUILayout.ToggleLeft("Free Move", m.FreeMovement);
                }


                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.ToggleLeft("Rotate At Direction", m.Rotate_at_Direction);
                    EditorGUILayout.ToggleLeft("Movement Detected", m.MovementDetected);
                    EditorGUILayout.ToggleLeft("Sprint", m.Sprint);
                    EditorGUILayout.ToggleLeft("Movement Locked", m.LockMovement);
                    EditorGUILayout.ToggleLeft("Use Gravity", m.UseGravity);
                }
            }

            EditorGUIUtility.labelWidth = 0;
        }

        #endregion
    }
}
#endif
