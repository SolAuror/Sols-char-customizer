// Last Change: 9/17/2026 5:33pm
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;


namespace MalbersAnimations.Controller
{
    // MWC - split into partial classes: _General, _States, _Stances, _Modes, _Advanced, _Debug
    [CustomEditor(typeof(MAnimal))]
    public partial class MAnimalEditor : Editor
    {
        // MWC - single version constant; ACUpdateChecker reads this so both stay in sync automatically
        public const string Version = "1.5.3b"; // MWC — bumped for the Super Item release
        public readonly string version = $"Animal Controller [{Version}]";

        private List<Type> StatesType = new();
        private ReorderableList Reo_List_States;
        private ReorderableList Reo_List_Modes;
        private ReorderableList Reo_List_Stances;
        private ReorderableList Reo_List_Speeds;

        private readonly Dictionary<string, ReorderableList> Reo_Abilities = new();

        private readonly Dictionary<string, Editor> State_Editor = new();

        public List<string> Warnings;

        private GUIStyle DescStyle;

        #region Serialized Properties
        private SerializedProperty
            S_State_List, S_PivotsList, Height, pivotMultiplier,
            S_Mode_List, UseMainCameraDirection,

            Editor_Tabs1, Editor_Tabs2, Runtime_Tabs1, Runtime_Tabs2, GlobalRootMotion,


            StartWithMode,
            OnEnterExitStances, OnEnterExitStates, OnEnterExitSpeeds,
            RB, Anim,
            m_Vertical, m_VerticalRaw, m_Horizontal, m_StateFloat, m_ModeStatus, m_State, m_StateStatus, m_StateExitStatus,
            m_LastState, m_Mode, m_Grounded, m_Movement, m_Random, m_ModePower, m_ModeLayer,
            m_SpeedMultiplier, m_UpDown, m_DeltaUpDown, m_StateOn, m_StateProfile, m_Sprint, m_ModeOn,// m_StanceOn,
            currentStance, defaultStance, Stances_List,
            m_Stance, m_LastStance, m_Slope, m_Type, m_StateTime, m_TargetAngle, m_StrafeAnim,
            lockInput, lockMovement, Rotator, AlignCycle, animalType, kinematicTimeline,
            RayCastRadius, useSphereCastAlign, MainCamera, sleep, m_gravityTime, m_ClampGravitySpeed, RootBone, //MWC: added useSphereCastAlign toggle

             m_CanStrafe, Aimer, m_strafe, OnStrafe, OnFreeMovement, OnGroundChangesGravity,
            m_StrafeNormalize,  /*FallForward, */m_StrafeLerp, OrientToGround,

            MainCollider, MainColliderColor, colliders,

            alwaysForward, AnimatorSpeed, m_TimeMultiplier,
            OnMovementLocked, OnMovementDetected, //OnMaxSlopeReached,
            OnInputLocked, OnSprintEnabled, OnGrounded, OnStanceChange, OnStateChange, OnStateProfile, OnModeStart, OnModeEnd,
            OnTeleport, OnPreTeleport,
            OnSpeedChange, OnAnimationChange, GroundLayer, AlignPosLerp, AlignPosDelta, AlignRotDelta,
            AlignRotLerp, m_gravity, m_gravityPower, useCameraUp, ground_Changes_Gravity,
             useSprintGlobal, SmoothVertical,
            TurnMultiplier, TurnLimit, InPlaceDamp,
            OverrideStartState, CloneStates, S_Speed_List, UseCameraInput,// TerrainSlopeLimit,
            SlopeLimit, SlideThreshold, SlideAmount, SlideDamp, AnimalMaterial,

            DefaultPlatform
            ,
             //maxAngleSlope,  deepSlope,

             states_C,
            LockUpDownMovement, LockHorizontalMovement, LockForwardMovement, DebreeTag;

        //EditorStuff
        private SerializedProperty
             ShowStateInInspector, Ability_Tabs, Mode_Tabs1, DebugModes, DebugStances, DebugStates,
              SelectedMode, SelectedStance, SelectedState, showPivots,
                Editor_EventTabs, ShowOnPlay, showModeList, showStateList
        ;
        #endregion

