// Last Change: 9/24/2026 12:57pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    /// <summary>Class to identify stances on the Animal Controller </summary>
    [System.Serializable]
    public class Stance
    {
        [Tooltip("ID value for the Stance")]
        public StanceID ID;

        [Tooltip("Enable Disable the Stance")]
        public BoolReference enabled = new(true);

        [Tooltip("Unique Input to play for each Ability")]
        public StringReference Input;

        [Tooltip("Lock the Stance if its Active. No other Stances can be enabled.")]
        public BoolReference persistent = new();

        [Tooltip("When this stance is active, no other stance can be activated, except the Default Stance." +
            " Use this when you dont want other stances to interrupt ")]
        public BoolReference activeOnly = new();

        [Tooltip("Does this Stance allows Strafing?")]
        public BoolReference CanStrafe = new();

        [Tooltip("After the Stance has exited, it cannot be activated again after the cooldown has passed")]
        public FloatReference CoolDown = new(0);

        [Tooltip("If this Stance was activated, it cannot be Exit until the Exit cooldown has passed")]
        public FloatReference ExitAfter = new(0);

        [Tooltip("Is/Is NOT active State on this list")]
        public bool Include = true;

        [Tooltip("Include/Exclude the States on this list that can be used with the Stance")]
        public IDListCheck<StateID> States = new();

        [Tooltip("What States can queue the activation of this Stance")]
        public IDListCheck<StateID> stateQueue = new();

        [Tooltip("Stances to Block while this stance is active")]
        public IDListCheck<StanceID> disableStances = new();


        //-----Remove in future updates-----

        public List<StateID> states = new(); //OLD WAY it will be replaced by the IDList. In future updates, this will be removed
        public List<StanceID> DisableStances = new(); //OLD WAY it will be replaced by the IDList. In future updates, this will be removed
        public List<StateID> StateQueue = new(); //OLD WAY it will be replaced by the IDList. In future updates, this will be removed

        //-----Remove in future updates-----



        [Tooltip("When the stance is playing ,it will override the main capsule collider to fit better the stance")]
        public bool OverrideCapsule = false;

        public OverrideCapsuleCollider newCapsule = new();

        /// <summary> Current Stored Input Value </summary>
        public bool InputValue { get; set; }

        [Tooltip("Strafe Multiplier when movement is detected. This will make the Character be aligned to the Strafe Direction Quickly." +
            " This value is multiplied by the [State] Movement Strafe Multiplier")]
        [Range(0, 1)]
        public float MovementStrafe = 1f;
        [Tooltip("Strafe Multiplier when there's no movement. This will make the Character be aligned to the Strafe Direction Quickly." +
            "  This value is multiplied by the [State] Idle Strafe Multiplier")]
        [Range(0, 1)]
        public float IdleStrafe = 1f;

        /// <summary>Set Block Values to check if anyone else have disabled this Stace</summary>
        public int DisableValue { get; set; }
        public bool DisableTemp => DisableValue < 0;

        /// <summary> The Stance is Enabled/Disable</summary>
        public bool Enabled { get => enabled.Value; set => enabled.Value = value; }
        public bool ActiveOnly { get => activeOnly.Value; set => activeOnly.Value = value; }

        /// <summary> Lock the Stance if its Active. No other Stances can be enabled.</summary>
        public bool Persistent
        {
            get => persistent.Value;
            set
            {
                persistent.Value = value;
                //Debug.Log($" Persistent [{ID.name} ] {value}");
            }
        }

        /// <summary>Current Activated Stance on the Animal</summary>
        public bool Active { get; set; }


        /// <summary>The State try to be activated but a state did not allowed. That State is on the QueueList so Lets queue it</summary>
        public bool Queued { get; set; }


        public MAnimal Animal { get; set; }

        /// <summary>  When was the Stance Activated? </summary>
        public float ActivationTime { get; private set; }

        /// <summary>  When was the Stance Exited? </summary>
        public float ExitTime { get; private set; }

        /// <summary>On Activation, Can the Stance Exit?</summary>
        public bool CanExit => ExitAfter == 0 || MTools.ElapsedTime(ActivationTime, ExitAfter);

        /// <summary> Remaining Time to activate again the stance </summary>
        public float CoolDownLeft => ExitTime + CoolDown - Time.time;
        /// <summary> Remaining Time to allow to exit the stance</summary>
        public float CanExitTimeLeft => ActivationTime + ExitAfter - Time.time;

        /// <summary>After Activation, can the Stance be activated again?</summary>
        public bool InCoolDown => CoolDown > 0 && !MTools.ElapsedTime(ExitTime, CoolDown);
        public EnterExitIDEvent<StanceID> events { get; set; }





        internal virtual void AwakeStance(MAnimal animal)
        {
            if (ID == null)
            {
                Debug.LogWarning($"<B>[{Animal.name}]</B> Has Empty Stances. Please set the correct Stance ID ", animal.gameObject);
            }

            Animal = animal;
            events = animal.OnEnterExitStances.Find(x => x.ID == ID);
            ActivationTime = float.MinValue;
            ExitTime = float.MinValue;
            Queued = false;

            if (disableStances == null || disableStances.Empty) disableStances = new(DisableStances); //OLD WAY it will be replaced by the IDList. In future updates, this will be removed
            if (stateQueue == null || stateQueue.Empty) stateQueue = new(StateQueue); //OLD WAY it will be replaced by the IDList. In future updates, this will be removed
            if (States == null || States.Empty) States = new(states, Include);   //OLD WAY it will be replaced by the IDList. In future updates, this will be removed

        }

        internal void ConnectInput(IInputSource InputSource, bool connect)
        {
            if (connect)
                InputSource.ConnectInput(Input, ActivatebyInput);
            else
                InputSource.DisconnectInput(Input, ActivatebyInput);
        }

        public virtual void SetPersistent(bool value)
        {
            if (Active || Queued)
            {
                Debugging($"Persistent [{value}]. Queued [{Queued}]");
                Persistent = value;
            }
            else
            {
                Debugging("Cannot Set Persistent. This is not the Active Stance");
            }
        }

        public virtual void Enable(bool value) => Enabled = value;
        public virtual void SetQueued(bool value)
        {
            Queued = value;
            Debugging($"Queued [{value}]");
        }

        public void ActivatebyInput(bool Input_Value)
        {
            if (Animal.LockInput) return;

            if (CanActivate)
            {
                InputValue = Input_Value;

                if (Input_Value)
                {
                    Animal.Stance = ID;         //Set the State
                }
                else
                {
                    if (Animal.ActiveStance.Persistent && Animal.ActiveStance == this)
                    {
                        Animal.Stance_ResetPersistent(); //Reset the Stance to the Default one when is persistent
                    }
                    else
                    {
                        Animal.Stance_Reset();
                    }

                    Queued = false;
                }
            }
        }

        /// <summary> Enable Temporally the State using a Counter </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: hint optimize small method call
        public void Disable_Temp_Restore()
        {
            DisableValue++;
            //Debug.Log($" {ID.name} DisableValue : {DisableValue}" );
        }

        /// <summary> Disable Temporally the State using a Counter </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: hint optimize small method call
        public void Disable_Temp()
        {
            DisableValue--;
            //Debug.Log($" {ID.name} DisableValue : {DisableValue}");
        }

        /// <summary> Verifies if the Stance can be activated </summary>
        public bool CanActivate
        {
            get
            {
                if (!Enabled) { Debugging("Failed. Stance is Disabled"); return false; }
                if (!Animal.enabled) { Debugging("Failed. Animal disabled"); return false; }
                if (DisableTemp) { Debugging($"Failed. Disable by External [{DisableValue}]"); return false; }

                //Only Activates the default one
                if (Animal.ActiveStance != null)
                {
                    if (Animal.ActiveStance.ActiveOnly && Animal.ActiveStance != this && Animal.DefaultStanceID != ID)
                    {
                        Debugging($"Ignored. Next ({ID.name}) Active Stance [{Animal.ActiveStance.ID.name}] Is Active Only");
                        return false;
                    }

                    if (Animal.ActiveStance.Persistent && Animal.ActiveStance != this)  //Make sure the stances are not the same
                    { Debugging($"Ignored. Active Stance [{Animal.ActiveStance.ID.name}] is Persistent"); return false; }

                    if (InCoolDown)
                    { Debugging($"Failed. Stance in CoolDown. Time left {CoolDownLeft:F2}"); return false; }

                    if (!Animal.ActiveStance.CanExit)
                    {
                        Debugging($"Failed. Active Stance [{Animal.ActiveStance.ID.name}] can't exit yet. Exit After {(Animal.ActiveStance.CanExitTimeLeft):F2}");
                        return false;
                    }
                }

                //The Active State can limit which Stances are allowed while it plays (the Default Stance is always allowed so the animal can return to it)
                var limitState = Animal.ActiveState;
                if (limitState != null && limitState.allowedStances != null && !limitState.allowedStances.Empty
                    && ID != Animal.DefaultStanceID && !limitState.ValidStance(ID))
                {
                    Debugging($"Failed. Active State [{limitState.ID.name}] does not allow this Stance (Allowed Stances)");
                    return false;
                }

                if (States.Empty) return true; //Return true since it has no limitations

                var ActiveState = Animal.ActiveStateID;

                var StatesCheck = States.Check(ActiveState);

                if (!StatesCheck)
                {
                    if (!States.include) Debugging($"Failed. Active State [{ActiveState.name}] is Excluded from the allowed States. Set Queued[{Queued}]");
                    else Debugging($"Failed. Active State [{ActiveState.name}] is Not Included in the allowed States. Set Queued[{Queued}]");
                }

                return States.Check(Animal.ActiveStateID); //Check if the Active State is on the list

            }
        }


        internal void Reset()
        {
            InputValue = false;
            // Persistent = false;
            Queued = false;
        }

        internal void Activate()
        {
            ActivationTime = Time.time;
            Active = true;
            Queued = false;

            //If we are activating the Default then Release the queue from the last stance (BUG)
            // if (ID == Animal.DefaultStanceID)
            //    Animal.LastActiveStance.Queued = false;

            events?.OnEnter?.Invoke();
        }

        internal void Exit()
        {
            Active = false;
            ExitTime = Time.time;
            events?.OnExit?.Invoke(); //CustomPatch: corrected/improved event usage (here the OnExit unity events are not guaranteed to be initialized)

            //Remember to reset the input value of a stance on exit
            if (!Queued) Animal.InputSource?.ResetInput(Input);
        }

        /// <summary> A new State has been activated</summary>
        internal void NewStateActivated(StateID stateID)
        {
            if (CanBeUsedOnState(stateID) && Queued)
            {
                SetQueued(false);
                Animal.Stance = this.ID; //Try to activate a queue Stance
            }
        }

        /// <summary>  Checks if this stance can be used on a given state   </summary>
        internal bool CanBeUsedOnState(StateID activeStateID)
        {
            if (States.Empty) return true; //Return true since it has no limitations
            return States.Check(activeStateID);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: force optimize small method call
        internal bool OnQueueState(StateID activeStateID)
        {
            return stateQueue.Check(activeStateID); //Find if the Active State is on the list
        }

        /// <summary>Editor only console log for this Stance, active when the Animal debugs Stances</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debugging(string value)
        {
#if UNITY_EDITOR    
            if (Animal.debugStances)
            {
                MDebug.Log($"<B>[{Animal.name}]</B> - <B><color=yellow>[Stances:{ID.name}({ID.ID})]</color> - <color=white>{value}</color></B>", Animal.gameObject);
            }
#endif
        }

        public virtual void OnValidate(MAnimal animal)
        {
            if (disableStances != null && disableStances.Empty && DisableStances != null && DisableStances.Count > 0)
            {
                disableStances = new(DisableStances);
                DisableStances = null;
                MTools.SetDirty(animal);
                //Debug.Log($"Stance [{ID.name}] on Animal [{animal.name}] is Updating to the new DisableStances IDList. Please save the prefab [AC 1.5.2]", animal.gameObject);
            }

            if (stateQueue.Empty && StateQueue != null && StateQueue.Count > 0)
            {
                stateQueue = new(StateQueue);
                StateQueue = null;
                MTools.SetDirty(animal);
                // Debug.Log($"Stance [{ID.name}] on Animal [{animal.name}] is Updating to the new StateQueue IDList.Please save the prefab [AC 1.5.2]", animal.gameObject);
            }

            if (States.Empty && states != null && states.Count > 0)
            {
                States = new(states, Include);
                states = null;
                MTools.SetDirty(animal);
                // Debug.Log($"Stance [{ID.name}] on Animal [{animal.name}] is Updating to the new States IDList. Please save the prefab [AC 1.5.2]", animal.gameObject);
            }
        }
    }
}