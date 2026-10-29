// Last Change: 9/24/2026 1:16pm
#if UNITY_EDITOR
using MalbersAnimations.Controller;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MalbersAnimations
{
    [System.Serializable]
    public class ModeItemAnim
    {
        public string name;
        public int index;
        public Motion clip;
    }

    public partial class MalbersAnimatorTools : EditorWindow
    {
        private AnimatorState[] m_AnimatorStates;
        private AnimatorStateMachine[] m_StateMachines;
        private AnimatorTransitionBase[] m_Transitions;

        public AnimatorController controller;
        public MAnimal Animal;

        [RequiredField] public StateID State;
        public StateID LastState;
        [RequiredField] public ModeID Mode;
        [RequiredField] public StanceID Stance;
        public StanceID LastStance;

        private int Editor_Tabs1;
        private int SelectedParameter;
        private int condition;
        private float value;

        private SerializedObject serializedObject;
        private SerializedProperty p_Animal, p_Controller, p_Abilities;

        public float m_ExitTime = 0.9f;
        public bool m_FixedDuration;
        public float m_TransitionDuration = 0.1f;
        public float m_TransitionOffset = 0.15f;
        public TransitionInterruptionSource m_InterruptionSource;
        public bool m_OrderedInterruption = true;
        public bool m_TransitionToSelf = false;

        //MWC: Per-field "mixed value" flags. Set when multiple selected transitions have differing values for that field.
        private bool mixedExitTime, mixedFixedDuration, mixedTransitionDuration,
            mixedTransitionOffset, mixedInterruptionSource, mixedOrderedInterruption, mixedTransitionToSelf;

        //MWC: Set by SetupModeTab to stop the next selection callback from clearing the externally-assigned Mode
        private bool m_SuppressModeClearOnce;

        [Tooltip("Ignore the Exit Transition")]
        public bool IgnoreExitTransition = false;
        public float m_TransitionExitTime = 0.95f;

        //MWC: Transition duration used when creating Mode Entry / Exit transitions
        public float ModeEntryDuration = 0.2f;
        public float ModeExitDuration = 0.2f;
        /// <summary>Has an Animator State selected </summary>
        private bool HAS_AS => m_AnimatorStates.Length > 0;
        private bool HAS_ASM => m_StateMachines.Length > 0;
        //  bool HAS_T => m_Transitions.Length > 0;

        public Vector2 Scroll;

        public bool ModifyTV = true;
        public bool CreateNewConditionF = true;
        //MWC: When ON, the Mode field is cleared if the selected state(s) have no ModeBehaviour. When OFF, the previous Mode is kept.
        public bool ClearModeOnNoMatch = false;
        //MWC: When ON, the State field is cleared if no StateID can be resolved from the selection. When OFF, the previous State is kept.
        public bool ClearStateOnNoMatch = false;
        //MWC: When ON, the Entry Transitions step also adds the self Loop Transition to each state. Off by default.
        public bool AddLoopTransition = false;

        private string[] ModeAbilities;
        public List<ModeItemAnim> Abilities;
        private int[] ModeAbilitiesIndex;

        //List<string> popupOptions; 
        private GUIContent plus;
        private GUIContent GC_AS;
        private GUIContent GC_T;
        private GUIContent GC_ASM;
        private GUIContent updateB;
        private GUIContent gc_modes;
        private GUIContent gc_modesAll;

        /// <summary> Cached style to use to draw the popup button. </summary>
        private GUIStyle popupStyle;

        [MenuItem("Tools/Malbers Animations/Animator Tools", false, 200)]
        public static void ShowWindow()
        {
            var window = (MalbersAnimatorTools)GetWindow(typeof(MalbersAnimatorTools), false, "Animator Tools");
            window.minSize = new Vector2(250, 120);

            window.titleContent = new GUIContent("AC Animator Tools", EditorGUIUtility.ObjectContent(null, typeof(AnimatorController)).image);
            //window.maxSize = new Vector2(650, 500); 
            window.Show();
        }

        private static GUIContent[] gs;


        private void OnEnable()
        {
            serializedObject = new SerializedObject(this);

            p_Animal = serializedObject.FindProperty("Animal");
            p_Controller = serializedObject.FindProperty("controller");
            p_Abilities = serializedObject.FindProperty("Abilities");

            FindImages();

            m_AnimatorStates = Selection.GetFiltered<AnimatorState>(SelectionMode.Editable);
            m_StateMachines = Selection.GetFiltered<AnimatorStateMachine>(SelectionMode.Editable);
            m_Transitions = Selection.GetFiltered<AnimatorTransitionBase>(SelectionMode.Editable);

            OnSelectionChange();

            p_Abilities.isExpanded = true;
        }

        private void FindImages()
        {
            var img_State = EditorGUIUtility.ObjectContent(CreateInstance<StateID>(), typeof(StateID)).image;
            var img_Mode = EditorGUIUtility.ObjectContent(CreateInstance<ModeID>(), typeof(ModeID)).image;
            var img_Stance = EditorGUIUtility.ObjectContent(CreateInstance<StanceID>(), typeof(StanceID)).image;
            var img_Transition = EditorGUIUtility.ObjectContent(null, typeof(AnimatorStateTransition)).image;
            var img_StateA = EditorGUIUtility.ObjectContent(null, typeof(AnimatorState)).image;
            var img_StateAM = EditorGUIUtility.ObjectContent(null, typeof(AnimatorStateMachine)).image;
            //var img_StateMachine = EditorGUIUtility.ObjectContent(null, typeof(AnimatorState)).image;


            GC_AS = new("Add", img_StateA, "Add the new State");
            GC_T = new("Add", img_Transition, "Add the new Transition");
            GC_ASM = new("Add", img_StateAM, "Add the new Animator State");

            gc_modes = new("Add", img_Mode);

            gc_modesAll = new("Add", img_Mode, "Add all the Transitions with one click");

            gs = new GUIContent[] {
                new(" States", img_State),
                new(" Modes", img_Mode),
                new(" Stances", img_Stance),
                new(" Transitions",img_Transition),
            };
        }


        private void OnSelectionChange()
        {
            m_AnimatorStates = Selection.GetFiltered<AnimatorState>(SelectionMode.Editable);

            m_StateMachines = Selection.GetFiltered<AnimatorStateMachine>(SelectionMode.Editable);
            m_Transitions = Selection.GetFiltered<AnimatorTransitionBase>(SelectionMode.Editable);
            //AnimatorLayers = Selection.GetFiltered<AnimatorControllerLayer>(SelectionMode.Unfiltered);

            //MWC: If a selected Animator State has a ModeBehaviour attached, extract its ModeID and update the Mode field
            UpdateModeFromSelection();

            //MWC: If a selected Sub-State Machine has an Any State transition with a State condition, resolve it to a StateID asset and update the State field
            UpdateStateFromSelection();

            //MWC: If a single transition is selected, copy its values into the Transition tab fields
            UpdateTransitionValuesFromSelection();

            //if (m_StateMachines.Length > 0)

            //{
            //    Debug.Log(" m_StateMachines[0].name = " + m_StateMachines[0].name);
            //    Debug.Log(" controller.layers[0].stateMachine.name = " + controller.layers[0].stateMachine.name);
            //}

            RecalculateModeAbilities(); //MWC: Build the Mode grid arrays (names + indexes) from m_AnimatorStates

            var gameobjects = Selection.GetFiltered<GameObject>(SelectionMode.TopLevel);

            foreach (var o in gameobjects)
            {
                if (!o.TryGetComponent<Animator>(out var AA)) return;
                p_Animal.objectReferenceValue = o.GetComponent<MAnimal>();

                if (p_Animal.objectReferenceValue != null)
                {
                    var C = (AnimatorController)AA.runtimeAnimatorController;

                    //if (C.layers[0].name != "States") C.layers[0].name = "States";
                    //if (C.layers[0].stateMachine.name != "States") C.layers[0].stateMachine.name = "States";

                    p_Controller.objectReferenceValue = C;


                    serializedObject.ApplyModifiedProperties();
                    Repaint();
                    return;
                }
            }


            string Path;

            //Find the Animator Controller  via AssetPath
            if (m_StateMachines.Length > 0)
                Path = AssetDatabase.GetAssetPath(m_StateMachines[0]);
            else if (m_AnimatorStates.Length > 0)
                Path = AssetDatabase.GetAssetPath(m_AnimatorStates[0]);
            else if (m_Transitions.Length > 0)
                Path = AssetDatabase.GetAssetPath(m_Transitions[0]);
            else
            {
                return;
            }

            var CC = (AnimatorController)AssetDatabase.LoadAssetAtPath(Path, typeof(AnimatorController));
            if (p_Controller.objectReferenceValue != CC)
            {

                //if (C.layers[0].name != "States") C.layers[0].name = "States";
                //if (C.layers[0].stateMachine.name != "States") C.layers[0].stateMachine.name = "States";
                p_Controller.objectReferenceValue = CC;
                p_Animal.objectReferenceValue = null;
            }

            serializedObject.ApplyModifiedProperties();
            Repaint();
        }

        //MWC: Rebuilds the Mode tab grid arrays (names + ability indexes) from the current m_AnimatorStates.
        //Extracted from OnSelectionChange so it can be reused when the window is set up externally (Add Mode Animations).
        private void RecalculateModeAbilities()
        {
            Abilities = new List<ModeItemAnim>();

            ModeAbilities = new string[m_AnimatorStates.Length];
            ModeAbilitiesIndex = new int[m_AnimatorStates.Length];

            int StartIndex = 1;

            if (Animal != null && Mode != null)
            {
                var AnimalMode = Animal.modes.Find(x => x.ID == Mode.ID);
                if (AnimalMode != null && AnimalMode.Abilities.Count > 0)
                    StartIndex = AnimalMode.Abilities.Max(x => x.Index.Value) + 1;
            }

            //First pass - extract the ability index from any AnyState transition that already has the Mode condition (ID*1000+index)
            var usedIndexes = new HashSet<int>();
            var hasExisting = new bool[ModeAbilitiesIndex.Length];

            for (int i = 0; i < m_AnimatorStates.Length; i++)
            {
                if (TryGetExistingAbilityIndex(m_AnimatorStates[i], out var existingIndex))
                {
                    ModeAbilitiesIndex[i] = existingIndex;
                    hasExisting[i] = true;
                    usedIndexes.Add(existingIndex);
                }
            }

            //Second pass - assign the next available index for states without an existing Mode transition (skipping taken indexes)
            int nextIndex = StartIndex;
            for (int i = 0; i < ModeAbilitiesIndex.Length; i++)
            {
                if (hasExisting[i]) continue;

                while (usedIndexes.Contains(nextIndex)) nextIndex++;
                ModeAbilitiesIndex[i] = nextIndex;
                usedIndexes.Add(nextIndex);
                nextIndex++;
            }

            for (int i = 0; i < m_AnimatorStates.Length; i++)
            {
                ModeAbilities[i] = m_AnimatorStates[i].name;

                Abilities.Add(new ModeItemAnim() { name = m_AnimatorStates[i].name, index = i + 1, clip = m_AnimatorStates[i].motion });

                //MWC: p_Abilities is null when running headless (no OnEnable) - skip the serialized refresh in that case
                if (p_Abilities != null)
                {
                    p_Abilities.serializedObject.Update();
                    p_Abilities.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        //MWC: Headless entry point for "Auto Setup Mode" - runs the full Set Everything pipeline (entry/exit transitions + abilities)
        //on a temporary instance, without opening the Animator Tools window.
        public static void AutoSetupMode(MAnimal animal, AnimatorController controller, ModeID mode, AnimatorState[] states)
        {
            var tool = CreateInstance<MalbersAnimatorTools>();
            try
            {
                tool.Animal = animal;
                tool.controller = controller;
                tool.Mode = mode;
                tool.m_AnimatorStates = states ?? new AnimatorState[0];
                tool.m_StateMachines = new AnimatorStateMachine[0];
                tool.m_Transitions = new AnimatorTransitionBase[0];

                tool.RecalculateModeAbilities(); //build ability names + indexes
                tool.SetEverything();            //entry transitions + exit transitions + add mode & abilities
            }
            finally
            {
                DestroyImmediate(tool);
            }
        }

        //MWC: External entry point used by the "Add Mode Animations" window. Fills the references, switches to the Modes tab,
        //selects the given states and rebuilds the grid — without going through OnSelectionChange's selection resolution.
        public static void OpenForModeSetup(MAnimal animal, AnimatorController controller, ModeID mode, AnimatorState[] states)
        {
            var window = (MalbersAnimatorTools)GetWindow(typeof(MalbersAnimatorTools), false, "Animator Tools");
            window.minSize = new Vector2(250, 120);
            window.titleContent = new GUIContent("AC Animator Tools", EditorGUIUtility.ObjectContent(null, typeof(AnimatorController)).image);
            window.Show();
            window.SetupModeTab(animal, controller, mode, states);
        }

        private void SetupModeTab(MAnimal animal, AnimatorController controller, ModeID mode, AnimatorState[] states)
        {
            Animal = animal;
            this.controller = controller;
            Mode = mode;
            Editor_Tabs1 = 1; //Modes tab
            m_SuppressModeClearOnce = true; //keep this Mode through the selection callback triggered below

            p_Animal.objectReferenceValue = animal;
            p_Controller.objectReferenceValue = controller;
            serializedObject.FindProperty("Mode").objectReferenceValue = mode;

            //Selection mirrors what the tool would normally read: the animal GameObject + the new states
            var selection = new List<Object>();
            if (animal != null) selection.Add(animal.gameObject);
            if (states != null) selection.AddRange(states);
            Selection.objects = selection.ToArray();

            //Refresh the selection-derived arrays and build the Mode grid directly (no clear-on-no-match side effects)
            m_AnimatorStates = states ?? new AnimatorState[0];
            m_StateMachines = Selection.GetFiltered<AnimatorStateMachine>(SelectionMode.Editable);
            m_Transitions = Selection.GetFiltered<AnimatorTransitionBase>(SelectionMode.Editable);

            RecalculateModeAbilities();

            serializedObject.ApplyModifiedProperties();
            Repaint();
            Focus();
        }

        //MWC: Reads the first ModeBehaviour found on the selected Animator States and sets the Mode field from its ModeID.
        //Only runs on the Modes tab; clears the Mode field when no selected state has a ModeBehaviour.
        private void UpdateModeFromSelection()
        {
            if (Editor_Tabs1 != 1) return; //Modes tab only

            ModeID detectedMode = null;

            if (m_AnimatorStates != null)
            {
                foreach (var AS in m_AnimatorStates)
                {
                    var modeB = AS.behaviours?.FirstOrDefault(b => b is ModeBehaviour) as ModeBehaviour;

                    if (modeB != null && modeB.ModeID != null)
                    {
                        detectedMode = modeB.ModeID;
                        break;
                    }
                }
            }

            //MWC: When no ModeBehaviour was found, only clear the Mode field if the Clear-on-no-match option is enabled
            if (detectedMode == null && !ClearModeOnNoMatch) return;

            //MWC: One-shot guard so the externally-set Mode (Add Mode Animations) isn't cleared by the selection callback
            //that fires right after SetupModeTab, before the states have a ModeBehaviour.
            if (detectedMode == null && m_SuppressModeClearOnce)
            {
                m_SuppressModeClearOnce = false;
                return;
            }

            Mode = detectedMode; //set the field (or clear it) so the StartIndex calculation below uses the detected Mode
            serializedObject.FindProperty("Mode").objectReferenceValue = detectedMode; //persist through ApplyModifiedProperties
        }

        //MWC: Looks for an existing AnyState transition into this state that already has the Mode condition (Equals ID*1000+index)
        //and reverses the formula to recover the ability index. Returns false if none is found for the current Mode.
        private bool TryGetExistingAbilityIndex(AnimatorState state, out int abilityIndex)
        {
            abilityIndex = -1;
            if (Mode == null) return false;

            var layerSM = FindAnyState(state); //the State Machine that holds the Any State transitions
            if (layerSM == null) return false;

            int baseValue = Mode.ID * 1000;

            foreach (var t in layerSM.anyStateTransitions)
            {
                if (t.destinationState != state) continue;

                foreach (var c in t.conditions)
                {
                    if (c.parameter == "Mode" && c.mode == AnimatorConditionMode.Equals)
                    {
                        int threshold = Mathf.RoundToInt(c.threshold);

                        //Only accept thresholds that belong to the current Mode's 1000-wide ID band
                        if (threshold >= baseValue && threshold < baseValue + 1000)
                        {
                            abilityIndex = threshold - baseValue;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        //MWC: Resolves a StateID from the selection and updates the State field (States tab only).
        //- If the first selected object is an Animator State, its Tag string is matched to a StateID asset by name.
        //- If a Sub-State Machine is selected, the Any State transition's "State" condition value is matched to a StateID asset by ID.
        private void UpdateStateFromSelection()
        {
            if (Editor_Tabs1 != 0) return; //States tab only
            if (controller == null) return; //MWC: nothing to resolve without a controller (e.g. during OnEnable)

            StateID detectedState = null;

            //Animator State selected: match its Tag to a StateID asset name (e.g. Tag "Fall" -> StateID "Fall")
            if (m_AnimatorStates != null && m_AnimatorStates.Length > 0)
            {
                var tag = m_AnimatorStates[0].tag;
                if (!string.IsNullOrEmpty(tag))
                    detectedState = MTools.GetAllInstances<StateID>().Find(s => s.name == tag);
            }
            //Sub-State Machine selected: resolve from its Any State transition State condition
            else if (m_StateMachines != null && m_StateMachines.Length > 0)
            {
                foreach (var ASM in m_StateMachines)
                {
                    if (TryGetStateIDFromTransition(ASM, out var stateID))
                    {
                        detectedState = stateID;
                        break;
                    }
                }
            }

            //When nothing matched, only clear the State field if the Clear-on-no-match option is enabled
            if (detectedState == null && !ClearStateOnNoMatch) return;

            State = detectedState; //set the field (or clear it)
            serializedObject.FindProperty("State").objectReferenceValue = detectedState; //persist through ApplyModifiedProperties
        }

        //MWC: Copies selected transition values into the tool's Transition fields. With multiple transitions selected,
        //fields shared by all keep their value, while differing fields are flagged as "mixed" and drawn with a mixed indicator.
        private void UpdateTransitionValuesFromSelection()
        {
            if (Editor_Tabs1 != 3) return; //Transitions tab only

            //Only AnimatorStateTransitions expose these values
            var transitions = m_Transitions?.OfType<AnimatorStateTransition>().ToList();
            if (transitions == null || transitions.Count == 0) return;

            var first = transitions[0];

            //Seed the fields from the first selected transition
            m_ExitTime = first.exitTime;
            m_FixedDuration = first.hasFixedDuration;
            m_TransitionDuration = first.duration;
            m_TransitionOffset = first.offset;
            m_InterruptionSource = first.interruptionSource;
            m_OrderedInterruption = first.orderedInterruption;
            m_TransitionToSelf = first.canTransitionToSelf;

            //Reset mixed flags, then mark any field that differs across the rest of the selection
            mixedExitTime = mixedFixedDuration = mixedTransitionDuration =
                mixedTransitionOffset = mixedInterruptionSource =
                mixedOrderedInterruption = mixedTransitionToSelf = false;

            for (int i = 1; i < transitions.Count; i++)
            {
                var t = transitions[i];
                if (!Mathf.Approximately(t.exitTime, m_ExitTime)) mixedExitTime = true;
                if (t.hasFixedDuration != m_FixedDuration) mixedFixedDuration = true;
                if (!Mathf.Approximately(t.duration, m_TransitionDuration)) mixedTransitionDuration = true;
                if (!Mathf.Approximately(t.offset, m_TransitionOffset)) mixedTransitionOffset = true;
                if (t.interruptionSource != m_InterruptionSource) mixedInterruptionSource = true;
                if (t.orderedInterruption != m_OrderedInterruption) mixedOrderedInterruption = true;
                if (t.canTransitionToSelf != m_TransitionToSelf) mixedTransitionToSelf = true;
            }

            //Persist the field values through ApplyModifiedProperties at the end of OnSelectionChange
            serializedObject.FindProperty("m_ExitTime").floatValue = m_ExitTime;
            serializedObject.FindProperty("m_FixedDuration").boolValue = m_FixedDuration;
            serializedObject.FindProperty("m_TransitionDuration").floatValue = m_TransitionDuration;
            serializedObject.FindProperty("m_TransitionOffset").floatValue = m_TransitionOffset;
            serializedObject.FindProperty("m_InterruptionSource").enumValueIndex = (int)m_InterruptionSource;
            serializedObject.FindProperty("m_OrderedInterruption").boolValue = m_OrderedInterruption;
            serializedObject.FindProperty("m_TransitionToSelf").boolValue = m_TransitionToSelf;
        }

        //MWC: Pushes all the Transition tab field values onto every selected transition and clears the mixed flags.
        private void ApplyAllTransitionValues()
        {
            if (m_Transitions == null) return;

            foreach (var transition in m_Transitions)
            {
                if (transition is AnimatorStateTransition t)
                {
                    t.exitTime = m_ExitTime;
                    t.hasFixedDuration = m_FixedDuration;
                    t.duration = m_TransitionDuration;
                    t.offset = m_TransitionOffset;
                    t.interruptionSource = m_InterruptionSource;
                    t.orderedInterruption = m_OrderedInterruption;
                    t.canTransitionToSelf = m_TransitionToSelf;
                }
            }

            //All selected transitions now share these values
            mixedExitTime = mixedFixedDuration = mixedTransitionDuration =
                mixedTransitionOffset = mixedInterruptionSource =
                mixedOrderedInterruption = mixedTransitionToSelf = false;

            if (controller) EditorUtility.SetDirty(controller);
        }

        //MWC: Finds an Any State transition into this Sub-State Machine that has a "State" Equals condition, then matches the value to a StateID asset in the project.
        private bool TryGetStateIDFromTransition(AnimatorStateMachine asm, out StateID stateID)
        {
            stateID = null;
            if (asm == null) return false;

            var layerSM = FindAnyState(asm); //the State Machine that holds the Any State transitions
            if (layerSM == null) return false;

            foreach (var t in layerSM.anyStateTransitions)
            {
                if (t.destinationStateMachine != asm) continue;

                foreach (var c in t.conditions)
                {
                    if (c.parameter == "State" && c.mode == AnimatorConditionMode.Equals)
                    {
                        int stateValue = Mathf.RoundToInt(c.threshold);
                        stateID = MTools.GetAllInstances<StateID>().Find(s => s.ID == stateValue);
                        return stateID != null;
                    }
                }
            }
            return false;
        }

        private void OnGUI()
        {
            //  serializedObject.Update();

            MalbersEditor.DrawDescription("This tools helps create the correct transitions for any Animal Controller");

            //  EditorGUILayout.PropertyField(SP_AllStanceIDs, true);

            FindIcons();

            using (new EditorGUI.DisabledGroupScope(true))
            {
                EditorGUILayout.LabelField("SELECTED: " +
                $" Anim States[{m_AnimatorStates.Length}]. " +
                $" States Machine [{m_StateMachines.Length}]. " +
                $" Transitions [{m_Transitions.Length}]", EditorStyles.boldLabel);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("References", EditorStyles.boldLabel);

                EditorGUILayout.PropertyField(p_Animal);

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(p_Controller);

                    if (p_Controller.objectReferenceValue)
                    {
                        CheckParameters();
                    }
                }
            }

            Editor_Tabs1 = GUILayout.Toolbar(Editor_Tabs1, gs, GUILayout.Height(22));

            // Debug.Log("controller = " + controller);
            if (controller == null) return;



            if (Editor_Tabs1 == 0) { DoStates(); } //States
            if (Editor_Tabs1 == 1) { DoModes(); } //States
            if (Editor_Tabs1 == 2) { DoStances(); } //States
            if (Editor_Tabs1 == 3) { DoTransitions(); } //States

            serializedObject.ApplyModifiedProperties();

        }

        /// <summary> Draws the buttons that add the Required (P) and Optional (p) MAnimal Animator Parameters to the controller </summary>
        private void CheckParameters()
        {
            var v_float = UnityEngine.AnimatorControllerParameterType.Float;
            var v_int = UnityEngine.AnimatorControllerParameterType.Int;
            var v_bool = UnityEngine.AnimatorControllerParameterType.Bool;
            var v_trigger = UnityEngine.AnimatorControllerParameterType.Trigger;

            //Same names and grouping as the MAnimal [Advanced ▸ Required/Optional Animator Parameters]. Uses the selected Animal names when there is one
            if (GUILayout.Button(new GUIContent("P", "Check/Add the Required Animator Parameters of the MAnimal"), GUILayout.Width(28)))
            {
                var animal = Animal != null ? new SerializedObject(Animal) : null;

                CheckAnimParameter(ParamName(animal, "m_StateOn", "StateOn"), v_trigger);
                CheckAnimParameter(ParamName(animal, "m_ModeOn", "ModeOn"), v_trigger);
                CheckAnimParameter(ParamName(animal, "m_Vertical", "Vertical"), v_float);
                CheckAnimParameter(ParamName(animal, "m_Horizontal", "Horizontal"), v_float);
                CheckAnimParameter(ParamName(animal, "m_State", "State"), v_int);
                CheckAnimParameter(ParamName(animal, "m_LastState", "LastState"), v_int);
                CheckAnimParameter(ParamName(animal, "m_StateStatus", "StateEnterStatus"), v_int);
                CheckAnimParameter(ParamName(animal, "m_StateFloat", "StateFloat"), v_float);
                CheckAnimParameter(ParamName(animal, "m_Mode", "Mode"), v_int);
                CheckAnimParameter(ParamName(animal, "m_ModeStatus", "ModeStatus"), v_int);
                CheckAnimParameter(ParamName(animal, "m_Grounded", "Grounded"), v_bool);
                CheckAnimParameter(ParamName(animal, "m_Movement", "Movement"), v_bool);
            }

            if (GUILayout.Button(new GUIContent("p", "Check/Add the Optional Animator Parameters of the MAnimal"), GUILayout.Width(28)))
            {
                var animal = Animal != null ? new SerializedObject(Animal) : null;

                CheckAnimParameter(ParamName(animal, "m_SpeedMultiplier", "SpeedMultiplier"), v_float);
                CheckAnimParameter(ParamName(animal, "m_UpDown", "UpDown"), v_float);
                CheckAnimParameter(ParamName(animal, "m_DeltaUpDown", "DeltaUpDown"), v_float);
                CheckAnimParameter(ParamName(animal, "m_VerticalRaw", "VerticalRaw"), v_float);
                CheckAnimParameter(ParamName(animal, "m_DeltaAngle", "DeltaAngle"), v_float);
                CheckAnimParameter(ParamName(animal, "m_Sprint", "Sprint"), v_bool);
                CheckAnimParameter(ParamName(animal, "m_StateProfile", "StateProfile"), v_int);
                CheckAnimParameter(ParamName(animal, "m_StateExitStatus", "StateExitStatus"), v_int);
                CheckAnimParameter(ParamName(animal, "m_StateTime", "StateTime"), v_float);
                CheckAnimParameter(ParamName(animal, "m_Stance", "Stance"), v_int);
                CheckAnimParameter(ParamName(animal, "m_LastStance", "LastStance"), v_int);
                CheckAnimParameter(ParamName(animal, "m_ModePower", "ModePower"), v_float);
                CheckAnimParameter(ParamName(animal, "m_ModeLayer", "ModeLayer"), v_int);
                CheckAnimParameter(ParamName(animal, "m_Slope", "Slope"), v_float);
                CheckAnimParameter(ParamName(animal, "m_Random", "Random"), v_int);
                CheckAnimParameter(ParamName(animal, "m_Strafe", "Strafe"), v_bool);
                CheckAnimParameter(ParamName(animal, "m_Type", "Type"), v_int);
            }
        }

        /// <summary> Returns the Animator Parameter name set on the MAnimal field, or the MAnimal default name when there is no Animal or the field is empty </summary>
        private static string ParamName(SerializedObject animal, string field, string defaultName)
        {
            var name = animal?.FindProperty(field)?.stringValue;
            return string.IsNullOrEmpty(name) ? defaultName : name;
        }

        private void CheckAnimParameter(string PName, AnimatorControllerParameterType Ptype)
        {
            var AllParameters = controller.parameters.ToList();

            if (!AllParameters.Exists(p => p.name == PName))
            {
                var p =
                new AnimatorControllerParameter() { name = PName, type = Ptype };
                controller.AddParameter(p);
                Debug.Log($"Added to the Animator [{controller.name}] the  {Ptype} Parameter [{PName}]");
            }
            else
            {
                Debug.Log($"Animator [{controller.name}] already has the {Ptype} Parameter: [{PName}]");
            }
        }

        private void FindIcons()
        {
            if (popupStyle == null)
            {
                popupStyle = new GUIStyle(GUI.skin.GetStyle("PaneOptions"));
                popupStyle.imagePosition = ImagePosition.ImageOnly;
            }


            if (plus == null)
            {
                plus = UnityEditor.EditorGUIUtility.IconContent("d_Toolbar Plus");
                plus.tooltip = "Add Condition to all selected transitions";
            }
            if (updateB == null)
            {
                updateB = UnityEditor.EditorGUIUtility.IconContent("d_RotateTool");
                updateB.tooltip = "Update Value";
            }
        }


        private void DoStates()
        {
            EditorGUILayout.Space(4);

            DrawStateConditions();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Transitions", EditorStyles.boldLabel);

                if (State != null)
                {
                    if (m_StateMachines.Length == 1)
                    {
                        #region BUTTON CREATE NEW STATE
                        using (new GUILayout.HorizontalScope())
                        {
                            //
                            if (GUILayout.Button(GC_AS, GUILayout.Height(22), GUILayout.Width(80)))
                            {

                                var newAnimState = m_StateMachines[0].AddState(State.name);
                                newAnimState.tag = State.name;
                                newAnimState.speedParameter = "SpeedMultiplier";
                                newAnimState.speedParameterActive = true;
                            }
                            EditorGUILayout.LabelField($"*New [{State.name}] State", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                        }
                        #endregion


                        #region BUTTON CREATE NEW  STATE MACHINE
                        using (new GUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(GC_ASM, GUILayout.Height(22), GUILayout.Width(80)))
                            {
                                var newAnimState = m_StateMachines[0].AddStateMachine(State.name);
                            }
                            EditorGUILayout.LabelField($"*New [{State.name}] StateMachine", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                        }
                        #endregion
                    }

                    //if there's any Anim State selecter
                    if (m_AnimatorStates.Length > 0 ||
                        //OR if theres any State Machine Selected but don't include the Base Layer State Machine
                        (m_StateMachines.Length > 0 && m_StateMachines[0].name != controller.layers[0].stateMachine.name))
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(GC_T, GUILayout.Height(22), GUILayout.Width(80)))
                            {
                                foreach (var AS in m_AnimatorStates)
                                {
                                    var Parent = FindAnyState(AS);

                                    var Any_ToState = Parent.AddAnyStateTransition(AS); //Add Any
                                    Create_AnyToState(Any_ToState);
                                }

                                foreach (var ASM in m_StateMachines)
                                {
                                    if (isLayerRoot(ASM)) continue; //Do not do the Root Layer

                                    var Parent = FindAnyState(ASM);
                                    var Any_ToState = Parent.AddAnyStateTransition(ASM); //Add Any
                                    Create_AnyToState(Any_ToState);
                                }
                            }

                            EditorGUILayout.LabelField($"*New Transition [{State.name}] to Selected States [{m_AnimatorStates.Length}]. [CHECK *ANY*]",
                           EditorStyles.boldLabel, GUILayout.MinWidth(50));

                        }
                    }
                }
            }
        }

        private void DrawStateConditions()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("State"));
                    if (State != null)
                    {
                        using (new EditorGUI.DisabledGroupScope(true))
                            EditorGUILayout.IntField(State.ID, GUILayout.Width(50));
                    }
                }

                if (State != null)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("LastState"));

                        if (LastState != null)
                        {
                            using (new EditorGUI.DisabledGroupScope(true))
                                EditorGUILayout.IntField(LastState.ID, GUILayout.Width(50));
                        }
                    }
                }

                //MWC: Toggle to control whether the State field is cleared when the selection resolves to no StateID
                ClearStateOnNoMatch = GUILayout.Toggle(ClearStateOnNoMatch,
                    new GUIContent("Clear State on No Match",
                    "When ON, selecting a state/sub-state machine that resolves to no StateID clears the State field.\nWhen OFF, the previously selected State is kept."));
            }
        }

        private void Create_AnyToStateMachineMode(AnimatorStateTransition Any_ToState)
        {
            Any_ToState.name = "To: " + Mode.name;
            Any_ToState.duration = ModeEntryDuration; //MWC: use the Entry transition duration from the tool
            Any_ToState.offset = 0;
            Any_ToState.hasExitTime = false;
            Any_ToState.interruptionSource = TransitionInterruptionSource.Destination; //IMPORTANT!

            Any_ToState.AddCondition(AnimatorConditionMode.If, 0, "ModeOn");

            Any_ToState.AddCondition(AnimatorConditionMode.Greater, Mode.ID * 1000, "Mode");
            Any_ToState.AddCondition(AnimatorConditionMode.Less, Mode.ID * 1000 + 1000, "Mode");


            EditorUtility.SetDirty(Any_ToState);
            p_Controller.serializedObject.ApplyModifiedProperties();
            p_Controller.serializedObject.Update();

            // EditorGUIUtility.PingObject(controller); //Final way of changing the name of the asset... dirty but it works
        }


        private void Create_AnyToState(AnimatorStateTransition Any_ToState)
        {
            Any_ToState.name = State.name;
            Any_ToState.duration = 0.2f;
            Any_ToState.offset = 0;
            Any_ToState.hasExitTime = false;
            Any_ToState.interruptionSource = TransitionInterruptionSource.Destination; //IMPORTANT!

            Any_ToState.AddCondition(AnimatorConditionMode.If, 0, "StateOn");

            Any_ToState.AddCondition(AnimatorConditionMode.Equals, State.ID, "State");


            if (LastState != null)
            {
                Any_ToState.AddCondition(AnimatorConditionMode.Equals, LastState.ID, "LastState");
                Any_ToState.name = $"{State.name} from [{LastState.name}]";

            }

            p_Controller.serializedObject.ApplyModifiedProperties();
            p_Controller.serializedObject.Update();

            // EditorGUIUtility.PingObject(controller); //Final way of changing the name of the asset... dirty but it works
        }

        private void DoModes()
        {
            EditorGUILayout.Space(4);
            DrawModeConditions();
            //EditorGUILayout.Space(4);

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                //Show Action IDS
                using (new GUILayout.HorizontalScope())
                {
                    if (Animal && HAS_AS && Mode != null)
                    {
                        var guiColor = GUI.color;

                        GUI.color = (Color.green + Color.white) / 2;

                        if (GUILayout.Button(gc_modesAll, GUILayout.Height(22), GUILayout.Width(80)))
                        {
                            AddEntryTransitions();
                        }
                        GUI.color = guiColor;
                        EditorGUILayout.LabelField(new GUIContent("Entry Transitions", "Adds all the entry transitions needed to make the Ability on the Mode work properly"), GUILayout.MinWidth(80));
                        //MWC: Transition duration used for the Mode entry transitions (no label)
                        ModeEntryDuration = EditorGUILayout.FloatField(new GUIContent("", "Entry transition duration"), ModeEntryDuration, GUILayout.Width(40));
                        GUI.color = (Color.green + Color.white) / 2;

                        EditorGUI.BeginDisabledGroup(IgnoreExitTransition);

                        if (GUILayout.Button(gc_modesAll, GUILayout.Height(22), GUILayout.Width(80)))
                        {
                            AddExitTransitions();
                        }
                        GUI.color = guiColor;
                        EditorGUILayout.LabelField(new GUIContent("Exit Transitions", "Adds all the exot transitions needed to make the Ability on the Mode work properly"), GUILayout.MinWidth(80));
                        //MWC: Transition duration used for the Mode exit + interrupted transitions (no label)
                        ModeExitDuration = EditorGUILayout.FloatField(new GUIContent("", "Exit transition duration"), ModeExitDuration, GUILayout.Width(40));

                        EditorGUI.EndDisabledGroup();

                        EditorGUIUtility.labelWidth = 20;
                        if (IgnoreExitTransition)
                        {
                            m_TransitionExitTime = EditorGUILayout.Slider(new GUIContent("", "The time it takes to exit the Mode"), m_TransitionExitTime, 0, 1);
                        }
                        IgnoreExitTransition = GUILayout.Toggle(IgnoreExitTransition, new GUIContent("Ignore Exit", "On the Mode Behavior. Set Ingore"), GUILayout.Width(80));
                        EditorGUIUtility.labelWidth = 0;


                        EditorUtility.SetDirty(controller);
                    }
                    //if (Mode)
                    //    EditorGUILayout.LabelField($" {Mode.name}", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                }

                if (HAS_ASM && Mode != null)
                {
                    if (m_StateMachines.Length == 1 && isLayerStateMachine(m_StateMachines[0]))
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(GC_AS, GUILayout.Height(22), GUILayout.Width(80)))
                            {
                                var newAnimState = m_StateMachines[0].AddState("Empty");
                                m_StateMachines[0].defaultState = newAnimState;
                            }
                            EditorGUILayout.LabelField($"*New Default Empty Animation State", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                        }
                    }
                    else
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button(GC_ASM, GUILayout.Height(22), GUILayout.Width(80)))
                            {
                                NewModeTransitions();
                            }
                            EditorGUILayout.LabelField($"*New Mode Transitions. <Check Any>", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                        }
                    }
                }
            }

            if (Animal && HAS_AS && Mode != null)
            {

                using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    if (GUILayout.Button(gc_modes, GUILayout.Height(22), GUILayout.Width(80)))
                    {
                        AddModesAnimalComponent();

                        Debug.Log($"<color=green><b>[{Animal.name}]. Mode [{Mode.name}] Abilities Updated</b></color>");

                    }
                    EditorGUILayout.LabelField($"Adds Mode and Abilities to [{Animal.name}]", EditorStyles.boldLabel, GUILayout.MinWidth(50));

                    //MWC: One-click button that runs Entry Transitions + Exit Transitions + Add Mode and Abilities
                    var guiColor = GUI.color;
                    GUI.color = (Color.green + Color.white) / 2;
                    if (GUILayout.Button(new GUIContent(" Set Everything", gc_modes.image,
                        "Runs Add Entry Transitions, Add Exit Transitions and Add Mode and Abilities in one click"), GUILayout.Height(22)))
                    {
                        SetEverything();
                    }
                    GUI.color = guiColor;
                }

                MalbersEditor.DrawDescription($"Animal [{Animal.name}] selected.\nModes and Abilities will be created on the MAnimal Component");

                //using (new GUILayout.HorizontalScope())
                //{
                //    //ADD NEW!!!!!! EXIT AND INTERRUPTED TRANSITIONS
                //    if (GUILayout.Button(GC_T, GUILayout.Height(22), GUILayout.Width(80)))
                //    {
                //        for (int i = 0; i < m_AnimatorStates.Length; i++)
                //        {
                //            var AS = m_AnimatorStates[i];

                //            var InterruptCondition = new AnimatorCondition
                //            {
                //                parameter = "Mode",
                //                mode = AnimatorConditionMode.NotEqual,
                //                threshold = (Mode.ID * 1000 + ModeAbilitiesIndex[i])
                //            };

                //            ExitTransition(AS, "Interrupted [N]", false, 0.8f, 0.2f, 0,
                //                TransitionInterruptionSource.None, new AnimatorCondition[] { InterruptCondition });
                //        }
                //        EditorUtility.SetDirty(controller);

                //        Debug.Log("<color=green><b> [NEW * Interrupted] Mode Transtion Created</b></color>");

                //    }
                //    EditorGUILayout.LabelField($"[Interrupted] Transtion -> Mode [NOT EQUAL]", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                //}

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (var X = new GUILayout.ScrollViewScope(Scroll))
                    {
                        Scroll = X.scrollPosition;
                        using (new GUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField($" Motion", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                            EditorGUILayout.LabelField($" Ability Name", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                            EditorGUILayout.LabelField($" Index", EditorStyles.boldLabel, GUILayout.Width(50));
                            //MWC: Extra columns to edit the Mode Behaviour of each selected state (widths match their input boxes below)
                            EditorGUILayout.LabelField($"No Exit", EditorStyles.boldLabel, GUILayout.Width(50));
                            EditorGUILayout.LabelField($"Exit Time", EditorStyles.boldLabel, GUILayout.Width(50));
                        }

                        var AnimalMode = Animal.modes.Find(x => x.ID == Mode.ID);


                        for (int i = 0; i < m_AnimatorStates.Length; i++)
                        {
                            using (new GUILayout.HorizontalScope())
                            {
                                using (new EditorGUI.DisabledGroupScope(true))
                                    EditorGUILayout.ObjectField(m_AnimatorStates[i].motion, typeof(Motion), false, GUILayout.MinWidth(50));

                                ModeAbilities[i] = EditorGUILayout.TextField(ModeAbilities[i], GUILayout.MinWidth(50));
                                ModeAbilitiesIndex[i] = EditorGUILayout.IntField(ModeAbilitiesIndex[i], GUILayout.Width(50));

                                //MWC: Edit the Mode Behaviour values for this state. If the state has no Mode Behaviour the fields are drawn disabled and ignored.
                                var modeB = m_AnimatorStates[i].behaviours?.FirstOrDefault(b => b is ModeBehaviour) as ModeBehaviour;

                                using (new EditorGUI.DisabledGroupScope(modeB == null))
                                {
                                    bool noExit = modeB != null && modeB.NoExitTransitions;
                                    float exitTime = modeB != null ? modeB.ExitTime : 0.95f;

                                    EditorGUI.BeginChangeCheck();
                                    noExit = EditorGUILayout.Toggle(noExit, GUILayout.Width(50));
                                    exitTime = EditorGUILayout.FloatField(exitTime, GUILayout.Width(50));

                                    if (EditorGUI.EndChangeCheck() && modeB != null)
                                    {
                                        modeB.NoExitTransitions = noExit;
                                        modeB.ExitTime = exitTime;
                                        MTools.SetDirty(modeB);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        //MWC: Entry Transitions logic (entry transitions + ModeBehaviour + loop transition). Reused by the Entry button and Set Everything.
        private void AddEntryTransitions()
        {
            DoModeStateTransition();

            //MODE BEHAVIOUR
            foreach (var AS in m_AnimatorStates)
            {
                bool hasModeB = AS.behaviours.ToList().Exists(x => x is ModeBehaviour);

                if (!hasModeB)
                {
                    var ModeB = AS.AddStateMachineBehaviour<ModeBehaviour>();
                    ModeB.ModeID = Mode;

                    ModeB.NoExitTransitions = IgnoreExitTransition;
                    if (IgnoreExitTransition) ModeB.ExitTime = m_TransitionExitTime;

                    MTools.SetDirty(ModeB);
                }
            }

            //LOOP TRANSITION (only when enabled; skip if the state already has one)
            if (AddLoopTransition)
                foreach (var AS in m_AnimatorStates)
                    if (!HasLoopTransition(AS)) LoopTransition(AS);
        }

        //MWC: Exit Transitions logic (default exit + interrupted). Reused by the Exit button and Set Everything.
        //If a state already has exit transitions, only their duration is updated instead of adding duplicates.
        private void AddExitTransitions()
        {
            for (int i = 0; i < m_AnimatorStates.Length; i++)
            {
                var AS = m_AnimatorStates[i];

                //MWC: Existing exit transitions -> update only the duration
                var existingExits = AS.transitions.Where(t => t.isExit).ToList();
                if (existingExits.Count > 0)
                {
                    foreach (var t in existingExits)
                        t.duration = ModeExitDuration;
                    continue;
                }

                //ADD EXIT AND INTERRUPTED TRANSITIONS
                ExitTransition(AS, "Exit", true, 0.8f, ModeExitDuration); //use the Exit transition duration from the tool

                var InterruptCondition = new AnimatorCondition
                {
                    parameter = "Mode",
                    mode = AnimatorConditionMode.NotEqual,
                    threshold = (Mode.ID * 1000 + ModeAbilitiesIndex[i])
                };

                ExitTransition(AS, "Interrupted [N]", false, 0.8f, ModeExitDuration, 0,
                    TransitionInterruptionSource.None, new AnimatorCondition[] { InterruptCondition });
            }
        }

        //MWC: Returns the existing AnyState mode-entry transition into this state (Mode Equals ID*1000+index), or null.
        private AnimatorStateTransition FindModeEntryTransition(AnimatorState state)
        {
            if (Mode == null) return null;

            var layerSM = FindAnyState(state);
            if (layerSM == null) return null;

            int baseValue = Mode.ID * 1000;

            foreach (var t in layerSM.anyStateTransitions)
            {
                if (t.destinationState != state) continue;

                foreach (var c in t.conditions)
                {
                    if (c.parameter == "Mode" && c.mode == AnimatorConditionMode.Equals)
                    {
                        int threshold = Mathf.RoundToInt(c.threshold);
                        if (threshold >= baseValue && threshold < baseValue + 1000) return t;
                    }
                }
            }
            return null;
        }

        //MWC: True if the state already has a self (loop) transition driven by the ModeStatus parameter.
        private bool HasLoopTransition(AnimatorState state)
        {
            foreach (var t in state.transitions)
            {
                if (t.destinationState != state) continue;
                foreach (var c in t.conditions)
                    if (c.parameter == "ModeStatus") return true;
            }
            return false;
        }

        //MWC: One-click setup - runs Entry Transitions, Exit Transitions (unless Ignore Exit) and Add Mode and Abilities.
        private void SetEverything()
        {
            AddEntryTransitions();

            if (!IgnoreExitTransition) AddExitTransitions();

            AddModesAnimalComponent();

            EditorUtility.SetDirty(controller);

            Debug.Log($"<color=green><b>[{Animal.name}]. Mode [{Mode.name}] - Set Everything completed (Entry + Exit + Abilities)</b></color>");
        }

        private void NewModeTransitions()
        {
            foreach (var ASM in m_StateMachines)
            {
                if (isLayerRoot(ASM)) continue; //Do not do the Root Layer

                Debug.Log("ASM = " + ASM.name);
                var Parent = FindAnyState(ASM);
                var Any_ToState = Parent.AddAnyStateTransition(ASM); //Add Any to State Machine
                Create_AnyToStateMachineMode(Any_ToState);
                Parent.AddStateMachineExitTransition(ASM);
            }
        }

        private void DrawModeConditions()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                //EditorGUILayout.LabelField($"Modes", EditorStyles.boldLabel);

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = 80;

                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Mode"));

                    EditorGUIUtility.labelWidth = 0;

                    if (Mode != null)
                    {
                        using (new EditorGUI.DisabledGroupScope(true))
                            EditorGUILayout.IntField(Mode.ID, GUILayout.Width(50));
                    }

                    if (GUILayout.Button("[Action] Abilities ID",/* GUILayout.Height(22),*/ GUILayout.Width(140)))
                    {
                        var Modal = ShowIDWindow.ShowWindow();
                        Modal.Editor_Tabs1 = 4; //Show Actions
                    }
                }

                using (new GUILayout.HorizontalScope())
                {
                    //MWC: Toggle to control whether the Mode field is cleared when the selected state(s) have no ModeBehaviour
                    ClearModeOnNoMatch = GUILayout.Toggle(ClearModeOnNoMatch,
                        new GUIContent("Clear Mode on No Match",
                        "When ON, selecting a state with no Mode Behaviour clears the Mode field.\nWhen OFF, the previously selected Mode is kept."));

                    //MWC: Toggle to control whether the Entry Transitions step also adds the self Loop Transition
                    AddLoopTransition = GUILayout.Toggle(AddLoopTransition,
                        new GUIContent("Add Loop Transition",
                        "When ON, the Entry Transitions step also adds the self Loop Transition (ModeStatus == -1) to each state."));
                }
            }
        }

        private void AddModesAnimalComponent()
        {
            //Add on the Animal the mode and the new abilities
            Mode animalMode = Animal.modes.Find(x => x.ID == Mode);

            if (animalMode == null)
            {
                animalMode = new Mode()
                {
                    ID = this.Mode,
                    AllowRotation = true,
                    AllowMovement = true,
                    DefaultIndex = new Scriptables.IntReference(),
                    AbilityIndex = 0
                };

                Animal.modes.Add(animalMode);
            }

            if (animalMode.Abilities == null) animalMode.Abilities = new List<Ability>();

            for (int i = 0; i < ModeAbilities.Length; i++)
            {
                var Index = ModeAbilitiesIndex[i];

                //do not add one alreadu creadte
                if (animalMode.Abilities.Exists(x => x.Name == ModeAbilities[i] || x.Index.Value == Index)) continue;

                animalMode.Abilities.Add(new Ability() { Name = ModeAbilities[i], Index = new Scriptables.IntReference(Index) });

                UnityEditor.EditorUtility.SetDirty(Animal);

                m_AnimatorStates[i].name = ModeAbilities[i]; //rename the Animator State
            }
            EditorUtility.SetDirty(controller);
        }

        private bool isLayerStateMachine(AnimatorStateMachine animatorStateMachine)
        {
            foreach (var item in controller.layers)
            {
                if (item.stateMachine == animatorStateMachine) return true;
            }
            return false;
        }

        private void DoModeStateTransition()
        {
            for (int i = 0; i < m_AnimatorStates.Length; i++)
            {
                var AS = m_AnimatorStates[i];

                //MWC: If the entry transition already exists for this state, only update its duration instead of adding a duplicate
                var existing = FindModeEntryTransition(AS);
                if (existing != null)
                {
                    existing.duration = ModeEntryDuration;
                    continue;
                }

                var Parent = FindAnyState(AS);
                var Any_ToState = Parent.AddAnyStateTransition(AS); //Add Any
                Any_ToState.duration = ModeEntryDuration; //MWC: use the Entry transition duration from the tool
                Any_ToState.offset = 0;
                Any_ToState.hasExitTime = false;


                var ModeOn = new AnimatorCondition
                {
                    parameter = "ModeOn",
                    mode = AnimatorConditionMode.If
                };

                var Mode__Value = new AnimatorCondition
                {
                    parameter = "Mode",
                    mode = AnimatorConditionMode.Equals,
                    threshold = (Mode.ID * 1000 + ModeAbilitiesIndex[i])
                };

                Any_ToState.conditions = new AnimatorCondition[2] { ModeOn, Mode__Value };
            }
        }
        private void DoStances()
        {
            if (controller == null) return;

            DrawStanceConditions();

            if (HAS_AS && Stance != null)
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(GC_T, GUILayout.Height(22), GUILayout.Width(80)))
                    {
                        foreach (var St in m_AnimatorStates)
                        {
                            var Parent = FindParentSM(St);
                            var T = Parent.AddEntryTransition(St);

                            if (State != null) St.tag = State.name;


                            T.AddCondition(AnimatorConditionMode.Equals, Stance.ID, "Stance");

                            T.name = $"to: [{Stance.name}]";

                            if (LastStance != null)
                            {
                                T.AddCondition(AnimatorConditionMode.Equals, LastStance.ID, "LastStance");

                                T.name = $"to: [{Stance.name}] from [{LastStance.name}]";
                            }
                        }
                    }
                    EditorGUILayout.LabelField($"[Start] Transition from <Entry>", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                }

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(GC_T, GUILayout.Height(22), GUILayout.Width(80)))
                    {
                        foreach (var St in m_AnimatorStates)
                        {
                            if (State != null) St.tag = State.name;

                            var Parent = FindParentSM(St);
                            var T = Parent.AddAnyStateTransition(St);

                            T.duration = 0.2f;
                            T.offset = 0;
                            T.hasExitTime = false;
                            T.name = $"Any to: [{Stance.name}]";

                            T.AddCondition(AnimatorConditionMode.If, 0, "StateOn");
                            T.AddCondition(AnimatorConditionMode.Equals, State.ID, "State");
                            T.AddCondition(AnimatorConditionMode.Equals, Stance.ID, "Stance");
                            if (LastStance != null)
                            {
                                T.AddCondition(AnimatorConditionMode.Equals, LastStance.ID, "LastStance");
                                T.name = $"Any to: [{Stance.name}] from [{LastStance.name}]";
                            }
                        }

                        Debug.Log("Created Transition. Please check Any State");
                    }
                    EditorGUILayout.LabelField($"[Start] Transition from <AnyState>", EditorStyles.boldLabel, GUILayout.MinWidth(50));
                }
            }
        }

        private void DrawStanceConditions()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Stance"));
                    if (Stance != null)
                    {
                        using (new EditorGUI.DisabledGroupScope(true))
                            EditorGUILayout.IntField(Stance.ID, GUILayout.Width(50));
                    }
                }


                if (Stance != null)
                {
                    // EditorGUILayout.LabelField($"Sta", EditorStyles.boldLabel);
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("State"));
                        if (State != null)
                        {
                            using (new EditorGUI.DisabledGroupScope(true))
                                EditorGUILayout.IntField(State.ID, GUILayout.Width(50));
                        }
                    }

                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("LastStance"));
                        if (LastStance != null)
                        {
                            using (new EditorGUI.DisabledGroupScope(true))
                                EditorGUILayout.IntField(LastStance.ID, GUILayout.Width(50));
                        }
                    }
                }
            }
        }

        private void DoTransitions()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"   Transitions", EditorStyles.boldLabel);

                if (HAS_AS)
                {
                    #region Default Exit Transition
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(GC_AS, GUILayout.Height(22), GUILayout.Width(80)))
                        {
                            foreach (var AS in m_AnimatorStates)
                            {
                                ExitTransition(AS);
                            }

                            //foreach (var AS in m_StateMachines) //EXIT TRANSITIONS CANNOT BE CREATED  for STATE MACHINES
                            //{
                            //    ExitTransition(AS);
                            //}
                        }
                        EditorGUILayout.LabelField($"*New [Default] Exit Transition", EditorStyles.boldLabel);
                    }
                    #endregion
                }

                if (HAS_AS || HAS_ASM)
                {
                    #region Default Loop Mode Transition
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(GC_T, GUILayout.Height(22), GUILayout.Width(80)))
                        {
                            foreach (var AS in m_AnimatorStates)
                            {
                                LoopTransition(AS);
                            }

                            foreach (var AS in m_StateMachines)
                            {
                                LoopTransitionASM(AS);
                            }
                        }
                        EditorGUILayout.LabelField($"*New Loop Transition [Mode-Ability]" +
                            $"[{m_AnimatorStates.Length + m_StateMachines.Length}]", EditorStyles.boldLabel);
                    }
                    #endregion
                }

                if (m_Transitions.Length > 0)
                {
                    if (controller && controller.parameters.Length > 0)
                    {
                        string[] Params = new string[controller.parameters.Length];

                        for (int i = 0; i < Params.Length; i++)
                        {
                            Params[i] = controller.parameters[i].name;
                        }

                        var param = controller.parameters[SelectedParameter];
                        AnimatorConditionMode conditionMode = AnimatorConditionMode.If;

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUI.indentLevel++;
                            CreateNewConditionF = GUILayout.Toggle(CreateNewConditionF, "Create *New* Condition", EditorStyles.foldoutHeader);
                            EditorGUI.indentLevel--;

                            if (CreateNewConditionF)
                            {
                                using (new GUILayout.HorizontalScope())
                                {
                                    SelectedParameter = EditorGUILayout.Popup(SelectedParameter, Params.ToArray());

                                    switch (param.type)
                                    {
                                        case AnimatorControllerParameterType.Float:
                                            condition = EditorGUILayout.Popup(condition, new string[2] { "Greater", "Less" });

                                            if (condition == 0) conditionMode = AnimatorConditionMode.Greater;
                                            else if (condition == 1) conditionMode = AnimatorConditionMode.Less;

                                            value = EditorGUILayout.FloatField(value);
                                            break;
                                        case AnimatorControllerParameterType.Int:
                                            condition = EditorGUILayout.Popup(condition, new string[4] { "Greater", "Less", "Equal", "Not Equal" });

                                            if (condition == 0) conditionMode = AnimatorConditionMode.Greater;
                                            else if (condition == 1) conditionMode = AnimatorConditionMode.Less;
                                            else if (condition == 2) conditionMode = AnimatorConditionMode.Equals;
                                            else if (condition == 3) conditionMode = AnimatorConditionMode.NotEqual;



                                            value = EditorGUILayout.FloatField(value);
                                            break;
                                        case AnimatorControllerParameterType.Bool:
                                            condition = EditorGUILayout.Popup(condition, new string[2] { "True", "False" });
                                            if (condition == 0) conditionMode = AnimatorConditionMode.If;
                                            else if (condition == 1) conditionMode = AnimatorConditionMode.IfNot;
                                            break;
                                        case AnimatorControllerParameterType.Trigger:
                                            break;
                                        default:
                                            break;
                                    }


                                    if (GUILayout.Button(plus, GUILayout.Width(28), GUILayout.Height(18)))
                                    {
                                        foreach (var transition in m_Transitions)
                                        {
                                            transition.AddCondition(conditionMode, value, param.name);
                                        }
                                        EditorUtility.SetDirty(controller);
                                    }
                                }
                            }
                        }
                    }

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUI.indentLevel++;
                        ModifyTV = GUILayout.Toggle(ModifyTV, "Update Transition Values", EditorStyles.foldoutHeader);
                        EditorGUI.indentLevel--;

                        if (ModifyTV)
                        {
                            using (new GUILayout.HorizontalScope())
                            {
                                //MWC: showMixedValue draws a "—" indicator when selected transitions differ; editing clears the mixed state
                                EditorGUI.showMixedValue = mixedExitTime;
                                EditorGUI.BeginChangeCheck();
                                m_ExitTime = EditorGUILayout.FloatField("Exit Time", m_ExitTime);
                                if (EditorGUI.EndChangeCheck()) mixedExitTime = false;
                                EditorGUI.showMixedValue = false;

                                if (GUILayout.Button(updateB, GUILayout.Width(28)))
                                {
                                    foreach (var transition in m_Transitions)
                                        if (transition is AnimatorStateTransition) (transition as AnimatorStateTransition).exitTime = m_ExitTime;
                                    mixedExitTime = false; //all transitions now share this value
                                }
                            }

                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUI.showMixedValue = mixedFixedDuration;
                                EditorGUI.BeginChangeCheck();
                                m_FixedDuration = EditorGUILayout.Toggle("Fixed Duration", m_FixedDuration);
                                if (EditorGUI.EndChangeCheck()) mixedFixedDuration = false;
                                EditorGUI.showMixedValue = false;

                                if (GUILayout.Button(updateB, GUILayout.Width(28)))
                                {
                                    foreach (var transition in m_Transitions)
                                        if (transition is AnimatorStateTransition) (transition as AnimatorStateTransition).hasFixedDuration = m_FixedDuration;
                                    mixedFixedDuration = false;
                                }
                            }

                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUI.showMixedValue = mixedTransitionDuration;
                                EditorGUI.BeginChangeCheck();
                                m_TransitionDuration = EditorGUILayout.FloatField("Transition Duration", m_TransitionDuration);
                                if (EditorGUI.EndChangeCheck()) mixedTransitionDuration = false;
                                EditorGUI.showMixedValue = false;

                                if (GUILayout.Button(updateB, GUILayout.Width(28)))
                                {
                                    foreach (var transition in m_Transitions)
                                        if (transition is AnimatorStateTransition) (transition as AnimatorStateTransition).duration = m_TransitionDuration;
                                    mixedTransitionDuration = false;
                                }
                            }

                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUI.showMixedValue = mixedTransitionOffset;
                                EditorGUI.BeginChangeCheck();
                                m_TransitionOffset = EditorGUILayout.FloatField("Transition Offset", m_TransitionOffset);
                                if (EditorGUI.EndChangeCheck()) mixedTransitionOffset = false;
                                EditorGUI.showMixedValue = false;

                                if (GUILayout.Button(updateB, GUILayout.Width(28)))
                                {
                                    foreach (var transition in m_Transitions)
                                        if (transition is AnimatorStateTransition) (transition as AnimatorStateTransition).offset = m_TransitionOffset;
                                    mixedTransitionOffset = false;
                                }
                            }

                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUI.showMixedValue = mixedInterruptionSource;
                                EditorGUI.BeginChangeCheck();
                                m_InterruptionSource = (TransitionInterruptionSource)EditorGUILayout.EnumPopup("Interruption Source", m_InterruptionSource);
                                if (EditorGUI.EndChangeCheck()) mixedInterruptionSource = false;
                                EditorGUI.showMixedValue = false;

                                if (GUILayout.Button(updateB, GUILayout.Width(28)))
                                {
                                    foreach (var transition in m_Transitions)
                                        if (transition is AnimatorStateTransition) (transition as AnimatorStateTransition).interruptionSource = m_InterruptionSource;
                                    mixedInterruptionSource = false;
                                }
                            }

                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUI.showMixedValue = mixedOrderedInterruption;
                                EditorGUI.BeginChangeCheck();
                                m_OrderedInterruption = EditorGUILayout.Toggle("Ordered Interruption", m_OrderedInterruption);
                                if (EditorGUI.EndChangeCheck()) mixedOrderedInterruption = false;
                                EditorGUI.showMixedValue = false;

                                if (GUILayout.Button(updateB, GUILayout.Width(28)))
                                {
                                    foreach (var transition in m_Transitions)
                                        if (transition is AnimatorStateTransition) (transition as AnimatorStateTransition).orderedInterruption = m_OrderedInterruption;
                                    mixedOrderedInterruption = false;
                                }
                            }

                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUI.showMixedValue = mixedTransitionToSelf;
                                EditorGUI.BeginChangeCheck();
                                m_TransitionToSelf = EditorGUILayout.Toggle("Can transition to Itself", m_TransitionToSelf);
                                if (EditorGUI.EndChangeCheck()) mixedTransitionToSelf = false;
                                EditorGUI.showMixedValue = false;

                                if (GUILayout.Button(updateB, GUILayout.Width(28)))
                                {
                                    foreach (var transition in m_Transitions)
                                        if (transition is AnimatorStateTransition) (transition as AnimatorStateTransition).canTransitionToSelf = m_TransitionToSelf;
                                    mixedTransitionToSelf = false;
                                }
                            }

                            //MWC: Apply every field above to all selected transitions at once
                            EditorGUILayout.Space(2);
                            if (GUILayout.Button(new GUIContent("Apply All",
                                "Apply all the values above to every selected transition"), GUILayout.Height(22)))
                            {
                                ApplyAllTransitionValues();
                            }
                        }
                    }

                    using (var X = new GUILayout.ScrollViewScope(Scroll))
                    {
                        Scroll = X.scrollPosition;

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            foreach (var AS in m_Transitions)
                            {
                                var TName = AS.isExit ? "Exit" : "T";


                                if (AS.destinationState != null) TName = AS.destinationState.name;
                                else if (AS.destinationStateMachine != null) TName = AS.destinationStateMachine.name;

                                if (!string.IsNullOrEmpty(AS.name)) TName += $" [{AS.name}]";

                                EditorGUILayout.LabelField($"Transition to: {TName}");
                            }
                        }
                    }
                }
            }
        }

        private void LoopTransition(AnimatorState _AS)
        {
            var loopTransition = _AS.AddTransition(_AS, true);

            loopTransition.AddCondition(AnimatorConditionMode.Equals, -1, "ModeStatus");

            loopTransition.name = "Loop";
            loopTransition.exitTime = 0.7f;
            loopTransition.duration = 0.25f;
            loopTransition.offset = 0.15f;
            loopTransition.interruptionSource = TransitionInterruptionSource.Destination;

        }
        private void LoopTransitionASM(AnimatorStateMachine _AS)
        {
            var loopTransition = FindParentSM(_AS).AddStateMachineTransition(_AS, _AS);
            loopTransition.AddCondition(AnimatorConditionMode.Equals, -1, "ModeStatus");
            loopTransition.name = "Loop";

            Debug.Log("_AS = " + _AS.name);

            foreach (var SMM in _AS.stateMachines)
            {
                var SM = SMM.stateMachine;
                foreach (var item in _AS.GetStateMachineTransitions(SM))
                {
                    Debug.Log("item = " + item.name);
                }
            }


            //loopTransition.exitTime = 0.55f;
            //loopTransition.duration = 0.2f;
            //loopTransition.offset = 0.2f;
            //loopTransition.interruptionSource = TransitionInterruptionSource.Destination;
        }


        public void AddTransitionCondition(AnimatorTransitionBase transition, AnimatorControllerParameter param, AnimatorConditionMode condition, float value)
        {
            if (transition == null) return;

            transition.AddCondition(condition, value, param.name);
        }


        private void ExitTransition(AnimatorState AS)
        {
            ExitTransition(AS, "Exit", true, 0.8f, 0.2f);
        }

        //private void ExitTransition(AnimatorStateMachine AS)
        //{
        //    ExitTransition(AS, "Exit", null);
        //}

        private void ExitInterruptedMode(AnimatorState AS)
        {
            var InterruptCondition = new AnimatorCondition
            {
                parameter = "ModeStatus",
                mode = AnimatorConditionMode.Equals,
                threshold = -2    //CAMBIAR A EXIT MODE
            };

            ExitTransition(AS, "Interrupted", false, 0.8f, 0.2f, 0, TransitionInterruptionSource.Destination, new AnimatorCondition[1] { InterruptCondition });
        }


        private AnimatorStateTransition ExitTransition(AnimatorState AS, string name = "",
            bool hasExitTime = false, float exitTime = 0.8f, float duration = 0.2f,
            float offset = 0, TransitionInterruptionSource intSource = TransitionInterruptionSource.Destination, AnimatorCondition[] conditions = null)
        {
            var transition = AS.AddExitTransition();
            transition.hasExitTime = hasExitTime;
            transition.exitTime = exitTime;
            transition.duration = duration;
            transition.offset = offset;
            if (name != "") transition.name = name;
            transition.interruptionSource = intSource;
            transition.conditions = conditions;

            return transition;
        }


        private AnimatorTransition ExitTransition(AnimatorStateMachine ASM, string name = "", AnimatorCondition[] conditions = null)
        {
            var parent = FindParentSM(ASM);
            Debug.Log("transition = " + parent.name);

            var transition = ASM.AddStateMachineExitTransition(parent);
            transition.isExit = true;

            if (name != "") transition.name = name;
            transition.conditions = conditions;

            return transition;
        }



        //private void AnyState()
        //{
        //    foreach (var SM in m_StateMachines)
        //    {
        //        // SM.name
        //    }
        //}

        public AnimatorStateMachine FindParentSM(AnimatorState child)
        {
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i]; //Store the First State Machine which is the Layer

                var result = FindParentSM(layer.stateMachine, child);
                if (result != null) return result;
            }
            return null;
        }


        public bool isLayerRoot(AnimatorStateMachine ASM)
        {
            var layer = controller.layers[0]; //Store the First State Machine which is the Layer
            if (layer.stateMachine == ASM) return true; //is the Same Layer SM... ignore!
            return false;
        }



        public AnimatorStateMachine FindParentSM(AnimatorStateMachine child)
        {
            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i]; //Store the First State Machine which is the Layer

                if (layer.stateMachine == child) return layer.stateMachine; //is the Same Layer SM... ignore!

                var result = FindParentSM(layer.stateMachine, child);
                if (result != null) return result;
            }
            return null;
        }

        public AnimatorStateMachine FindParentSM(AnimatorStateMachine Parent, AnimatorState child)
        {
            AnimatorStateMachine result = null;

            //Check all State Machine States to see if it correspond to see if it the AnimatorState Parent
            for (int i = 0; i < Parent.states.Length; i++)
            {
                if (Parent.states[i].state == child)
                {
                    return Parent;
                }
            }

            //If the child was not found Search all the StateMachines children then
            for (int i = 0; i < Parent.stateMachines.Length; i++)
            {
                result = FindParentSM(Parent.stateMachines[i].stateMachine, child);
                if (result != null) return result;
            }
            return result;
        }


        public AnimatorStateMachine FindParentSM(AnimatorStateMachine Parent, AnimatorStateMachine child)
        {
            AnimatorStateMachine result = null;

            //Check all State Machine States to see if it correspond to see if it the AnimatorState Parent
            for (int i = 0; i < Parent.stateMachines.Length; i++)
            {
                if (Parent.stateMachines[i].stateMachine == child) return Parent;
            }

            //If the child was not found Search all the StateMachines children then
            for (int i = 0; i < Parent.stateMachines.Length; i++)
            {
                var SM = Parent.stateMachines[i].stateMachine;

                if (SM == child) return Parent;


                result = FindParentSM(SM, child);
                if (result != null) return result;
            }
            return result;
        }


        public AnimatorStateMachine FindAnyState(AnimatorState child)
        {
            if (controller == null) return null; //MWC: guard against null controller (e.g. during OnEnable before it's resolved)

            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i]; //Store the First State Machine which is the Layer

                var result = FindParentSM(layer.stateMachine, child);
                if (result != null) return layer.stateMachine;
            }
            return null;
        }

        public AnimatorStateMachine FindAnyState(AnimatorStateMachine child)
        {
            if (controller == null) return null; //MWC: guard against null controller (e.g. during OnEnable before it's resolved)

            for (int i = 0; i < controller.layers.Length; i++)
            {
                var layer = controller.layers[i]; //Store the First State Machine which is the Layer

                var result = FindParentSM(layer.stateMachine, child);
                if (result != null) return layer.stateMachine;
            }
            return null;
        }


        public static AnimatorState Animator_FindState(AnimatorStateMachine rStateMachine, string rName)
        {
            for (int i = 0; i < rStateMachine.states.Length; i++)
            {
                if (rStateMachine.states[i].state.name == rName)
                {
                    return rStateMachine.states[i].state;
                }
            }

            return null;
        }
        public static AnimatorStateTransition Animator_FindTransition(AnimatorState rFrom, AnimatorState rTo, int rIndex)
        {
            int lIndex = -1;

            for (int i = 0; i < rFrom.transitions.Length; i++)
            {
                if (rFrom.transitions[i].destinationState == rTo)
                {
                    lIndex++;
                    if (lIndex == rIndex)
                    {
                        return rFrom.transitions[i];
                    }
                }
            }

            return null;
        }
        public static AnimatorStateTransition Animator_FindAnyStateTransition(AnimatorStateMachine rFrom, AnimatorState rTo, int rIndex)
        {
            int lIndex = -1;

            for (int i = 0; i < rFrom.anyStateTransitions.Length; i++)
            {
                if (rFrom.anyStateTransitions[i].destinationState == rTo)
                {
                    lIndex++;
                    if (lIndex == rIndex)
                    {
                        return rFrom.anyStateTransitions[i];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a blend tree that we can assign to a node
        /// </summary>
        /// <param name="rName"></param>
        /// <param name="rAnimatorController"></param>
        /// <param name="rAnimatorLayer"></param>
        /// <returns></returns>
        public static BlendTree Animator_CreateBlendTree(string rName, AnimatorController rAnimatorController, int rAnimatorLayer)
        {
            // Create the blend tree. This is so bad, but apparently what we have to do or the blend
            // tree vanishes after we run.
            BlendTree lBlendTree;
            AnimatorState lDeleteState = rAnimatorController.CreateBlendTreeInController("DELETE_ME", out lBlendTree);

            lBlendTree.name = rName;
            lBlendTree.blendType = BlendTreeType.SimpleDirectional2D;
            lBlendTree.blendParameter = "Input X";
            lBlendTree.blendParameterY = "Input Y";

            // Get rid of the dummy state we created to create the blend tree
            lDeleteState.motion = null;
            rAnimatorController.layers[rAnimatorLayer].stateMachine.RemoveState(lDeleteState);

            return lBlendTree;
        }
    }
}
#endif