        private Vector2 ScrollEvents;


        private MAnimal m;
        // private MonoScript script;
        private GenericMenu addMenu;
        private GUIStyle AbilityStyleDesc;

        // MWC - moved field declarations here from mid-file positions
        private static readonly GUIContent Icon_Include;
        private static readonly GUIContent Icon_Exclude;

        /// <summary> Cached style to use to draw the popup button. </summary>
        private GUIStyle popupStyle;

        private SerializedProperty SpeedTabs, SelectedSpeed;

        private void FindSerializedProperties()
        {
            DebugModes = serializedObject.FindProperty("debugModes");
            DebugStances = serializedObject.FindProperty("debugStances");
            DebugStates = serializedObject.FindProperty("debugStates");
            UseMainCameraDirection = serializedObject.FindProperty("UseMainCameraDirection");


            MainCollider = serializedObject.FindProperty("MainCollider");
            MainColliderColor = serializedObject.FindProperty("MainColliderColor");
            colliders = serializedObject.FindProperty("colliders");

            //Modes
            showModeList = serializedObject.FindProperty("showModeList");
            showStateList = serializedObject.FindProperty("showStateList");
            Mode_Tabs1 = serializedObject.FindProperty("Mode_Tabs1");
            Ability_Tabs = serializedObject.FindProperty("Ability_Tabs");
            SelectedMode = serializedObject.FindProperty("SelectedMode");
            GlobalRootMotion = serializedObject.FindProperty("GlobalRootMotion");


            ShowOnPlay = serializedObject.FindProperty("ShowOnPlay");
            S_PivotsList = serializedObject.FindProperty("pivots");
            sleep = serializedObject.FindProperty("sleep");
            S_Mode_List = serializedObject.FindProperty("modes");

            ground_Changes_Gravity = serializedObject.FindProperty("ground_Changes_Gravity");
            Stances_List = serializedObject.FindProperty("Stances");
            states_C = serializedObject.FindProperty("states_C");


            AnimalMaterial = serializedObject.FindProperty("AnimalMaterial");

            DefaultPlatform = serializedObject.FindProperty("defaultPlatform");
            DebreeTag = serializedObject.FindProperty("DebrisTag");
            SelectedState = serializedObject.FindProperty("SelectedState");
            SelectedStance = serializedObject.FindProperty("SelectedStance");
            ShowStateInInspector = serializedObject.FindProperty("ShowStateInInspector");

            m_CanStrafe = serializedObject.FindProperty("m_CanStrafe");
            m_StrafeNormalize = serializedObject.FindProperty("m_StrafeNormalize");
            m_strafe = serializedObject.FindProperty("m_strafe");
            Aimer = serializedObject.FindProperty("Aimer");
            OnStrafe = serializedObject.FindProperty("OnStrafe");
            m_StrafeLerp = serializedObject.FindProperty("m_StrafeLerp");

            alwaysForward = serializedObject.FindProperty("alwaysForward");

            MainCamera = serializedObject.FindProperty("m_MainCamera");


            S_Speed_List = serializedObject.FindProperty("speedSets");

            currentStance = serializedObject.FindProperty("currentStance");
            defaultStance = serializedObject.FindProperty("defaultStance");


            RB = serializedObject.FindProperty("RB");
            Anim = serializedObject.FindProperty("Anim");

            UseCameraInput = serializedObject.FindProperty("useCameraInput");
            useCameraUp = serializedObject.FindProperty("useCameraUp");
            StartWithMode = serializedObject.FindProperty("StartWithMode");

            OnEnterExitStates = serializedObject.FindProperty("OnEnterExitStates");
            OnEnterExitStances = serializedObject.FindProperty("OnEnterExitStances");
            OnEnterExitSpeeds = serializedObject.FindProperty("OnEnterExitSpeeds");

            Height = serializedObject.FindProperty("height");
            pivotMultiplier = serializedObject.FindProperty(nameof(m.m_pivotMultiplier));

            // ModeIndexSelected = serializedObject.FindProperty("ModeIndexSelected");

            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            Editor_Tabs2 = serializedObject.FindProperty("Editor_Tabs2");

            Runtime_Tabs1 = serializedObject.FindProperty("Runtime_Tabs1");
            Runtime_Tabs2 = serializedObject.FindProperty("Runtime_Tabs2");

            m_Vertical = serializedObject.FindProperty("m_Vertical");
            m_VerticalRaw = serializedObject.FindProperty("m_VerticalRaw");
            // Center = serializedObject.FindProperty("Center");
            m_Horizontal = serializedObject.FindProperty("m_Horizontal");
            m_StateFloat = serializedObject.FindProperty("m_StateFloat");
            m_ModeStatus = serializedObject.FindProperty("m_ModeStatus");
            m_State = serializedObject.FindProperty("m_State");
            m_StateStatus = serializedObject.FindProperty("m_StateStatus");
            m_StateExitStatus = serializedObject.FindProperty("m_StateExitStatus");
            m_LastState = serializedObject.FindProperty("m_LastState");
            m_Mode = serializedObject.FindProperty("m_Mode");
            m_Grounded = serializedObject.FindProperty("m_Grounded");
            m_Movement = serializedObject.FindProperty("m_Movement");
            m_Random = serializedObject.FindProperty("m_Random");
            m_ModePower = serializedObject.FindProperty("m_ModePower");
            m_ModeLayer = serializedObject.FindProperty("m_ModeLayer");
            m_StrafeAnim = serializedObject.FindProperty("m_Strafe");
            m_SpeedMultiplier = serializedObject.FindProperty("m_SpeedMultiplier");

            m_UpDown = serializedObject.FindProperty("m_UpDown");
            m_DeltaUpDown = serializedObject.FindProperty("m_DeltaUpDown");

            m_StateOn = serializedObject.FindProperty("m_StateOn");
            m_StateProfile = serializedObject.FindProperty("m_StateProfile");
            //m_StanceOn = serializedObject.FindProperty("m_StanceOn");
            m_ModeOn = serializedObject.FindProperty("m_ModeOn");
            m_Sprint = serializedObject.FindProperty("m_Sprint");

            m_Stance = serializedObject.FindProperty("m_Stance");
            m_LastStance = serializedObject.FindProperty("m_LastStance");

            m_Slope = serializedObject.FindProperty("m_Slope");
            m_Type = serializedObject.FindProperty("m_Type");
            m_StateTime = serializedObject.FindProperty("m_StateTime");
            m_TargetAngle = serializedObject.FindProperty("m_DeltaAngle");
            lockInput = serializedObject.FindProperty("lockInput");
            lockMovement = serializedObject.FindProperty("lockMovement");
            Rotator = serializedObject.FindProperty("Rotator");
            animalType = serializedObject.FindProperty("animalType");
            kinematicTimeline = serializedObject.FindProperty("kinematicTimeline");
            RayCastRadius = serializedObject.FindProperty("rayCastRadius");
            useSphereCastAlign = serializedObject.FindProperty("useSphereCastAlign"); //MWC: opt-in SphereCast ground detection toggle
            AlignCycle = serializedObject.FindProperty("AlignCycle");
            AnimatorSpeed = serializedObject.FindProperty("AnimatorSpeed");
            m_TimeMultiplier = serializedObject.FindProperty("m_TimeMultiplier");
            // m_TargetHorizontal = serializedObject.FindProperty("m_TargetHorizontal");

            LockForwardMovement = serializedObject.FindProperty("lockForwardMovement");
            LockHorizontalMovement = serializedObject.FindProperty("lockHorizontalMovement");
            LockUpDownMovement = serializedObject.FindProperty("lockUpDownMovement");



            //OnMaxSlopeReached = serializedObject.FindProperty("OnMaxSlopeReached");
            OnMovementLocked = serializedObject.FindProperty("OnMovementLocked");
            OnMovementDetected = serializedObject.FindProperty("OnMovementDetected");
            OnInputLocked = serializedObject.FindProperty("OnInputLocked");
            OnSprintEnabled = serializedObject.FindProperty("OnSprintEnabled");
            OnGrounded = serializedObject.FindProperty("OnGrounded");
            OnStanceChange = serializedObject.FindProperty("OnStanceChange");
            OnStateChange = serializedObject.FindProperty("OnStateChange");
            OnStateProfile = serializedObject.FindProperty("OnStateProfile");
            OnModeStart = serializedObject.FindProperty("OnModeStart");
            OnFreeMovement = serializedObject.FindProperty("OnFreeMovement");
            OnGroundChangesGravity = serializedObject.FindProperty("OnGroundChangesGravity");

            OnModeEnd = serializedObject.FindProperty("OnModeEnd");
            OnSpeedChange = serializedObject.FindProperty("OnSpeedChange");
            OnTeleport = serializedObject.FindProperty("OnTeleport");
            OnPreTeleport = serializedObject.FindProperty("OnPreTeleport");
            OnAnimationChange = serializedObject.FindProperty("OnAnimationChange");


            showPivots = serializedObject.FindProperty("showPivots");
            GroundLayer = serializedObject.FindProperty("groundLayer");

            //TerrainSlopeLimit = serializedObject.FindProperty("TerrainSlopeLimit");
            SlopeLimit = serializedObject.FindProperty("SlopeLimit");
            SlideThreshold = serializedObject.FindProperty("slideThreshold");
            SlideThreshold = serializedObject.FindProperty("slideThreshold");
            SlideAmount = serializedObject.FindProperty("slideAmount");
            SlideDamp = serializedObject.FindProperty("slideDamp");


            AlignPosLerp = serializedObject.FindProperty("AlignPosLerp");
            OrientToGround = serializedObject.FindProperty("m_OrientToGround");
            AlignPosDelta = serializedObject.FindProperty("AlignPosDelta");
            AlignRotDelta = serializedObject.FindProperty("AlignRotDelta");
            AlignRotLerp = serializedObject.FindProperty("AlignRotLerp");


            m_gravity = serializedObject.FindProperty("m_gravityDir");
            m_gravityPower = serializedObject.FindProperty("m_gravityPower");
            m_gravityTime = serializedObject.FindProperty("m_gravityTime");
            m_ClampGravitySpeed = serializedObject.FindProperty("m_clampGravitySpeed");

            useSprintGlobal = serializedObject.FindProperty("useSprintGlobal");
            SmoothVertical = serializedObject.FindProperty("SmoothVertical");
            TurnMultiplier = serializedObject.FindProperty("TurnMultiplier");
            TurnLimit = serializedObject.FindProperty("TurnLimit");
            InPlaceDamp = serializedObject.FindProperty("inPlaceDamp");


            OverrideStartState = serializedObject.FindProperty("OverrideStartState");
            CloneStates = serializedObject.FindProperty("CloneStates");
            RootBone = serializedObject.FindProperty("RootBone");
            Editor_EventTabs = serializedObject.FindProperty("Editor_EventTabs");


            SelectedSpeed = serializedObject.FindProperty("SelectedSpeed");
            SpeedTabs = serializedObject.FindProperty("SpeedTabs");

        }

