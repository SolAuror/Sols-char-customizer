// Last Change: 9/24/2026 1:40pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace MalbersAnimations.Controller
{
    /// All Callbacks/Public Methods are Here
    public partial class MAnimal
    {
        #region INPUTS

        /// <summary>Disconnect and Reconnect the States and Modes to the Input Source</summary>
        public virtual void ResetInputSource()
        {
            UpdateInputSource(false); //Disconnect
            UpdateInputSource(true); //Reconnect
        }

        /// <summary>Updates all the Input from Malbers Input in case needed (Rewired connection(</summary>
        public virtual void UpdateInputSource(bool connect)
        {
            //CustomPatch: corrected null check for possible Unity object type
            if (InputSource.IsUnityRefNull())
                InputSource = gameObject.FindInterface<IInputSource>(); //Find if we have a InputSource

            if (InputSource != null)
            {
                foreach (var state in states)
                    state.ConnectInput(InputSource, connect);

                foreach (var mode in modes)
                    mode.ConnectInput(InputSource, connect);


                foreach (var stance in Stances)
                    stance.ConnectInput(InputSource, connect);
            }
        }


        #endregion

        #region Teleport   
        /// <summary>Teleports the Animal to a Transform position </summary>
        public virtual void Teleport(Transform newPos)
        {
            if (JustTeleported) return; //If the Animal was just teleported then do nothing
            if (Sleep) return;
            if (newPos) Teleport(newPos.position);
        }

        /// <summary>Teleports the Animal to a Transform Position, It also Rotates the Animal to that transform Rotation</summary>
        public virtual void TeleportRot(Transform newPos)
        {
            if (Sleep) return;
            if (JustTeleported) return; //If the Animal was just teleported then do nothing

            if (newPos)
            {
                Rotation = newPos.rotation; //Rotation First then the position
                Teleport(newPos.position);
            }
        }


        public virtual void Teleport(Vector3 newPos)
        {
            if (JustTeleported) return; //If the Animal was just teleported then do nothing

            OnPreTeleport.Invoke(newPos); //Invoke the Pre Teleport Event
            Teleport_Internal(newPos);
            OnTeleport.Invoke(newPos);
        }

        /// <summary>Used by the States to Teleport without sending the Event </summary>
        internal void Teleport_Internal(Vector3 newPos)
        {
            if (JustTeleported) return; //If the Animal was just teleported then do nothing

            Position = newPos;
            LastPosition = Position;
            Reset_Platform(); //Reset any platform the animal was on

            JustTeleported = true; //Set the Just Teleported to true for a short time
            this.Delay_Action(5, () => JustTeleported = false); //Give a cooldown of 5 Update cycles
        }

        private bool JustTeleported { get; set; } = false;




        //MWC: Conditional strips the call and its interpolated-string argument from player builds
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debuging(string value, string color1 = "white")
        {
#if UNITY_EDITOR
            MDebug.Log($"<B>[{name}]</B> → <color={color1}>{value}</color>", this);
#endif
        }

        #endregion

        #region Gravity

        /// <summary>Resets the gravity to the default Vector.Down value</summary>
        public virtual void Gravity_ResetDirection() => Gravity_DirectionFromGround(false);

        /// <summary>Resets the gravity power to the default value</summary>
        public virtual void Gravity_ResetPower() => GravityPower = defaultGravityPower;

        /// <summary>The Ground will change the Gravity Direction. Using the ground Normal as reference</summary>
        /// <param name="value"> Enable/Disable the logic</param>
        public virtual void Gravity_DirectionFromGround(bool value)
        {
            ground_Changes_Gravity.Value = value;
            OnGroundChangesGravity.Invoke(value);

            //Reset Camera Input when Gravity is restored!
            if (!value)
            {
                UseCameraInput = DefaultCameraInput;
                Gravity = Vector3.down;
            }
        }

        /// <summary>Clears the Gravity Logic</summary>
        internal virtual void Gravity_ResetValues()
        {
            GravityTime = m_gravityTime;
            GravityStoredVelocity = Vector3.zero;
            GravityOffset = Vector3.zero;
        }

        internal void ResetUPVector()
        {
            if (RB && !RB.isKinematic)
                RB.linearVelocity = Vector3.ProjectOnPlane(RB.linearVelocity, UpVector);

            AdditivePosition = Vector3.ProjectOnPlane(AdditivePosition, UpVector);
            DeltaPos = Vector3.ProjectOnPlane(DeltaPos, UpVector);

            InertiaPositionSpeed = Vector3.ProjectOnPlane(InertiaPositionSpeed, UpVector); //Remove the UpDown Inertia

            DeltaVelocity = Vector3.ProjectOnPlane(DeltaVelocity, UpVector);

            Gravity_ResetValues();
        }


        /// <summary>Clears the Gravity Logic</summary>
        public virtual void ResetGravityValues() => Gravity_ResetValues();


        /// <summary> IDeltaRootMotion  </summary>
        public virtual void ResetDeltaRootMotion() => Reset_Movement();



        /// <summary>Aling the character instantly to the Gravity Direction</summary>
        public virtual void AlignToGravity()
        {
            Quaternion AlignRot = Quaternion.FromToRotation(t.up, UpVector) * Rotation;  //Calculate the orientation to Terrain 
            Rotation = AlignRot;
        }

        /// <summary>The Ground will change the Gravity Direction. Using the ground Normal as reference</summary>
        /// <param name="value"> Enable/Disable the logic</param>
        public virtual void GroundChangesGravity(bool value) => Gravity_DirectionFromGround(value);

        #endregion

        #region Stances

        /// <summary>Toggle the New Stance with the Default Stance▼▲ </summary>
        public virtual void Stance_Toggle(StanceID NewStance)
        {
            if (NewStance == null) Debug.LogError("The Stance you are trying to Toggle is NULL. Please check your reference!");
            else
                Stance = (Stance.ID == NewStance.ID) ? DefaultStanceID : NewStance;
        }

        public virtual void Stance_Set(StanceID id) => Stance = id;


        public virtual void Stance_SetPersistent(StanceID ID)
        {
            var Stance = Stance_Get(ID); //Find the next stance

            if (Stance != null)
            {
                Stance_Set(ID); // Set it as active
                if (ActiveStance.ID == ID || Stance.Queued) //If it was activated or it was sent to Queue then Set it persistent
                {
                    Stance.SetPersistent(true);
                }
            }
        }
        public virtual void Stance_ResetPersistent(StanceID ID)
        {
            var Stance = Stance_Get(ID); //Find the next stance
            Stance?.SetPersistent(false); //Reset Persistent
            Stance?.SetQueued(false); //Reset the queue
            Stance_Reset(); //Restore Default
        }
        public virtual void Stance_Activate(StanceID id) => Stance = id;

        /// <summary>  Change the Default Stance to another ID  </summary>
        public virtual void Stance_SetDefault(StanceID id)
        {
            if (Stance_Get(id) != null) //Find if the stance exist!! important
            {
                DefaultStanceID = id;
            }
        }

        public Stance Pin_Stance { get; set; }

        public virtual Stance Stance_Get(StanceID id)
        {
            //MWC: dictionary lookup — avoids the List.Find closure allocation (this is called from every stance API)
            if (id != null && stances_Dict != null && stances_Dict.TryGetValue(id.ID, out var cached))
                return Pin_Stance = cached;

            Pin_Stance = Stances.Find(x => x.ID == id); //Fallback: before Awake, or stances added at runtime
            if (Pin_Stance != null && id != null) stances_Dict?.TryAdd(id.ID, Pin_Stance); //MWC: cache late-found stances
            return Pin_Stance;
        }

        public virtual void Stance_Set(int id)
        {
            //MWC: use the stance dictionary before falling back to List.Find
            if (stances_Dict == null || !stances_Dict.TryGetValue(id, out var stance))
                stance = Stances.Find(x => x.ID.ID == id);

            if (stance != null)
                Stance = stance.ID;
        }
        public virtual void Stance_Enable(StanceID id) => Stance_Get(id)?.Enable(true);
        public virtual void Stance_Disable(StanceID id) => Stance_Get(id)?.Enable(false);
        public virtual void Stance_Enable(StanceID id, bool val) => Stance_Get(id)?.Enable(val);
        public virtual void Stance_Enable_Temporal(StanceID id) => Stance_Get(id)?.Disable_Temp_Restore();
        public virtual void Stance_Disable_Temporal(StanceID id) => Stance_Get(id)?.Disable_Temp();
        public virtual void Stance_Persistent(StanceID id, bool val) => Stance_Get(id)?.SetPersistent(val);
        public virtual void Stance_PersistentOn(StanceID id) => Stance_Persistent(id, true);
        public virtual void Stance_Activate_Persistent(StanceID id)
        {
            Stance_Get(id);

            if (Pin_Stance != null)
            {
                Stance_Set(Pin_Stance.ID);
                Pin_Stance.SetPersistent(true);
            }
        }

        public virtual void Stance_ResetPersistent()
        {
            ActiveStance.SetPersistent(false);
            Stance_Reset();
        }
        /// <summary>Changes the Last Stance on the Animator</summary>
        public virtual void Stance_SetLast(int id)
        {
            LastStanceID = id;
            TryAnimParameter(hash_LastStance, LastStanceID);        //Set on the Animator the Last Stance
        }

        /// <summary>Changes the Last State on the Animator</summary>
        public virtual void LastState_Reset() => TryAnimParameter(hash_LastState, -1);   //Sent to the Animator the previews Active State

        /// <summary> Set the Current State to be the Default Stance </summary>
        public virtual void Stance_Reset() => Stance = defaultStance;

        public virtual void Stance_Reset_To_Default() => Stance_Reset();

        /// <summary> Restore the Default Stance to the starting Default Stance value</summary>
        public virtual void Stance_ResetDefaultValue() => DefaultStanceID = StartingStance;

        /// <summary> Restore the Default Stance to the starting Default Stance value</summary>
        public virtual void Stance_RestoreDefaultValue() => DefaultStanceID = StartingStance;

        public virtual void Stance_RestoreDefault() => Stance_ResetDefaultValue();


        /// <summary>Add a listener to the stance Enter Event </summary>
        public virtual void Stance_EnterEvent_AddListener(StanceID stanceID, UnityAction action) => Stance_GetEvents(Stance_Get(stanceID))?.OnEnter.AddListener(action);

        /// <summary>Remove a listener to the stance Enter Event </summary>
        public virtual void Stance_EnterEvent_RemoveListener(StanceID stanceID, UnityAction action) => Stance_Get(stanceID)?.events?.OnEnter?.RemoveListener(action);

        /// <summary>Add a listener to the stance Exit Event </summary>
        public virtual void Stance_ExitEvent_AddListener(StanceID stanceID, UnityAction action) => Stance_GetEvents(Stance_Get(stanceID))?.OnExit.AddListener(action);

        /// <summary>Remove a listener to the stance Exit Event </summary>
        public virtual void Stance_ExitEvent_RemoveListener(StanceID stanceID, UnityAction action) => Stance_Get(stanceID)?.events?.OnExit?.RemoveListener(action);

        /// <summary> Returns the On Enter/Exit Stance events row of a Stance, creating the row when the Animal does not have one yet </summary>
        private EnterExitIDEvent<StanceID> Stance_GetEvents(Stance stance)
        {
            if (stance == null) return null;
            stance.events ??= EnterExitEvent_GetOrCreate(ref OnEnterExitStances, stance.ID);
            return stance.events;
        }

        /// <summary> Finds the On Enter/Exit events row for an ID on a list, adding a new empty row when there is none </summary>
        private static EnterExitIDEvent<T> EnterExitEvent_GetOrCreate<T>(ref List<EnterExitIDEvent<T>> list, T id) where T : IDs
        {
            list ??= new();
            var row = list.Find(x => x.ID == id);

            if (row == null)
            {
                row = new EnterExitIDEvent<T>() { ID = id };
                list.Add(row);
            }

            row.OnEnter ??= new();
            row.OnExit ??= new();
            return row;
        }

        #endregion

        #region Animator Methods

        /// <summary>  Method required for the Interface IAnimator Listener to send messages From the Animator to any class who uses this Interface</summary>
        public virtual bool OnAnimatorBehaviourMessage(string message, object value)
        {
            foreach (var state in states)
                state.ReceiveMessages(message, value);

            return this.InvokeWithParams(message, value);
        }

        /// <summary>Update the Animator Speed stuffs</summary>
        public void SetAnimatorSpeed(float value) => Anim.speed = AnimatorSpeed = value;


        /// <summary>Set a Int on the Animator</summary>
        public virtual void SetAnimParameter(int hash, int value)
        {
            // Debug.Log($"SetAnimParameter hash {hash} value{value}");
            Anim.SetInteger(hash, value);
        }

        /// <summary>Set a float on the Animator</summary>
        public virtual void SetAnimParameter(int hash, float value) => Anim.SetFloat(hash, value);

        /// <summary>Set a Bool on the Animator</summary>
        public virtual void SetAnimParameter(int hash, bool value) => Anim.SetBool(hash, value);

        /// <summary>Set a Trigger to the Animator</summary>
        public virtual void SetAnimParameter(int hash)
        {
            Anim.SetTrigger(hash);
            // Debug.Log("SetTrigger");
        }

        public virtual void ResetAnimTrigger(int hash)
        {
            Anim.ResetTrigger(hash);
            //  Debug.Log("ResetTrigger");
        }

        public virtual void TryAnimParameter(int Hash, float value)
        {
            if (Hash != 0) SetFloatParameter?.Invoke(Hash, value);
        }
        public virtual void TryModeOn() => TryAnimParameter(hash_ModeOn);

        public virtual void TryAnimParameter(int Hash, int value)
        {
            // Debug.Log($"ANIMAL Int hash:{Hash}, value{value}");
            if (Hash != 0)
                SetIntParameter?.Invoke(Hash, value);
        }

        public virtual void TryAnimParameter(int Hash, bool value)
        {
            if (Hash != 0) SetBoolParameter?.Invoke(Hash, value);
        }

        public virtual void TryAnimParameter(int Hash)
        {
            if (Hash != 0) SetTriggerParameter?.Invoke(Hash);
        }

        /// <summary> Set the Parameter Random to a value and pass it also to the Animator </summary>
        public void SetRandom(int value, int priority)
        {
            if (!enabled || Sleep) return;

            if (priority >= RandomPriority)
            {
                RandomPriority = priority;
                RandomID = Randomizer ? value : 0;
                TryAnimParameter(hash_Random, RandomID);
            }
        }

        public void ResetRandomPriority(int priority)
        {
            if (priority >= RandomPriority)
                RandomPriority = 0;
        }


        /// <summary>Used by Animator Events </summary>
        public virtual void EnterTag(string tag) => AnimStateTag = Animator.StringToHash(tag);
        #endregion

        #region States
        /// <summary> Set the StateFloat value and pass it  to the StateFloat parameter on the  Animator </summary>
        public void State_SetFloat(float value)
        {
            // Debug.Log($"State_Float: {value:F3}");
            State_Float = value;
            SetFloatParameter?.Invoke(hash_StateFloat, State_Float);
        }


        /// <summary> Set the StateFloat value and pass it  to the StateFloat parameter on the  Animator. Uses a Smooth Value </summary>
        public void State_SetFloat(float value, float smoothValue)
        {
            // Debug.Log($"State_Float: {value:F3}");
            State_Float = Mathf.Lerp(State_Float, value, smoothValue * DeltaTime);
            SetFloatParameter?.Invoke(hash_StateFloat, State_Float);
        }


        /// <summary>Find an old State and replace it for  a new one at Runtime </summary>
        public void State_Replace(State NewState)
        {
            var oldState = states.Find(s => s.ID == NewState.ID);

            if (NewState == oldState)
            {
                //Do nothing if we are replacing the same state
                oldState.Enable(true);
                return;
            }

            if (CloneStates)
            {
                //MWC: removed CreateInstance call whose result was immediately overwritten by Instantiate (leaked a ScriptableObject per replace)
                State instance = ScriptableObject.Instantiate(NewState);                           //Create a clone from the Original Scriptable Objects! IMPORTANT
                instance.name = instance.name.Replace("(Clone)", "(C)");
                NewState = instance;
            }


            if (oldState)
            {
                var wasActiveState = oldState.IsActiveState;

                var index = states.IndexOf(oldState);
                var oldStatePriority = oldState.Priority;

                if (CloneStates) Destroy(oldState); //Destroy the Clone

                oldState = NewState;
                oldState.AwakeState(this);
                oldState.Priority = oldStatePriority;
                oldState.InitializeState();
                // oldState.ExitState();


                states[index] = oldState; //Replace the list Item

                //MWC: keep the fast-lookup dictionary in sync with the replaced state
                if (states_Dict != null && oldState.ID != null) states_Dict[oldState.ID.ID] = oldState;

                UpdateInputSource(true); //Need to Update the Sources

                if (wasActiveState)
                {
                    oldState.ForceActivate();
                    AnimStateTag = -1;
                    OnStateChange.Invoke(oldState.ID);//Invoke the Event only when the State is no longer Pending
                    OnState?.Invoke(oldState.ID);
                    //  StartCoroutine(C_EnterCoreAnim(oldState));  //Little Hack! 
                }
            }
        }

        /// <summary>Force the Activation of an state regarding if is enable or not</summary>
        public virtual void State_Force(StateID ID) => State_Force(ID.ID);

        /// <summary>Returns if the Animal has a state by its ID</summary>
        public bool HasState(StateID ID) => HasState(ID.ID);
        public bool HasState<T>() where T : State => states.Exists(s => s is T);



        /// <summary>Returns if the Animal has a state by its int ID value</summary>
        public bool HasState(int ID) => State_Get(ID) != null;

        /// <summary>Returns if the Animal has a state by its name</summary>
        public bool HasState(string stateName) => states.Exists(s => s.name == stateName);

        public int StateEnterStatus { get; set; }
        public int StateExitStatus { get; set; }

        /// <summary>Set the State Status on the Animator</summary>
        public virtual void State_SetEnterStatus(int status)
        {
            StateEnterStatus = status;
            SetIntParameter?.Invoke(hash_StateEnterStatus, status);
            //if (debugStates)
            // Debuging($"StateEnterStatus [{status}]");
        }

        /// <summary>Set the State Status on the Animator</summary>
        public virtual void State_SetStatus(int status) => State_SetEnterStatus(status);


        /// <summary>Set the State Exit Status on the Animator</summary>
        public virtual void State_SetExitStatus(int ExitStatus)
        {
            StateExitStatus = ExitStatus;
            TryAnimParameter(hash_StateExitStatus, ExitStatus);

            //  Debuging($"State_SetExitStatus [{StateExitStatus}]");
        }

        public virtual void State_Enable(StateID ID) => State_Enable(ID.ID);
        public virtual void State_Disable(StateID ID) => State_Disable(ID.ID);

        public virtual void State_Enable(int ID)
        {
            State_Get(ID)?.Enable(true);

            var cache = states_C.Find(x => x.state.ID == ID);
            if (cache != null) cache.active = true;
        }

        public virtual void State_Disable(int ID)
        {
            State_Get(ID)?.Enable(false);

            var cache = states_C.Find(x => x.state.ID == ID);
            if (cache != null) cache.active = false; //MWC: was 'true' — disabling a state left it marked active in the states_C cache
        }

        public virtual void ActiveState_Persistent(bool value) => ActiveState.IsPersistent = value;


        /// <summary>Force the Activation of an state regarding if is enable or not</summary>
        public virtual void State_Force(int ID) => State_Force(ID, -1);


        /// <summary>Force the Activation of an state regarding if is enable or not</summary>
        public virtual void State_Force(int ID, int enterStatus)
        {
            //Do nothing if is the same state and cannot transition to itself
            if (ActiveState != null && ActiveState.ID == ID && !ActiveState.CanTransitionToItself) return;

            var state = State_Get(ID);

            if (state != null) state.ForceActivate(enterStatus);
        }

        /// <summary>  Allow Lower States to be activated </summary>
        public virtual void State_AllowExit() => ActiveState.AllowExit();


        /// <summary>  Allow Lower States to be activated  </summary>
        public virtual void State_Allow_Exit(StateID ID) => State_Allow_Exit(ID.ID);
        public virtual void State_Allow_Exit(int nextState)
        {
            if (ActiveState.AllowExit())
            {
                if (nextState != -1) State_Activate(nextState);
            }
        }
        public virtual void State_Allow_Exit(int nextState, int exitStatus)
        {
            if (ActiveState.AllowExit())
            {
                State_SetExitStatus(exitStatus);
                if (nextState != -1) State_Activate(nextState);
            }
        }

        public virtual void State_SetPersistent(StateID ID, bool value) => State_Get(ID)?.SetPersistent(value);

        public virtual void State_InputTrue(StateID ID) => State_Get(ID)?.SetInput(true);
        public virtual void State_InputFalse(StateID ID) => State_Get(ID)?.SetInput(false);


        /// <summary>Try to Activate a State directly from the Animal Script </summary>
        public virtual void State_Activate(StateID ID) => State_Activate(ID.ID);

        /// <summary>Try to Activate a State by its ID, Checking the necessary conditions for activation (NO Returns)</summary>
        public virtual void State_Try(StateID ID) => State_TryActivate(ID.ID);


        /// <summary>Try to Activate a State by its ID, Checking the necessary conditions for activation</summary>
        public virtual bool State_TryActivate(int ID)
        {
            State NewState = State_Get(ID);

            if (NewState && NewState.CanBeActivated)
            {
                return NewState.InternalTryActivate() && NewState.TryOverride;
            }
            return false;
        }

        /// <summary>Try to Activate a State directly from the Animal Script </summary>
        public virtual void State_Activate(int ID)
        {
            State NewState = State_Get(ID);

            if (NewState)
            {
                if (JustActivateState) //Delay it because another state was just activated
                {
                    this.Delay_Action(() => ActiveState.IsPending, () => CanActivateState(NewState));
                }
                else CanActivateState(NewState);
            }

            static void CanActivateState(State newState)
            {
                if (newState.CanBeActivated) newState.Activate();
            }
        }

        /// <summary>Try to Activate a State directly from the Animal Script </summary>
        public virtual void State_Activate(int ID, int StateStatus)
        {
            State NewState = State_Get(ID);

            if (NewState)
            {
                if (JustActivateState) //Delay it because another state was just activated
                {
                    this.Delay_Action(() => ActiveState.IsPending, () => CanActivateState(NewState));
                }
                else CanActivateState(NewState);
            }

            void CanActivateState(State newState)
            {
                if (newState.CanBeActivated) newState.Activate(StateStatus);
            }
        }

        /// <summary> Return a State by its  ID value </summary>
        public virtual State State_Get(int ID)
        {
            //MWC: dictionary lookup (mirrors Mode_Get) — avoids the List.Find closure allocation on hot paths
            if (states_Dict != null && states_Dict.TryGetValue(ID, out var cached)) return cached;

            var found = states.Find(s => s.ID == ID); //Fallback: before Awake, or states added at runtime
            if (found != null) states_Dict?.TryAdd(ID, found); //MWC: cache late-found states
            return found;
        }


        public virtual bool State_TryGet(int ID, out State state)
        {
            state = State_Get(ID);
            return state != null;
        }

        public virtual bool Stance_TryGet(StanceID ID, out Stance stance)
        {
            stance = Stance_Get(ID);
            return stance != null;
        }

        /// <summary> Return a State by its type</summary>
        public virtual T State_Get<T>() where T : State => states.Find(s => s is T) as T;


        /// <summary> Return a State by its ID</summary>
        public virtual State State_Get(StateID ID)
        {
            if (ID == null) return null;
            return State_Get(ID.ID);
        }

        /// <summary> Call the Reset State on the given State </summary>
        public virtual void State_Reset(int ID) => State_Get(ID)?.ResetState();

        /// <summary> Call the Reset State on the given State </summary>
        public virtual void State_Reset(StateID ID) => State_Reset(ID.ID);

        ///<summary> Find to the Possible State and store it to the (PreState) using an StateID value</summary>
        public virtual void State_Pin(StateID stateID) => State_Pin(stateID.ID);

        ///<summary> Find to the Possible State and store it to the (PreState) using an int value</summary>
        public virtual void State_Pin(int stateID) => Pin_State = State_Get(stateID);

        ///<summary>Use the (PinState) the and Try to activate it using an Input</summary>
        public virtual void State_Pin_ByInput(bool input) => Pin_State?.ActivatebyInput(input);
        public virtual void State_Pin_ByInputToggle() => Pin_State?.ActivatebyInput(!Pin_State.InputValue);

        ///<summary> Send to the Possible State (PreState) the value of the Input</summary>
        public virtual void State_Activate_by_Input(StateID stateID, bool input) => State_Activate_by_Input(stateID.ID, input);

        ///<summary> Send to the Possible State (PreState) the value of the Input</summary>
        public virtual void State_Activate_by_Input(int stateID, bool input)
        {
            State_Pin(stateID);
            State_Pin_ByInput(input);
        }

        public virtual void State_Persistent(bool value) => ActiveState.IsPersistent = value;

        ///<summary>If the Pin State is the Active State then it will set the StateExit Status</summary>
        public virtual void State_Pin_ExitStatus(int stateExitStatus)
        {
            if (Pin_State != null && Pin_State.IsActiveState)
                State_SetExitStatus(stateExitStatus);
        }

        #region State Enter Exit Events Listeners

        /// <summary>  Adds a Listener to the Enter Event of a State  </summary>
        public virtual void State_EnterEvent_AddListener(StateID stateID, UnityAction action) => State_GetEvents(State_Get(stateID))?.OnEnter.AddListener(action);

        /// <summary>  Removes a Listener to the Enter Event of a State  </summary>
        public virtual void State_EnterEvent_RemoveListener(StateID stateID, UnityAction action)
        {
            var state = State_Get(stateID);

            if (state != null)
                state.EnterExitEvent?.OnEnter?.RemoveListener(action);
        }

        /// <summary>  Adds a Listener to the Exit Event of a State  </summary>
        public virtual void State_ExitEvent_AddListener(StateID stateID, UnityAction action) => State_GetEvents(State_Get(stateID))?.OnExit.AddListener(action);

        /// <summary>  Removes a Listener to the Exit Event of a State  </summary>
        public virtual void State_ExitEvent_RemoveListener(StateID stateID, UnityAction action)
        {
            var state = State_Get(stateID);
            if (state != null)
                state.EnterExitEvent?.OnExit?.RemoveListener(action);
        }

        /// <summary> Returns the On Enter/Exit State events row of a State, creating the row when the Animal does not have one yet </summary>
        private EnterExitIDEvent<StateID> State_GetEvents(State state)
        {
            if (state == null) return null;
            state.EnterExitEvent ??= EnterExitEvent_GetOrCreate(ref OnEnterExitStates, state.ID);
            return state.EnterExitEvent;
        }
        #endregion

        #endregion

        #region Modes

        ///// <summary> Returns if the Animal has a mode By its Type</summary>
        //public virtual T Mode_Get<T>() where T : Mode => modes.Find(s => s is T) as T;


        /// <summary> Returns if the Animal has a mode By its ID</summary>
        public bool HasMode(ModeID ID) => HasMode(ID.ID);

        /// <summary> Returns if the Animal has a mode By its ID</summary>
        public bool HasMode(int ID) => Mode_Get(ID) != null;

        /// <summary> Returns a Mode by its ID</summary>
        public virtual Mode Mode_Get(ModeID ModeID)
        {
            if (ModeID == null) return null;
            return Mode_Get(ModeID.ID);
        }

        /// <summary> Returns a Mode by its ID</summary>
        public virtual Mode Mode_Get(int ModeID)
        {
            if (modes_Dict == null) { CacheAllModes(); } //Just in case

            if (modes_Dict.TryGetValue(ModeID, out Mode mode))
            {
                return mode;
            }
            return null;
        }

        public virtual void Mode_SetPower(float value)
        {
            ModePower = value;
            // Debug.Log("ModePower = " + ModePower);
            TryAnimParameter(hash_ModePower, ModePower);
        }

        /// <summary>Activate a Random Ability on the Animal using a Mode ID</summary>
        public virtual void Mode_Activate(ModeID ModeID) => Mode_Activate(ModeID.ID, -99);

        /// <summary>Enable a mode on the Animal</summary>
        /// <param name="ModeID">ID of the Mode</param>
        /// <param name="AbilityIndex">Ability Index. If this value is -1 then the Mode will activate a random Ability</param>
        public virtual void Mode_Activate(ModeID ModeID, int AbilityIndex) => Mode_Activate(ModeID.ID, AbilityIndex);

        public virtual void Mode_Activate_By_Input(ModeID ModeID, bool InputValue) => Mode_Get(ModeID.ID).ActivatebyInput(InputValue);



        /// <summary>Activate a mode on the Animal combining the Mode and Ability e.g 4002. Values under 1000 are a plain Mode ID and play a random Ability</summary>
        public virtual void Mode_Activate(int ModeID)
        {
            if (ModeID == 0) return;

            var id = Mathf.Abs(ModeID / 1000);

            if (id == 0)
                Mode_Activate(ModeID, -99); //A plain Mode ID (e.g. 4): play a random Ability
            else
                Mode_Activate(id, ModeID % 1000);
        }

        /// <summary>Activate a mode on the Animal</summary>
        /// <param name="ModeID">ID of the Mode</param>
        /// <param name="AbilityIndex">Ability Index. If this value is -99 then the Mode will activate a random Ability</param>
        public virtual void Mode_Activate(int ModeID, int AbilityIndex)
        {
            var mode = Mode_Get(ModeID);

            if (mode != null)
            {
                Pin_Mode = mode;
                Pin_Mode.TryActivate(AbilityIndex);
            }
            else
            {
                Debug.LogWarning("You are trying to Activate a Mode but here's no Mode with the ID or is Disabled: " + ModeID);
            }
        }


        /// <summary>Activate a mode on the Animal</summary>
        /// <param name="ModeID">ID of the Mode</param>
        /// <param name="AbilityIndex">Ability Index. If this value is -99 then the Mode will activate a random Ability</param>
        public virtual void Mode_Activate(int ModeID, int AbilityIndex, AbilityStatus status)
        {
            var mode = Mode_Get(ModeID);

            if (mode != null)
            {
                Pin_Mode = mode;

                var ability = Pin_Mode.GetAbility(AbilityIndex);

                if (ability != null)
                {
                    ability.Status = status; //Change the Status of the ability
                }

                Pin_Mode.TryActivate(AbilityIndex);
            }
            else
            {
                Debug.LogWarning("You are trying to Activate a Mode but here's no Mode with the ID or is Disabled: " + ModeID);
            }
        }

        /// <summary>Force the activation of a Mode and Ability, interrupting the Mode that is playing (a higher Priority Mode blocks it only when [Force Ignore Priority] is off)</summary>
        public virtual bool Mode_ForceActivate(ModeID ModeID, int AbilityIndex)
        {
            if (ModeID == null) return false;
            return Mode_ForceActivate(ModeID.ID, AbilityIndex);
        }

        /// <summary>Force the activation of a Random Ability on a Mode, interrupting the Mode that is playing (a higher Priority Mode blocks it only when [Force Ignore Priority] is off)</summary>
        public virtual void Mode_ForceActivate(ModeID ModeID)
        {
            if (ModeID == null) return;
            Mode_ForceActivate(ModeID.ID, -99);
        }

        /// <summary>Force the activation of a Mode and Ability (-99 = random Ability), interrupting the Mode that is playing (a higher Priority Mode blocks it only when [Force Ignore Priority] is off)</summary>
        public virtual bool Mode_ForceActivate(int ModeID, int AbilityIndex)
        {
            // Debug.Log($"Mode: {ModeID} ,  AbilityIndex; {AbilityIndex}");

            var mode = Mode_Get(ModeID);

            if (mode != null)
            {
                Pin_Mode = mode;
                return Pin_Mode.ForceActivate(AbilityIndex);
            }
            return false;
        }


        public virtual bool Mode_ForceActivate(int ModeID, int AbilityIndex, AbilityStatus status, float time = 0)
        {
            var mode = Mode_Get(ModeID);

            if (mode != null)
            {
                Pin_Mode = mode;
                return Pin_Mode.ForceActivate(AbilityIndex, status, time);
            }
            return false;
        }

        public virtual bool Mode_ForceActivate(int ModeID, int AbilityIndex, AbilityStatus status, float time = 0, float power = 0)
        {
            var mode = Mode_Get(ModeID);

            if (mode != null)
            {
                Pin_Mode = mode;
                Mode_SetPower(power);
                return Pin_Mode.ForceActivate(AbilityIndex, status, time);
            }
            return false;
        }


        /// <summary>Force Activate a mode on the Animal combining the Mode and Ability e.g 4002. Values under 1000 are a plain Mode ID and play a random Ability</summary>
        public virtual void Mode_ForceActivate(int ModeID)
        {
            if (ModeID == 0) return;

            var MODEID = Mathf.Abs(ModeID / 1000);

            if (MODEID == 0)
            {
                Mode_ForceActivate(ModeID, -99); //A plain Mode ID (e.g. 4): play a random Ability
            }
            else
            {
                Mode_ForceActivate(MODEID, ModeID % 1000);
            }
        }


        /// <summary>  Returns True and Activate  the mode in case ir can be Activated, if not it will return false</summary>
        public bool Mode_TryActivate(int ModeID, int AbilityIndex = -99)
        {
            var mode = Mode_Get(ModeID);

            if (mode != null)
            {
                Pin_Mode = mode;
                return Pin_Mode.TryActivate(AbilityIndex);
            }
            return false;
        }

        public bool Mode_TryActivate(int ModeID, int AbilityIndex, AbilityStatus status, float time = 0)
        {
            var mode = Mode_Get(ModeID);

            if (mode != null)
            {
                Pin_Mode = mode;

                return Pin_Mode.TryActivate(AbilityIndex, status, time);
            }
            return false;
        }

        public virtual void Mode_Stop() => Mode_Stop(false);


        /// <summary>Stop all modes </summary>
        public virtual void Mode_Stop(bool ResetModeStatus)
        {
            if (IsPlayingMode)
            {
                activeMode.InputValue = false; //Set the Input to false
                ActiveMode.Reset();
                Mode_Interrupt();
            }
            else
            {
                ModeAbility = 0; //Reset Mode 
                SetModeStatus(0);
                return;
            }

            if (ResetModeStatus)
            {
                SetModeStatus(0);
            }

            //ActiveMode.PlayingMode = false;
            ActiveMode = null;
            ModeTime = 0;
            IsPreparingMode = false;

            if (debugModes) Debuging($"Mode Stopped. [Ability] and [Mode] Set to Null");
        }




        /// <summary> Re-Check all the Sprint conditions and Invoke the Sprint Event </summary>
        public virtual void SprintUpdate() => Sprint = sprint; //Check Again the sprint every time a new state is active IMPORTANT
        public virtual void Sprint_Set(bool value) => Sprint = value;

        /// <summary>Set ModeStatus to -2 to Interrupt the mode animations (Only interrupt when there's a mode playing)</summary>
        public virtual void Mode_Interrupt()
        {
            // Debug.Log($"Mode Interrupted");

            if (IsPlayingMode)
            {
                ModeAbility = 0;
                SetModeStatus(Int_ID.Interrupted);
                ResetModeOn();
                //if (debugModes) Debuging($"Mode Interrupted");
            }

            IsPreparingMode = false;
        }


        /// <summary>Set ModeStatus to 0 to Interrupt the mode animations</summary>
        public virtual void Mode_Interrupt_Forced()
        {
            IsPreparingMode = false;
            ModeAbility = 0;
            SetModeStatus(0); //Important. Mode Status needs to be zero so Empty Mode states can be activated
            ResetModeOn();
        }

        private void ResetModeOn()
        {
            if (hash_ModeOn != 0) Anim.ResetTrigger(hash_ModeOn); //Reset the MODE ON Parameter
        }



        /// <summary>Deactivate all modes</summary>
        public virtual void Mode_Disable_All()
        {
            foreach (var mod in modes) mod.Disable();
        }

        /// <summary>Reactivate all modes</summary>
        public virtual void Mode_Enable_All()
        {
            foreach (var mod in modes) mod.Enable();
        }

        /// <summary>Disable a Mode by his ID</summary>
        public virtual void Mode_Disable(ModeID id) => Mode_Disable((int)id);

        /// <summary>Disable a Mode by his ID</summary>
        public virtual void Mode_Disable(int id) => Mode_Get(id)?.Disable();

        public virtual void Mode_Disable(string mod)
        {
            foreach (var M in modes)
            {
                if (mod.Contains(M.Name))
                    M.Disable();
            }
        }

        public virtual void Mode_Enable(string mod)
        {
            foreach (var M in modes)
            {
                if (mod.Contains(M.Name))
                    M.Enable();
            }
        }

        /// <summary>Enable a Mode by his ID</summary>
        public virtual void Mode_Enable(ModeID id) => Mode_Enable(id.ID);

        /// <summary>Enable a Mode by his ID</summary>
        public virtual void Mode_Enable(int id) => Mode_Get(id)?.Enable();

        /// <summary> Enable the Mode temporarily by an external source, use Disable Temporal when using this</summary>
        public virtual void Mode_Enable_Temporal(int id) => Mode_Get(id)?.Enable_Temporal();
        /// <summary> Enable the Mode temporarily by an external source, use Disable Temporal when using this</summary>
        public virtual void Mode_Enable_Temporal(ModeID id) => Mode_Enable_Temporal(id.ID);


        /// <summary> Disable the Mode temporarily by an external source, use Enable Temporal when using this</summary>
        public virtual void Mode_Disable_Temporal(ModeID id) => Mode_Disable_Temporal((int)id);
        /// <summary> Disable the Mode temporarily by an external source, use Enable Temporal when using this</summary>
        public virtual void Mode_Disable_Temporal(int id) => Mode_Get(id)?.Disable_Temporal();

        public virtual void Mode_Ability_Disable(string abilityName)
        {
            foreach (var M in modes)
            {
                foreach (var ability in M.Abilities)
                {
                    if (abilityName.Contains(ability.Name))
                        ability.Active = false;
                }
            }
        }

        public virtual void Mode_Ability_Enable(string abilityName)
        {
            foreach (var M in modes)
            {
                foreach (var ability in M.Abilities)
                {
                    if (abilityName.Contains(ability.Name))
                        ability.Active = true;
                }
            }
        }

        /// <summary>Set the Active Ability Index of a Mode</summary>
        public virtual void Mode_ActiveAbilityIndex(int Mode, int ActiveAbility) => Mode_Get(Mode).SetAbilityIndex(ActiveAbility);


        /// <summary>Pin a mode to Activate later</summary>
        public virtual void Mode_Pin(ModeID ID)
        {
            if (Pin_Mode != null && Pin_Mode.ID == ID) return;  //the mode is already pinned

            var pin = Mode_Get(ID);

            Pin_Mode = null; //Important! Clean the Pin Mode 
            if (pin != null && pin.Active) Pin_Mode = pin;
        }

        /// <summary>Pin an Ability on the Pin Mode to Activate later</summary>
        public virtual void Mode_Pin_Ability(int AbilityIndex)
        {
            if (AbilityIndex == 0) return;
            Pin_Mode?.SetAbilityIndex(AbilityIndex);
        }


        /// <summary>Enable/Disables an Ability on a mode, Returns true if the Method succeeded</summary>
        public virtual bool Mode_Ability_Enable(int ModeID, int AbilityID, bool enable)
        {
            var mode = Mode_Get(ModeID);
            if (mode != null)
            {
                var ability = mode.GetAbility(AbilityID);
                if (ability != null)
                {
                    ability.Active = enable;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Pin an Ability on the Pin Mode to Activate later</summary>
        public virtual void Mode_Pin_Ability_Enable(int AbilityIndex)
        {
            if (AbilityIndex == 0) return;
            var ability = Pin_Mode?.GetAbility(AbilityIndex);
            if (ability != null) ability.Active = true;
        }

        /// <summary>Pin an Ability on the Pin Mode to Disable it</summary>
        public virtual void Mode_Pin_Ability_Disable(int AbilityIndex)
        {
            if (AbilityIndex == 0) return;
            var ability = Pin_Mode?.GetAbility(AbilityIndex);
            if (ability != null) ability.Active = false;
        }

        /// <summary>Disable a mode Ability using its Combined Index</summary>
        public virtual void Mode_Ability_Disable(int IndexCombined)
        {
            var ability = Mode_AbilitybyIndexCombined(IndexCombined);

            if (ability != null)
                ability.Active = false;
        }

        /// <summary>Enable a mode Ability using its Combined Index</summary>
        public virtual void Mode_Ability_Enable(int IndexCombined)
        {
            var ability = Mode_AbilitybyIndexCombined(IndexCombined);

            if (ability != null)
                ability.Active = true;
        }

        /// <summary>Returns tbe first ModeAbility found by Name</summary>
        public virtual Ability Mode_AbilitybyName(string name)
        {
            for (int i = 0; i < modes.Count; i++)
            {
                for (int j = 0; j < modes[i].Abilities.Count; j++)
                {
                    if (modes[i].Abilities[j].Name == name)
                    {
                        return modes[i].Abilities[j];
                    }
                }
            }
            return null;
        }

        /// <summary>Returns a ModeAbility by its Combined Index. E.g. Eat is 4002</summary>
        public virtual Ability Mode_AbilitybyIndexCombined(int IndexCombined)
        {
            var mode = modes.Find(m => m.ID == IndexCombined / 1000);

            if (mode != null)
            {
                return mode.Abilities.Find(ab => ab.Index == IndexCombined % 1000);
            }
            return null;
        }

        /// <summary>Pin an Ability on the Pin Mode to Activate later</summary>
        public virtual void Mode_Pin_Disable_Ability(int AbilityIndex) => Mode_Pin_Ability_Disable(AbilityIndex);


        /// <summary>Changes  Pinned Mode Status in all the Abilities</summary>
        public virtual void Mode_Pin_Status(int aMode)
        {
            if (Pin_Mode != null)
            {
                foreach (var ability in Pin_Mode.Abilities)
                {
                    ability.Status = (AbilityStatus)aMode;
                }
            }
        }

        /// <summary>Changes the Pinned Mode time when using Hold by time Status</summary>
        public virtual void Mode_Pin_Time(float time)
        {
            if (Pin_Mode != null)
                foreach (var ab in Pin_Mode.Abilities)
                    ab.AbilityTime = time;
        }

        public virtual void Mode_Pin_Enable(bool value) => Pin_Mode?.SetActive(value);
        public virtual void Mode_Pin_Enable_Invert(bool value) => Pin_Mode?.SetActive(!value);

        public virtual void Mode_Pin_Input(bool value) => Pin_Mode?.ActivatebyInput(value);

        /// <summary>Tries to Activate the Pin Mode</summary>
        public virtual void Mode_Pin_Activate() => Pin_Mode?.TryActivate();

        /// <summary>Tries to Activate the Pin Mode with an Ability</summary>
        public virtual void Mode_Pin_AbilityActivate(int AbilityIndex) => Pin_Mode?.TryActivate(AbilityIndex);

        #endregion

        #region Movement
        public virtual void Strafe_Toggle() => Strafe ^= true;

        private bool worldSpacePlayerInput;

        /// <summary>Camera-relative player input already converted to world space.
        /// Retains analog magnitude and player turning, without AI destination braking.</summary>
        public void SetWorldMovementInput(Vector3 input)
        {
            worldSpacePlayerInput = true;
            UseRawInput = true;
            UsingMoveWithDirection = true;
            Rotate_at_Direction = false;
            RawRotateDirAxis = Vector3.zero;
            RawInputAxis = Vector3.ClampMagnitude(input, 1);
        }

        /// <summary>Gets the movement from the Input Script or AI</summary>
        public virtual void Move(Vector3 direction)
        {
            worldSpacePlayerInput = false;
            UsingMoveWithDirection = true;
            UseRawInput = false;
            Rotate_at_Direction = false;
            RawRotateDirAxis = Vector3.zero;
            DeltaAngle = 0;
            RawInputAxis = direction;//.normalized; //Store the Last Raw Direction sent (Important to Normalize?? why??)
        }

        /// <summary>Gets the movement from the Input using a 2 Vector  (ex UI Axis Joystick)</summary>
        public virtual void Move(Vector2 move) => Move(new Vector3(move.x, 0, move.y));

        /// <summary>Gets the movement from the Input ignoring the Direction Vector, using a 2 Vector  (ex UI Axis Joystick)</summary>
        public virtual void MoveWorld(Vector2 move) => MoveWorld(new Vector3(move.x, 0, move.y));

        /// <summary>Stop the animal from moving, cleaning the Movement Axis</summary>
        public virtual void StopMoving()
        {
            RawInputAxis = Vector3.zero;
            RawRotateDirAxis = Vector3.zero;
            DeltaAngle = 0;
        }
        public virtual void Stop() => StopMoving();

        /// <summary>Stop the animal from moving,Cleans all movement values</summary>
        public virtual void Reset_Movement()
        {
            DeltaAngle =
            HorizontalSpeed = 0;

            RawInputAxis =
            RawRotateDirAxis =
            MovementAxisSmoothed =
            InertiaPositionSpeed =
            TargetSpeed =
            DeltaPos =
            HorizontalVelocity =
            LastSelfMove = //MWC: clear the stored Normal-mode self-movement too (avoids a stale 1-frame impulse after teleports/resets)
            LastUpInertiaMove = //MWC: clear the pending UpInertia direct-move too (same stale-impulse protection)
            DeltaRootMotion = Vector3.zero;
            LastPosition = Position;
            //Debug.Log("ResetMov");
        }

        public virtual void Lock(bool value)
        {
            LockInput = value;
            LockMovement = value;
        }

        /// <summary>Add Inertia to the Movement</summary>d
        public virtual void AddInertia(ref Vector3 Inertia, float speed = 1f)
        {
            AdditivePosition += Inertia;
            Inertia = Vector3.Lerp(Inertia, Vector3.zero, DeltaTime * speed);
        }
        #endregion

        #region Speeds
        /// <summary>Change the Speed Up</summary>
        public virtual void SpeedUp() => Speed_Add(+1);

        /// <summary> Changes the Speed Down </summary>
        public virtual void SpeedDown() => Speed_Add(-1);

        /// <summary> Get a SpeedSet by its name</summary>
        public virtual MSpeedSet SpeedSet_Get(string name)
        {
            var speed = speedSets.Find(x => x.name == name);

            //Search the custom speeds sets in the states
            if (speed == null)
            {
                foreach (var st in states)
                {
                    speed = st.SpeedSets.Find(x => x.name == name);
                    if (speed != null) break;
                }
            }
            return speed;
        }

        public virtual MSpeed Speed_GetModifier(string name, int index)
        {
            var set = SpeedSet_Get(name);

            if (set != null && index < set.Speeds.Count)
                return set[index - 1];

            return MSpeed.Default;
        }

        /// <summary>Set a custom speed created via script and it uses it as the Current Speed Modifier (used on the Fall and Jump State)</summary>
        public virtual void SetCustomSpeed(MSpeed customSpeed, bool keepInertiaSpeed = false)
        {
            CustomSpeed = true;
            CurrentSpeedModifier = customSpeed;

            if (keepInertiaSpeed)
            {
                SetTargetSpeed(); //Important needs to calculate the Target Speed again
                InertiaPositionSpeed = TargetSpeed; //Set the Target speed to the Fall Speed so there's no Lerping when the speed changes\
            }
        }

        private void Speed_Add(int change) => CurrentSpeedIndex += change;


        /// <summary> Set an specific Speed for the active speed modifier </summary>
        public virtual void Speed_CurrentIndex_Set(int speedIndex) => CurrentSpeedIndex = speedIndex;

        /// <summary> Set an specific Speed for the active State using IntVars </summary>
        public virtual void Speed_CurrentIndex_Set(IntVar speedIndex) => CurrentSpeedIndex = speedIndex;

        /// <summary>Lock the Speed on the Current Speed Set</summary>
        public virtual void Speed_Lock(bool lockSpeed) => CurrentSpeedSet.LockSpeed = lockSpeed;

        /// <summary>Lock the Speed on a Speed Set using the SpeedSet Name</summary>
        public virtual void Speed_Lock(string SpeedSetName, bool lockSpeed) => Speed_Lock(SpeedSetName, lockSpeed, 0);

        /// <summary>Lock the Speed on a Speed Set using the SpeedSet Name, and the Speed LockIndex</summary>
        public virtual void Speed_Lock(string SpeedSetName, bool lockSpeed, int LockIndex)
        {
            var speedSet = SpeedSet_Get(SpeedSetName);

            if (speedSet != null)
            {
                //Set the Index of the Speed Set that will be locked, if the LockIndex is zero use the speed set on the Inspector
                if (LockIndex != 0) speedSet.LockIndex = Mathf.Clamp(LockIndex, 1, speedSet.Speeds.Count);

                speedSet.LockSpeed = lockSpeed;

                if (lockSpeed)
                {
                    OnSpeedChange.Invoke(speedSet.LockedSpeedModifier);
                    EnterSpeedEvent(CurrentSpeedIndex);
                }
            }
        }
        public virtual void SpeedSet_Lock(string SpeedSetName) => Speed_Lock(SpeedSetName, true, 0);
        public virtual void SpeedSet_Unlock(string SpeedSetName) => Speed_Lock(SpeedSetName, false, 0);


        /// <summary>Set a Speed Modifier and its indexx</summary>
        public virtual void SpeedSet_Set_Active(string SpeedSetName, int activeIndex)
        {
            var speedSet = SpeedSet_Get(SpeedSetName);

            if (speedSet != null)
            {
                speedSet.CurrentIndex = activeIndex;

                if (CurrentSpeedSet == speedSet)
                {
                    CurrentSpeedIndex = activeIndex;
                    speedSet.StartVerticalIndex = activeIndex; //Set the Start Vertical Index as the new Speed 
                }
            }
            else
            {
                CurrentSpeedIndex = activeIndex; //Change the current active speed to the first index

                //Debug.Log($"SpeedSet_Set_Active: {activeIndex}");
            }
        }


        /// <summary>  Update all the internal code of the current Speed Modifier  </summary>
        public virtual void Speed_Update_Current() => CurrentSpeedIndex = CurrentSpeedIndex;

        /// <summary> Change the Top Index of a Speed Set</summary>
        public virtual void Speed_SetTopIndex(int topIndex)
        {
            CurrentSpeedSet.TopIndex = topIndex;
            Speed_Update_Current();
        }

        public virtual void Speed_SetTopIndex(string SpeedSetName, int topIndex)
        {
            var speedSet = SpeedSet_Get(SpeedSetName);
            if (speedSet != null)
            {
                speedSet.TopIndex = topIndex;
                Speed_Update_Current();
            }
        }

        /// <summary>If the Animal is inside a zone then activate that zone</summary>
        public virtual void Zone_Activate()
        {
            if (Sleep || LockInput) return; //Do nothing if the the Character is Sleep or Locked
            if (InZone) Zone.ActivateZone(this);
        }

        /// <summary> Change the Speed of a Speed Set</summary>
        public virtual void SpeedSet_Set_Active(string SpeedSetName, string activeSpeed)
        {
            var speedSet = speedSets.Find(x => x.name.ToLower() == SpeedSetName.ToLower());

            if (speedSet != null)
            {
                var mspeedIndex = speedSet.Speeds.FindIndex(x => x.name.ToLower() == activeSpeed.ToLower());

                if (mspeedIndex != -1)
                {
                    speedSet.CurrentIndex = mspeedIndex + 1;

                    if (CurrentSpeedSet == speedSet)
                    {
                        CurrentSpeedIndex = mspeedIndex + 1;
                        speedSet.StartVerticalIndex = CurrentSpeedIndex; //Set the Start Vertical Index as the new Speed 
                    }
                }
            }
            else
            {
                Debug.LogWarning("There's no Speed Set called : " + SpeedSetName);
            }
        }
        #endregion

        #region Extras


        /// <summary> Adds a Custom Force to the Animal </summary>
        /// <param name="Direction">Direction of the Force Applied to the Animal</param>
        /// <param name="Force"> Amount of Force applied to the Direction</param>
        /// <param name="Acceleration">Smoothens value to apply the force. Higher values faster the force is applied</param>
        /// <param name="ResetGravity">Every time a force is applied, the Gravity Acceleration will be reset</param>
        /// <param name="ForceAirControl">The animal can move while is been pushed by the force if this parameter is true. </param>
        /// <param name="LimitForce">Limits the magnitude of the Force to a value </param>
        public virtual void Force_Add(
            Vector3 Direction, float Force, float Acceleration,
            bool ResetGravity, bool ForceAirControl = true, float LimitForce = 0)
        {
            var CurrentForce = CurrentExternalForce;// + GravityStoredVelocity; //Calculate the Starting force

            if (LimitForce > 0 && CurrentForce.magnitude > LimitForce)
                CurrentForce = CurrentForce.normalized * LimitForce; //Add the Bounce

            CurrentExternalForce = CurrentForce;
            ExternalForce = Direction.normalized * Force;
            ExternalForceAcel = Acceleration;

            if (ActiveState is Fall fall) //If we enter to a zone from the Fall state.. Reset the Fall Current Distance
            {
                fall.FallCurrentDistance = 0;
            }

            if (ResetGravity) Gravity_ResetValues();

            ExternalForceAirControl = ForceAirControl;

            if (Acceleration == 0) Acceleration = 1;

            CurrentExternalForce = Vector3.Lerp(CurrentExternalForce, ExternalForce, Acceleration * DeltaTime); //one step

            //Debug.Log($"Force Added {CurrentExternalForce}", this);
        }

        /// <summary> Removes the current active force applied to the animal  </summary>
        /// <param name="Acceleration"> Current acceleration to remove the force. When set to Zero then the force will be removed instantly</param>
        public virtual void Force_Remove(float Acceleration = 0)
        {
            //if (debugStates)
            //  Debug.Log("Force Removed", this);

            ExternalForceAcel = Acceleration;
            ExternalForce = Vector3.zero;
        }

        /// <summary> Removes every force applied to the animal </summary>
        internal void Force_Reset()
        {
            //if (debugStates)

            CurrentExternalForce = Vector3.zero;
            ExternalForce = Vector3.zero;
            ExternalForceAcel = 0;
        }


        /// <summary> This is used to add an External force to </summary>
        private void ApplyExternalForce()
        {
            //if (CurrentExternalForce.CloseToZero()) return;

            var Acel = ExternalForceAcel > 0 ? (DeltaTime * ExternalForceAcel) : 1; //Use Full for changing

            CurrentExternalForce = Vector3.Lerp(CurrentExternalForce, ExternalForce, Acel);

            if (CurrentExternalForce.sqrMagnitude <= 0.001f) CurrentExternalForce = Vector3.zero; //clean Tiny forces

            if (CurrentExternalForce != Vector3.zero)
                AdditivePosition += CurrentExternalForce * DeltaTime;

        }


        /// <summary> Disable the animal Component after a time  </summary>
        public virtual void DisableSelf(float time) => this.Delay_Action(time, () => enabled = false);


        /// <summary> If the Animal has touched the ground then Grounded will be set to true  </summary>
        public bool CheckIfGrounded()
        {
            AlignRayCasting();
            if (MainRay || FrontRay && !DeepSlope)
            {
                hit_Hip.distance = Height * 2f; //?!??!?
                return Grounded = true;   //Activate the Grounded Parameter so the Idle and the Locomotion State can be activated
            }

            return false;
        }


        public virtual void UpInertia_Store()
        {
            UpInertia = Vector3.Project(Inertia, UpVector); //Store the Up Vector
                                                            // Debug.Log($"UpInertia {UpInertia} ... DeltaPlatformPos{DeltaPlatformPos}");
        }

        /// <summary>Applies the stored Up Inertia directly to the Position (used by Fall and JumpBasic to keep the Up Momentum)</summary>
        public virtual void UpInertia_Apply()
        {
            if (UpInertia == Vector3.zero) return;

            var move = UpInertia * DeltaTime;
            Position += move;
            LastUpInertiaMove += move; //MWC: track the direct write so the next DeltaPos includes it (fixes inflated fall distance)
            //AdditivePosition += UpInertia * DeltaTime;

            //  Debug.Log($"Apply!!! UpInertia {UpInertia}");
        }

        public virtual void UpInertia_Clear() => UpInertia = Vector3.zero;

        /// <summary> Use the height for aligning to the ground not the Pivots  </summary>
        public bool CheckIfGrounded_Height()
        {
            AlignRayCasting(Height);

            if (MainRay && FrontRay && !DeepSlope)
            {
                //hit_Hip.distance = Height * 2f; //?!??!?
                return Grounded = true;   //Activate the Grounded Parameter so the Idle and the Locomotion State can be activated
            }
            return false;
        }

        public void Always_Forward(bool value) => AlwaysForward = value;

        /// <summary>Enables (-1 all, N by index) or disables (0) the Attack Triggers. Disabling always works so a sleeping animal never keeps a trigger on</summary>
        public virtual void ActivateDamager(int ID, int prof)
        {
            if ((Sleep || !enabled) && ID != 0) return;

            if (ID == -1)                         //Enable all Attack Triggers
            {
                foreach (var dam in Attack_Triggers) dam.DoDamage(true, prof);
            }
            else if (ID == 0)                     //Disable all Attack Triggers
            {
                foreach (var dam in Attack_Triggers) dam.DoDamage(false, prof);
            }
            else
            {
                var Att_T = Attack_Triggers.FindAll(x => x.Index == ID);        //Enable just a trigger with an index

                if (Att_T != null)
                    foreach (var dam in Att_T) dam.DoDamage(true, prof);
            }
        }
        public void DamagerAnimationStart(int hash) { }
        public void DamagerAnimationEnd(int hash) { }


        #region Colliders

        //MWC — Editor helper only, no longer called at runtime. It cannot tell a body collider from anything
        //else parented under the character, so review the list it produces before keeping it.
        /// <summary>Collects every non trigger collider under the Animal into [Internal Colliders] </summary>
        public void FindInternalColliders()
        {
            var colls = GetComponentsInChildren<Collider>(true).ToList();      //Get all the Active colliders
            colliders = new List<Collider>();

            foreach (var item in colls)
            {
                if (item.gameObject == gameObject) continue; //Do not Find the Main Collider here

                if (!item.isTrigger)
                {
                    colliders.Add(item);        //Add the Animal Colliders Only
                    item.sharedMaterial = AnimalMaterial; //Set the Physics Material
                }
            }


#if UNITY_EDITOR
            if (!Application.isPlaying) MTools.SetDirty(this);
#endif
        }



        public void FindMainCollider()
        {
            if (MainCollider == null)
            {
                MainCollider = GetComponent<CapsuleCollider>();
#if UNITY_EDITOR
                if (!Application.isPlaying) MTools.SetDirty(this);
#endif
            }
        }

        public void SetDefaultMainColliderValues()
        {
            if (MainCollider) MainCapsuleDefault = new(MainCollider);

            MainCapsuleDefault.modify = (CapsuleModifier)(-1);
        }

        /// <summary>  Resets the MainCollider of the Animal Controller  </summary>
        public void Reset_MainCollider()
        {
            MainCapsuleDefault.Modify(MainCollider);
            //  Debug.Log("Mood MainCol");
        }

        /// <summary>Restores the MainCollider to its default values and re-applies the Active State and Active Stance capsule overrides on top (used when a Mode Ability stops overriding the capsule)</summary>
        public void Restore_MainCollider()
        {
            Reset_MainCollider();

            if (ActiveState != null && ActiveState.OverrideCapsule)
                ActiveState.newCapsule.Modify(MainCollider);

            if (HasStances && ActiveStance != null && ActiveStance.OverrideCapsule)
                ActiveStance.newCapsule.Modify(MainCollider);
        }

        /// <summary>Enable/Disable All Colliders on the animal. Avoid the Triggers </summary>
        public virtual void EnableColliders(bool active)
        {
            foreach (var item in colliders)
            {
                if (item)
                {
                    if (item.transform == transform) continue; //Do nothing with the Collider in the Main GameObject
                    item.enabled = active;
                }
            }

            MainCollider_Enable(active);
        }
        #endregion

        /// <summary>Disable this Script and MalbersInput Script if it has it.</summary>
        public virtual void DisableAnimal()
        {
            enabled = false;
            //CustomPatch: corrected null check for possible Unity object type
            IInputSource MI = GetComponent<IInputSource>();
            if (!MI.IsUnityRefNull())
                MI.Enable(false); //Disable the Input source
        }

        public void SetTimeline(bool isOnTimeline)
        {
#if UNITY_EDITOR
            if (debugStates || debugStances || debugModes)
                MDebug.Log($"[{name}] Set Timeline {isOnTimeline}", this);
#endif

            Sleep = isOnTimeline;
            InTimeline = isOnTimeline;
            RB.isKinematic = isOnTimeline; //Make Sure the Animal is set to Kinematic\

            //  RB.collisionDetectionMode = isOnTimeline ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;

            Mode_Stop();

            if (!isOnTimeline)
            {
                //Reset all State,Stance and Mode Inputs
                foreach (var m in modes)
                    m.InputValue = false;

                foreach (var m in states)
                    m.InputValue = false;

                foreach (var m in Stances)
                    m.InputValue = false;
            }
            else
            {
                ResetInputSource();
            }
        }

        /// <summary>InertiaPositionSpeed = TargetSpeed</summary>
        public void ResetInertiaSpeed() => InertiaPositionSpeed = TargetSpeed;

        public void ResetInertiaSpeed(Vector3 newTargetSpeed) => InertiaPositionSpeed = TargetSpeed = newTargetSpeed;

        public void UseCameraBasedInput() => UseCameraInput = true;

        //private void OnDestroy()
        //{
        //    OnDisable();
        //}
        #endregion
    }
}
