// Last Change: 10/14/2026 2:45pm
using System.Collections.Generic;
using UnityEngine;
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using UnityEngine.Serialization;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Controller.AI
{
    [AddComponentMenu("Malbers/Animal Controller/AI/Animal Brain")]
    public class MAnimalBrain : MonoBehaviour, IAnimatorListener
    {
        //Every enabled Brain registers itself so other systems can find AI without FindObjectsByType scans
        private static readonly List<MAnimalBrain> brains = new();

        /// <summary>Every enabled Brain in the loaded scenes (read only)</summary>
        public static IReadOnlyList<MAnimalBrain> Brains => brains;

        /// <summary>Invoked when a Brain is enabled and added to <see cref="Brains"/></summary>
        public static event Action<MAnimalBrain> BrainEnabled;

        /// <summary>Invoked when a Brain is disabled and removed from <see cref="Brains"/></summary>
        public static event Action<MAnimalBrain> BrainDisabled;

        /// <summary>Clears the registry when Domain Reload is disabled (Enter Play Mode options)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetBrains()
        {
            brains.Clear();
            BrainEnabled = null;
            BrainDisabled = null;
        }

        /// <summary>Reference for the Ai Control Movement</summary>
        public IAIControl AIControl;

        /// <summary>Transform used to raycast Rays to interact with the world</summary>
        [RequiredField, Tooltip("Transform used to raycast Rays to interact with the world")]
        public Transform Eyes;
        /// <summary>Time needed to make a new transition. Necessary to avoid Changing to multiple States in the same frame</summary>
        [Tooltip("Time needed to make a new transition. Necessary to avoid Changing to multiple States in the same frame")]
        public FloatReference TransitionCoolDown = new(0.2f);

        /// <summary>Reference AI State for the animal</summary>
        [CreateScriptableAsset] public MAIState currentState;

        [Tooltip("Removes all AI Components when the Animal Dies. (Brain, AiControl, Agent)")]
        [FormerlySerializedAs("RemoveAIOnDeath")]
        public bool DisableAIOnDeath = true;
        public bool debug = false;
        public bool debugAIStates = false;


        public IntEvent OnTaskStarted = new();
        public IntEvent OnTaskDone = new();
        public IntEvent OnDecisionSucceeded = new();
        public IntEvent OnAIStateChanged = new();

        /// <summary>Last Time the Animal make a new transition</summary>
        private float TransitionLastTime;

        /// <summary>Last Time the Animal  started a transition</summary>
        public float StateLastTime { get; set; }

        /// <summary>Check if all the Task are done..</summary>
        public bool AllTasksDone()
        {
            //MWC: TasksDone is pre-allocated to MaxAlloc size, so only check the slots used by the current state's tasks.
            //Iterating the whole array made this always return false because the unused slots stay false.
            if (currentState == null || currentState.tasks == null) return true;

            for (int i = 0; i < currentState.tasks.Length; i++)
            {
                if (!TasksDone[i]) return false;
            }
            return true;
        }

        /// <summary>Check if an Specific Task is Done..</summary>
        public void TaskDone(MTask task)
        {
            if (currentState != null)
            {
                for (int i = 0; i < currentState.tasks.Length; i++)
                {
                    if (currentState.tasks[i] == task)
                    {
                        TaskDone(i);
                        break;
                    }
                }
            }
        }


        /// <summary>Check if an Specific Task is Done..</summary>
        public bool IsTasksDone(int index)
        {
            return TasksDone[index % TasksDone.Length];
        }


        [Tooltip("Maximum amount of memory allocated for the Brain Arrays.")]
        public int MaxAlloc = 20;

        /// <summary>Tasks Local Vars (1 Int,1 Bool,1 Float)</summary>
        public BrainVars[] TasksVars { get; set; }
        /// <summary>Saves on the a Task that it has finish is stuff</summary>
        internal bool[] TasksDone { get; set; }
        /// <summary>Store if a Task has Started</summary>
        internal bool[] TasksStarted { get; set; }

        /// <summary>Read only view of the Task Done flags (one slot per Task index of the current AI State)</summary>
        public IReadOnlyList<bool> TasksDoneList => TasksDone;
        /// <summary>Read only view of the Task Started flags (one slot per Task index of the current AI State)</summary>
        public IReadOnlyList<bool> TasksStartedList => TasksStarted;

        /// <summary>True if the Task at this index of the current AI State has started</summary>
        public bool IsTaskStarted(int TaskIndex) => TasksStarted != null && TasksStarted[TaskIndex];

        /// <summary>Time Elapsed for the Tasks on an AI State</summary>
        public float[] TasksStartTime { get; set; }
        public float[] TasksUpdateTime { get; set; }


        /// <summary>Current Decision Results</summary>
        internal bool[] DecisionResult { get; set; }
        /// <summary>Decision Local Vars to store values on Prepare Decision</summary>
        public BrainVars[] DecisionsVars { get; set; }
        internal bool BrainInitialize { get; set; }

        /// <summary>True while OnEnable is waiting for the AI Control to be ready before calling StartBrain</summary>
        private bool StartPending { get; set; }

        //MWC: Per-slot strongly-typed runtime state for Tasks/Decisions. Cleaner alternative to the fixed BrainVars
        //grab-bag: each behavior declares its own BrainState subclass with as many fields as it needs.
        /// <summary>Per-slot typed runtime state for Tasks (one entry per Task index)</summary>
        public BrainState[] TaskStates { get; set; }
        /// <summary>Per-slot typed runtime state for Decisions (one entry per Transition index)</summary>
        public BrainState[] DecisionStates { get; set; }

        #region Properties


        /// <summary>Reference for the Animal</summary>
        public MAnimal Animal { get; private set; }

        /// <summary>Reference for the AnimalStats</summary>
        public Stats AnimalStats { get; set; }

        #region Target References
        /// <summary>Reference for the Current Target the Animal is using</summary>
        public Transform Target { get; set; }
        //{ 
        //    get => target; 
        //    set 
        //    {
        //    target = value;
        //    }
        //}
        //private Transform target;

        /// <summary>Reference for the Target's  Animal Component</summary>
        public MAnimal TargetAnimal { get; set; }

        /// <summary>Reference for the Target's   Local Vars Component</summary>
        public ILocalVars TargetVars { get; set; }

        /// <summary>Reference for the Local Variables</summary>
        public ILocalVars LocalVars
        {
            get
            {
                return localVars ??= gameObject.GetComponent<ILocalVars>();
            }
            set
            {
                localVars = value;
            }
        }

        [SerializeField] private ILocalVars localVars;

        /// <summary>Reference Extra Local Variables</summary>
        public ILocalVars ExtraLocalVars { get; set; }

        public Vector3 Position => AIControl.Transform.position;

        public float AIHeight => Animal.transform.lossyScale.y * AIControl.StoppingDistance;

        /// <summary>True if the Current Target has Stats</summary>
        public bool TargetHasStats => TargetStats != null;

        /// <summary>Reference for the Target the Stats Component</summary>
        public Stats TargetStats { get; set; }
        #endregion

        /// <summary>Reference for the Last WayPoint the Animal used</summary>
        public IWayPoint LastWayPoint { get; set; }



        /// <summary>Time Elapsed for the State Decisions</summary>
        [HideInInspector] public float[] DecisionsTime;// { get; set; }

        #endregion

        #region Unity Callbakcs
        void Awake()
        {
            if (Animal == null) Animal = gameObject.FindComponent<MAnimal>();
            LocalVars ??= gameObject.FindInterface<ILocalVars>();
            LocalVars ??= gameObject.AddComponent<MLocalVars>();  //Add it if you do not have the component

            if (AIControl.IsUnityRefNull()) //CustomPatch: fixed wrong null-check for unity object interface type
                AIControl = gameObject.FindInterface<IAIControl>();

            AnimalStats = Animal.FindComponent<Stats>();

            AllocateArrays();
        }

        /// <summary>Creates the Task and Decision arrays (once). Called on Awake, callable earlier for Brains built in code or tests</summary>
        public void AllocateArrays()
        {
            if (TasksDone != null) return; //Already allocated

            //Optimize the Memory Allocations for the Brain Arrays
            TasksVars = new BrainVars[MaxAlloc];
            TasksUpdateTime = new float[MaxAlloc];
            TasksStartTime = new float[MaxAlloc];
            TasksDone = new bool[MaxAlloc];
            TasksStarted = new bool[MaxAlloc];

            DecisionsVars = new BrainVars[MaxAlloc];
            DecisionsTime = new float[MaxAlloc];
            DecisionResult = new bool[MaxAlloc];

            TaskStates = new BrainState[MaxAlloc];       //MWC: typed per-slot state (null refs, filled on demand)
            DecisionStates = new BrainState[MaxAlloc];   //MWC: typed per-slot state (null refs, filled on demand)
        }


        /// <summary>Subscribes to the AI Control and Animal events, then starts the Brain once the AI Control is ready</summary>
        public void OnEnable()
        {
            AIControl.TargetSet.AddListener(OnTargetSet);
            AIControl.OnArrived.AddListener(OnTargetArrived);

            //The position arrived event is not part of IAIControl, only the built-in AI Control has it
            if (AIControl is MAnimalAIControl aiControl) aiControl.OnTargetPositionArrived.AddListener(OnPositionArrived);

            Animal.OnStateChange.AddListener(OnAnimalStateChange);
            Animal.OnStanceChange.AddListener(OnAnimalStanceChange);
            Animal.OnModeStart.AddListener(OnAnimalModeStart);
            Animal.OnModeEnd.AddListener(OnAnimalModeEnd);

            //Invoke(nameof(StartBrain), 0.1f); //Start AI a Frame later;

            if (!brains.Contains(this)) brains.Add(this);
            BrainEnabled?.Invoke(this);

            StartPending = true;
            this.Delay_Action(() => !AIControl.AIReady, StartBrain);

            //StartBrain();
        }

        /// <summary>Removes every event listener and exits the tasks of the current AI State</summary>
        public void OnDisable()
        {
            AIControl.TargetSet.RemoveListener(OnTargetSet);
            AIControl.OnArrived.RemoveListener(OnTargetArrived);

            if (AIControl is MAnimalAIControl aiControl) aiControl.OnTargetPositionArrived.RemoveListener(OnPositionArrived);

            ListenTargetAnimal(null); //Stop listening to the Target Animal events


            Animal.OnStateChange.RemoveListener(OnAnimalStateChange);
            Animal.OnStanceChange.RemoveListener(OnAnimalStanceChange);
            Animal.OnModeStart.RemoveListener(OnAnimalModeStart);
            Animal.OnModeEnd.RemoveListener(OnAnimalModeEnd);

            //  AIControl.Stop();
            StopAllCoroutines();
            StartPending = false;

            brains.Remove(this);
            BrainDisabled?.Invoke(this);

            if (currentState)
            {
                for (int i = 0; i < currentState.tasks.Length; i++)
                {
                    if (currentState.tasks[i] != null) //CustomPatch: fixed wrong null-check for unity object
                    {
                        //Exit the Current Tasks
                        currentState.tasks[i].ExitAIState(this, i);
                    }
                }
            }
            BrainInitialize = false;
        }

        void Update()
        {
            if (BrainInitialize && currentState != null)
            {
                currentState.Update_State(this);
            }
        }

        #endregion

        /// <summary>Activates the AI Control and plays the Current State. Disables the Brain (with a warning) when the Current State is empty</summary>
        public void StartBrain()
        {
            StartPending = false;

            if (currentState)
            {
                AIControl.AutoNextTarget = false;
                AIControl.SetActive(true);

                OnTargetSet(AIControl.Target);


                for (int i = 0; i < currentState.tasks.Length; i++)
                {
                    if (currentState.tasks[i] == null)
                    {
                        Debug.LogError($"The [{currentState.name}] AI State has an Empty Task. AI States can't have empty Tasks. {Animal.name}", currentState);
                        // enabled = false;
                        return;
                    }
                    ;

                }

                BrainInitialize = true; //Set before playing the state so StartNewState plays it right away

                StartNewState(currentState);


                LastWayPoint = null;

                if (AIControl.Target)
                    SetLastWayPoint(AIControl.Target);
            }
            else
            {
                Debug.LogWarning($"[{name}] The AI Brain has no <B>Current State</B> assigned, so it was disabled. " +
                    "Assign an AI State to the Brain's Current State field, or start one from code with Brain.StartNewState().", this);
                enabled = false;
            }
        }

        public virtual void TransitionToState(MAIState nextState, bool decisionValue, MAIDecision decision, int Index)
        {
            if (MTools.ElapsedTime(TransitionLastTime, TransitionCoolDown)) //This avoid making transition on the same Frame ****IMPORTANT
            {
                if (nextState != null && nextState != currentState) //Do not transition to itself!
                {
                    TransitionLastTime = Time.time;

                    decision.FinishDecision(this, Index);

                    Debugging($"<color=white>Changed AI State from <B>[{currentState.name}]</B> to" +
                        $" <B>[{nextState.name}]</B>. Decision: <b>[{decision.name}]</b> = <B>[{decisionValue}]</B>.</color>", currentState);

                    InvokeDecisionEvent(decisionValue, decision);

                    StartNewState(nextState);
                }
            }
        }

        /// <summary>Editor only console log for the Brain, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        protected virtual void Debugging(string Log, UnityEngine.Object val)
        {
#if UNITY_EDITOR
            if (debug) MDebug.Log($"<B><color=green>[{Animal.name}]</color> - </B> " + Log, val);
#endif
        }

        private void InvokeDecisionEvent(bool decisionValue, MAIDecision decision)
        {
            if (decision.send == MAIDecision.WSend.SendTrue && decisionValue)
            {
                OnDecisionSucceeded.Invoke(decision.DecisionID);
            }
            else if (decision.send == MAIDecision.WSend.SendFalse && !decisionValue)
            {
                OnDecisionSucceeded.Invoke(decision.DecisionID);
            }
        }

        /// <summary>  Activate a new state in the Brain Component  </summary>
        public virtual void Play(MAIState newState) => StartNewState(newState);

        /// <summary>  Activate a new state in the Brain Component  </summary>
        public virtual void StartNewState(MAIState newState)
        {
            if (newState == null)
            {
                Debug.LogWarning($"[{name}] StartNewState was called with an empty AI State. Ignored.", this);
                return;
            }

            //The Brain is not running yet (disabled, before Awake, or waiting for the AI Control).
            //Store the State and let StartBrain play it, so the Brain is fully initialized and the State is not started twice.
            if (!BrainInitialize)
            {
                currentState = newState;

                if (!enabled) enabled = true;                                  //OnEnable will call StartBrain once the AI Control is ready
                else if (isActiveAndEnabled && !StartPending) StartBrain();   //Enabled but StartBrain already ran (or never will)
                return;
            }

            StateLastTime = Time.time;   //Store the last time the Animal made a transition

            if (currentState != null && currentState != newState)
            {
                currentState.Finish_Tasks(this);                 //Finish all the Task on the Current State
                                                                 // currentState.Finish_Decisions(this);           //Finish all the Decisions on the Current State
            }

            currentState = newState;                            //Set a new State

            ResetVarsOnNewState();

            OnAIStateChanged.Invoke(currentState.ID);
            currentState.Start_AIState(this);                      //Start all Tasks on the new State
            currentState.Prepare_Decisions(this);               //Start all Tasks on the new State


            Debugging($"<color=white>Play AI State <B><color=cyan>[{currentState.name}]</color></B>. " +
                $"Tasks[{currentState.tasks.Length}]. Decisions[{currentState.transitions.Length}]</color>", currentState);

        }

        /// <summary>Prepare all the local variables on the New State before starting new tasks</summary>
        private void ResetVarsOnNewState()
        {
            if (currentState)
            {
                var tasks = (currentState.tasks != null && currentState.tasks.Length > 0) ? currentState.tasks.Length : -1;
                var transitions = (currentState.transitions != null && currentState.transitions.Length > 0) ? currentState.transitions.Length : -1;


                //OPTIMIZATION  TIP:

                if (tasks > 0)
                {
                    Array.Clear(TasksVars, 0, tasks);
                    Array.Clear(TasksUpdateTime, 0, tasks);
                    Array.Clear(TasksStartTime, 0, tasks);
                    Array.Clear(TasksDone, 0, tasks);
                    Array.Clear(TasksStarted, 0, tasks);
                }

                if (transitions > 0)
                {
                    Array.Clear(DecisionsVars, 0, transitions);
                    Array.Clear(DecisionsTime, 0, transitions);
                    Array.Clear(DecisionResult, 0, transitions);
                }

                //TasksVars = new BrainVars[tasks];                //Local Variables you can use on your tasks
                //TasksUpdateTime = new float[tasks];              //Reset all the Tasks    Time elapsed time
                //TasksStartTime = new float[tasks];               //Reset all the Tasks    Time elapsed time

                //TasksDone = new bool[tasks];                     //Reset if they are Done
                //TasksStarted = new bool[tasks];                  //Reset if they tasks are started

                //DecisionsVars = new BrainVars[transitions];      //Local Variables you can use on your Decisions
                //DecisionsTime = new float[transitions];          //Reset all the Decisions Time elapsed time
                //DecisionResult = new bool[transitions];          //Reset if they tasks are started


                //Make sure disabled Tasks are set to Task Done!!! Important!
                for (int i = 0; i < tasks; i++)
                {
                    if (!currentState.tasks[i].active) TasksDone[i] = true;
                }
            }
        }

        public bool IsTaskDone(int TaskIndex) => TasksDone[TaskIndex];

        /// <summary>  Set if a Task is finished or not </summary>
        /// <param name="TaskIndex">Index of the task</param>
        /// <param name="value">True[Default] if the Task is finished, False is not</param>
        public void TaskDone(int TaskIndex, bool value = true) //If the first task is done then go and do the next one
        {
            if (!TasksDone[TaskIndex])
            {
                TasksDone[TaskIndex] = value;
                OnTaskDone.Invoke(currentState[TaskIndex].MessageID.Value); //Invoke when a task is done!!!

                //Start the next task that needs to wait for the previous one
                if (TaskIndex + 1 < currentState.tasks.Length && currentState.tasks[TaskIndex + 1].WaitForPreviousTask)
                {
                    currentState.StartWaitforPreviusTask(this, TaskIndex + 1);
                }
            }
        }

        /// <summary> Check if the time elapsed of a task using a duration or CountDown time </summary>
        /// <param name="duration">Duration of the countDown|CoolDown</param>
        /// <param name="index">Index of the Task on the AI State Tasks list</param>
        public bool CheckIfDecisionsCountDownElapsed(float duration, int index)
        {
            DecisionsTime[index] += Time.deltaTime;
            return DecisionsTime[index] >= duration;
        }

        /// <summary>Set the time on which a task has started on the current AI State</summary>
        public void SetTaskStartTime(int Index)
        {
            TasksStartTime[Index] = Time.time;
        }


        /// <summary>Reset the Time elapsed on a Decision using its index from the Transition List </summary>
        /// <param name="Index">Index of the Decision on the AI State Transition List</param>
        public void ResetDecisionTime(int Index) => DecisionsTime[Index] = 0;

        //MWC: Get-or-create the typed runtime state for a Task slot. Allocates only when the slot is empty or holds
        //a different behavior type (e.g. a new AI State reuses the index for a different Task); reused on every
        //re-entry afterwards, so there is no per-frame allocation. Behaviors re-initialize their fields in StartTask.
        /// <summary>Typed per-slot runtime state for a Task. Created on first use, reused afterwards.</summary>
        public T TaskState<T>(int index) where T : BrainState, new() => GetOrCreateState<T>(TaskStates, index);

        //MWC: Get-or-create the typed runtime state for a Decision slot. See TaskState<T> for allocation semantics.
        /// <summary>Typed per-slot runtime state for a Decision. Created on first use, reused afterwards.</summary>
        public T DecisionState<T>(int index) where T : BrainState, new() => GetOrCreateState<T>(DecisionStates, index);

        /// <summary>Finds the state of type T in the slot's chain, or creates it. Several types can live in one slot: a wrapper (Chance, AND/OR) and the behaviour it wraps share the index.</summary>
        private static T GetOrCreateState<T>(BrainState[] slots, int index) where T : BrainState, new()
        {
            //A wrapper Task/Decision and the one it wraps run on the SAME index, and each has its own state type.
            //With one object per slot they kept replacing each other: ChanceTask stored its roll, the wrapped Task
            //overwrote the slot with its own state, and the next ChanceTask.UpdateTask found a fresh (false) roll and
            //ran the [Failed] branch forever. The slot is now a chain: every type gets its own object, created once.
            for (var s = slots[index]; s != null; s = s.Next)
                if (s is T found) return found;

            var state = new T { Next = slots[index] };
            slots[index] = state;
            return state;
        }

        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);



        #region SelfAnimal Event Listeners
        /// <summary>Notifies the Tasks and Decisions of the current AI State that this Animal changed State. Disables the AI on Death</summary>
        void OnAnimalStateChange(int state)
        {
            if (currentState != null) //CustomPatch: fixed wrong null-check for unity object
            {
                currentState.OnAnimalStateEnter(this, Animal.ActiveState);
                currentState.OnAnimalStateExit(this, Animal.LastState);
                currentState.OnAnimalEventDecisions(this, Animal);
            }

            if (state == StateEnum.Death) //meaning this animal has died
            {
                for (int i = 0; i < currentState.tasks.Length; i++)         //Exit the Current Tasks
                    currentState.tasks[i].ExitAIState(this, i);

                StopAllCoroutines();
                BrainInitialize = false;
                enabled = false;

                if (DisableAIOnDeath)
                {
                    AIControl.SetActive(false);
                    AIControl.ClearTarget();
                }
            }
        }

        /// <summary>Notifies the Tasks and Decisions of the current AI State that this Animal changed Stance</summary>
        void OnAnimalStanceChange(int stance)
        {
            if (currentState != null) //CustomPatch: fixed wrong null-check for unity object
            {
                currentState.OnAnimalStanceChange(this, Animal.Stance.ID);
                currentState.OnAnimalEventDecisions(this, Animal);
            }
        }

        /// <summary>Notifies the Tasks and Decisions of the current AI State that this Animal started a Mode</summary>
        void OnAnimalModeStart(int mode, int ability)
        {
            if (currentState != null) //CustomPatch: fixed wrong null-check for unity object
            {
                currentState.OnAnimalModeStart(this, Animal.ActiveMode);
                currentState.OnAnimalEventDecisions(this, Animal);
            }
        }

        /// <summary>Notifies the Tasks and Decisions of the current AI State that this Animal ended a Mode (the Mode passed is the one that ended)</summary>
        void OnAnimalModeEnd(int mode, int ability)
        {
            if (currentState != null) //CustomPatch: fixed wrong null-check for unity object
            {
                currentState.OnAnimalModeEnd(this, Animal.LastMode); //ActiveMode is already empty (or the next Mode) when Mode End fires
                currentState.OnAnimalEventDecisions(this, Animal);
            }
        }


        #endregion

        #region TargetAnimal Event Listeners

        /// <summary>Target Animal whose events the Brain is currently listening to</summary>
        private MAnimal listenedTargetAnimal;

        /// <summary>Moves the Target Animal event listeners to a new Animal. Null only removes them</summary>
        private void ListenTargetAnimal(MAnimal targetAnimal)
        {
            if (ReferenceEquals(listenedTargetAnimal, targetAnimal)) return;

            //ReferenceEquals: the old Target may be destroyed already, its UnityEvents still need the listeners removed
            if (!ReferenceEquals(listenedTargetAnimal, null))
            {
                listenedTargetAnimal.OnStateChange.RemoveListener(OnTargetAnimalStateChange);
                listenedTargetAnimal.OnStanceChange.RemoveListener(OnTargetAnimalStanceChange);
                listenedTargetAnimal.OnModeStart.RemoveListener(OnTargetAnimalModeStart);
                listenedTargetAnimal.OnModeEnd.RemoveListener(OnTargetAnimalModeEnd);
            }

            //Never listen to itself as a Target: the Self Animal listeners already cover it
            listenedTargetAnimal = (targetAnimal != null && targetAnimal != Animal) ? targetAnimal : null;

            if (listenedTargetAnimal != null)
            {
                listenedTargetAnimal.OnStateChange.AddListener(OnTargetAnimalStateChange);
                listenedTargetAnimal.OnStanceChange.AddListener(OnTargetAnimalStanceChange);
                listenedTargetAnimal.OnModeStart.AddListener(OnTargetAnimalModeStart);
                listenedTargetAnimal.OnModeEnd.AddListener(OnTargetAnimalModeEnd);
            }
        }

        /// <summary>Notifies the Tasks and Decisions of the current AI State that the Target Animal changed State</summary>
        private void OnTargetAnimalStateChange(int state)
        {
            if (currentState == null || listenedTargetAnimal == null) return;

            currentState.OnTargetAnimalStateEnter(this, listenedTargetAnimal.ActiveState);
            currentState.OnTargetAnimalStateExit(this, listenedTargetAnimal.LastState);
            currentState.OnAnimalEventDecisions(this, listenedTargetAnimal);
        }

        /// <summary>Notifies the Tasks and Decisions of the current AI State that the Target Animal changed Stance</summary>
        private void OnTargetAnimalStanceChange(int stance)
        {
            if (currentState == null || listenedTargetAnimal == null) return;

            currentState.OnTargetAnimalStanceChange(this, stance);
            currentState.OnAnimalEventDecisions(this, listenedTargetAnimal);
        }

        /// <summary>Notifies the Tasks and Decisions of the current AI State that the Target Animal started a Mode</summary>
        private void OnTargetAnimalModeStart(int mode, int ability)
        {
            if (currentState == null || listenedTargetAnimal == null) return;

            currentState.OnTargetAnimalModeStart(this, listenedTargetAnimal.ActiveMode);
            currentState.OnAnimalEventDecisions(this, listenedTargetAnimal);
        }

        /// <summary>Notifies the Tasks and Decisions of the current AI State that the Target Animal ended a Mode (the Mode passed is the one that ended)</summary>
        private void OnTargetAnimalModeEnd(int mode, int ability)
        {
            if (currentState == null || listenedTargetAnimal == null) return;

            currentState.OnTargetAnimalModeEnd(this, listenedTargetAnimal.LastMode);
            currentState.OnAnimalEventDecisions(this, listenedTargetAnimal);
        }

        /// <summary>Notifies the Tasks of the current AI State that the AI Control arrived to its Target</summary>
        private void OnTargetArrived(Transform target)
        {
            if (currentState != null) currentState.OnTargetArrived(this, target);
        }

        /// <summary>Notifies the Tasks of the current AI State that the AI Control arrived to its destination position</summary>
        private void OnPositionArrived(Vector3 position)
        {
            if (currentState != null) currentState.OnPositionArrived(this, position);
        }
        #endregion


        /// <summary>Stores if the Current Target is an Animal and if it has the Stats component </summary>
        private void OnTargetSet(Transform target)
        {
            Target = target;

            //Reset TargetVars
            TargetAnimal = null;
            TargetVars = null;
            TargetStats = null;

            if (target)
            {
                TargetAnimal = target.FindComponent<MAnimal>();
                TargetVars = target.FindInterface<ILocalVars>();
                TargetStats = target.FindComponent<Stats>();
            }

            ListenTargetAnimal(TargetAnimal); //Listen to the new Target Animal events (removes the old ones)
        }

        public bool CheckForPreviousTaskDone(int index)
        {
            if (index == 0) return true;

            if (!TasksStarted[index] && IsTaskDone(index - 1))
                return true;

            return false;
        }

        public void SetLastWayPoint(Transform target)
        {
            var newLastWay = target.gameObject.FindInterface<IWayPoint>();
            if (newLastWay != null && target != null) //CustomPatch: fixed wrong null-check for unity object (target)
                LastWayPoint = target.gameObject.FindInterface<IWayPoint>(); //If not is a waypoint save the last one
        }



        [SerializeField] private int Editor_Tabs1;
#if UNITY_EDITOR

        private void OnValidate()
        {
            Animal ??= gameObject.FindComponent<MAnimal>();
        }
        void Reset()
        {
            //   remainInState = MTools.GetInstance<MAIState>("Remain in State");
            AIControl = this.FindComponent<MAnimalAIControl>();

            if (!AIControl.IsUnityRefNull()) //CustomPatch: fixed wrong null-check for unity object interface type
            {
                AIControl.AutoNextTarget = false;
                AIControl.UpdateDestinationPosition = false;
                AIControl.LookAtTargetOnArrival = false;
            }
            else
            {
                Debug.LogWarning("There's No AI Control in this GameObject, Please add one");
            }
        }

        void OnDrawGizmos()
        {
            //MWC: At edit time the AI State inspector previews any Brain via MAIStateEditor.DrawPreviewGizmos. Only the runtime state is drawn here.
            if (!Application.isPlaying) return;

            if (isActiveAndEnabled && currentState && Eyes)
            {
                Gizmos.color = currentState.GizmoStateColor;
                Gizmos.DrawWireSphere(Eyes.position, 0.2f);

                if (debug)
                {
                    if (currentState != null)
                    {
                        if (currentState.tasks != null)
                            foreach (var task in currentState.tasks)
                            {
                                if (task != null) task.DrawGizmos(this);
                            }


                        if (currentState.transitions != null)
                            foreach (var tran in currentState.transitions)
                            {
                                if (tran != null && tran.decision != null)  //CustomPatch: fixed wrong null-check for unity object (tran.decision)
                                    tran.decision.DrawGizmos(this);
                            }
                    }
                }

                if (Application.isPlaying && debugAIStates)
                {
                    string decisions = "";

                    var Styl = new GUIStyle(EditorStyles.boldLabel);
                    Styl.normal.textColor = Color.yellow;

                    UnityEditor.Handles.Label(Eyes.position, "State: " + currentState.name + decisions, Styl);
                }
            }
        }
#endif 
    }

    public enum Affected { Self, Target }
    public enum ExecuteTask { OnStart, OnUpdate, OnExit }

    //MWC: Base class for per-agent, per-slot runtime state of a Task or Decision. Because Tasks/Decisions are
    //ScriptableObjects shared by every AI, they cannot store runtime state in their own fields. Instead each
    //behavior declares its own BrainState subclass (with any fields it needs) and stores it on the brain via
    //MAnimalBrain.TaskState<T>()/DecisionState<T>(). This replaces hijacking the fixed BrainVars struct.
    public abstract class BrainState
    {
        /// <summary>Next state stored on the same slot (a wrapper and the behaviour it wraps share the index)</summary>
        internal BrainState Next;
    }

    [System.Serializable]
    public struct BrainVars
    {
        public int intValue;
        public float floatValue;
        public bool boolValue;
        public Vector3 vector3;
        public Component[] Components;
        public MonoBehaviour mono;
        public GameObject[] gameobjects;

        /// <summary>Runtime integer variables of the AI Brain, keyed by variable hash</summary>
        public Dictionary<int, int> ints { get; set; }
        /// <summary>Runtime float variables of the AI Brain, keyed by variable hash</summary>
        public Dictionary<int, float> floats { get; set; }
        /// <summary>Runtime bool variables of the AI Brain, keyed by variable hash</summary>
        public Dictionary<int, bool> bools { get; set; }
        // public Dictionary<int, Component> D_components;

        public void SetVar(int key, bool value) => bools[key] = value;
        public void SetVar(int key, int value) => ints[key] = value;
        public void SetVar(int key, float value) => floats[key] = value;
        public bool GetBool(int key) => bools[key];
        public int GetInt(int key) => ints[key];
        public float GetFloat(int key) => floats[key];


        public bool TryGetBool(int key, out bool value) => bools.TryGetValue(key, out value);
        public bool TryGetInt(int key, out int value) => ints.TryGetValue(key, out value);
        public bool TryGetFloat(int key, out float value) => floats.TryGetValue(key, out value);

        public void AddVar(int key, bool value)
        {
            if (bools == null) bools = new Dictionary<int, bool>();
            bools.Add(key, value);
        }

        public void AddVar(int key, int value)
        {
            if (ints == null) ints = new Dictionary<int, int>();
            ints.Add(key, value);
        }

        public void AddVar(int key, float value)
        {
            if (floats == null) floats = new Dictionary<int, float>();
            floats.Add(key, value);
        }

        public void AddComponents(Component[] components)
        {
            if (Components == null || Components.Length == 0) Components = components;
            else
            {
                Components = Components.Concat(components).ToArray();
            }
        }


        public void AddComponent(Component comp)
        {
            if (Components == null || Components.Length == 0) Components = new Component[1] { comp };
            else
            {
                var ComponentsL = Components.ToList();
                ComponentsL.Add(comp);
                Components = ComponentsL.ToArray();
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MAnimalBrain)), CanEditMultipleObjects]
    public class MAnimalBrainEditor : Editor
    {
        SerializedProperty Eyes, debug, TransitionCoolDown, DisableAIOnDeath, Editor_Tabs1, debugAIStates, OnTaskDone, MaxAlloc,
            currentState, OnTaskStarted, OnDecisionSucceeded, OnAIStateChanged;

        protected string[] Tabs1 = new string[] { "General", "Events", "Debug" };

        MAnimalBrain M;

        private void OnEnable()
        {
            M = (MAnimalBrain)target;

            Eyes = serializedObject.FindProperty("Eyes");
            TransitionCoolDown = serializedObject.FindProperty("TransitionCoolDown");
            DisableAIOnDeath = serializedObject.FindProperty("DisableAIOnDeath");
            currentState = serializedObject.FindProperty("currentState");

            OnTaskStarted = serializedObject.FindProperty("OnTaskStarted");
            OnTaskDone = serializedObject.FindProperty("OnTaskDone");
            OnDecisionSucceeded = serializedObject.FindProperty("OnDecisionSucceeded");
            OnAIStateChanged = serializedObject.FindProperty("OnAIStateChanged");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            debug = serializedObject.FindProperty("debug");
            //  AISource = serializedObject.FindProperty("AISource");
            debugAIStates = serializedObject.FindProperty("debugAIStates");
            MaxAlloc = serializedObject.FindProperty("MaxAlloc");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Brain Logic for the Animal");
            {

                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs1);

                switch (Editor_Tabs1.intValue)
                {
                    case 0: DrawGeneral(); break;
                    case 1: DrawEvents(); break;
                    case 2: DrawDebug(); break;
                    default: break;
                }

                if (Eyes.objectReferenceValue == null) EditorGUILayout.HelpBox("The AI Eyes [Reference] is missing. Please add a transform the AI Eyes parameters", MessageType.Error);

            }
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDebug()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(debugAIStates, new GUIContent("Debug On Screen"));

                if (Application.isPlaying)
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                    {

                        EditorGUILayout.ObjectField("Brain Target", M.Target, typeof(Transform), false);

                        if (M.enabled && M.BrainInitialize && M.currentState != null)
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.ObjectField("AI State", M.currentState, typeof(MAIState), false);


                                EditorGUILayout.LabelField("Tasks", EditorStyles.boldLabel);

                                for (int i = 0; i < M.currentState.tasks.Length; i++)
                                {
                                    using (new GUILayout.HorizontalScope())
                                    {
                                        EditorGUILayout.ObjectField(GUIContent.none, M.currentState.tasks[i], typeof(MTask), false, GUILayout.MinWidth(100));
                                        EditorGUILayout.LabelField($"  Started: {(M.TasksStarted[i] ? "☑" : "[  ]")}. Done: {(M.TasksDone[i] ? "☑" : "[  ]")}", GUILayout.MinWidth(100));
                                        EditorGUILayout.LabelField($"Start Time: {M.TasksStartTime[i]:F2}", GUILayout.MinWidth(50));
                                    }
                                }


                                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                {
                                    EditorGUILayout.LabelField("Task Variables", EditorStyles.boldLabel);
                                    for (int i = 0; i < M.currentState.tasks.Length; i++)
                                    {
                                        var TasksVars = serializedObject.FindProperty("TasksVars");

                                        if (TasksVars != null && TasksVars.arraySize > i)
                                        {
                                            EditorGUI.indentLevel++;
                                            EditorGUILayout.PropertyField(TasksVars.GetArrayElementAtIndex(i),
                                                new GUIContent(M.currentState.tasks[i].name), true);
                                            EditorGUI.indentLevel--;
                                        }
                                    }
                                }
                            }

                        }
                        EditorGUILayout.Space();

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField("Decision Variables", EditorStyles.boldLabel);
                            for (int i = 0; i < M.currentState.transitions.Length; i++)
                            {
                                var DecisionsVars = serializedObject.FindProperty("DecisionsVars");

                                if (M.currentState.transitions[i] != null)
                                {
                                    var Des = M.currentState.transitions[i].decision;

                                    var waiting = "";

                                    if (Des.WaitForAllTasks && !M.AllTasksDone()) waiting = "[WAIT T*]";
                                    if (Des.waitForTask != -1 && !M.IsTaskDone(Des.waitForTask)) waiting = "[WAIT T]";


                                    EditorGUILayout.ObjectField($"Decision [{i}] {waiting}", Des, typeof(MAIDecision), false, GUILayout.MinWidth(100));
                                    if (DecisionsVars != null && DecisionsVars.arraySize > i)
                                    {
                                        EditorGUI.indentLevel++;
                                        EditorGUILayout.PropertyField(DecisionsVars.GetArrayElementAtIndex(i),
                                            new GUIContent(M.currentState.transitions[i].decision.name), true);
                                        EditorGUI.indentLevel--;
                                    }
                                }
                            }
                        }
                        Repaint();
                    }
                }
            }
        }

        private void DrawGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(Eyes);
                    MalbersEditor.DrawDebugIcon(debug);
                }
                EditorGUILayout.PropertyField(currentState);
                EditorGUILayout.Space(2);
                // EditorGUILayout.PropertyField(remainInState);
                EditorGUILayout.PropertyField(TransitionCoolDown);
                EditorGUILayout.PropertyField(DisableAIOnDeath);
                EditorGUILayout.PropertyField(MaxAlloc);
            }
        }

        private void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnAIStateChanged);
                EditorGUILayout.PropertyField(OnTaskStarted);
                EditorGUILayout.PropertyField(OnTaskDone);
                EditorGUILayout.PropertyField(OnDecisionSucceeded);
            }
        }
    }
#endif
}