        protected virtual void OnEnable()
        {
            m = (MAnimal)target;
            FindSerializedProperties();
            StatesType.Clear();
            StatesType = MTools.GetAllTypes<State>();

            S_State_List = serializedObject.FindProperty("states");

            Reo_List_States = new ReorderableList(serializedObject, S_State_List, true, true, true, true)
            {
                drawHeaderCallback = Draw_Header_State,
                drawElementCallback = Draw_Element_State,
                onReorderCallbackWithDetails = OnReorderCallback_States,
                onAddCallback = OnAddCallback_State,
                onRemoveCallback = OnRemove_State,
                onSelectCallback = Selected_State,
            };

            Reo_List_Modes = new ReorderableList(serializedObject, S_Mode_List, true, true, true, true)
            {
                drawElementCallback = Draw_Element_Modes,
                drawHeaderCallback = Draw_Header_Modes,
                onAddCallback = OnAdd_Modes,
                onRemoveCallback = OnRemoveCallback_Mode,
                onSelectCallback = Selected_Mode,
                onReorderCallback = (list) => { ModeArray_Popup(); }
            };

            Reo_List_Speeds = new ReorderableList(serializedObject, S_Speed_List, true, true, true, true)
            {
                drawElementCallback = Draw_Element_Speed,
                drawHeaderCallback = Draw_Header_Speed,
                onAddCallback = OnAddCallback_Speeds,
                onRemoveCallback = OnRemoveCallback_Speeds,

                onSelectCallback = (list) =>
                {
                    SelectedSpeed.intValue = list.index;
                }

            };

            Reordable_Stances();

            Reo_List_States.index = SelectedState.intValue;
            Reo_List_Modes.index = SelectedMode.intValue;

            UpdateCacheState();

            StateArray_Popup();
            ModeArray_Popup();

            CheckWarnings();
        }

