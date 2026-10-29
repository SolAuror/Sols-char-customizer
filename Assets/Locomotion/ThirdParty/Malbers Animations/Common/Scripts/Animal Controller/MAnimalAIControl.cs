// Last Change: 9/18/2026 1:20pm
using UnityEngine;
using System.Collections;
using MalbersAnimations.Events;

using UnityEngine.AI;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine.Events;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations.Controller.AI
{
    [AddComponentMenu("Malbers/Animal Controller/AI/AI Control")]
    public class MAnimalAIControl : MonoBehaviour, IAIControl, IAnimatorListener
    {
        #region Components and References
        /// <summary> Reference for the Agent</summary>
        [SerializeField] private NavMeshAgent agent;

        /// <summary> Reference for the Animal</summary>
        [RequiredField] public MAnimal animal;

        [Tooltip("On AI Enable. Disable the Input Source. " +
            "Input source conflict with the AI source (Both will try to control the Animal Controller and it may cause unwanted issues ")]
        public bool DisableInputAIOn = true;

        [Tooltip("On AI Disable. Enable the Input Source. " +
            "Input source conflict with the AI source (Both will try to control the Animal Controller and it may cause unwanted issues ")]
        public bool EnableInputAIOff = true;

        //CustomPatch: Added auto validate destination position logic params
        public bool autoValidateDestination;
        public float startValidationMovementRange = 2f;
        public int maxNumValidationIterations = 4;

        /// <summary>Cache if the Animal has an Interactor</summary>
        public IInteractor Interactor { get; internal set; }

        public bool AIReady { get; internal set; }

        public bool ArriveLookAt => false; //do this later

        public virtual bool Active => enabled && gameObject.activeInHierarchy;
        #endregion

        #region Internal Variables
        /// <summary>Target Last Position (Useful to know if the Target is moving)</summary>
        protected Vector3 TargetLastPosition;

        /// <summary>Remaining Distance to the Destination Point</summary>
        public virtual float RemainingDistance { get; set; }
        //public virtual float RemainingDistance
        //{
        //    get { return remainingDistance; }
        //    set
        //    {
        //        remainingDistance = value;
        //        Debug.Log($"remainingDistance {remainingDistance}");
        //    }
        //}
        //float remainingDistance;
        public virtual bool IsMoving { get; set; }

        /// <summary> Returns the Current Agent Remaining Distance </summary>
        public virtual float AgentRemainingDistance => Agent.remainingDistance;

        ///// <summary>Store the Current Remaining Distance. This is used to slowdown the Animal when is circling around and it cannot arrive to the destination</summary>
        //public virtual float MinRemainingDistance { get; set; }

        /// <summary>Used to Slow Down the Animal when its close the Destination</summary>
        public float SlowMultiplier
        {
            get
            {
                var result = 1f;
                if (CurrentSlowingDistance > CurrentStoppingDistance && RemainingDistance < CurrentSlowingDistance)
                    result = Mathf.Max(RemainingDistance / CurrentSlowingDistance, slowingLimit);

                return result;
            }
        }

        public Transform Transform { get; internal set; }

        /// <summary>Stores the Agent Direction used to move the Animal</summary>
        public Vector3 AIDirection //{ get; set; }
        {
            get => aiDirection;
            set
            {
                aiDirection = value;

                //Debug.Log($"<B>AIDirection {aiDirection}");
            }
        }
        Vector3 aiDirection;


        /// <summary>Is the Agent in a OffMesh Link</summary>       
        public bool InOffMeshLink { get; set; }

        public virtual bool AgentInOffMeshLink => Agent.isOnOffMeshLink;

        //MWC: was reading agent.path.corners twice — each get allocates a NavMeshPath AND a Vector3[].
        //It also returned corners[0], which is the path ORIGIN (the agent itself), not the next corner,
        //so it returned ~the agent position in both branches. agent.steeringTarget is the real next
        //corner, computed natively with no allocation. Property is currently unused anywhere in the project.
        public virtual Vector3 AgentNextCorner =>
            (agent != null && agent.isOnNavMesh && agent.hasPath) ? agent.steeringTarget : AgentTransform.position;

        /// <summary>Store if the Animal is on a Blocking Agent State</summary>       
        public bool StateIsBlockingAgent { get; set; }

        /// <summary>Is the Agent Enabled/Active ?</summary>       
        public virtual bool ActiveAgent
        {
            get => agent.enabled && agent.isOnNavMesh;
            set
            {
                agent.enabled = value;
                if (agent.isOnNavMesh) agent.isStopped = !value;
                //Debug.Log($"<B>{(agent.enabled ? "[•]" : "[  ]")}</B> Agent Enable [{animal.name}]");
            }
        }

        /// <summary>Checks if the Animal Can Fly</summary>
        public virtual bool CanFly { get; private set; }


        /// <summary>Updates the Destination Position if the Target Moves</summary>
        public virtual bool UpdateDestinationPosition { get; set; }


        /// <summary>Destination Position to use on Agent.SetDestination()</summary>
        public virtual Vector3 DestinationPosition { get; set; }


        private IEnumerator I_WaitToNextTarget;
        private IEnumerator IFreeMoveOffMesh;
        private IEnumerator IMoveOffMeshLink;
        private IEnumerator IClimbOffMesh;
        #endregion

        #region Public Variables 

        //OLD Way, this will be deprecated in favor of the IDList version
        [Tooltip("When the animal is on any of these States, The AI agent will be disable to improve performance.")]
        [ContextMenuItem("Set Default", "SetDefaultStopAgent")]
        public List<StateID> StopAgentOn;

        [ContextMenuItem("Set Default", "SetDefaultStopAgent")]
        [Tooltip("When the animal is on any of these States, The AI agent will be disable to improve performance.")]
        public IDList<StateID> stopAgentOn;


        [Tooltip("Multiplier used for Waypoints Wait time. Set it to zero if you want to ignore waiting on waypoints")]
        [Min(0), SerializeField] private float waitTimeMult = 1f;


        [Min(0)] public float UpdateAI = 0.2f;
        private float CurrentTime;

        [Tooltip("Default Stopping Distance used by the AI Control. This value will be ignored if the Target has the [AI Target] component attached")]
        [Min(0)][SerializeField] protected float stoppingDistance = 0.6f;
        [Min(0)][SerializeField] protected float PointStoppingDistance = 0.6f;

        [Tooltip("Self Distance to keep the character away from the target")]
        [Min(0)][SerializeField] protected float selfRadius = 0.5f;

        /// <summary>The animal will change automatically to Walk if the distance to the target is this value</summary>
        [SerializeField]
        [Tooltip("Default Slowing Distance used by the AI Control. Once the Animal arrive to this destination it will start slowing its current speed.")]
        [Min(0)] protected float slowingDistance = 1f;

        [Tooltip("If the AI Animal is scaled, use the scale factor to find the Target")]
        public BoolReference UseScale = new(true);


        [Tooltip("If the AI Animal was assigned a new target, the current playing mode will be interrupted")]
        public BoolReference InterruptModeOnTarget = new(true);

        [Tooltip("If the AI Animal was assigned a new target, the current playing mode will be interrupted")]
        public BoolReference ForceStopModeOnTarget = new(true);

        [Tooltip("It will clear the Target if the component is disabled")]
        public BoolReference ClearTargetOnDisable = new(true);

        [Tooltip("How high a target can be from the terrain so the Animal can follow  it")]
        [SerializeField][Min(0)] private float targetHeight = 5f;

        [Tooltip("The Animal will stop if the target is too high to reach")]
        public BoolReference StopOnTargetTooHigh = new(true);

        [Tooltip("Distance from the Animals Root to apply LookAt Target Logic when the Animal arrives to a target.")]
        [Min(0)] public float LookAtOffset = 1;

        //MWC: hysteresis thresholds for the arrival look-at rotation (angle in degrees) to avoid wobble.
        [Tooltip("Angle (degrees) between the Animal's forward and the target direction needed to START rotating in place on arrival.")]
        [Min(0)] public float LookAtStartAngle = 4f;

        [Tooltip("Angle (degrees) at which the Animal STOPS the in-place rotation on arrival. Must be lower than the Start Angle to prevent wobble.")]
        [Min(0)] public float LookAtStopAngle = 1.5f;

        [Tooltip("Limit for the Slowing Multiplier to be applied to the Speed Modifier")]
        [Range(0, 1)]
        [SerializeField] private float slowingLimit = 0.3f;

        [SerializeField] private Transform target;
        [SerializeField] private Transform nextTarget;

        /// <summary>When the AI Arrives to a Waypoint Target, it will set the Next Target from the AIWaypoint</summary>
        public bool AutoNextTarget { get; set; }

        /// <summary>The Animal will Rotate/Look at the Target when he arrives to it</summary>
        public bool LookAtTargetOnArrival { get; set; }


        public bool debug = false;
        public bool debugGizmos = true;
        public bool debugStatus = true;
        #endregion

        #region Events
        [Space]
        public Vector3Event OnTargetPositionArrived = new();
        public TransformEvent OnTargetArrived = new();
        public TransformEvent OnTargetSet = new();

        public TransformEvent TargetSet => OnTargetSet;
        public TransformEvent OnArrived => OnTargetArrived;
        public UnityEvent OnEnabled = new();
        public UnityEvent OnDisabled = new();

        #endregion 

        [Tooltip("What State to play when the next WayPoint is a FreeMovement (Air Waypoint)")]
        public StateID AirDestinationState;

        #region Properties  
        /// <summary>is the Animal, Flying, swimming, On Free Mode?</summary>
        public bool FreeMove { get; set; }
        public int Index { get; set; }

        /// <summary>height of the Agent</summary>
        public virtual float Height => targetHeight * animal.ScaleFactor;

        /// <summary> Is the Target too high?  </summary>
        public virtual bool TargetTooHigh { get; set; }

        /// <summary>Default Stopping Distance</summary>
        public virtual float StoppingDistance { get => stoppingDistance; set => stoppingDistance = value; }
        public virtual float DefaultStoppingDistance => StoppingDistance;
        public virtual float SelfRadius { get => selfRadius; set => selfRadius = value; }

        public virtual Vector3 AgentDesiredVelocity => Agent.desiredVelocity;

        /// <summary>  Check if the Animal is Waiting on Target with multiple targeters</summary>
        public bool IsWaitingOnTarget { get; set; }
        //{
        //    get => isWaitingOnTarget;
        //    set
        //    {
        //        isWaitingOnTarget = value;
        //        //  Debug.Log($"{animal.name} WaitingOnTarget: {value}", this);
        //    }
        //}
        //private bool isWaitingOnTarget;

        /// <summary>Has the animal arrived to the destination</summary>
        public bool HasArrived// { get; set; }
        {
            get => hasArrived;
            set
            {
                hasArrived = value;
                //Debuging($" Has Arrived: {value}");
            }
        }
        private bool hasArrived;


        /// <summary>Current Stopping distance of the Current Target/Destination</summary>
        public virtual float CurrentStoppingDistance
        {
            get => (currentStoppingDistance + selfRadius) * ScaleFactor;
            set
            {
                Agent.stoppingDistance = currentStoppingDistance = value;
                //Debug.Log($"[{animal.name}] CurrentStoppingDistance: {value}");
            }
        }
        protected float currentStoppingDistance;

        protected float ScaleFactor;


        /// <summary>Default Slowing Distance</summary>
        public virtual float SlowingDistance => slowingDistance;

        private float currentSlowingDistance;
        /// <summary>Current Slowing Distance from the Current AI Target</summary>
        public virtual float CurrentSlowingDistance { get => currentSlowingDistance + selfRadius; set => currentSlowingDistance = value; }

        /// <summary>Is the Animal Playing a mode</summary>
        public bool IsOnMode => animal.IsPlayingMode;

        /// <summary>  Stop all Modes that does not allow Movement  </summary>
        private bool IsOnNonMovingMode => (IsOnMode && !animal.ActiveMode.AllowMovement);

        /// <summary>Is the Target a WayPoint?</summary>
        public IWayPoint IsWayPoint { get; set; }

        /// <summary>Is the Target an AITarget</summary>
        public IAITarget IsAITarget { get; set; }

        /// <summary>Is the Target an Targeter</summary>
        public IAITargeterTarget ITargeter { get; set; }


        /// <summary>Is the Target an AITarget</summary>
        public IInteractable IsTargetInteractable { get; protected set; }

        /// <summary>The Target is an Air Target</summary>
        internal bool IsAirDestination => !IsAITarget.IsUnityRefNull() && IsAITarget.TargetType == WayPointType.Air; //CustomPatch: corrected null check of unity object interface type
        /// <summary>The Target is an Ground Target</summary>
        internal bool IsGroundDestination => !IsAITarget.IsUnityRefNull() && IsAITarget.TargetType == WayPointType.Ground; //CustomPatch: corrected null check of unity object interface type

        /// <summary>Reference of the Nav Mesh Agent</summary>
        public virtual NavMeshAgent Agent => agent;

        public Transform AgentTransform;
        public Transform Owner => animal.transform;

        public virtual Vector3 GetCenterPosition() => AgentTransform.position;

        public Vector3 GetCenterY() => animal.Center;

        /// <summary> Self Target Type </summary>
        public virtual WayPointType TargetType => animal.FreeMovement ? WayPointType.Air : WayPointType.Ground;


        /// <summary>is the Target transform moving??</summary>
        public virtual bool TargetIsMoving { get; internal set; }


        /// <summary> Is the Animal waiting x time to go to the Next waypoint</summary>
        public virtual bool IsWaiting { get; internal set; }

        public virtual Vector3 LastOffMeshDestination { get; set; }

        /// <summary> Store where the Offmesh Link ends </summary>
        public virtual Vector3 EndOffMeshPos { get; set; }

        public Vector3 NullVector { get; set; }

        /// <summary>True when a valid destination has been assigned (not the NullVector sentinel)</summary>
        public bool HasDestination => DestinationPosition != NullVector; //MWC: cleaner destination-state check instead of comparing to magic NullVector directly

        public virtual Transform NextTarget
        {
            get => nextTarget;
            set
            {
                nextTarget = value;
                // Debug.Log("Next Target: " + value);
            }

        }

        public virtual Transform Target { get => target; set => target = value; }
        public float WaitTimeMult { get => waitTimeMult; set => waitTimeMult = value; }

        /// <summary>Stores the Local Agent Position relative to the Animal</summary>
        protected Vector3 AgentPosition;

        #endregion

        public virtual void SetActive(bool value)
        {
            // Debug.Log("value = " + value);
            if (gameObject.activeInHierarchy)
                enabled = value;
        }

        #region Unity Functions 
        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);


        protected virtual void Awake()
        {
            if (animal == null) animal = gameObject.FindComponent<MAnimal>();
            ValidateAgent();

            this.Transform = transform;

            Interactor = animal.FindInterface<IInteractor>();       //Check if there's any Interactor
            //InputSource = animal.FindInterface<IInputSource>();     //Check if there's any Input Source
            animal.UseSmoothVertical = true;                        //This needs to be disable so the slow distance works!!!!!!

            LookAtTargetOnArrival = true;                           //By Default Look Target on Arrival set it to True
            AutoNextTarget = true;                                  //By Default Auto Next Target is set to True
            UpdateDestinationPosition = true;

            if (animal.TryGetComponent(out IAITarget agent))
            {
                selfRadius = agent.StopDistance();
            }
            else if (selfRadius == 0) selfRadius = DefaultStoppingDistance;



            NullVector = new Vector3(-998.9999f, -998.9999f, -998.9999f);
            DestinationPosition = NullVector;
            CanFly = animal.HasState(StateEnum.Fly);                //Check if the Animal can Fly
            SetAgent();
        }

        /// <summary>  Set the Default properties for the Nav mesh Agent </summary>
        protected virtual void SetAgent()
        {
            if (agent == null) agent = AgentTransform?.GetComponent<NavMeshAgent>(); //MWC: was discarding GetComponent result — agent stayed null even when found

            if (agent)
            {
                AgentPosition = Agent.transform.localPosition;
                Agent.angularSpeed = 0;
                Agent.speed = 1;                                                    //The Agent needs a speed different from 0 to calculate the velocity
                Agent.acceleration = 0;
                Agent.autoBraking = false;
                Agent.updateRotation = false;                                       //The Animal will control the rotation . NOT THE AGENT
                Agent.updatePosition = false;                                       //The Animal will control the  position . NOT THE AGENT
                Agent.autoTraverseOffMeshLink = false;                              //Offmesh links are handled by animation
                Agent.stoppingDistance = selfRadius;
            }
        }

        protected virtual void OnEnable()
        {
            animal.OnStateActivate.AddListener(OnState);
            animal.OnModeStart.AddListener(OnModeStart);
            animal.OnModeEnd.AddListener(OnModeEnd);
            animal.OnTeleport.AddListener(OnTeleport);

            IsWaiting = true; //The AI Has not Started yet
            FreeMove = false;
            AIReady = false;

            ScaleFactor = (UseScale ? animal.ScaleFactor : 1f);

            if (animal.ActiveState) //If the animal has an active state.
                FreeMove = (animal.ActiveState.General.FreeMovement);

            if (FreeMove) ActiveAgent = false;
            if (Agent && !Agent.isOnNavMesh) ActiveAgent = false;
            HasArrived = false;
            TargetIsMoving = false;
            IsWaitingOnTarget = false;

            this.Delay_Action(StartAI);//Start AI a Frame later; 


            //Disable any Input Source in case it was active
            if (!animal.InputSource.IsUnityRefNull() && DisableInputAIOn) //CustomPatch: corrected null check for possible Unity object type
            {
                animal.InputSource.Enable(false);
                Debuging("Input Move Disabled");
            }

            OnEnabled.Invoke();
        }

        protected virtual void OnDisable()
        {
            //Debug.Log("OnDisableOnDisableOnDisableOnDisableOnDisableOnDisableOnDisableOnDisableOnDisableOnDisable");
            animal.OnStateActivate.RemoveListener(OnState);       //Listen when the State changes..
            animal.OnModeStart.RemoveListener(OnModeStart);       //Listen when a Mode Starts.. 
            animal.OnModeEnd.RemoveListener(OnModeEnd);          //Listen when a Mode Ends..  

            animal.OnTeleport.RemoveListener(OnTeleport);

            Stop();
            StopAllCoroutines();
            //MWC: null stored coroutine refs so stale non-null handles don't confuse the re-enable null-guards
            I_WaitToNextTarget = null;
            IFreeMoveOffMesh = null;
            IMoveOffMeshLink = null;
            IClimbOffMesh = null;
            OnDisabled.Invoke();

            animal.Rotate_at_Direction = false;
            AIReady = false;


            //Disable any Input Source in case it was active
            if (!animal.InputSource.IsUnityRefNull() && EnableInputAIOff) //CustomPatch: corrected null type check for unity object interface type
            {
                animal.InputSource.Enable(true);
                animal.Reset_Movement();
                Debuging("Input Move Enabled");
            }

            if (ClearTargetOnDisable) { ClearTarget(); }

            //MWC: the targeter slot used to be given back ONLY through ClearTarget() above, so with
            //[Clear Target On Disable] off a disabled AI kept occupying a spot on a limited AI Target forever
            //(blocking every other character) and kept its TargetersRefresh listener alive, firing
            //Destination_RefreshTarget on a disabled component. Release it either way — [target] itself is
            //preserved, and StartAI() re-acquires a slot on re-enable.
            ReleaseTargeter(); //No-op when ClearTarget() already released it
        }

        private void OnTeleport(Vector3 arg0)
        {
            //MWC: use Warp() to snap NavMeshAgent to the teleported position; toggling enabled/disabled does not move the agent
            if (Agent && Agent.isOnNavMesh)
                Agent.Warp(animal.transform.position);

            CalculatePath();
            Move();

            CompleteOffMeshLink();
            CheckAirTarget(); //Every time a State Changes Check again in case it failed by mistake
        }

        private void OnDestroy() => ReleaseTargeter(); //MWC: was only removing the listener, never the slot

        /// <summary>Give back the slot this AI occupies on a limited [AI Target] and unsubscribe from it.
        /// Safe to call repeatedly and when the target has already been destroyed.</summary>
        protected virtual void ReleaseTargeter()
        {
            //Always cleared here, not only by AITarget.RemoveTargeter: when the limited Target was DESTROYED while
            //this AI was queued on it, the flag stayed ON for good and every attack Task refused to fire on the next
            //Target ("waiting" is only meaningful while a limited Target holds this AI in its queue).
            IsWaitingOnTarget = false;

            if (ITargeter.IsUnityRefNull()) { ITargeter = null; Index = -1; return; } //Target already destroyed: just drop the reference

            ITargeter.RemoveTargeter(this);
            ITargeter.TargetersRefresh?.RemoveListener(Destination_RefreshTarget);
            Index = -1;         //Reset the Targeter Index
                                // ChasingTargeter = false;
            ITargeter = null;
        }

        protected virtual void Update() { Updating(); }
        #endregion

        #region Animal Events Listen
        /// <summary>Called when the Animal Enter an Action, Attack, Damage or something similar</summary>
        public virtual void OnModeStart(int ModeID, int ability)
        {
            Debuging($"has started a Mode: <B>[{animal.ActiveMode.ID.name}]</B>. Ability: <B>[{animal.ActiveMode.ActiveAbility.Name}]</B>");
            if (animal.ActiveMode.AllowMovement) return; //Don't stop the Animal Movement if the Mode can make movements
            else
            {
                animal.InertiaPositionSpeed = Vector3.zero;
                animal.StopMoving();
                animal.MovementAxisSmoothed = Vector3.zero;
            }

            var Dest = DestinationPosition; //Store the Destination with modes
            Stop(); //If the Agent was moving Stop it
            DestinationPosition = Dest; //Restore the Destination with modes
        }

        /// <summary>  Listen if the Animal Has finished a mode  </summary>
        public virtual void OnModeEnd(int ModeID, int ability)
        {
            if (StateIsBlockingAgent) return; //Do nothing if the current State is blocking the agent.

            Debuging($"Mode End: <B>[{ModeID}]</B>. Ability: <B>[{ability}]</B>");


            //MWC: do NOT re-path when the animal had already arrived. CalculatePath() clears the
            //HasArrived latch and re-enables the Agent, so the next Update re-runs Arrive_Destination
            //at zero remaining distance and fires IAITarget.TargetArrived() again. With an Action Zone
            //(Zone.TargetArrived -> ForceActivateZone) that replayed the Mode on every Mode End,
            //looping the animation forever until a new destination was clicked.
            if (!ActiveAgent && !HasArrived) //Re-enable the AI
            {
                CalculatePath();
                Move();
            }

            CompleteOffMeshLink();
            CheckAirTarget(); //Every time a State Changes Check again in case it failed by mistake
        }


        /// <summary>Listen to the Animal when it changes States</summary>
        public virtual void OnState(int stateID)
        {
            //MWC: update state flags before early-return so they reflect current state when AI resumes from wait
            FreeMove = (animal.ActiveState.General.FreeMovement); //Recheck if the current State is a FreeState
            StateIsBlockingAgent = stopAgentOn != null && stopAgentOn.Contains(animal.ActiveStateID);

            if (IsWaiting) return; //Do nothing if the Agent is waiting

            if (CheckAirTarget()) return; //Every time a State Changes Check again in case it failed by mistake

            if (HasArrived) return;

            if (StateIsBlockingAgent) //Check if we are on a State that does not require the Agent
            {
                if (Agent && Agent.isOnNavMesh) Agent.ResetPath(); //Reset the Path of the Agent

                ActiveAgent = false; //Disable the Agent
            }
            else
            {
                if (!IsOnNonMovingMode && !ActiveAgent)
                {
                    CalculatePath();
                    Move();
                }
                CompleteOffMeshLink();
            }
        }
        #endregion

        public virtual void StartAI()
        {
            var targ = target; target = null;
            SetTarget(targ);                                                  //Set the first Target (IMPORTANT)  it also set the next future targets

            if (AgentTransform == animal.transform)
                Debug.LogWarning("The Nav Mesh Agent needs to be attached to a child Gameobject, not in the same gameObject as the Animal Component");
            AIReady = true;
        }



        public virtual void Updating()
        {
            ResetAgentPosition();

            if (IsWaiting || InOffMeshLink) return;    //Do nothing while is in an offmeshLink or its Waiting

            CheckMovingTarget();

            if (FreeMove)
            {
                if (IsAirDestination && animal.ActiveStateID.ID != StateEnum.Fly)
                {
                    animal.State_Activate(StateEnum.Fly); //Forcing Fly if the animal was not flying
                    Debuging("Force! Flying!");
                }

                FreeMovement();
            }
            else
            {
                UpdateAgent();
            }
        }

        /// <summary>Reset the Agent Transform position to its Local Offset</summary>
        protected virtual void ResetAgentPosition()
        {
            //MWC: ran unconditionally every frame for every AI character. Two problems:
            //  - 'nextPosition' on a disabled / off-NavMesh agent is a no-op that Unity logs an error for.
            //  - the localPosition write pushed a transform dirty flag every frame even though nothing
            //    ever moves the agent child (Agent.updatePosition is false).
            //Both are now skipped when they cannot do anything. Same result, far less work.
            if (agent == null || !agent.isOnNavMesh) return;

            if (AgentTransform.localPosition != AgentPosition)
                AgentTransform.localPosition = AgentPosition;              //Important! Reset the Agent Position to the default Position

            Agent.nextPosition = AgentTransform.position;                  //IMPORTANT!!!!Update the Agent Position to the Transform position
        }


        /// <summary>Check if there's a path to go to</summary>
        public virtual bool PathPending() => ActiveAgent && Agent.pathPending; //CustomPatch: removed redundant double checking of Agent.isOnNavMesh


        /// <summary> Updates the Agents using he animation root motion </summary>
        public virtual void UpdateAgent()
        {
            if (HasArrived)
            {
                LookTargetOnArrival();
            }
            else if (ActiveAgent)
            {
                // Means is still calculating the path to the Destination
                if (PathPending())
                {
                    // AIDirection = Vector3.zero; //Reset the AI Direction
                    animal.StopMoving(); //Stop the Animal while the Agent is calculating the path
                    return;
                }

                SetRemainingDistance(AgentRemainingDistance);

                //MWC: PathPartial (destination unreachable — very common with Point&Click on a clipped NavMesh)
                //was never handled. Unity reports remainingDistance as Infinity for a partial path, so the
                //arrival check below could never become true and the character walked to the end of the
                //reachable path and stood there forever. Fall back to the straight-line distance to the end of
                //the path we actually have, so the arrival logic can resolve it. Only kicks in when Unity
                //hands us a non-finite value, so complete paths keep using the real along-path distance.
                if (IsPathPartial() && (float.IsInfinity(RemainingDistance) || float.IsNaN(RemainingDistance)))
                    SetRemainingDistance(Vector3.Distance(AgentTransform.position, Agent.pathEndPosition));

                if (!Arrive_Destination())   //if we haven't arrived to the destination ... Find the way
                {
                    if (IsWaitingOnTarget) return; //Do nothing if is waiting on target
                    //If is not in OffMesh Link
                    if (!CheckOffMeshLinks())
                    {
                        if (IsPathIncomplete())
                        {
                            AIDirection = Vector3.zero; //Reset the AI Direction
                            CalculatePath();
                        }
                        else
                        {
                            NormalizeDirection();
                            Move();   //Calculate the AI DIRECTION
                        }
                    }
                }
            }
        }

        //MWC: tracks whether the arrival look-at rotation is currently engaged (for angle hysteresis).
        private bool _lookAligning;

        private void LookTargetOnArrival()
        {
            if (LookAtTargetOnArrival && LookAtOffset > 0)
            {
                if (DestinationPosition == NullVector)
                    DestinationPosition = (target != null ? target.position : transform.position + transform.forward);

                //MWC: pivot from the animal's real position (NOT a Forward-offset origin). The old
                //origin moved with animal.Forward while rotating, creating a rear-steer feedback loop
                //that oscillated (wobbled) around a static target.
                var Origin = animal.Position;
                var LookAtDir = (target != null ? target.position : DestinationPosition) - Origin;

                //MWC: flatten to the horizontal plane so pitch/height differences don't jitter the yaw.
                LookAtDir = Vector3.ProjectOnPlane(LookAtDir, animal.UpVector);

                if (debugGizmos)
                {
                    MDebug.Draw_Arrow(Origin, LookAtDir, Color.magenta);
                    MDebug.DrawWireSphere(Origin, Color.magenta, 0.1f);
                }

                //MWC: hysteresis — start rotating only past ~4°, keep going until under ~1.5°.
                //Prevents flip-flopping around a single hard threshold given the rotation smoothing/inertia.
                var lookAngle = Vector3.Angle(LookAtDir, animal.Forward);

                if (lookAngle > LookAtStartAngle)
                {
                    animal.RotateAtDirection(LookAtDir);
                    _lookAligning = true;
                }
                else if (_lookAligning && lookAngle > LookAtStopAngle)
                {
                    animal.RotateAtDirection(LookAtDir);
                }
                else
                {
                    _lookAligning = false;
                    animal.StopMoving();
                }
            }
        }


        ///// <summary> The AI has as a target a AI Target with Multiple targeters </summary>
        //public bool ChasingTargeter { get; private set; }

        protected virtual bool IsPathIncomplete() => ActiveAgent && !FreeMove && Agent.pathStatus == NavMeshPathStatus.PathInvalid;

        /// <summary>The Agent found a path, but it cannot reach the requested destination (unreachable target).
        /// Kept separate from IsPathIncomplete: a partial path is still walkable, so it must NOT trigger the
        /// per-frame CalculatePath() retry that an invalid path does.</summary>
        protected virtual bool IsPathPartial() =>
            ActiveAgent && !FreeMove && Agent.pathStatus == NavMeshPathStatus.PathPartial;

        /// <summary>Check if the Height of the Destination is near the Animal</summary>
        protected virtual bool DestinationTooHigh()
        {
            TargetTooHigh = false;
            if (FreeMove) return true; //When Flying do not check the Height of the Point
            if (targetHeight == 0) return true; //Do nothing if Target heigh is zero

            if (NavMesh.SamplePosition(DestinationPosition, out var hit, Height, NavMesh.AllAreas))
            {
                if (debugGizmos)
                {
                    MDebug.DrawWireSphere(hit.position, Color.cyan, 0.1f, UpdateAI);
                    Debug.DrawRay(hit.position, animal.UpVector * Height, Color.cyan, UpdateAI);
                }
                DestinationPosition = hit.position; //Use the Projected NavMesh Position
                return true;
            }
            else
            {
                TargetTooHigh = true;
                Debuging($"<color=orange>Target too High!: <B>{DestinationPosition}</B>.  Stopping</color>");
            }

            return TargetTooHigh;
        }

        /// <summary> Check if the Target is moving </summary>
        public virtual void CheckMovingTarget()
        {
            if (UpdateAI == 0 || MTools.ElapsedTime(CurrentTime, UpdateAI))
            {
                if (Target && IsWayPoint == null) //Make Sure the Target is not a Waypoint or a Wander Area (Those are not moving targets)
                {
                    TargetIsMoving = (Target.position - TargetLastPosition).sqrMagnitude > (0.0001f);
                    TargetLastPosition = Target.position;

                    if (TargetIsMoving)
                        Update_DestinationPosition();
                }

                CurrentTime = Time.time;
            }
        }


        public virtual void CalculatePath()
        {
            if (FreeMove) return;               //Do nothing when its on Free Move

            //if (IsWaiting) return;            //Do nothing when its waiting

            if (!ActiveAgent) //Enable the Agent in case is disabled
            {
                if (!StateIsBlockingAgent) ActiveAgent = true;
                ResetFreeMoveOffMesh();
            }

            if (Agent.isOnNavMesh)
            {
                // if (Agent.destination != DestinationPosition) //Calculate the New Path **ONLY** when the Destination is Different
                {
                    //CustomPatch: Added auto validate destination position logic
                    if (autoValidateDestination)
                    {
                        if (TryValidateDestination(startValidationMovementRange, maxNumValidationIterations, agent.areaMask, DestinationPosition, out Vector3 validDestination))
                            DestinationPosition = validDestination;
                        else
                            Debuging($"<color=red>Failed to calculate a valid nav mesh destination near specified {nameof(DestinationPosition)} <B>{DestinationPosition}</B></color>");
                    }

                    Agent.SetDestination(DestinationPosition);  //Set the Current Destination;

                    if (IsWayPoint != null) DestinationPosition = Agent.destination; //Important use the Cast value on the terrain.
                }

                NormalizeDirection();

                HasArrived = false;
            }

            //  Debuging($"<color=green>Calculate Path to: <B>{DestinationPosition}</B></color>");
        }

        //CustomPatch: Added option to auto validate destination position
        public virtual bool TryValidateDestination(float movementRange, int maxNumIterations, int agentAreaMask, Vector3 destination, out Vector3 validDestination)
        {
            NavMeshHit hit = default;
            validDestination = destination;
            bool foundDestination = false;

            //TODO: Further improve worst-case scenarios and allow only max 2 queries per frame
            for (int i = 1; i <= maxNumIterations; i++)
            {
                float rangeToCheck = movementRange * i; //MWC: was `lastCheckedRange *= i` which caused factorial growth (1×,2×,6×,24×); now linear (1×,2×,3×,4×)
                if (NavMesh.SamplePosition(destination, out hit, rangeToCheck, agentAreaMask)) //MWC: use passed agentAreaMask param instead of agent.areaMask (param was dead)
                {
                    validDestination = hit.position; //MWC: was assigning to local 'destination' not the out param — validDestination always returned original value
                    foundDestination = true;
                    break;
                }
            }
            return foundDestination;
        }


        private void NormalizeDirection()
        {
            if (AgentDesiredVelocity != Vector3.zero)
            {
                AIDirection = AgentDesiredVelocity.normalized;
            }
        }


        /// <summary> Move the Animal using the Agent Direction and the Slow Multiplier </summary>
        public virtual void Move()
        {
            IsMoving = AIDirection != Vector3.zero;

            animal.Move(AIDirection * SlowMultiplier);
        }

        /// <summary> Disable the AI Agent and it Stops the Animal</summary>
        public virtual void Stop()
        {
            ActiveAgent = false; //Disable the Agent 
            AIDirection = Vector3.zero;
            DestinationPosition = NullVector;
            animal.StopMoving(); //Stop the Animal
            InOffMeshLink = false;
            Debuging($"[Stopped]. Agent Disabled");
            IsMoving = false;
        }

        /// <summary>Update The Target Position </summary>
        protected virtual void Update_DestinationPosition()
        {
            if (UpdateDestinationPosition)
            {
                DestinationPosition = GetTargetPosition();                          //Update the Target Position 

                DestinationTooHigh(); //Check if the new destination position is too high

                if (TargetTooHigh && StopOnTargetTooHigh)
                {
                    Stop();
                    return;
                }

                //Double check if the Animal is far from the target
                var DistanceOnMovingTarget = Vector3.Distance(DestinationPosition, AgentTransform.position);

                if (DistanceOnMovingTarget >= CurrentStoppingDistance)
                {
                    HasArrived = false;
                    CalculatePath();
                    Move();
                }
                else
                {
                    //Debug.Log($"{animal.name}  arrived");
                    HasArrived = true; //Check if the animal hasn't arrived to a moving target
                }

                //if (IsWaitingOnTarget)
                //{

                //    LookTargetOnArrival();
                //}

                //if (IsWaitingOnTarget)
                //{
                //    if (DistanceOnMovingTarget >= ITargeter.WaitTargeterDistance) //If we are too far from the Wait Distance then Move again
                //    {
                //        UpdateStoppingDistanceMultipleTargets();
                //        HasArrived = false;
                //        CalculatePath();
                //        Move();
                //    }
                //    else
                //    {
                //        Stop();
                //    }

                //    return;
                //}


            }
        }


        protected void Destination_RefreshTarget()
        {
            //  Debug.Log("Destination_RefreshTarget");
            DestinationPosition = (!IsAITarget.IsUnityRefNull()) ? IsAITarget.GetCenterPosition(Index) : target.position; //CustomPatch: corrected null check of unity object interface type

            //ChasingTargeter = Index != -1;      //Set to true if we got assigned an Index for the targeter (UPDATE)

            UpdateStoppingDistanceMultipleTargets();
            Update_DestinationPosition();
        }


        /// <summary>  Update the Stopping Distance from Multiple Targets this is called by the TARGET EVENTS  </summary>
        private void UpdateStoppingDistanceMultipleTargets()
        {
            if (TargeterOn)
            {
                CurrentStoppingDistance = ITargeter.GetTargeterStoppingDistance(Index);
            }
        }

        /// <summary> Store the remaining distance -- but if navMeshAgent is still looking for a path Keep Moving </summary>
        protected virtual void SetRemainingDistance(float value)
        {
            // Debug.Log($"{animal.name} RemainingDistance {RemainingDistance}");
            RemainingDistance = value;
        }


        /// <summary>Check if we have Arrived to the Destination</summary>
        public virtual bool Arrive_Destination()
        {
            if (InOffMeshLink) return false;

            //MULTIPLE TARGETS CHECK!
            if (TargeterOn) //Meaning there is limited targeting to the AI Control
            {
                if (IsWaitingOnTarget)
                {
                    //MWC: this branch used to [return true] ("arrived while waiting") WITHOUT setting HasArrived,
                    //without calling IAITarget.TargetArrived() and without invoking OnTargetArrived. Because
                    //HasArrived stayed false, UpdateAgent re-entered here every single frame, re-running
                    //RestoreDefaultAITargetValues() (which writes Agent.stoppingDistance) forever, and a queued
                    //character that reached its spot never fired its arrival. IsWaitingOnTarget is owned by
                    //AITarget (it drives TargetersWaiting), so we must NOT clear it here — instead restore the
                    //real targeter values and fall through to the normal arrival logic, which latches HasArrived.
                    if (ITargeter.StopDistance() >= RemainingDistance)
                    {
                        RestoreDefaultAITargetValues(); //We are already inside the real spot distance
                                                        //fall through ↓
                    }
                    else
                    {
                        if (ITargeter.WaitTargeterDistance >= RemainingDistance)
                        {
                            LookTargetOnArrival(); //Hold on the waiting ring and face the target

                            //AIDirection = Vector3.zero;
                            // animal.StopMoving(); //Stop the Animal while waiting
                        }

                        return false;
                    }
                }
            }

            if (CurrentStoppingDistance >= RemainingDistance)
            {
                HasArrived = true;
                SetRemainingDistance(0);                               //Reset the Remaining Distance
                AIDirection = Vector3.zero;                          //Reset AI Direction

                //Check when the Agent is trapped on an NavMesh that cannot exit
                //MWC: PathPartial now routes here too. We reached the end of what is walkable but NOT the
                //destination, so this is a failed arrival: stop / take the next target, and do NOT fire
                //IAITarget.TargetArrived() or OnTargetArrived at a position the character never reached.
                if (IsPathIncomplete() || IsPathPartial())
                {
                    Debuging($"[<color=orange>Agent Path Status: {Agent.pathStatus}]. Force Stop. <B>Checking Next Target </B></color>");

                    if (AutoNextTarget)  //Set and Move to the Next Target
                        MovetoNextTarget();
                    else
                        Stop();

                    return true;
                }


                Move();

                if (target)
                {
                    Debuging($"<color=green>has arrived to: <B>{target.name}</B> → {DestinationPosition} </color>");

                    CheckInteractions();

                    //If we have arrived to an AI Target and the Destination is the same one
                    if (!IsAITarget.IsUnityRefNull()/* && IsAITarget.GetPosition() == DestinationPosition*/) //CustomPatch: corrected null check of unity object interface type
                    {
                        //Call the method that the Target has arrived to the destination
                        IsAITarget.TargetArrived(animal.gameObject);

                        LookAtTargetOnArrival = IsAITarget.ArriveLookAt;

                        //if the next waypoint is on the Ground then set the free Movement to false
                        if (IsAITarget.TargetType == WayPointType.Ground) FreeMove = false;


                        if (AutoNextTarget)  //Set and Move to the Next Target
                            MovetoNextTarget();
                        else
                            Stop();
                    }

                    OnTargetArrived.Invoke(target);                         //Invoke the Event On Target Arrived
                    OnTargetPositionArrived.Invoke(DestinationPosition);    //Invoke the Event On Target Position Arrived
                }
                else
                {

                    OnTargetPositionArrived.Invoke(DestinationPosition);    //Invoke the Event On Target Position Arrived
                    Debuging($"<color=green>has arrived to: <B>{DestinationPosition}</B>.  Stop</color>");
                    Stop(); //The target was removed
                }
                return true;
            }
            return false;
        }

        /// <summary>  Restore the regular stopping distance </summary>
        private void RestoreDefaultAITargetValues()
        {
            CurrentStoppingDistance = ITargeter.StopDistance();
            DestinationPosition = ITargeter.GetCenterPosition();
        }


        #region Set Assing Target and Next Targets


        /// <summary> The Current AI Target can have multiple targets and limits  </summary>
        public bool TargeterOn => !ITargeter.IsUnityRefNull() && ITargeter.Targeters > 0; //MWC: use IsUnityRefNull for destroyed-Unity-object safety, consistent with rest of file

        /// <summary>Set the next Target</summary>   
        public virtual void SetTarget(Transform newTarget, bool move)
        {
            // DO NOT SET THIS BECAUSE NEXT TARGET CAN BE ALSO the SAME (Wander Areas)
            // if (newTarget == target) return; //Do not assign the same target??? can be a bug??


            //if (newTarget != null || newTarget.gameObject.activeInHierarchy == false)
            //{
            //    Debuging($"<color=red>Cannot set Target [{newTarget.name}] because is Inactive in the Hierarchy</color>");

            //    return;
            //}

            //Meaning it has an OLD targeter
            if (newTarget != target) ReleaseTargeter(); //MWC: shared with OnDisable/OnDestroy, see ReleaseTargeter


            target = newTarget;
            OnTargetSet.Invoke(newTarget);                                 //Invoked that the Target has changed.

            if (target != null)
            {
                TargetLastPosition = newTarget.position;                   //Since is a new Target "Reset the Target last position"
                DestinationPosition = newTarget.position;                  //Update the Target Position 


                var AITargets = newTarget.FindInterfaces<IAITarget>(); //Find all the AI Targets and find the closest one (Dragon Feet)
                IsAITarget = ClosestTarget(AITargets);


                if (!IsAITarget.IsUnityRefNull() && IsAITarget is IAITargeterTarget targ) //MWC: use IsUnityRefNull for destroyed-Unity-object safety, consistent with rest of file
                {
                    ITargeter = targ;
                    ITargeter.AddTargeter(this);       //Save the AI CONTROL INDEX FOR THE CURRENT TARGET

                    if (ITargeter.TargetersRefresh == null) ITargeter.TargetersRefresh = new UnityEvent();

                    ITargeter.TargetersRefresh.AddListener(Destination_RefreshTarget);

                    // IsWaitingOnTarget = ITargeter.TargeterIsWaiting(Index);
                }
                //else
                //{
                //    ChasingTargeter = false;
                //}

                IsTargetInteractable = newTarget.FindInterface<IInteractable>();
                IsWayPoint = newTarget.FindInterface<IWayPoint>();

                NextTarget = null;

                if (IsWayPoint != null) NextTarget = IsWayPoint.NextTarget(); //Find the Next Target on the Waypoint

                Debuging($"<color=yellow>New Target <B>[{newTarget.name}]</B> → [{DestinationPosition}]. Move = [{move}] IsAiTarget {!IsAITarget.IsUnityRefNull()}</color>"); //  corrected null check of unity object interface type

                CheckAirTarget();

                //Resume the Agent is MoveAgent is true
                if (move)
                {
                    ResetAIValues();

                    CurrentStoppingDistance = GetTargetStoppingDistance();
                    CurrentSlowingDistance = GetTargetSlowingDistance();

                    //Recheck the Stopping Distance if is a Multiple Targeter
                    UpdateStoppingDistanceMultipleTargets();


                    // var OldDest = DestinationPosition;
                    DestinationPosition = GetTargetPosition();

                    CalculatePath();

                    if (InterruptModeOnTarget && animal.IsPlayingMode)
                        animal.Mode_Interrupt(); //Interrupt a mode if is playing to go to the next target.
                    else if (ForceStopModeOnTarget && animal.IsPlayingMode) //MWC: changed to else if — both default to true, causing double-stop on the same mode
                        animal.Mode_Stop(true); //Force Stop a mode if is playing to go to the next target.

                    Move();
                    Debuging($"<color=yellow>is travelling to <B>Target: [{newTarget.name}]</B> → [{DestinationPosition}]  Index [{Index}]</color>");
                }
            }
            else
            {
                IsAITarget = null;                  //Reset the AI Target
                IsTargetInteractable = null;        //Reset the AI Target Interactable
                IsWayPoint = null;                  //Reset the Waypoint
                Debuging($"<color=yellow>Clear Target()</color>");
                if (move) Stop(); //Means the Target is null so Stop the Animal
            }
        }

        public virtual void SetTarget(GameObject target) => SetTarget(target, true);
        public virtual void SetTarget(GameObject target, bool move) => SetTarget(target != null ? target.transform : null, move);

        /// <summary>Remove the current Target and stop the Agent </summary>
        public virtual void ClearTarget() => SetTarget((Transform)null, false);

        /// <summary>Remove the current Target </summary>
        public virtual void NullTarget() => target = null;

        /// <summary>Assign a new Target but it does not move it to it</summary>
        public virtual void SetTargetOnly(Transform target) => SetTarget(target, false);
        public virtual void SetTargetOnly(GameObject target) => SetTarget(target, false);
        public virtual void SetTarget(Transform target) => SetTarget(target, true);

        /// <summary> Returns the Current Target Destination</summary>
        public virtual Vector3 GetTargetPosition()
        {
            var TargetPos = (!IsAITarget.IsUnityRefNull()) ? IsAITarget.GetCenterPosition(Index) : target.position; //MWC: use IsUnityRefNull for destroyed-Unity-object safety

            //HACK FOR WHEN THE TARGET REMOVED THEIR AI TARGET COMPONENT???
            if (TargetPos == Vector3.zero) TargetPos = target.position;

            return TargetPos;
        }

        public virtual float GetTargetStoppingDistance() => (IsAITarget != null) ? IsAITarget.StopDistance() : (stoppingDistance * animal.ScaleFactor);
        public virtual float GetTargetSlowingDistance() => (IsAITarget != null) ? IsAITarget.SlowDistance() : (slowingDistance * animal.ScaleFactor);

        /// <summary>Set the Next Target from  on the NextTargets Stored on the Waypoints or Zones</summary>

        public virtual void SetNextTarget(GameObject next)
        {
            NextTarget = next.transform;
            IsWayPoint = next.GetComponent<IWayPoint>(); //Check if the next gameobject is a Waypoint.
        }


        /// <summary> Resets al the Internal Values of the AI Control  </summary>
        public virtual void ResetAIValues()
        {
            StopWait();                                                 //If the Animal was waiting Reset the waiting IMPORTANT!!
            SetRemainingDistance(float.PositiveInfinity);                 //Set the Remaining Distance as the Max Float Value

            // MinRemainingDistance = float.PositiveInfinity;              //Set the Remaining Distance as the Max Float Value
            HasArrived = false;
        }

        /// <summary> Find the Closest  </summary>
        private IAITarget ClosestTarget(IAITarget[] targets)
        {
            IAITarget result = null;

            if (targets != null)
            {
                float closeDist = float.PositiveInfinity;
                foreach (var t in targets)
                {
                    var Dist = (transform.position - t.GetCenterPosition()).sqrMagnitude;

                    if (closeDist > Dist)
                    {
                        result = t;
                        closeDist = Dist;
                    }
                }
            }

            return result;
        }

        public virtual void MovetoNextTarget()
        {
            if (NextTarget == null)
            {
                Debuging("There's no Next Target");
                Stop();
                return;
            }

            if (IsWayPoint != null)
            {
                StopWait();

                if (WaitTimeMult > 0)
                {   //IMPORTANT YOU NEED TO WAIT 1 FRAME ALWAYS TO GO TO THE NEXT WAYPOINT
                    I_WaitToNextTarget = C_WaitToNextTarget(IsWayPoint.WaitTime * WaitTimeMult, NextTarget);

                    StartCoroutine(I_WaitToNextTarget);
                }
            }
            else
            {
                SetTarget(NextTarget);
            }
        }

        public void StopWait()
        {
            IsWaiting = false;
            if (I_WaitToNextTarget != null) StopCoroutine(I_WaitToNextTarget); //Stop the coroutine in case it was playing
        }

        /// <summary> Check if the Next Target is a Air Target, if true then go to it</summary>
        internal virtual bool CheckAirTarget()
        {
            if (!CanFly) return false;

            if (IsAirDestination && !FreeMove)    //If the animal can fly, there's a new wayPoint & is on the Air
            {
                if (Target) Debuging($"Target {Target} is in the Air.  Activating Air Destination State State", Target.gameObject);
                animal.State_Activate(AirDestinationState != null ? AirDestinationState : StateEnum.Fly);
                FreeMove = true;

                ActiveAgent = false; //Disable the Agent
            }

            return IsAirDestination;
        }

        #endregion

        public virtual void SetDestination(Vector3 PositionTarget) => SetDestination(PositionTarget, true);

        /// <summary>Set the next Destination Position without having a target</summary>   
        public virtual void SetDestination(Vector3 newDestination, bool move)
        {
            LookAtTargetOnArrival = false; //Do not Look at the Target when its setting a destination

            if (newDestination == DestinationPosition) return;  //Means that you're already going to the same point so Skip the code

            //We are already near the destination point
            if (Vector3.Distance(newDestination, DestinationPosition) < stoppingDistance) return;

            CurrentStoppingDistance = PointStoppingDistance;    //Reset the stopping distance when Set Destination is used.

            ResetAIValues();

            if (IsOnNonMovingMode)
                animal.Mode_Interrupt();

            IsWayPoint = null;

            if (I_WaitToNextTarget != null)
                StopCoroutine(I_WaitToNextTarget);                          //if there's a coroutine active then stop it

            DestinationPosition = newDestination;                           //Update the Target Position



            if (move)
            {
                CalculatePath();
                Move();
                Debuging($"<color=yellow>is travelling to: {DestinationPosition} </color>");
            }
        }

        /// <summary>Set the next Destination Position without having a target</summary>   
        public virtual void SetDestination(Vector3Var newDestination) => SetDestination(newDestination.Value);

        public virtual void SetDestinationClearTarget(Vector3 PositionTarget)
        {
            target = null;
            SetDestination(PositionTarget, true);
        }


        /// <summary>Check Interactions when Arriving to the Destination</summary>
        protected virtual void CheckInteractions()
        {
            if (IsTargetInteractable != null && IsTargetInteractable.Auto) //If the interactable is set to Auto!!!!!!!
            {
                if (!Interactor.IsUnityRefNull()) //CustomPatch: corrected null check of unity object interface type
                {
                    Debuging($"Interact with : <b><{IsTargetInteractable.UserGo.name}></b>. Interactor [{Interactor.UserGo.name}]");
                    Interactor.Interact(IsTargetInteractable); //Do an Interaction if the Animal has an Interactor
                }
                else
                {
                    Debuging($"Interact with : <b><{IsTargetInteractable.UserGo.name}></b>.  Interactor:Null");
                    IsTargetInteractable.Interact(0, animal.gameObject); //Do an Empty Interaction does not have an interactor
                }

            }
        }

        /// <summary> Move Freely towards the Destination.. No Obstacle is calculated</summary>
        protected virtual void FreeMovement()
        {
            if (!HasArrived)
            {
                AIDirection = (DestinationPosition - animal.transform.position); //Important to be normalized!!
                SetRemainingDistance(AIDirection.magnitude);

                AIDirection = AIDirection.normalized * SlowMultiplier; //Important to be normalized!!

                //Debug.Log("AIDirection = " + AIDirection);

                animal.Move(AIDirection);
                Arrive_Destination();
            }
        }


        protected virtual bool CheckOffMeshLinks()
        {
            if (AgentInOffMeshLink && !InOffMeshLink)                         //Check if the Agent is on a OFF MESH LINK (Do this once! per offmesh link)
            {
                InOffMeshLink = true;                                            //Just to avoid entering here again while we are on a OFF MESH LINK
                LastOffMeshDestination = DestinationPosition;

                Debug.DrawRay(DestinationPosition, Vector3.up * 3, Color.white, 2);

                OffMeshLinkData OMLData = Agent.currentOffMeshLinkData;

                var StartPoint = OMLData.startPos;
                var EndPoint = OMLData.endPos;
                EndOffMeshPos = OMLData.endPos;

                if (debugGizmos)
                {
                    var debTime = 3f;

                    MDebug.DrawLine(StartPoint, EndPoint, Color.yellow, debTime);
                    MDebug.DrawRay(StartPoint, Vector3.up * 2, Color.yellow, debTime);
                    MDebug.DrawWireSphere(StartPoint, Color.yellow, 0.3f, debTime);
                    MDebug.DrawRay(EndPoint, Vector3.up * 2, Color.yellow, debTime);
                    MDebug.DrawWireSphere(EndPoint, Color.yellow, 0.3f, debTime);
                }

                if (OMLData.linkType == OffMeshLinkType.LinkTypeManual)        //Means that it has a OffMesh Link component
                {

#if UNITY_6000_0_OR_NEWER
                    var _link = OMLData.owner as Unity.AI.Navigation.NavMeshLink;    //Check if the OffMeshLink is a Manually placed  Link
#else
                    var _link = OMLData.offMeshLink;                                //Check if the OffMeshLink is a Manually placed Link
#endif

                    if (_link)
                    {
                        var AnimalLink = _link.GetComponent<MAIAnimalLink>();

                        //CUSTOM OFFMESHLINK
                        if (AnimalLink)
                        {
                            MDebug.DrawRay(animal.Position, StartPoint.DirectionTo(EndPoint), Color.red, 2);
                            if (debug) Debuging($"<color=white>is on a <b>[OffmeshLink Animal Link]</b> -> [{AnimalLink.transform.name}]</color>");
                            AnimalLink.Execute(this, animal, StartPoint, EndPoint);
                            return true;
                        }

                        //Search if the OFFMESH IS An ACTION ZONE (EXAMPLE CRAWL)
                        var IsOffMeshZone = _link.FindInterface<IZone>();

                        if (IsOffMeshZone != null)                                           //if the OffmeshLink is a zone and is not making an action
                        {
                            if (debug) Debuging($"<color=white>is on a <b>[OffmeshLink Zone]</b> -> [{IsOffMeshZone.transform.name}]</color>");
                            IsOffMeshZone.ActivateZone(animal);
                            return true;
                        }

                        AIDirection = StartPoint.DirectionTo(EndPoint);     //Calculate the Direction to the OffMeshLink
                        animal.Move(AIDirection);                           //Move where the AI DIRECTION FROM THE OFFMESH IS Pointing

                        if (_link.CompareTag("Fly"))
                        {
                            Debuging($"<color=white>is On a <b>[OffmeshLink]</b> -> [Fly]</color>");
                            FlyOffMesh(EndPoint);
                        }
                        else if (_link.CompareTag("Climb"))
                        {
                            Debuging($"<color=white>is On a <b>[OffmeshLink]</b> -> [Climb] -> {_link.transform.name}</color>");
                            ClimbOffMesh();
                        }
                        else if (_link.area == 2)  //2 is Off mesh Jump
                        {
                            AIDirection = StartPoint.DirectionTo(EndPoint);
                            animal.Move(AIDirection);//Move where the AI DIRECTION FROM THE OFFMESH IS Pointing
                            animal.State_Activate(StateEnum.Jump);       //if the OffMesh Link is a Jump type activate the jump
                            Debuging($"<color=white>is On a <b>[OffmeshLink]</b> -> [Jump]</color>");
                        }
                    }
                    else
                    {
                        Debuging($"<color=white>is On a <b>[Undefined or NavMeshLink]</b></color>");

                        if (IMoveOffMeshLink != null) StopCoroutine(IMoveOffMeshLink);
                        IMoveOffMeshLink = C_OffMeshNotFound(OMLData);
                        StartCoroutine(IMoveOffMeshLink);

                        CompleteAgentOffMesh();
                    }
                }
                else if (OMLData.linkType == OffMeshLinkType.LinkTypeJumpAcross)             //Means that it has a OffMesh Link component
                {
                    AIDirection = StartPoint.DirectionTo(EndPoint);
                    animal.Move(AIDirection);//Move where the AI DIRECTION FROM THE OFFMESH IS POINting
                    Debuging($"<color=white>is On a <b>[OffmeshLink]</b> -> [LinkTypeJumpAcross]</color>");
                    animal.State_Activate(StateEnum.Jump); //2 is Jump State
                }
                else if (OMLData.linkType == OffMeshLinkType.LinkTypeDropDown)
                {
                    Debug.DrawRay(OMLData.endPos, Vector3.up, Color.yellow, 2);

                    //This was causing issues on tiny slopes 
                    CompleteOffMeshLink();
                }

                return true;
            }
            return false;
        }


        protected virtual IEnumerator C_OffMeshNotFound(OffMeshLinkData OMLData)
        {
            yield return null;
            // ActiveAgent = false;
            EndOffMeshPos = OMLData.endPos;

            var Dist = Vector3.Distance(transform.position, EndOffMeshPos);

            while (Dist > stoppingDistance)
            {
                AIDirection = MTools.DirectionTarget(transform.position, EndOffMeshPos).normalized;
                animal.Move(AIDirection);//Move where the AI DIRECTION FROM THE OFFMESH IS POINting
                Dist = Vector3.Distance(transform.position, EndOffMeshPos);
                yield return null;
            }
            ActiveAgent = true;

            Debuging("Exit Undefined OffMeshLink");
            //InOffMeshLink = false;
            CompleteOffMeshLink();
            yield return null;

        }

        /// <summary> Completes the OffmeshLink in case the animal was in one </summary>
        public virtual void CompleteOffMeshLink()
        {
            if (InOffMeshLink)
            {
                CompleteAgentOffMesh();

                InOffMeshLink = false;
                DestinationPosition = LastOffMeshDestination;   //restore the OffMesh Link
                CalculatePath();
                Move();

                Debuging($"<color=white>Complete <b>[OffmeshLink]</b></color>");
            }
        }

        protected virtual void CompleteAgentOffMesh()
        {
            if (Agent && Agent.isOnOffMeshLink)
                Agent.CompleteOffMeshLink();                    //Complete an offmesh link in case the Agent was in one
        }

        protected virtual void FlyOffMesh(Vector3 endPoint) //MWC: renamed param from 'target' to 'endPoint' — was shadowing class-level Transform target field
        {
            ResetFreeMoveOffMesh();
            IFreeMoveOffMesh = C_FlyMoveOffMesh(endPoint);
            StartCoroutine(IFreeMoveOffMesh);
        }

        protected virtual void ClimbOffMesh()
        {
            if (IClimbOffMesh != null) StopCoroutine(IClimbOffMesh);
            IClimbOffMesh = C_Climb_OffMesh();
            StartCoroutine(IClimbOffMesh);
        }


        /// <summary>Check if the The animal was moving on a Free OffMesh Link </summary>
        protected virtual void ResetFreeMoveOffMesh()
        {
            if (IFreeMoveOffMesh != null)
            {
                InOffMeshLink = false;
                StopCoroutine(IFreeMoveOffMesh);
                IFreeMoveOffMesh = null;
            }
        }

        protected virtual IEnumerator C_WaitToNextTarget(float time, Transform NextTarget)
        {
            IsWaiting = true;

            if (time > 0)
            {
                yield return null; //SUUUUUUUUUPER  IMPORTANT!!!!!!!!!
                Debuging($"<color=white> is waiting <B>{time:F2}</B> seconds to go to <B>[{NextTarget.name}]</B> → {DestinationPosition} </color>");

                animal.Move(AIDirection = Vector3.zero); //Stop the Animal
                yield return new WaitForSeconds(time);
            }
            SetTarget(NextTarget);
        }

        protected virtual IEnumerator C_FlyMoveOffMesh(Vector3 endPoint) //MWC: renamed param from 'target' (Vector3) — was shadowing class-level Transform target field
        {
            animal.State_Activate(AirDestinationState); //Set the State to Fly
            InOffMeshLink = true;
            float distance = float.MaxValue;
            EndOffMeshPos = endPoint;

            while (distance > CurrentStoppingDistance)
            {
                if (target == null) break; //MWC: now correctly checks class-level Transform target field (was checking Vector3 struct — always false)
                animal.Move((endPoint - animal.transform.position).normalized * SlowMultiplier);
                distance = Vector3.Distance(animal.transform.position, endPoint);
                yield return null;
            }
            animal.ActiveState.AllowExit();

            Debuging("Exit Fly State Off Mesh");

            InOffMeshLink = false;
        }

        protected virtual IEnumerator C_Climb_OffMesh()
        {
            animal.State_Activate(StateEnum.Climb); //Set the State to Climb
            InOffMeshLink = true;
            yield return null;
            ActiveAgent = false;

            EndOffMeshPos = target != null ? target.position : animal.transform.position; //MWC: guard null target — target may be cleared during coroutine

            while (animal.ActiveState.ID == StateEnum.Climb)
            {
                animal.SetInputAxis(Vector3.forward); //Move Upwards on the Climb
                yield return null;
            }

            Debuging("Exit Climb State Off Mesh");

            InOffMeshLink = false;

            IClimbOffMesh = null;
        }

        public void ResetStoppingDistance() => CurrentStoppingDistance = DefaultStoppingDistance;
        public void ResetSlowingDistance() => CurrentSlowingDistance = SlowingDistance;
        public float StopDistance() => CurrentStoppingDistance;
        public float SlowDistance() => SlowingDistance;

        public virtual void ValidateAgent()
        {
            if (agent == null) agent = gameObject.FindComponent<NavMeshAgent>();

            AgentTransform = (agent != null) ? agent.transform : transform;
        }

        //MWC: the CustomPatch comment claimed Conditionals were added but the attribute was missing — now actually applied
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        protected virtual void Debuging(string Log) { if (debug) MDebug.Log($"<B>[{animal.name} AI]</B> " + Log, this); }

        [System.Diagnostics.Conditional("UNITY_EDITOR")] //MWC: strips call-site string interpolation from player builds
        protected virtual void Debuging(string Log, GameObject obj) { if (debug) MDebug.Log($"<B>[{animal.name} AI]</B> " + Log, obj); }

#if UNITY_EDITOR
        [HideInInspector] public int Editor_Tabs1;

        protected virtual void OnValidate()
        {
            if (animal == null) animal = gameObject.FindComponent<MAnimal>();
            ValidateAgent();

            //MWC: two null-deref bugs here. (1) The inner test used `||` so when [StopAgentOn] was null
            //it fell through to StopAgentOn.Count -> NullReferenceException. (2) When [stopAgentOn] was null
            //the first branch short-circuited into new IDList<StateID>(null) -> new List<T>(null) throws.
            //Only migrate when there is actually something legacy to migrate.
            if (StopAgentOn != null && StopAgentOn.Count > 0 && (stopAgentOn == null || stopAgentOn.Count == 0))
            {
                stopAgentOn = new IDList<StateID>(StopAgentOn);
                StopAgentOn = null;
            }
            else stopAgentOn ??= new IDList<StateID>(); //MWC: never leave the list null

            //MWC: keep the arrival look-at hysteresis valid — Stop angle must stay below Start angle,
            //otherwise the band collapses and the in-place rotation can wobble again.
            if (LookAtStopAngle >= LookAtStartAngle)
                LookAtStopAngle = Mathf.Max(0f, LookAtStartAngle - 0.5f);
        }


        void Reset()
        {
            SetDefaultStopAgent();

            AirDestinationState = MTools.GetInstance<StateID>("Fly"); //Set the Air Destination State to be Fly
        }

        void SetDefaultStopAgent()
        {
            stopAgentOn = new IDList<StateID>()
            {
                items = new List<StateID>()
                {
                MTools.GetInstance<StateID>("Fall"),
                MTools.GetInstance<StateID>("Jump"),
                MTools.GetInstance<StateID>("Fly") ,
                MTools.GetInstance<StateID>("Climb")
                }
            };
        }

        #region In Game Log

        /// <summary> Colors used by the In-Game Log (Scene View) </summary>
        private const string C_On = "#6BFF6B", C_Off = "#8E949A", C_Label = "#C8CED4", C_Value = "#FFC94D", C_Warning = "#FF5C5C";

        /// <summary> Rich text row for a bool on the In-Game Log. Green when it's true, dimmed when it's false </summary>
        private string LogFlag(bool val, string label) => $"\n<color={(val ? C_On : C_Off)}>{(val ? "[X]" : "[  ]")} {label}</color>";

        /// <summary> Rich text row for a value on the In-Game Log </summary>
        private string LogRow(string label, string value, string color = C_Value) => $"\n<color={C_Label}>{label}:</color> <color={color}>{value}</color>";

        /// <summary> Name of a Transform used on the In-Game Log. Dimmed when there's no Transform </summary>
        private string LogName(Transform t) => t != null ? t.name : "-none-";

        /// <summary> Float used on the In-Game Log. Infinity values are printed as a symbol </summary>
        private string LogFloat(float value) => float.IsInfinity(value) || float.IsNaN(value) ? "---" : value.ToString("F2");

        /// <summary> World position where the In-Game Log is drawn. On top of the Animal </summary>
        private Vector3 LogPosition => transform.position + (animal != null ? animal.UpVector * (animal.Height * 2f) : Vector3.up * 2f);

        private static GUIStyle _debugLabelStyle; //MWC: cached debug label style (see OnDrawGizmosSelected)

        /// <summary> Solid dark color for the In-Game Log background. It tints the white texture used by the style </summary>
        private static readonly Color LogBackgroundColor = new(0.04f, 0.05f, 0.07f, 1f);

        /// <summary> Cached style used by the In-Game Log. Rich text on top of a solid dark box </summary>
        private static GUIStyle DebugLabelStyle
        {
            get
            {
                if (_debugLabelStyle == null || _debugLabelStyle.normal.background == null) //The texture is lost on Scene changes
                {
                    _debugLabelStyle = new GUIStyle(GUI.skin.box)
                    {
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.UpperLeft,
                        richText = true, //Needed for the colors
                        padding = new RectOffset(10, 10, 8, 8),
                        border = new RectOffset(0, 0, 0, 0)
                    };
                    _debugLabelStyle.normal.textColor = Color.white;

                    //A plain white texture tinted by GUI.backgroundColor. It's never lost, unlike a texture created here
                    _debugLabelStyle.normal.background = EditorGUIUtility.whiteTexture;
                }
                return _debugLabelStyle;
            }
        }

        /// <summary> Draws the AI Status on the Scene View, on top of the Animal </summary>
        protected virtual void DrawInGameLog()
        {
            var log = $"<color=#FFFFFF><b>AI Log [{name}]</b></color>";

            log += LogRow("Target", LogName(Target), Target != null ? C_Value : C_Off);
            log += LogRow("Next Target", LogName(NextTarget), NextTarget != null ? C_Value : C_Off);
            log += LogRow("Remaining", LogFloat(RemainingDistance));
            log += LogRow("Stop Distance", LogFloat(CurrentStoppingDistance));

            if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
            {
                var status = Agent.pathStatus;

                log += LogRow("Path", status.ToString(), status switch
                {
                    NavMeshPathStatus.PathComplete => C_On,
                    NavMeshPathStatus.PathPartial => C_Value,
                    _ => C_Warning,
                });
            }
            else if (Agent != null && !FreeMove)
            {
                log += LogRow("Path", Agent.isActiveAndEnabled ? "Agent not on the NavMesh" : "Agent Disabled", C_Warning);
            }

            log += LogFlag(HasArrived, "Has Arrived");
            log += LogFlag(ActiveAgent, "Active Agent");
            log += LogFlag(TargetIsMoving, "Target is Moving");
            log += LogFlag(!IsAITarget.IsUnityRefNull(), "Target is AITarget"); //CustomPatch: corrected null check of unity object interface type
            log += LogFlag(!IsWayPoint.IsUnityRefNull(), "Target is WayPoint"); //CustomPatch: corrected null check of unity object interface type
            log += LogFlag(TargetTooHigh, "Target Too High");
            log += LogFlag(IsWaiting, "Waiting");
            log += LogFlag(IsWaitingOnTarget, "Waiting on Target");
            log += LogFlag(IsOnMode, "On Mode");
            log += LogFlag(FreeMove, "Free Move");
            log += LogFlag(InOffMeshLink, "In OffMesh Link");

            //The Gizmos below leave Handles.color tinted (and Handles.Label multiplies the whole label by it)
            var dHandles = Handles.color;
            var dC = GUI.color;
            var dCC = GUI.contentColor;
            var dBG = GUI.backgroundColor;

            Handles.color = GUI.color = GUI.contentColor = Color.white;
            GUI.backgroundColor = LogBackgroundColor;

            Handles.Label(LogPosition, log, DebugLabelStyle);

            Handles.color = dHandles;
            GUI.color = dC;
            GUI.contentColor = dCC;
            GUI.backgroundColor = dBG;
        }

        #endregion

        protected virtual void OnDrawGizmosSelected()
        {
            var isPlaying = Application.isPlaying;

            if (isPlaying && debugStatus) DrawInGameLog();

            if (!debugGizmos) return;


            //Paths
            //MWC: NavMeshAgent.path allocates a fresh NavMeshPath on every get, and .corners allocates a
            //fresh Vector3[] on every get. The old loop touched Agent.path.corners 3x per corner, so drawing
            //an N-corner path allocated ~6N objects per frame while selected. Read both once.
            if (isPlaying && Agent && Agent.isOnNavMesh)
            {
                var corners = Agent.path.corners;

                Gizmos.color = Color.yellow;
                for (int i = 1; i < corners.Length; i++)
                {
                    Gizmos.DrawLine(corners[i - 1], corners[i]);
                }
            }

            if (debugGizmos)
            {

                if (isPlaying)
                {
                    MDebug.Draw_Arrow(AgentTransform.position, AIDirection * 2, Color.white);
                }

                if (Target)
                {
                    Gizmos.color = Color.yellow;

                    MDebug.DrawLine(transform.position, Target.position, 3);
                    Gizmos.DrawSphere(Target.position, 0.03f);

                    var Stop = (isPlaying) ? currentStoppingDistance : StoppingDistance;

                    Handles.color = new Color(0, 1, 0, 0.2f);
                    Handles.DrawSolidDisc(DestinationPosition, Vector3.up, Stop);
                    Handles.color = Color.green;
                    Handles.DrawWireDisc(DestinationPosition, Vector3.up, Stop);

                    // Gizmos.DrawLine(transform.position, Target.position);
                }

                if (AgentTransform)
                {
                    var scale = animal ? animal.ScaleFactor : transform.lossyScale.y;
                    var Pos = (isPlaying) ? DestinationPosition : AgentTransform.position;
                    var Stop = (isPlaying) ? CurrentStoppingDistance : StoppingDistance * scale;
                    var Slow = (isPlaying) ? CurrentSlowingDistance : SlowingDistance * scale;


                    if (!ITargeter.IsUnityRefNull() && Index != -1)
                    {
                        Stop = ITargeter.StopDistance();
                        Slow = 0;
                    }

                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(AgentTransform.position, 0.025f);


                    if (Slow > Stop)
                    {
                        Handles.color = Color.cyan;
                        Handles.DrawWireDisc(Pos, Vector3.up, Slow);
                    }

                    if (isPlaying)
                    {
                        Handles.color = HasArrived ? Color.green : Color.red;
                        //Handles.DrawWireDisc(Pos, Vector3.up, Stop);
                        Handles.DrawWireDisc(AgentTransform.position, Vector3.up, selfRadius * scale);
                    }
                    else
                    {
                        Handles.color = (Color.red + Color.yellow) / 2;
                        Handles.DrawWireDisc(AgentTransform.position, Vector3.up, stoppingDistance * scale);
                    }

                    if (selfRadius > 0)
                    {
                        var solidColor = hasArrived ? new Color(1, 0.92f, 0.16f, 0.2f) : new Color(1, 0, 0, 0.2f);
                        var WireColor = hasArrived ? Color.yellow : Color.red;

                        Handles.color = solidColor;
                        Handles.DrawSolidDisc(AgentTransform.position, Vector3.up, selfRadius * scale);
                        Handles.color = WireColor;
                        Handles.DrawWireDisc(AgentTransform.position, Vector3.up, selfRadius * scale);
                    }
                }
            }
        }
#endif
    }

    #region Inspector