        private void CheckWarnings()
        {
            Warnings = new();

            //Check missing IDs on Modes
            foreach (var m in m.modes)
            {
                if (m.ID == null)
                {
                    Warnings.Add("⚠ There is a missing ID in the Mode List. Please review your modes. IDs cannot be null");
                    break;
                }
            }

            //Check Missing IDs on Stances
            foreach (var stance in m.Stances)
            {
                if (stance.ID == null)
                {
                    Warnings.Add("⚠ There is a missing ID in the Stance List. Please review your stances. IDs cannot be null");
                }
            }

            //check missing states
            foreach (var state in m.states)
            {
                if (state == null)
                {
                    Warnings.Add("⚠ There is a missing State in the State List. Please review your states. states cannot be null");
                    break;
                }
            }


            //check missing IDs on States
            foreach (var state in m.states)
            {
                if (state != null && state.ID == null)
                {
                    Warnings.Add("⚠ There is a missing ID in the State List. Please review your states. IDs cannot be null");
                    break;
                }
            }

            //Check colliders that have the same layer as the ground layer

            var allColliders = m.GetComponentsInChildren<Collider>(true);

            foreach (var col in allColliders)
            {
                if (col != null && MTools.Layer_in_LayerMask(col.gameObject.layer, m.groundLayer.Value))
                {
                    Warnings.Add($"⚠ Collider [{col.name}] is on the same layer as the [Ground Layer].\nThis causes issues with ground detection. Please change the layer of this collider or the ground layer.");
                }
            }

            if (m.DefaultStanceID == null) Warnings.Add("⚠ The <Default Stance> is missing. Please assign a Default Stance ID in the [Stances] Tab");
            if (m.Stance == null) Warnings.Add("⚠ The <Current Stance> is missing. Please assign a Current Stance ID in the [Stances] Tab");
        }

        // MWC — destroy cached sub-editors on disable to prevent Unity editor object leaks
        protected virtual void OnDisable()
        {
            foreach (var ed in State_Editor.Values)
                if (ed != null) DestroyImmediate(ed);
            State_Editor.Clear();
        }

        private void CheckGuiStyles()
        {
            if (AbilityStyleDesc == null)
            {
                AbilityStyleDesc = new GUIStyle(MTools.StyleGray)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    stretchWidth = true
                };
                AbilityStyleDesc.normal.textColor = Color.white;
            }

            DescStyle = new GUIStyle(MalbersEditor.ComponentDesc_Style);

            popupStyle ??= new(GUI.skin.GetStyle("PaneOptions"))
            {
                imagePosition = ImagePosition.ImageOnly
            };
        }

        private void Selected_Mode(ReorderableList list)
        {
            SelectedMode.intValue = list.index;
            // MWC — removed ModeArray_Popup()/StateArray_Popup() calls here:
            // selection doesn't change popup content; arrays are rebuilt on add/remove/reorder/OnEnable
        }

        private void ModeArray_Popup()
        {
            ModePopupList = new string[S_Mode_List.arraySize];

            for (int i = 0; i < ModePopupList.Length; i++)
            {
                ModePopupList[i] = m.modes[i].ID != null ? m.modes[i].ID.name : "<EMPTY>";
            }
        }

        private void StateArray_Popup()
        {
            StatePopupList = new string[S_State_List.arraySize];

            for (int i = 0; i < StatePopupList.Length; i++)
            {
                if (m.states[i] != null)
                {
                    StatePopupList[i] = m.states[i].ID != null ? m.states[i].ID.name : "<EMPTY>";
                }
            }
        }

        private string[] ModePopupList;
        private string[] StatePopupList;


        // int SelectedAbility;