#if UNITY_EDITOR
    [CustomEditor(typeof(MAnimalAIControl), true), CanEditMultipleObjects]
    public class AnimalAIControlEd : Editor
    {
        private MAnimalAIControl M;

        protected SerializedProperty
            stoppingDistance, selfRadius,
            SlowingDistance, LookAtOffset, LookAtStartAngle, LookAtStopAngle, targett, UpdateAI, slowingLimit, targetHeight, StopOnTargetTooHigh, UseScale, ClearTargetOnDisable,
            agent, animal, PointStoppingDistance, OnTargetPositionArrived, OnTargetArrived, InterruptModeOnTarget, ForceStopModeOnTarget,
            // disableInput, enableInput,
            AirDestinationState, OnDisabled, OnEnabled, stopAgentOn,
            OnTargetSet, debugGizmos, debugStatus, debug, Editor_Tabs1, nextTarget,
            DisableInputAIOn, EnableInputAIOff,
            AgentTransform,// OffMeshAlignment,
                           // StopAgentOn,
            WaitTimeMult//, TurnAngle
            ;

        //CustomPatch: Added auto validate destination position logic
        private SerializedProperty autoValidateDestination;
        private SerializedProperty startValidationMovementRange;
        private SerializedProperty numValidationIterations;
        protected virtual void OnEnable()
        {
            M = (MAnimalAIControl)target;

            animal = serializedObject.FindProperty("animal");
            UseScale = serializedObject.FindProperty("UseScale");
            targetHeight = serializedObject.FindProperty("targetHeight");
            StopOnTargetTooHigh = serializedObject.FindProperty("StopOnTargetTooHigh");
            AgentTransform = serializedObject.FindProperty("AgentTransform");
            WaitTimeMult = serializedObject.FindProperty("waitTimeMult");
            OnDisabled = serializedObject.FindProperty("OnDisabled");
            OnEnabled = serializedObject.FindProperty("OnEnabled");
            ClearTargetOnDisable = serializedObject.FindProperty("ClearTargetOnDisable");
            AirDestinationState = serializedObject.FindProperty("AirDestinationState");
            stopAgentOn = serializedObject.FindProperty("stopAgentOn");
            GetAgentProperty();

            slowingLimit = serializedObject.FindProperty("slowingLimit");
            // TurnAngle = serializedObject.FindProperty("TurnAngle");

            EnableInputAIOff = serializedObject.FindProperty("EnableInputAIOff");
            DisableInputAIOn = serializedObject.FindProperty("DisableInputAIOn");

            OnTargetSet = serializedObject.FindProperty("OnTargetSet");
            InterruptModeOnTarget = serializedObject.FindProperty("InterruptModeOnTarget");
            ForceStopModeOnTarget = serializedObject.FindProperty("ForceStopModeOnTarget");

            OnTargetArrived = serializedObject.FindProperty("OnTargetArrived");
            OnTargetPositionArrived = serializedObject.FindProperty("OnTargetPositionArrived");
            stoppingDistance = serializedObject.FindProperty("stoppingDistance");
            selfRadius = serializedObject.FindProperty("selfRadius");
            PointStoppingDistance = serializedObject.FindProperty("PointStoppingDistance");
            SlowingDistance = serializedObject.FindProperty("slowingDistance");
            LookAtOffset = serializedObject.FindProperty("LookAtOffset");
            LookAtStartAngle = serializedObject.FindProperty("LookAtStartAngle");
            LookAtStopAngle = serializedObject.FindProperty("LookAtStopAngle");
            targett = serializedObject.FindProperty("target");
            nextTarget = serializedObject.FindProperty("nextTarget");
            //OffMeshAlignment = serializedObject.FindProperty("OffMeshAlignment");

            debugGizmos = serializedObject.FindProperty("debugGizmos");
            debugStatus = serializedObject.FindProperty("debugStatus");
            debug = serializedObject.FindProperty("debug");

            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            // StopAgentOn = serializedObject.FindProperty("StopAgentOn");

            UpdateAI = serializedObject.FindProperty("UpdateAI");

            //CustomPatch: Added auto validate destination position logic
            autoValidateDestination = serializedObject.FindProperty("autoValidateDestination");
            startValidationMovementRange = serializedObject.FindProperty("startValidationMovementRange");
            numValidationIterations = serializedObject.FindProperty("maxNumValidationIterations");

            if (M.StopAgentOn == null && M.stopAgentOn == null || M.stopAgentOn.Count == 0)
            {
                M.stopAgentOn = new IDList<StateID>
                {
                    items = new List<StateID>(2)
                    {
                        MTools.GetInstance<StateID>("Fall"),
                        MTools.GetInstance<StateID>("Jump")
                    }
                };

                MTools.SetDirty(M);
                serializedObject.ApplyModifiedProperties();
            }
        }

        public virtual void GetAgentProperty() => agent = serializedObject.FindProperty("agent");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("AI Source. Moves the animal using an AI Agent");

            if (M.Agent != null && M.animal != null && M.Agent.transform == M.animal.transform)
            {
                EditorGUILayout.HelpBox("The NavMesh Agent needs to be attached to a child gameObject. " +
                    "It cannot be in the same gameObject as the Animal Controller", MessageType.Error);
            }


            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, new string[] { "General", "Events", "Debug" });

                int Selection = Editor_Tabs1.intValue;

                if (Selection == 0) ShowGeneral();
                else if (Selection == 1) ShowEvents();
                else if (Selection == 2) ShowDebug();

                if (cc.changed)
                    Undo.RecordObject(target, "Animal AI Control Changed");
            }


            serializedObject.ApplyModifiedProperties();
        }
        private void ShowGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                targett.isExpanded = MalbersEditor.Foldout(targett.isExpanded, "Targets");

                if (targett.isExpanded)
                {
                    EditorGUILayout.PropertyField(targett, new GUIContent("Target", "Target to follow"));
                    EditorGUILayout.PropertyField(nextTarget, new GUIContent("Next Target", "Next Target the animal will go"));
                    EditorGUILayout.PropertyField(ClearTargetOnDisable);
                    EditorGUILayout.PropertyField(InterruptModeOnTarget);
                    EditorGUILayout.PropertyField(ForceStopModeOnTarget);
                }
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EnableInputAIOff.isExpanded = MalbersEditor.Foldout(EnableInputAIOff.isExpanded, "Input Source");

                if (EnableInputAIOff.isExpanded)
                {
                    EditorGUILayout.PropertyField(DisableInputAIOn);
                    EditorGUILayout.PropertyField(EnableInputAIOff);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                {
                    UpdateAI.isExpanded = MalbersEditor.Foldout(UpdateAI.isExpanded, "AI Parameters");

                    if (UpdateAI.isExpanded)
                    {
                        // EditorGUILayout.LabelField("AI Parameters", EditorStyles.boldLabel);

                        EditorGUILayout.PropertyField(UpdateAI, new GUIContent("Update Agent", " Recalculate the Path for the Agent every x seconds "));
                        EditorGUILayout.PropertyField(stoppingDistance);

                        var guicolor = GUI.color;

                        GUI.color = Color.green;
                        EditorGUILayout.PropertyField(selfRadius);

                        GUI.color = guicolor;
                        EditorGUILayout.PropertyField(SlowingDistance);

                        EditorGUILayout.PropertyField(PointStoppingDistance, new GUIContent("Point Stop Distance", "Stop Distance used on the SetDestination method. No Target Assigned"));
                        EditorGUILayout.PropertyField(StopOnTargetTooHigh);

                        EditorGUILayout.PropertyField(UseScale);
                        if (M.StopOnTargetTooHigh.Value)
                            EditorGUILayout.PropertyField(targetHeight);


                        EditorGUILayout.PropertyField(LookAtOffset);
                        EditorGUILayout.PropertyField(LookAtStartAngle);
                        EditorGUILayout.PropertyField(LookAtStopAngle);

                        EditorGUILayout.PropertyField(slowingLimit);
                        EditorGUILayout.PropertyField(WaitTimeMult);

                        // EditorGUILayout.PropertyField(OffMeshAlignment);
                    }
                }
                if (EditorGUI.EndChangeCheck())
                {
                    if (M.Agent)
                    {
                        M.Agent.stoppingDistance = stoppingDistance.floatValue;
                        serializedObject.ApplyModifiedProperties();
                    }
                }
            }

            //CustomPatch: Added auto validate destination position logic
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                autoValidateDestination.isExpanded = MalbersEditor.Foldout(autoValidateDestination.isExpanded, "Auto Validate Destination");
                if (autoValidateDestination.isExpanded)
                {
                    EditorGUILayout.PropertyField(autoValidateDestination);
                    EditorGUILayout.PropertyField(startValidationMovementRange);
                    EditorGUILayout.PropertyField(numValidationIterations);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                animal.isExpanded = MalbersEditor.Foldout(animal.isExpanded, "References");

                if (animal.isExpanded)
                {
                    EditorGUILayout.PropertyField(AirDestinationState);
                    EditorGUILayout.PropertyField(animal, new GUIContent("Animal", "Reference for the Animal Controller"));
                    EditorGUILayout.PropertyField(AgentTransform, new GUIContent("Agent", "Reference for the AI Agent Transform"));
                    EditorGUILayout.PropertyField(stopAgentOn, new GUIContent("Stop Agent On"));

                    M.ValidateAgent();

                    if (!M.AgentTransform)
                    {
                        EditorGUILayout.HelpBox("There's no Agent found on the hierarchy on this gameobject\nPlease add a NavMesh Agent Component", MessageType.Error);
                    }
                }
            }
        }

        private void ShowEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnEnabled);
                EditorGUILayout.PropertyField(OnDisabled);
                EditorGUILayout.PropertyField(OnTargetPositionArrived, new GUIContent("On Position Arrived"));
                EditorGUILayout.PropertyField(OnTargetArrived, new GUIContent("On Target Arrived"));
                EditorGUILayout.PropertyField(OnTargetSet, new GUIContent("On New Target Set"));
            }
        }

        /// <summary> Bold Style when the value is true. Kept for child editors (A* AI Control) </summary>
        protected GUIStyle Bold(bool tru) => tru ? EditorStyles.boldLabel : EditorStyles.miniBoldLabel;

        /// <summary> Color used to highlight an active Debug flag </summary>
        protected static readonly Color FlagOnColor = new(0.4f, 1f, 0.4f, 1f);

        /// <summary> Color used on the Path Status when the path is invalid </summary>
        private static readonly Color FlagErrorColor = new(1f, 0.45f, 0.45f, 1f);

        private GUIStyle flagOn, flagOff;

        /// <summary> Bold white label used by the active Debug flags. It gets tinted green by GUI.color </summary>
        protected GUIStyle FlagOnStyle => flagOn ??= new GUIStyle(EditorStyles.label)
        { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };

        /// <summary> Dimmed label used by the inactive Debug flags, so the active ones pop out </summary>
        protected GUIStyle FlagOffStyle => flagOff ??= new GUIStyle(EditorStyles.label)
        { normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) } };

        /// <summary> Draws a read only Debug flag. Green and bold when it's true, dimmed when it's false </summary>
        protected void DebugFlag(string label, bool value, string tooltip = null)
        {
            var dC = GUI.color;
            GUI.color = value ? FlagOnColor : dC;
            EditorGUILayout.ToggleLeft(new GUIContent(label, tooltip), value, value ? FlagOnStyle : FlagOffStyle);
            GUI.color = dC;
        }

        /// <summary> Draws a read only Transform field with a Ping button next to it </summary>
        private void DebugTransform(string label, Transform value, string tooltip)
        {
            using (new GUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    EditorGUILayout.ObjectField(new GUIContent(label, tooltip), value, typeof(Transform), true);

                using (new EditorGUI.DisabledGroupScope(value == null))
                {
                    if (GUILayout.Button(new GUIContent("◎", "Ping and select this Transform on the Hierarchy"),
                        EditorStyles.miniButton, GUILayout.Width(24)))
                    {
                        EditorGUIUtility.PingObject(value);
                        Selection.activeTransform = value;
                    }
                }
            }
        }

        /// <summary> Keep the Debug values updated while the game is playing </summary>
        public override bool RequiresConstantRepaint() => Application.isPlaying && M != null && Editor_Tabs1 != null && Editor_Tabs1.intValue == 2;

        private void ShowDebug()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = 50f;
                    EditorGUILayout.PropertyField(debug, new GUIContent("Console", "Print on the Console what the AI is doing"));
                    EditorGUILayout.PropertyField(debugGizmos, new GUIContent("Gizmos", "Draw the AI Gizmos on the Scene View"));
                    EditorGUIUtility.labelWidth = 80f;
                    EditorGUILayout.PropertyField(debugStatus, new GUIContent("In-Game Log", "Show the AI Status on the Game View"));
                    EditorGUIUtility.labelWidth = 0f;
                }

                if (!Application.isPlaying)
                {
                    EditorGUILayout.HelpBox("Enter Play Mode to see the AI live values", MessageType.Info);
                    return;
                }

                if (M.animal == null)
                {
                    EditorGUILayout.HelpBox("There's no Animal Controller connected to this AI Control", MessageType.Error);
                    return;
                }

                ShowDebugTargets();
                ShowDebugDistances();
                ShowDebugFlags();
                ShowDebugAgent();

                DrawChildDebug();
            }
        }

        /// <summary> Debug Tab: Current Target, Next Target and where the AI is heading </summary>
        private void ShowDebugTargets()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DebugTransform("Target", M.Target, "Target the AI is currently moving to");
                DebugTransform("Next Target", M.NextTarget, "Target the AI will move to when it arrives");

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.Vector3Field(new GUIContent("Destination", "World position the Agent is moving to"), M.DestinationPosition);
                    EditorGUILayout.Vector3Field(new GUIContent("AI Direction", "Direction sent to the Animal Controller as the Move Input"), M.AIDirection);
                }
            }
        }

        /// <summary> Debug Tab: Distances, slow multiplier and the arrival progress bar </summary>
        private void ShowDebugDistances()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.FloatField(new GUIContent("Stop Distance", "Distance the AI needs to stop from the Target"), M.CurrentStoppingDistance);
                    EditorGUILayout.FloatField(new GUIContent("Slow Distance", "Distance the AI starts slowing down"), M.CurrentSlowingDistance);
                    EditorGUILayout.FloatField(new GUIContent("Remaining Distance", "Distance left to arrive to the Destination"), M.RemainingDistance);
                    EditorGUILayout.FloatField(new GUIContent("Slow Multiplier", "Speed multiplier applied to the Animal while slowing down"), M.SlowMultiplier);
                    EditorGUILayout.IntField(new GUIContent("Targeter Index", "Index of the current WayPoint on the Targeter"), M.Index);
                }

                DrawArrivalBar();
            }
        }

        /// <summary> Draws how close the AI is to the Destination, from the Slow Distance to the Stop Distance </summary>
        private void DrawArrivalBar()
        {
            var remaining = M.RemainingDistance;

            if (float.IsNaN(remaining) || float.IsInfinity(remaining)) return;

            var slow = Mathf.Max(M.CurrentSlowingDistance, M.CurrentStoppingDistance);

            if (slow <= 0) return;

            var progress = Mathf.InverseLerp(slow, M.CurrentStoppingDistance, remaining);
            var rect = GUILayoutUtility.GetRect(1, 18, GUILayout.ExpandWidth(true));

            var dC = GUI.color;
            GUI.color = M.HasArrived ? FlagOnColor : dC;
            EditorGUI.ProgressBar(rect, M.HasArrived ? 1 : progress, M.HasArrived ? "Arrived" : $"Remaining {remaining:F2}");
            GUI.color = dC;
        }

        /// <summary> Debug Tab: all the AI booleans, highlighted in green when they are active </summary>
        private void ShowDebugFlags()
        {
            var labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 70;

            using (new GUILayout.HorizontalScope())
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Target", EditorStyles.miniBoldLabel);
                    DebugFlag("Target is Moving", M.TargetIsMoving, "The Target has moved from its last position");
                    DebugFlag("Target is AITarget", !M.IsAITarget.IsUnityRefNull(), "The Target has an AI Target component"); //CustomPatch: corrected null check of unity object interface type
                    DebugFlag("Target is WayPoint", !M.IsWayPoint.IsUnityRefNull(), "The Target is a WayPoint"); //CustomPatch: corrected null check of unity object interface type
                    DebugFlag("Target Too High", M.TargetTooHigh, "The Target is above the Target Height value");
                    DebugFlag("Waiting on Target", M.IsWaitingOnTarget, "The AI is waiting on the Target before moving to the next one");
                    DebugFlag("Targeter", M.TargeterOn, "Another component is feeding the Targets to this AI Control");

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Behaviour", EditorStyles.miniBoldLabel);
                    DebugFlag("LookAt Target", M.LookAtTargetOnArrival, "The AI rotates towards the Target when it arrives");
                    DebugFlag("Auto Next Target", M.AutoNextTarget, "The AI moves to the Next Target automatically");
                    DebugFlag("Update Destination", M.UpdateDestinationPosition, "The Destination is updated when the Target moves");
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Animal", EditorStyles.miniBoldLabel);
                    DebugFlag("AI is Moving", M.IsMoving, "The AI is sending a Move direction to the Animal");
                    DebugFlag("Is On Mode", M.IsOnMode, "The Animal is playing a Mode");
                    DebugFlag("Free Move", M.FreeMove, "The Animal is on a Free Movement State. The Agent is not used");
                    DebugFlag("In OffMesh Link", M.InOffMeshLink, "The AI is crossing an OffMesh Link");
                    DebugFlag("Waiting", M.IsWaiting, "The AI is waiting. It won't move");
                    DebugFlag("Has Arrived", M.HasArrived, "The AI has arrived to the Destination");

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Agent", EditorStyles.miniBoldLabel);
                    DebugFlag("Active Agent", M.ActiveAgent, "The NavMesh Agent is enabled and calculating paths");

                    if (M.Agent && M.Agent.isActiveAndEnabled && M.Agent.isOnNavMesh)
                    {
                        DebugFlag("Agent in NavMesh", M.Agent.isOnNavMesh, "The Agent is placed on the NavMesh");
                        DebugFlag("Agent in OffMesh", M.AgentInOffMeshLink, "The Agent is on an OffMesh Link");
                        DebugFlag("Agent Path Pending", M.Agent.pathPending, "The Agent is still calculating the path");
                    }
                }
            }

            EditorGUIUtility.labelWidth = labelWidth;
        }

        /// <summary> Debug Tab: NavMesh Agent path status, corners and speed </summary>
        private void ShowDebugAgent()
        {
            if (M.Agent == null) return;

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!M.Agent.isActiveAndEnabled || !M.Agent.isOnNavMesh)
                {
                    EditorGUILayout.HelpBox(!M.Agent.isActiveAndEnabled ?
                        "The NavMesh Agent is disabled" :
                        "The NavMesh Agent is not on the NavMesh. It cannot calculate a path", MessageType.Warning);
                    return;
                }

                var status = M.Agent.pathStatus;
                var dC = GUI.color;

                GUI.color = status switch
                {
                    NavMeshPathStatus.PathComplete => FlagOnColor,
                    NavMeshPathStatus.PathPartial => Color.yellow,
                    _ => FlagErrorColor,
                };

                EditorGUILayout.LabelField(new GUIContent("Agent Path Status",
                    "Complete: the Agent can reach the Destination. Partial/Invalid: it cannot"),
                    new GUIContent(status.ToString()), EditorStyles.boldLabel);

                GUI.color = dC;

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.IntField(new GUIContent("Agent Corners", "Corners of the path the Agent is following"), M.Agent.path.corners.Length);
                    EditorGUILayout.FloatField(new GUIContent("Agent Speed", "Current velocity of the NavMesh Agent"), M.Agent.velocity.magnitude);
                }
            }
        }

        protected virtual void DrawChildDebug()
        { }

    }
#endif
    #endregion
}