        private readonly string[] tab1 = new string[] { "General", "States", "Modes", "Stances" };
        private readonly string[] tab2 = new string[] { "Advanced", "Speeds", "Events", "Debug" };

        private readonly string[] DebugTab1 = new string[] { "Data", "State", "Ground", "Speeds" };
        private readonly string[] DebugTab2 = new string[] { "Input", "Mode", "Forces", "Movement" };

        private GUIContent _icon_Show;
        private GUIStyle icon_EYE;
        public GUIStyle Icon_EYE
        {
            get
            {
                icon_EYE ??= new GUIStyle(MTools.Style(new Color(0, 0.5f, 2f, 0.3f)));
                return icon_EYE;
            }
        }

        public GUIContent Icon_Show
        {
            get
            {
                if (_icon_Show == null)
                {
                    _icon_Show = EditorGUIUtility.IconContent("d_ViewToolOrbit", "Enable/Disable");
                    _icon_Show.tooltip = "Hide Animal Inspector on PlayMode. This will increase the speed of the game if the animal is selected ";
                }

                return _icon_Show;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            CheckGuiStyles();
            var descri = version;

            if (Application.isPlaying)
            {
                if (m.Sleep)
                {
                    descri += "      [[SLEEP]]";
                }
                else if (m.InTimeline)
                {
                    descri += "      [[IN TIMELINE]]";
                }
                else if (m.LockInput && m.LockMovement)
                {
                    descri += "      [[LOCKED]]";
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                MalbersEditor.DrawDescription(descri, DescStyle);

                var currentGUIColor = GUI.color;
                GUI.color = ShowOnPlay.boolValue ? (GUI.color + Color.white) * 2 : (GUI.color + Color.black) / 1.65f;

                ShowOnPlay.boolValue = GUILayout.Toggle(ShowOnPlay.boolValue, Icon_Show, Icon_EYE,
                      GUILayout.Width(25), GUILayout.Height(22));
                GUI.color = currentGUIColor;
            }


            //Draw the warnings if there are any
            if (Warnings != null && Warnings.Count > 0)
            {
                var CC = GUI.color;
                GUI.color = Color.yellow + Color.red;


                foreach (var warning in Warnings)
                {
                    EditorGUILayout.HelpBox(warning, MessageType.None);
                }

                GUI.color = CC;
            }

            if (!ShowOnPlay.boolValue && Application.isPlaying)
            {
                EditorGUILayout.HelpBox("The Inspector is hidden in Play Mode to improve performance. Use the [Eye] icon to show it again", MessageType.Info);
            }
            else
            {

                using (var hasCC = new EditorGUI.ChangeCheckScope())
                {
                    Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, tab1);
                    if (Editor_Tabs1.intValue != 4) Editor_Tabs2.intValue = 4;

                    Editor_Tabs2.intValue = GUILayout.Toolbar(Editor_Tabs2.intValue, tab2);
                    if (Editor_Tabs2.intValue != 4) Editor_Tabs1.intValue = 4;


                    //First Tabs
                    int Selection = Editor_Tabs1.intValue;

                    switch (Selection)
                    {
                        case 0: ShowGeneral(); break;
                        case 1: ShowStates(); break;
                        case 2: ShowModes(); break;
                        case 3: ShowStances(); break;
                        default: break;
                    }


                    //2nd Tabs
                    Selection = Editor_Tabs2.intValue;

                    switch (Selection)
                    {
                        case 0: ShowAdvanced(); break;
                        case 1: ShowSpeeds(); break;
                        case 2: ShowEvents(); break;
                        case 3: ShowDebug(); break;
                        default: break;
                    }

                    if (hasCC.changed)
                    { CheckWarnings(); }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        public void SetPivots()
        {
            m.Pivot_Hip = m.pivots.Find(item => item.name.ToUpper() == "HIP");
            m.Pivot_Chest = m.pivots.Find(item => item.name.ToUpper() == "CHEST");
        }

        #region GUICONTENT

        private readonly GUIContent G_Rotator = new("Rotator", "Used to add extra Rotations to the Animal");
        private readonly GUIContent G_RootBone = new("RootBone", "Bone to Identify the Main Root Bone of the Animal. Mainly Used for TimeLine and Flying Animals");

        private readonly GUIContent G_RayCastRadius = new("RayCast Radius", "Instead of using Raycast for checking the ground beneath the animal we use SphereCast, this is the Radius of that Sphere");

        private readonly GUIContent G_animalType = new("Type", "Value set on the Animator for Additive Pose Fixing");


        private readonly GUIContent G_AbilityIndex = new("Active", "Active Ability Index \n(if set to -99 it will Play a Random Ability )\n(if set to 0 it wont play anything)");
        private readonly GUIContent G_DefaultIndex = new("Default", "Default Ability Index to return to when exiting the mode \n(if set to -99 it will Play a Random Ability )");
        private readonly GUIContent G_ResetToDefault = new("R", "Reset to Default:\nWhen Exiting the Mode\nthe Active Index will reset\nto the Default");
        private readonly GUIContent G_CloneStates = new("Clone States", "Creates instances of the States so they cannot be overwritten by other animals using the same scriptable objects");

        private readonly GUIContent G_GroundLayer = new("Ground Layer", "Layers the Animal considers ground");
        private readonly GUIContent G_AlignPosLerp = new("Align Pos Lerp", "Smoothness value to Snap to ground while Grounded");
        private readonly GUIContent G_AlignPosDelta = new("Align Pos Delta", "Smoothness Position value to Snap to ground when using a non Grounded State");
        private readonly GUIContent G_AlignRotDelta = new("Align Rot Delta", "Smoothness Rotation value to Snap to ground when using a non Grounded State");
        private readonly GUIContent G_AlignRotLerp = new("Align Rot Lerp", "Smoothness value to Align to ground slopes while Grounded");

        private readonly GUIContent G_Modifier = new("Modifier", "Extra Logic to give the Animal when Entering or Exiting the Modes");

        private readonly GUIContent G_gravityDirection = new("Direction", "Direction of the Gravity applied to the animal");
        private readonly GUIContent G_GravityForce = new("Force", "Force of the Gravity, by Default it 9.8");
        private readonly GUIContent G_GravityCycle = new("Start Gravity Cycle", "Start the gravity with an extra time to push the animal down.... higher values stronger Gravity");

        private readonly GUIContent G_useSprintGlobal = new("Can Sprint", "Can the Animal Sprint?");
        private readonly GUIContent G_CanStrafe = new("Can Strafe", "Can the Animal Strafe?\nStrafing requires new sets of strafe animations. Make sure you have proper animations to Use this feature. Check the Help button for more Info [?]");
        private readonly GUIContent G_Strafe = new("Strafe", "Activate the Strafe on the Animal.");
        private readonly GUIContent G_StrafeNormalize = new("Normalize", "Normalize the value of the Strafe Angle on the Animation (-1 to 1 instead of -180 to 180)");
        private readonly GUIContent G_StrafeLerp = new("Lerp", "Lerp Value to smoothly enter the  Strafe");

        private readonly GUIContent G_SmoothVertical = new("Smooth Vertical", "Used for Joysticks to increase the speed by the Stick Pressure");

        private readonly GUIContent G_OverrideStartState = new("Override Start State", "Overrides the Start State");
        private readonly GUIContent G_StartWithMode =
            new("Start with Mode", "On Start .. Plays a Mode. Use the Mode ID.\nIf you want an specific Ability within the mode. Set the Mode and the Ability in the Format (Mode*1000+Ability). E.g Eat = 4002");
        #endregion

        //-------------------------STATES-----------------------------------------------------------
        private void OnSceneGUI()
        {
            foreach (var pivot in m.pivots)
            {
                if (pivot.EditorModify)
                {
                    Transform t = m.transform;

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        Vector3 piv = t.TransformPoint(pivot.position);
                        Vector3 NewPivPosition = Handles.PositionHandle(piv, t.rotation);
                        //   pivot.position = m.transform.InverseTransformPoint(NewPivPosition);

                        if (cc.changed)
                        {
                            Undo.RecordObject(m, "Pivots");
                            pivot.position = t.InverseTransformPoint(NewPivPosition);

                            EditorUtility.SetDirty(target);
                        }
                    }
                }
            }
        }
    }
}
#endif
