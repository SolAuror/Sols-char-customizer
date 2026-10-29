// Last Change: 9/24/2026 1:52pm
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    public partial class MAnimal
    {
        /// <summary> stored transform shortcut </summary>
        public Transform t;

        private Vector3 GizmoDeltaPos = Vector3.zero;

        //MWC: self-movement applied to the Transform in the previous Normal-mode cycle (feeds the next DeltaPos)
        internal Vector3 LastSelfMove = Vector3.zero;

        /// <summary>Direct-Position movement applied by UpInertia_Apply in the previous cycle (re-added to DeltaPos like DeltaPlatformPos)</summary>
        internal Vector3 LastUpInertiaMove = Vector3.zero;

        private void CheckUnscaledParent(Transform character)
        {
            if (character.parent == null) return;

            if (character.parent.transform.localScale != Vector3.one)
            {
                MDebug.LogWarning("The Character is parented to an Object with an Uneven Scale. Unparenting");
                character.parent = null;
            }
            else
            {
                CheckUnscaledParent(character.parent);
            }
        }


        private void UpdateCacheState()
        {
            //Use the same that is already on the states
            if (states_C == null || states_C.Count == 0 || (states_C.Count != states.Count))
            {
                states_C = new();

                foreach (var st in states)
                {
                    states_C.Add(new() { active = st.Active, priority = st.Priority, state = st });
                }
            }
        }


        /// <summary> Reparent the RootBone and Rotator so it works perfectly with the Free Movement </summary>
        public void UpdateRotatorParent()
        {
            var CurrentScale = t.localScale; //IMPORTANT ROTATOR Animals needs to set the Rotator Bone with no scale first.
            t.localScale = Vector3.one;

            if (Rotator != null)
            {
                if (RootBone == null)
                {
                    if (Anim.avatar && Anim.avatar.isHuman)
                        RootBone = Anim.GetBoneTransform(HumanBodyBones.Hips).parent; //Get the RootBone from
                    else
                        RootBone = Anim.avatarRoot;
                    //Rotator.GetChild(0);           //Find the First Rotator Child  THIS CAUSE ISSUES WITH TIMELINE!!!!!!!!!!!!

                    if (RootBone == null)
                        MDebug.LogWarning("Make sure the Root Bone is Set on the Advanced Tab -> Misc -> RootBone. This is the Character's Avatar root bone");
                }

                if (RootBone != null && !RootBone.SameHierarchy(Rotator)) //If the rootbone is not grandchild Parent it
                {
                    //If the Rotator and the RootBone does not have the same position then create one
                    if (Rotator.position != RootBone.position)
                    {
                        RotatorOffset = new GameObject("Offset");
                        RotatorOffset.transform.SetPositionAndRotation(Position, Rotation);
                        RotatorOffset.layer = gameObject.layer; //Set the same layer as the Animal

                        RotatorOffset.transform.SetParent(Rotator);
                        RootBone.SetParent(RotatorOffset.transform);

                        RotatorOffset.transform.localScale = Vector3.one;
                        //RootBone.localScale = Vector3.one;
                    }
                    else
                    {
                        RootBone.parent = Rotator;
                    }
                }

                Rotator.gameObject.layer = gameObject.layer; //Set the same layer as the Animal
            }

            t.localScale = CurrentScale; //Set the scale back to the original value
        }

        //MWC: cache States and Stances in dictionaries keyed by their int ID (mirrors modes_Dict)
        //so State_Get/Stance_Get avoid List.Find closure allocations on hot paths
        internal void CacheStatesAndStances()
        {
            states_Dict = new();
            foreach (var st in states)
            {
                if (st == null || st.ID == null) continue;
                if (!states_Dict.TryAdd(st.ID.ID, st))
                    MDebug.LogWarning($"State [{st.name}] uses a duplicate ID ({st.ID.ID}). Only the first one will be used on fast lookups.", this);
            }

            stances_Dict = new();
            if (Stances != null)
            {
                foreach (var st in Stances)
                {
                    if (st == null || st.ID == null) continue;
                    stances_Dict.TryAdd(st.ID.ID, st);
                }
            }
        }

        private void CacheComponents()
        {
            if (Anim == null) Anim = this.FindComponent<Animator>();   //Cache the Animator
            if (RB == null) RB = this.FindComponent<Rigidbody>();      //Cache the Rigid Body  
            if (Aimer == null) Aimer = this.FindComponent<Aim>();       //Cache the Aim Component 
            if (MainCollider == null) MainCollider = GetComponent<CapsuleCollider>();       //Cache the Main Capsule Collider
            if (t == null) t = transform;


        }

        public PhysicsMaterial AnimalMaterial;


        public void Awake()
        {
            CacheComponents();

            //CustomPatch: corrected null check for possible Unity object type
            if (InputSource.IsUnityRefNull()) InputSource = this.FindInterface<IInputSource>(); //Find if we have a InputSource


            if (RB != null && RB.transform != t)
                MDebug.LogWarning("The Rigidbody is not on the same GameObject as the MAnimal component. Make sure both components are on the same GameObject to avoid issues.", this);

            if (MainCollider != null && MainCollider.transform != t)
                MDebug.LogWarning("The Main Collider is not on the same GameObject as the MAnimal component. Make sure both components are on the same GameObject to avoid issues.", this);


            if (!CloneStates)
                MDebug.LogWarning
                    (
                        $"[{name}] has [ClonesStates] disabled. " +
                        $"If multiple characters use the same states, it will cause issues." +
                        $" Use this only for runtime changes on a single character"
                    , this);


            DefaultCameraInput = UseCameraInput;

            if (AnimalMaterial != null)
            {
                if (MainCollider != null && MainCollider.sharedMaterial == null) MainCollider.sharedMaterial = AnimalMaterial; //Set the Main Collider Material

                if (colliders != null && colliders.Count > 0)
                {
                    foreach (var c in colliders)
                    {
                        if (c != null && c.sharedMaterial == null)
                            c.sharedMaterial = AnimalMaterial; //Set the Main Collider Material
                    }
                }
            }


            AdditivePosition = Vector3.zero;
            AdditiveRotation = Quaternion.identity;

            defaultGravityPower = m_gravityPower; //Store the Default Gravity Power in a private value
            // Anim.updateMode = AnimatorUpdateMode.AnimatePhysics; //Set the Animator to Update in the Physics Update

            //Clear the ModeQue and Ability Input
            ModeQueueInput = new();
            AbilityQueueInput = new();

            GroundRootPosition = true;

            CheckUnscaledParent(t);

            UpdateRotatorParent();

            GetHashIDs();

            SetPivots();

            CalculateCenter();

            //Initialize all SpeedModifiers
            foreach (var set in speedSets) set.CurrentIndex = set.StartVerticalIndex;

            if (Anim)
            {
                // Anim.Rebind(); //Reset the Animator Controller
                Anim.speed = AnimatorSpeed * TimeMultiplier;                         //Set the Global Animator Speed

                var AllModeBehaviours = Anim.GetBehaviours<ModeBehaviour>();

                if (AllModeBehaviours != null)
                {
                    foreach (var ModeB in AllModeBehaviours)
                        ModeB.InitializeBehaviour(this);
                }
                else
                {
                    if (modes != null && modes.Count > 0)
                    {
                        MDebug.LogWarning("Please check your Animator Controller. There's no Mode Behaviors Attached to it. Re-import the Animator again");
                    }
                }
            }


            //Initialize The Default Stance (if theres no Stance added on the list, it will create one with the ID 0 called Default)
            if (defaultStance == null)
            {
                defaultStance = ScriptableObject.CreateInstance<StanceID>();
                defaultStance.name = "Default";
                defaultStance.ID = 0;

                currentStance = defaultStance;
            }

            if (Stances == null || Stances.Count == 0)
            {
                Stances = new()
                {
                    new Stance() { ID = defaultStance }
                };
            }

            StartingStance = defaultStance; //Store the starting Stance


            //MWC — [Internal Colliders] is authored by hand, it is no longer collected at runtime.
            //FindInternalColliders() swept EVERY non trigger collider under the character, so anything else
            //parented there (pooled projectiles, attached props) was registered as part of the body and had
            //its Physics Material overwritten with [Animal Material]. Use the [Find Internal Colliders]
            //context menu on the field to fill the list in the Editor, then review it.
            SetDefaultMainColliderValues();

            for (int i = 0; i < states.Count; i++)
            {
                if (states[i] == null) continue; //Skip Null States

                if (CloneStates || states[i].animal != this) //If the State does not belong to this Animal or if CloneStates is true, then clone the state so it does not affect other Animals that use the same state
                {
                    //Create a clone from the Original Sriptable Objects! IMPORTANT
                    var instance = ScriptableObject.Instantiate(states[i]);
                    instance.name = instance.name.Replace("(Clone)", "(Runtime)");
                    states[i] = instance;
                }


                //NEW LOCAL PRIORITY AND ACTIVE VALUES
                states[i].Active = states_C[i].active;
                states[i].Priority = states_C[i].priority;
                states[i].AwakeState(this);

                if (states[i].Priority == 0) MDebug.LogWarning($"State [{states[i].name}] has priority [0]. Please set a proper priority value", states[i]);
            }

            AwakeAllModes();

            Stances ??= new(); //Check if stances is null

            HasStances = Stances.Count > 0;
            if (HasStances)
            {
                foreach (var stance in Stances) stance.AwakeStance(this); //Awake all Stances

                LastActiveStance = Stance_Get(DefaultStanceID);
                ActiveStance = LastActiveStance;
            }


            currentSpeedSet = defaultSpeedSet;
            AlignUniqueID = UnityEngine.Random.Range(0, 99999);



            if (CanStrafe && !Aimer) MDebug.LogWarning("This character can strafe but there's no Aim component. Please add the Aim component");

            //Editor Checking (Make sure the Animator has an Avatar)
            if (Anim.avatar == null)
                MDebug.LogWarning("There's no Avatar on the Animator", Anim);

            if (RB) defaultKinematic = RB.isKinematic; // Cache the default kinematic value of the Rigidbody to reset it later if needed

            DefaultCameraInput = UseCameraInput; //Cache the Default Camera Input


            //CHECK THE NEW STATE PRIORITY AND ACTIVE VALUES After the clone!
            UpdateCacheState();

            CacheStatesAndStances(); //MWC: build the fast-lookup dictionaries (after the states were cloned!)


            if (height == 1) CalculateCenter(true); //Update the height if is 1 (Default Value)

            //Fix to have on Awake all the states and stances not Null
            ActiveStance = Stance_Get(DefaultStanceID);
            JustActivateState = true;
        }


        private void AwakeAllModes()
        {
            modes_Dict = new();

            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i].ID == null) continue; //Skip Modes with no ID

                modes[i].Priority = modes.Count - i;
                modes[i].AwakeMode(this);

                modes_Dict.Add(modes[i].ID.ID, modes[i]); //Save the modes into a dictionary so they are easier to find.
            }
        }

        private void CacheAllModes()
        {
            modes_Dict = new();

            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i].ID == null)
                {
                    Debug.LogWarning($"Mode [{i}] has no ID, please set an ID to be able to use it on the Mode Queue and Abilities. Skipping this mode.", this);
                    continue; //Skip Modes with no ID
                }
                //MWC: Guard against duplicate Mode IDs so CacheAllModes no longer throws an ArgumentException; keep the first entry and warn.
                if (!modes_Dict.TryAdd(modes[i].ID.ID, modes[i]))
                {
                    Debug.LogWarning($"Mode [{i}] '{modes[i].ID.name}' uses a duplicate ID ({modes[i].ID.ID}). Each Mode ID must be unique. Only the first entry will be used.", this);
                    continue; //Skip duplicate Modes
                }

                modes[i].CacheAbilities(); //Save all abilities to find it faster.
            }
        }

        /// <summary>Resets states, stances and physics and restarts the animal on its start state (runs the previous state's On Exit event)</summary>
        public virtual void ResetController()
        {
            FindCamera();

            //MWC: was rescanning the whole hierarchy (GetComponentsInChildren) on every Enable AND every Sleep→wake.
            //The set is now cached; call UpdateDamagerSet() explicitly when weapons/damagers change (IMDamagerSet).
            if (Attack_Triggers == null) UpdateDamagerSet();

            ResetAnimatorParameterCache(); //MWC: animator floats are cleared on Disable/Sleep — force the next parameter writes through

            MainCollider_Enable(true); //Make sure the Main Collider is enabled

            GravityExtraPower = 1;

            //Clear the ModeQue and Ability Input
            ModeQueueInput = new();
            AbilityQueueInput = new();

            LockMovement = false;
            LockInput = false;
            ActiveMode = null;

            foreach (var state in states)
            {
                state.InitializeState();
                state.InputValue = false;
                state.ResetState();

                //Make sure the states are not in cooldown when the controller is reset 
                state.CurrentExitTime = -state.ExitCooldown.Value * 5;
                state.CurrentEnterTime = -state.EnterCooldown.Value * 5;


                state.OnAnimalEnabled();
            }

            foreach (var stance in Stances) stance.Reset(); //Reset All Stances!!

            Reset_RigidBody();

            //  CacheAnimatorState(); //Find all Animator Tags

            EnableColliders(true); //Make sure to enable all colliders

            CheckIfGrounded(); //Make the first Alignment 
                               // CalculateCenter();

            lastState = null;

            if (states == null || states.Count == 0)
            { Debug.LogError("The Animal must have at least one State added", this); return; }

            if (Stances == null || Stances.Count == 0)
            { Debug.LogError("The Animal must have at least one Stance added", this); return; }


            var StartStateIndex = states.Count - 1; //Find Idle


            if (OverrideStartState != null)
            {
                StartStateIndex = states.FindIndex(item => item.ID == OverrideStartState); //Find the Index of the Override State
            }

            var previousState = activeState; //The state playing before the reset (null on the first enable)

            CleanStateStart(StartStateIndex);

            //The reset ended the previous state without an exit: run its On Exit event (e.g. a respawn leaving Death re-enables what Death disabled)
            if (previousState != null && previousState != activeState)
                previousState.EnterExitEvent?.OnExit?.Invoke();

            //Reset Just Activate State The next Frame
            JustActivateState = true;
            this.Delay_Action(() => { JustActivateState = false; });

            var stan = currentStance;
            currentStance = null; //CLEAR STANCE
            Stance_Set(stan);
            State_SetFloat(0);
            UsingMoveWithDirection = (UseCameraInput); //IMPORTANT

            if (IsPlayingMode) Mode_Stop();

            //Set Start with Mode
            if (StartWithMode.Value != 0)
            {
                if (StartWithMode.Value / 1000 == 0)
                {
                    Mode_Activate(StartWithMode.Value);
                }
                else
                {
                    var mode = StartWithMode.Value / 1000;
                    var modeAb = StartWithMode.Value % 1000;
                    if (modeAb == 0) modeAb = -99;
                    Mode_Activate(mode, modeAb);
                }
            }

            LastPosition = Position; //Store Last Animal Position

            //  ForwardMultiplier = 1f; //Initialize the Forward Multiplier
            GravityMultiplier = 1f;

            MovementAxis =
            MovementAxisRaw =
            AdditivePosition =
            InertiaPositionSpeed =
            SlopeDirectionSmooth =
            MovementAxisSmoothed = Vector3.zero; //Reset Vector Values

            LockMovementAxis = (new Vector3(LockHorizontalMovement ? 0 : 1, LockUpDownMovement ? 0 : 1, LockForwardMovement ? 0 : 1));

            UseRawInput = true; //Set the Raw Input as default.
            UseAdditiveRot = true;
            UseAdditivePos = true;
            Grounded = true;
            Randomizer = true;
            AlwaysForward = AlwaysForward;         // Execute the code inside Always Forward .... Why??? Don't know ..something to do with the Input stuff

            StrafeLogic();

            // GlobalOrientToGround = GlobalOrientToGround; // Execute the code inside Global Orient
            SpeedMultiplier = 1;
            CurrentCycle = 0;
            Gravity_ResetValues();

            var TypeHash = TryOptionalParameter(m_Type);

            TryAnimParameter(TypeHash, animalType); //This is only done once!

            //Reset FreeMovement.
            if (Rotator) Rotator.localRotation = Quaternion.identity;

            Bank = 0;
            PitchAngle = 0;
            PitchDirection = Vector3.forward;

            if (!GlobalOrientToGround) DisablePivotChest();

            void CleanStateStart(int ID)
            {
                activeState = states[ID];
                ActiveStateID = activeState.ID;         //Set the New ActivateID
                activeState.Activate();
                lastState = activeState;                //Do not use the Properties....
                activeState.IsPending = false;          //Force the active state to start without entering the animation.
                activeState.CanExit = true;             //Force that it can exit... so another can activate it
                activeState.General.Modify(this);       //Force the active state to Modify all the Animal Settings
                activeState.InCoreAnimation = true;
                ActiveState = activeState;
                activeState.DisableModes_Temp(true, activeState.disableModes);      //Make sure the modes are disabled on start

                //  OnStateActivate.Invoke(activeState.ID);                             //Play the correct animations
                SetIntParameter?.Invoke(hash_State, activeState.ID.ID);                     //Sent to the Animator the value to Apply  
                TryAnimParameter(hash_StateOn);                                     //Enable State On
            }
        }

        protected virtual void UpdateMainPivot()
        {
            if (Has_Pivot_Chest)
                CurrentMainPivotCache = Pivot_Chest.position;
            else if (Has_Pivot_Hip)
                CurrentMainPivotCache = Pivot_Hip.position;
            else
                CurrentMainPivotCache = new Vector3(0, Height, 0);

            //Debug.Log($"Has_Pivot_Chest {Has_Pivot_Chest} Has_Pivot_Hip {Has_Pivot_Hip}");
        }

        public virtual void Reset_RigidBody()
        {
            //Reset RB Properties
            if (RB)
            {
                RB.useGravity = false;
                RB.constraints = RigidbodyConstraints.FreezeRotation;
                RB.linearDamping = 0;
                RB.angularDamping = 0;
                RB.isKinematic = defaultKinematic; //Make use the Rigibody is not kinematic

                //MWC: RB interpolation fights the transform-driven movement used when the Animator runs in Normal mode
                //(interpolation re-derives the rendered pose from physics poses the transform writes never updated = rubber-band jitter).
                //Fixed-mode rigs are left untouched — Interpolate is correct and desired there.
                if (Anim != null && Anim.updateMode != AnimatorUpdateMode.Fixed && RB.interpolation != RigidbodyInterpolation.None)
                {
                    RB.interpolation = RigidbodyInterpolation.None;
                    MDebug.LogWarning($"[{name}] Rigidbody Interpolation was set to [None]. Interpolation causes jitter when the Animator Update Mode is [Normal] (transform-driven movement)", this);
                }
            }
        }

        /// <summary> Find the Main Camera if Main CameraDirection is true </summary>
        public virtual void FindCamera()
        {
            if (MainCamera == null) //Find the Camera if is not already set
            {
                if (UseMainCameraDirection.Value)
                {
                    m_MainCamera.UseConstant = true;

                    var mainCam = MTools.FindMainCamera();

                    if
                        (mainCam) m_MainCamera.Value = mainCam.transform;
                }
                else
                {
                    //CustomPatch: optimized and corrected usage of dummy used for getting world transform for camera related input calculations
                    if (s_worldTransformDummyGameObj == null)
                    {
                        s_worldTransformDummyGameObj = new GameObject("AC - World Direction Dummy");
                        s_worldTransformDummyGameObj.hideFlags = HideFlags.HideInHierarchy;
                        GameObject.DontDestroyOnLoad(s_worldTransformDummyGameObj);
                    }

                    m_MainCamera.Value = s_worldTransformDummyGameObj.transform;
                }
            }
        }

        private static GameObject s_worldTransformDummyGameObj;

        [ContextMenu("Set Pivots")]
        public void SetPivots()
        {
            Pivot_Hip = pivots.Find(item => string.Equals(item.name, "HIP", System.StringComparison.OrdinalIgnoreCase));
            Pivot_Chest = pivots.Find(item => string.Equals(item.name, "CHEST", System.StringComparison.OrdinalIgnoreCase));

            Has_Pivot_Hip = Pivot_Hip != null;
            Has_Pivot_Chest = Pivot_Chest != null;
            Starting_PivotChest = Has_Pivot_Chest;

            UpdateMainPivot(); //Update the Main Pivot Position

            if (!Application.isPlaying) MTools.SetDirty(this);
        }

        public void OnEnable()
        {
            Animals ??= new List<MAnimal>();
            Animals.Add(this);                                              //Save the the Animal on the current List

            ResetInputSource(); //Connect the Inputs


            SetBoolParameter += SetAnimParameter;
            SetIntParameter += SetAnimParameter;
            SetFloatParameter += SetAnimParameter;
            SetTriggerParameter += SetAnimParameter;

            if (!alwaysForward.UseConstant && alwaysForward.Variable != null)
                alwaysForward.Variable.OnValueChanged += Always_Forward;

            ResetController();

            if (Grounded) this.Delay_Action(() => SetAnimParameter(hash_Grounded, Grounded)); //Set the Grounded Hash on the next frame (Important for the Grounded Logic

            Sleep = false;
        }

        private void OnApplicationQuit()
        {
            ApplicationIsQuitting = true;
        }

        private bool ApplicationIsQuitting = false;

        public void OnDisable()
        {
            if (ApplicationIsQuitting) return;

            Animals?.Remove(this);       //Remove all this animal from the Overall AnimalList

            UpdateInputSource(false); //Disconnect the inputs


            MTools.ResetFloatParameters(Anim); //Reset all Anim Floats!!
            if (RB && !RB.isKinematic) RB.linearVelocity = Vector3.zero;

            if (!alwaysForward.UseConstant && alwaysForward.Variable != null) //??????
                alwaysForward.Variable.OnValueChanged -= Always_Forward;

            if (states != null)
            {
                foreach (var st in states)
                {
                    if (st != null)
                    {
                        st.ExitState();
                        //st.OnAnimalDisabled();
                    }
                }
            }

            if (IsPlayingMode)
            {
                ActiveMode?.Reset();
                Mode_Stop();
            }

            OverrideStartState = ActiveStateID; //Save the current State to start with it next time


            if (ActiveState != null)
                ActiveState.EnterExitEvent?.OnExit?.Invoke();

            //This needs to be at the end of the Disable stuff
            SetBoolParameter -= SetAnimParameter;
            SetIntParameter -= SetAnimParameter;
            SetFloatParameter -= SetAnimParameter;
            SetTriggerParameter -= SetAnimParameter;

            StopAllCoroutines();

        }

        /// <summary> Calculates the center and Height of the Animal </summary>
        public void CalculateCenter(bool updateHeight = false)
        {
            if (Has_Pivot_Hip)
            {
                if (updateHeight) height = Pivot_Hip.position.y;
                Center = Pivot_Hip.position; //Set the Center to be the Pivot Hip Position
            }
            else if (Has_Pivot_Chest)
            {
                if (updateHeight) height = Pivot_Chest.position.y;
                Center = Pivot_Chest.position;
            }

            if (Has_Pivot_Chest && Has_Pivot_Hip)
            {
                Center = (Pivot_Chest.position + Pivot_Hip.position) / 2;
            }

            center.y = 0; //Remove Y since that is calculated by the Height of the Animal

            if (!Application.isPlaying) MTools.SetDirty(this);
        }

        /// <summary>Update all the Attack Triggers Inside the Animal...In case there are more or less triggers</summary>
        public void UpdateDamagerSet()
        {
            Attack_Triggers = GetComponentsInChildren<IMDamager>(true).ToList();        //Save all Attack Triggers.

            foreach (var at in Attack_Triggers)
            {
                at.UserGo = (gameObject);                 //Tell to   very Damager that this Animal is the Owner
                                                          // at.Enabled = false;
            }
        }

        #region Animator Stuff
        protected virtual void GetHashIDs()
        {
            if (Anim == null) return;

            //animatorHashParams = new();

            animatorHashParams = new(Anim.parameters.Select(p => p.nameHash)); // Cache all Animator Parameters Hashes

            //foreach (var parameter in Anim.parameters)
            //{
            //    animatorHashParams.Add(parameter.nameHash);
            //}

            #region Main Animator Parameters
            //Movement
            hash_Vertical = Animator.StringToHash(m_Vertical);

            hash_Horizontal = Animator.StringToHash(m_Horizontal);
            hash_SpeedMultiplier = Animator.StringToHash(m_SpeedMultiplier);

            hash_Movement = Animator.StringToHash(m_Movement);
            hash_Grounded = Animator.StringToHash(m_Grounded);

            //States
            hash_State = Animator.StringToHash(m_State);
            hash_StateEnterStatus = Animator.StringToHash(m_StateStatus);


            hash_LastState = Animator.StringToHash(m_LastState);
            hash_StateFloat = Animator.StringToHash(m_StateFloat);

            //Modes
            hash_Mode = Animator.StringToHash(m_Mode);

            hash_ModeStatus = Animator.StringToHash(m_ModeStatus);


            //Triggers
            hash_ModeOn = Animator.StringToHash(m_ModeOn);
            hash_StateOn = Animator.StringToHash(m_StateOn);


            #endregion

            #region Optional Parameters

            //Movement 
            hash_StateExitStatus = TryOptionalParameter(m_StateExitStatus);
            //hash_StateEnterStatus = TryOptionalParameter(m_StateStatus);
            hash_SpeedMultiplier = TryOptionalParameter(m_SpeedMultiplier);

            hash_VerticalRaw = TryOptionalParameter(m_VerticalRaw);

            hash_UpDown = TryOptionalParameter(m_UpDown);
            hash_DeltaUpDown = TryOptionalParameter(m_DeltaUpDown);

            hash_Slope = TryOptionalParameter(m_Slope);


            hash_DeltaAngle = TryOptionalParameter(m_DeltaAngle);
            hash_Sprint = TryOptionalParameter(m_Sprint);

            //States
            hash_StateTime = TryOptionalParameter(m_StateTime);


            hash_Strafe = TryOptionalParameter(m_Strafe);
            //hash_TargetHorizontal = TryOptionalParameter(m_TargetHorizontal);

            //Stance
            hash_Stance = TryOptionalParameter(m_Stance);

            hash_LastStance = TryOptionalParameter(m_LastStance);

            //Misc
            hash_Random = TryOptionalParameter(m_Random);
            hash_ModePower = TryOptionalParameter(m_ModePower);
            hash_ModeLayer = TryOptionalParameter(m_ModeLayer);


            hash_StateProfile = TryOptionalParameter(m_StateProfile);
            // hash_StanceOn = TryOptionalParameter(m_StanceOn);
            #endregion
        }


        //Send 0 if the Animator does not contain
        private int TryOptionalParameter(string param)
        {
            var AnimHash = Animator.StringToHash(param);

            if (!animatorHashParams.Contains(AnimHash))
                return 0;
            return AnimHash;
        }

        private bool sameAnimTag;

        protected virtual void CacheAnimatorState()
        {
            // m_PreviousCurrentState = m_CurrentState;
            //  m_PreviousNextState = m_NextState;

            m_CurrentState = Anim.GetCurrentAnimatorStateInfo(0);
            m_NextState = Anim.GetNextAnimatorStateInfo(0);

            //If the animator is in transition (Next state has full path )
            if (m_NextState.fullPathHash != 0)
            {
                //If the animations are different but the tags are the same
                if (m_CurrentState.fullPathHash != AnimState.fullPathHash
                    && m_CurrentState.tagHash == m_NextState.tagHash)
                {
                    if (!sameAnimTag)
                    {
                        sameAnimTag = true;
                        currentAnimTag = -1; //Reset the current anim-tag so the method can be called again
                    }
                }
                else
                {
                    sameAnimTag = false;
                }

                AnimStateTag = m_NextState.tagHash;
                AnimState = m_NextState;
            }

            else
            {
                if (m_CurrentState.fullPathHash != AnimState.fullPathHash)
                {
                    AnimStateTag = m_CurrentState.tagHash;
                }
                AnimState = m_CurrentState;
            }

            var lastStateTime = StateTime;
            StateTime = Mathf.Repeat(AnimState.normalizedTime, 1);

            //Check if the Animation Started again.
            if (lastStateTime > StateTime)
                StateCycle?.Invoke(ActiveStateID);
        }

        //MWC: last values written to the Animator — used to skip redundant SetFloat calls and delegate invocations (perf)
        private float lastAnim_Vertical, lastAnim_Horizontal, lastAnim_UpDown, lastAnim_DeltaUpDown,
                      lastAnim_DeltaAngle, lastAnim_Slope, lastAnim_SpeedMult, lastAnim_StateTime;

        private const float AnimParamEpsilon = 0.0001f;

        //MWC: invalidate the cache so the next UpdateAnimatorParameters writes every value (used after the Animator floats were reset)
        private void ResetAnimatorParameterCache()
        {
            lastAnim_Vertical = lastAnim_Horizontal = lastAnim_UpDown = lastAnim_DeltaUpDown =
            lastAnim_DeltaAngle = lastAnim_Slope = lastAnim_SpeedMult = lastAnim_StateTime = float.MinValue;
        }

        /// <summary>Link all Parameters to the animator</summary>
        protected virtual void UpdateAnimatorParameters()
        {
            //MWC: write only the values that actually changed beyond a small epsilon
            if (Mathf.Abs(VerticalSmooth - lastAnim_Vertical) > AnimParamEpsilon)
                SetFloatParameter.Invoke(hash_Vertical, lastAnim_Vertical = VerticalSmooth);     //Mandatory

            if (Mathf.Abs(HorizontalSmooth - lastAnim_Horizontal) > AnimParamEpsilon)
                SetFloatParameter.Invoke(hash_Horizontal, lastAnim_Horizontal = HorizontalSmooth); //Mandatory

            if (Mathf.Abs(UpDownSmooth - lastAnim_UpDown) > AnimParamEpsilon)
                TryAnimParameter(hash_UpDown, lastAnim_UpDown = UpDownSmooth);

            if (Mathf.Abs(DeltaUpDown - lastAnim_DeltaUpDown) > AnimParamEpsilon)
                TryAnimParameter(hash_DeltaUpDown, lastAnim_DeltaUpDown = DeltaUpDown);

            if (Mathf.Abs(DeltaAngle - lastAnim_DeltaAngle) > AnimParamEpsilon)
                TryAnimParameter(hash_DeltaAngle, lastAnim_DeltaAngle = DeltaAngle);

            if (Mathf.Abs(SlopeNormalized - lastAnim_Slope) > AnimParamEpsilon)
                TryAnimParameter(hash_Slope, lastAnim_Slope = SlopeNormalized);

            if (Mathf.Abs(SpeedMultiplier - lastAnim_SpeedMult) > AnimParamEpsilon)
                TryAnimParameter(hash_SpeedMultiplier, lastAnim_SpeedMult = SpeedMultiplier);

            if (Mathf.Abs(StateTime - lastAnim_StateTime) > AnimParamEpsilon)
                TryAnimParameter(hash_StateTime, lastAnim_StateTime = StateTime);
        }
        #endregion

        #region Additional Speeds (Movement, Turn) 

        public bool ModeNotAllowMovement => IsPlayingMode && !ActiveMode.AllowMovement;


        /// <summary>Multiplier added to the Additive position when the mode is playing.
        /// This will fix the issue Additive Speeds to mess with RootMotion Modes  </summary>
        public float Mode_Multiplier => IsPlayingMode ? ActiveMode.PositionMultiplier : 1;
        private void MoveRotator()
        {
            if (!FreeMovement && Rotator)
            {
                if (PitchAngle != 0 || Bank != 0)
                {
                    float limit = 0.005f;
                    var lerp = DeltaTime * (CurrentSpeedSet.PitchLerpOff);

                    Rotator.localRotation = Quaternion.Slerp(Rotator.localRotation, Quaternion.identity, lerp);

                    PitchAngle = Mathf.Lerp(PitchAngle, 0, lerp); //Lerp to zero the Pitch Angle when going Down
                    Bank = Mathf.Lerp(Bank, 0, lerp);

                    if (Mathf.Abs(PitchAngle) < limit && Mathf.Abs(Bank) < limit)
                    {
                        Bank = PitchAngle = 0;
                        Rotator.localRotation = Quaternion.identity;
                    }
                }
            }
            else
            {
                CalculatePitchDirectionVector();
            }
        }

        public virtual void FreeMovementRotator(float Ylimit, float bank)
        {
            CalculatePitch(Ylimit);
            CalculateBank(bank);
            CalculateRotator();
        }

        internal virtual void CalculateRotator()
        {
            if (Rotator) Rotator.localEulerAngles = new Vector3(PitchAngle, 0, Bank); //Angle for the Rotator
        }
        internal virtual void CalculateBank(float bank) =>
            Bank = Mathf.Lerp(Bank, -bank * Mathf.Clamp(HorizontalSmooth, -1, 1), DeltaTime * CurrentSpeedSet.BankLerp);
        internal virtual void CalculatePitch(float Pitch)
        {
            float NewAngle = 0;

            if (MovementAxis != Vector3.zero)             //Rotation PITCH
            {
                NewAngle = 90 - Vector3.Angle(UpVector, PitchDirection);
                NewAngle = Mathf.Clamp(-NewAngle, -Pitch, Pitch);
            }

            var deltatime = DeltaTime * CurrentSpeedSet.PitchLerpOn;

            PitchAngle = Mathf.Lerp(PitchAngle, Strafe ? Pitch * VerticalSmooth : NewAngle, deltatime);
            DeltaUpDown = Mathf.Lerp(DeltaUpDown, -Mathf.DeltaAngle(PitchAngle, NewAngle), deltatime * 2);

            if (Mathf.Abs(DeltaUpDown) < 0.01f) DeltaUpDown = 0;
        }


        /// <summary>Calculates the Pitch direction to Appy to the Rotator Transform</summary>
        internal virtual void CalculatePitchDirectionVector()
        {
            var dir = Move_Direction != Vector3.zero ? Move_Direction : Forward;
            PitchDirection = Vector3.Lerp(PitchDirection, dir, DeltaTime * CurrentSpeedSet.PitchLerpOn * 2);
        }

        public void SetTargetSpeed()
        {
            //var lerp = CurrentSpeedModifier.lerpPosition * DeltaTime;

            if ((!UseAdditivePos) ||        //Do nothing when UseAdditivePos is False
               (ModeNotAllowMovement))      //Do nothing when the Mode Locks the Movement
            {
                //TargetSpeed = Vector3.Lerp(TargetSpeed, Vector3.zero, lerp);
                TargetSpeed = Vector3.zero;
                return;
            }

            Vector3 TargetDir = ActiveState.Speed_Direction();

            // MDebug.Draw_Arrow(Position + GizmoDeltaPos, TargetDir, Color.blue);

            //IMPORTANT USE THE SLOPE IF the Animal uses only one slope
            if (Grounded && Has_Pivot_Chest && !Has_Pivot_Hip)
                TargetDir = Quaternion.FromToRotation(Up, SlopeNormal) * TargetDir;

            float Speed_Modifier = Strafe ? CurrentSpeedModifier.strafeSpeed.Value : CurrentSpeedModifier.position.Value;


            if (InGroundChanger)
            {
                var GroundSpeedRoot = (RootMotion && DeltaTime > 0) ? (Anim.deltaPosition / DeltaTime).magnitude : 0; //MWC: guard DeltaTime==0 to avoid NaN
                Speed_Modifier = Speed_Modifier + GroundChanger.Position + GroundSpeedRoot;
            }

            if (Strafe)
            {
                TargetDir = (Forward * VerticalSmooth) + (Right * HorizontalSmooth);

                if (FreeMovement)
                    TargetDir += (Up * UpDownSmooth);

            }
            else
            {
                if ((VerticalSmooth < 0) && CurrentSpeedSet != null)//Decrease when going backwards and NOT Strafing
                {
                    TargetDir *= -CurrentSpeedSet.BackSpeedMult.Value;
                    Speed_Modifier = CurrentSpeedSet[0].position; //Get the current speed modifier and the additive mode speed

                    if (InGroundChanger)
                    {
                        var GroundSpeedRoot = (RootMotion && DeltaTime > 0) ? (Anim.deltaPosition / DeltaTime).magnitude : 0; //MWC: guard DeltaTime==0 to avoid NaN
                        Speed_Modifier = Speed_Modifier + GroundChanger.Position + GroundSpeedRoot;
                    }
                }
                if (FreeMovement)
                {
                    float SmoothZYInput = Mathf.Clamp01(Mathf.Max(Mathf.Abs(UpDownSmooth), Mathf.Abs(VerticalSmooth))); // Get the Average Multiplier of both Z and Y Inputs
                    TargetDir *= SmoothZYInput;
                }
                else
                {
                    TargetDir *= VerticalSmooth; //Use Only the Vertical Smooth while grounded
                }
            }

            if (TargetDir.magnitude > 1) TargetDir.Normalize();

            Speed_Modifier += Mode_Additive_Pos; //Add the Mode Additive Position

            TargetSpeed = DeltaTime * Mode_Multiplier * ScaleFactor * Speed_Modifier * TargetDir;   //Calculate these Once per Cycle Extremely important 

            HorizontalVelocity = Vector3.ProjectOnPlane(Inertia + SlopeDirectionSmooth, SlopeNormal);

            HorizontalSpeed = HorizontalVelocity.magnitude;

            if (debugGizmos) MDebug.Draw_Arrow(Position + GizmoDeltaPos, TargetSpeed, Color.green);

            // MDebug.Draw_Arrow(Position + GizmoDeltaPos, TargetDir * 5, Color.cyan);
        }

        /// <summary> Add more Speed to the current Move animations</summary>  
        protected virtual void AdditionalSpeed(float time)
        {
            var Speed = CurrentSpeedModifier;

            var LerpPos = (Strafe) ? Speed.lerpStrafe : Speed.lerpPosition;

            if (InGroundChanger && GroundChanger.Lerp > 0) LerpPos = GroundChanger.Lerp; //USE GROUND CHANGER LERP


            InertiaPositionSpeed = (LerpPos > 0) ?
                Vector3.Lerp(InertiaPositionSpeed, UseAdditivePos ? TargetSpeed : Vector3.zero, DampingFactor(LerpPos, time)) : TargetSpeed;

            AdditivePosition += InertiaPositionSpeed;

            //Avoids code returning NaN
            if (float.IsNaN(InertiaPositionSpeed.x) || float.IsNaN(InertiaPositionSpeed.y) || float.IsNaN(InertiaPositionSpeed.z))
                InertiaPositionSpeed = TargetSpeed;

            if (debugGizmos)  //Draw the Inertia Direction 
            {
                MDebug.Draw_Arrow(Position + GizmoDeltaPos + (Vector3.one * 0.02f), 2 * ScaleFactor * InertiaPositionSpeed, new Color(.8f, .5f, 0));
            }
        }
        /// <summary>The full Velocity we want to without lerping, for the Additional Position NOT INLCUDING ROOTMOTION</summary>
        public Vector3 TargetSpeed { get; internal set; }


        /// <summary>Add more Rotations to the current Turn Animations  </summary>
        protected virtual void AdditionalRotation(float time)
        {
            if (IsPlayingMode && !ActiveMode.AllowRotation) return;          //Do nothing if the Mode Does not allow Rotation

            float SpeedRotation = CurrentSpeedModifier.rotation * AdditiveRotationMultiplier;

            if (VerticalSmooth < 0.01 && !CustomSpeed && CurrentSpeedSet != null)
            {
                SpeedRotation = CurrentSpeedSet[0].rotation; //When not moving ???
            }

            SpeedRotation += Mode_Additive_Rot; //Add the Mode Rotation

            if (SpeedRotation < 0) return;      //Do nothing if the rotation is lower than 0

            if (MovementDetected)
            {
                if (UsingMoveWithDirection)
                {
                    if (DeltaAngle != 0)
                    {
                        var TargetLocalRot = Quaternion.Euler(0, DeltaAngle * Mode_Multiplier_Rot, 0);

                        var targetRotation =
                            Quaternion.Slerp(Quaternion.identity, TargetLocalRot, DampingFactor((SpeedRotation + 1) / 4 * (TurnMultiplier + 1), time));

                        AdditiveRotation *= targetRotation;
                    }
                }
                else
                {
                    float Turn = SpeedRotation * 10 * Mode_Multiplier_Rot;           //Add Extra Multiplier

                    //Add +Rotation when going Forward and -Rotation when going backwards
                    float TurnInput = Mathf.Clamp(HorizontalSmooth, -1, 1) * (MovementAxis.z >= 0 ? 1 : -1);

                    AdditiveRotation *= Quaternion.Euler(0, Turn * TurnInput * time /** ModeRotation*/, 0);
                    var TargetGlobal = Quaternion.Euler(0, TurnInput * (TurnMultiplier + 1), 0);
                    var AdditiveGlobal = Quaternion.Slerp(Quaternion.identity, TargetGlobal, time * (SpeedRotation + 1) /** ModeRotation*/);
                    AdditiveRotation *= AdditiveGlobal;
                }
            }
        }

        internal void SetMaxMovementSpeed()
        {
            float maxspeedV = CurrentSpeedModifier.Vertical;
            float maxspeedH = 1;

            if (Strafe)
            {
                maxspeedH = maxspeedV;
            }
            VerticalSmooth = MovementAxis.z * maxspeedV;
            HorizontalSmooth = MovementAxis.x * maxspeedH;
            UpDownSmooth = MovementAxis.y;
        }


        /// <summary> Movement Trot Walk Run (Velocity changes)</summary>
        // Rates remain in inverse seconds. Exponential damping avoids snapping on a
        // long frame and gives the same response at 30, 60 and 120 Hz.
        private static float DampingFactor(float rate, float deltaTime) =>
            rate > 0 ? 1f - Mathf.Exp(-rate * Mathf.Max(0, deltaTime)) : 1f;

        internal void MovementSystem()
        {
            float maxspeedV = CurrentSpeedModifier.Vertical;
            float maxspeedH = 1;

            var LerpUpDown = DampingFactor(CurrentSpeedSet.PitchLerpOn, DeltaTime);
            var LerpVertical = DampingFactor(CurrentSpeedModifier.lerpPosAnim, DeltaTime);
            var LerpTurn = DampingFactor(CurrentSpeedModifier.lerpRotAnim, DeltaTime);
            var LerpAnimator = DampingFactor(CurrentSpeedModifier.lerpAnimator, DeltaTime);

            if (Strafe)
            {
                maxspeedH = maxspeedV;

                if (CurrentSpeedModifier.lerpStrafe > 0)
                {
                    LerpTurn = DampingFactor(CurrentSpeedModifier.lerpStrafe, DeltaTime);
                    LerpVertical = LerpTurn;
                }
            }

            if (ModeNotAllowMovement) //Active mode and Is playing Mode is failing!!**************
                MovementAxis = Vector3.zero;

            float Horiz;

            float v = MovementAxis.z;


            if (Rotate_at_Direction)
            {
                var MaxValue = 2f;

                var isRight = Vector3.Dot(Vector3.Cross(Forward, RawRotateDirAxis), UpVector) > 0; //Calculate if the direction is to the right or left of the animal

                if (!isRight) MaxValue = -2f; //Apply the MaxValue to the left or right depending on the direction

                var LerpAngle = 30f; //The angle where the MaxValue is applied

                //get the current angle from the animal forward and the movement direction
                var angle = Vector3.Angle(Forward, RawRotateDirAxis);
                angle = Mathf.Clamp(angle, 0, LerpAngle);
                MaxValue *= Mathf.Lerp(0, 1, angle / LerpAngle);

                float r = 0;
                v = 0; //Remove the Forward (Vertical) since its rotating in its place

                Horiz = Mathf.SmoothDamp(HorizontalSmooth, MaxValue, ref r, inPlaceDamp * DeltaTime); //Using properly the smooth  down

            }
            else
            {
                Horiz = Mathf.Lerp(HorizontalSmooth, MovementAxis.x * maxspeedH, LerpTurn);
            }

            VerticalSmooth = LerpVertical > 0 ?
                Mathf.Lerp(VerticalSmooth, v * maxspeedV, LerpVertical) :
                MovementAxis.z * maxspeedV;           //smoothly transitions between Speeds

            HorizontalSmooth = LerpTurn > 0 ? Horiz : MovementAxis.x * maxspeedH;               //smoothly transitions between Directions

            UpDownSmooth = LerpVertical > 0 ?
                Mathf.Lerp(UpDownSmooth, MovementAxis.y, LerpUpDown) :
                MovementAxis.y;                                                //smoothly transitions between Directions


            SpeedMultiplier = (LerpAnimator > 0) ?
                Mathf.Lerp(SpeedMultiplier, CurrentSpeedModifier.animator.Value, LerpAnimator) :
                CurrentSpeedModifier.animator.Value;  //Change the velocity of the animator

            if (Mathf.Abs(VerticalSmooth) < zero) VerticalSmooth = 0;
            if (Mathf.Abs(HorizontalSmooth) < zero) HorizontalSmooth = 0;
            if (Mathf.Abs(UpDownSmooth) < zero) UpDownSmooth = 0;
        }

        private const float zero = 0.005f;

        #endregion

        #region Platorm movement


        /// <summary>  Reference for the Animal to check if it is on a Ground Changer  </summary>
        public GroundSpeedChanger GroundChanger { get; set; }
        /// <summary> True if GroundChanger is not Null </summary>
        public bool InGroundChanger;

        /// <summary>Check if the Animal can do the Ground RootMotion </summary>
        internal bool GroundRootPosition = true;


        public virtual void Reset_Platform() => SetPlatform(defaultPlatform);
        public void SetPlatform(Transform newPlatform)
        {
            if (platform != newPlatform)
            {
                GroundRootPosition = true;
                platform = newPlatform;

                if (platform != null)
                {
                    //Debug.Log($"NEW PLATFORM {platform}");
                    var NewGroundChanger = newPlatform.GetComponent<GroundSpeedChanger>();

                    if (NewGroundChanger)
                    {
                        GroundRootPosition = false; //Important! Calculate RootMotion instead of adding it
                        if (GroundChanger != null) GroundChanger.OnExit.React(this); //set to the ground changer that this has enter 
                        GroundChanger = NewGroundChanger;
                        GroundChanger.OnEnter.React(this); //set to the ground changer that this has enter 
                    }
                    else
                    {
                        if (GroundChanger != null) GroundChanger.OnExit.React(this); //set to the ground changer that this has enter 
                        GroundChanger = null;
                    }

                    Last_Platform_Pos = platform.position;
                    Last_Platform_Rot = platform.rotation;
                }
                else  //No Platform
                {
                    if (GroundChanger != null) GroundChanger.OnExit.React(this); //set to the ground changer that this has enter 
                    GroundChanger = null;

                    DeltaPlatformPos = Vector3.zero;
                    DeltaPlatformRot = Quaternion.identity;

                    // Debug.Log("RESET PLATFORM VALUES");

                    MainPivotSlope = 0;
                    ResetSlopeValues();
                }

                InGroundChanger = GroundChanger != null;


                foreach (var s in states)
                    s.OnPlatformChanged(platform);
            }
        }

        public void PlatformMovement()
        {
            if (platform == null) return;

            DeltaPlatformPos = platform.position - Last_Platform_Pos;
            Quaternion Inverse_Rot = Quaternion.Inverse(Last_Platform_Rot);
            DeltaPlatformRot = Inverse_Rot * platform.rotation;

            if (DeltaPlatformRot != Quaternion.identity)
            {
                // Compute offset from the platform’s pivot
                Vector3 pivot = Last_Platform_Pos;
                Vector3 offset = Position - pivot;
                Vector3 rotatedOffset = DeltaPlatformRot * offset;
                DeltaPlatformPos += rotatedOffset - offset;
            }

            Position += DeltaPlatformPos;
            Rotation *= DeltaPlatformRot;

            Last_Platform_Pos = platform.position;
            Last_Platform_Rot = platform.rotation;

            //Debug.Log("Platform");
        }

        public Vector3 DeltaPlatformPos { get; private set; }
        public Quaternion DeltaPlatformRot { get; private set; }
        #endregion

        #region Terrain Alignment
        /// <summary> Store the GameObjectFront Hit.. This is used to compare the tag and find if it is a debree or not.  </summary>
        private GameObject MainFrontHit;
        private bool isDebrisFront;

        /// <summary>  Raycasting stuff to align and calculate the ground from the animal ****IMPORTANT***  </summary>
        /// <param name="distance">
        /// if is set to zero then Use the PIVOT_MULTIPLIER. Set the Distance when you want to cast from the Animal Height instead.
        /// </param>
        //MWC: ground alignment cast — optional SphereCast (using RayCastRadius) instead of the thin Raycast.
        //The SphereCast hit distance is compensated by the radius so the Align logic receives the same distance a Raycast would report.
        private bool AlignCast(Vector3 origin, out RaycastHit hit, float distance)
        {
            if (useSphereCastAlign.Value)
            {
                var radius = RayCastRadius * ScaleFactor;

                if (Physics.SphereCast(origin, radius, -Up, out hit, distance, GroundLayer, QueryTriggerInteraction.Ignore))
                {
                    hit.distance += radius; //SphereCast reports the sphere-center travel distance
                    return true;
                }
                return false;
            }

            return Physics.Raycast(origin, -Up, out hit, distance, GroundLayer, QueryTriggerInteraction.Ignore);
        }

        internal virtual void AlignRayCasting(float distance = 0)
        {
            //Debug.Log($"Align RayCasting!!");
            MainRay = FrontRay = false;
            hit_Chest = new RaycastHit() { normal = Vector3.zero };         //Clean the Ray casts every time
            hit_Hip = new RaycastHit();                                     //Clean the Raycast every time
            hit_Chest.distance = hit_Hip.distance = Height;                 //Reset the Distances to the Height of the animal

            if (distance == 0) distance = Pivot_Multiplier * ScaleFactor; //IMPORTANT

            if (AlignCast(Main_Pivot_Point, out hit_Chest, distance)) //MWC: was Physics.Raycast — routed through AlignCast for the SphereCast option
            {
                var hitChestCollider = hit_Chest.collider;
                var hitChestTransform = hit_Chest.transform;
                if (MTools.Layer_in_LayerMask(hitChestCollider.gameObject.layer, groundLayer.Value) && hitChestCollider.transform.SameHierarchy(transform))
                { MDebug.LogWarning($"The Internal Collider [{hitChestCollider.name}] is on the Ground Layer Mask. Please change the Layer of the gameobject", hitChestCollider); }

                FrontRay = true;

                //Store if the Front Hit is a Debris so Storing if is a Debree it will be only be done once
                var hitChestGameObj = hitChestTransform.gameObject; //CustomPatch: optimized redundant gameobject access
                if (MainFrontHit != hitChestGameObj)
                {
                    MainFrontHit = hitChestGameObj;
                    isDebrisFront = MainFrontHit.CompareTag(DebrisTag);
                }

                //If is a debree clean everything like it was a Flat Terrain (CHECK DEBREEEE)
                if (isDebrisFront)
                {
                    MainPivotSlope = 0;
                    hit_Chest.normal = UpVector;
                    ResetSlopeValues();
                }
                else
                {
                    //Store the Downward Slope Direction
                    SlopeNormal = hit_Chest.normal;
                    MainPivotSlope = Vector3.SignedAngle(SlopeNormal, UpVector, Right);
                    SlopeDirection = Vector3.ProjectOnPlane(Gravity, SlopeNormal).normalized;

                    SlopeDirectionAngle = 90 - Vector3.Angle(Gravity, SlopeDirection);
                    if (Mathf.Approximately(SlopeDirectionAngle, 90)) SlopeDirectionAngle = 0;
                }

                if (debugGizmos)
                {
                    MDebug.DrawRay(hit_Chest.point + GizmoDeltaPos, 0.2f * ScaleFactor * SlopeNormal, Color.green);
                    MDebug.DrawWireSphere(Main_Pivot_Point + GizmoDeltaPos + -Up * (hit_Chest.distance - RayCastRadius), Color.green, RayCastRadius * ScaleFactor);
                    MDebug.Draw_Arrow(hit_Chest.point + GizmoDeltaPos, SlopeDirection * 0.5f, Color.black, 0, 0.1f);
                }

                SetPlatform(hitChestTransform);

                //Physic Logic (Push RigidBodies Down with the Weight)
                AddForceToGround(hitChestCollider, hit_Chest.point);

            }
            else
            {
                Reset_Platform();
            }

            if (Has_Pivot_Hip && Has_Pivot_Chest) //Ray From the Hip to the ground
            {
                var hipPoint = Pivot_Hip.World(t);

                MDebug.DrawWireSphere(hipPoint, Color.yellow, RayCastRadius * ScaleFactor);

                if (AlignCast(hipPoint, out hit_Hip, distance)) //MWC: was Physics.Raycast — routed through AlignCast for the SphereCast option
                {
                    var hitHipCollider = hit_Hip.collider;

                    if (MTools.Layer_in_LayerMask(hitHipCollider.gameObject.layer, groundLayer.Value) && hitHipCollider.transform.SameHierarchy(transform))
                    { MDebug.LogWarning($"The Internal Collider [{hitHipCollider}] is on the Ground Layer Mask. Please change the Layer of the gameobject", hitHipCollider); }

                    MainRay = true;

                    if (debugGizmos)
                    {
                        MDebug.DrawRay(hit_Hip.point + GizmoDeltaPos, 0.2f * ScaleFactor * hit_Hip.normal, Color.green);
                        MDebug.DrawWireSphere(hipPoint + GizmoDeltaPos + -Up * (hit_Hip.distance - RayCastRadius), Color.green, RayCastRadius * ScaleFactor);
                    }

                    SetPlatform(hit_Hip.transform);               //Platforming logic

                    AddForceToGround(hitHipCollider, hit_Hip.point);


                    //If there's no Front Ray but we did find a Hip Ray, so save the hit chest
                    if (!FrontRay)
                        hit_Chest = hit_Hip;

                }
                else
                {
                    MainRay = false;

                    Reset_Platform();

                    if (FrontRay)
                    {
                        MovementAxis.z = 1; //Force going forward in case there's no Back Ray (HACK)
                        hit_Hip = hit_Chest;  //In case there's no Hip Ray
                        //MainRay = true; //Fake is Grounded even when the HOP Ray did not Hit .
                    }
                }
            }
            else
            {
                MainRay = FrontRay; //Just in case you dont have HIP RAY IMPORTANT FOR HUMANOID CHARACTERS
                hit_Hip = hit_Chest;  //In case there's no Hip Ray
            }

            //   Debug.Log($"hit_Hip {hit_Hip.distance}: hit_Chest {hit_Chest.distance}");
            if (ground_Changes_Gravity && hit_Hip.normal != Vector3.zero)
                Gravity = -hit_Hip.normal;


            CalculateSurfaceNormal();
        }

        public void ResetSlopeValues()
        {
            SlopeDirection = Vector3.zero;
            SlopeDirectionSmooth = Vector3.ProjectOnPlane(SlopeDirectionSmooth, UpVector);
            SlopeDirectionAngle = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] //: force optimize small method call
        private void AddForceToGround(Collider collider, Vector3 point)
        {
            var attachedRigidBody = collider.attachedRigidbody; //: fixed wrong null check for unity object
            if (attachedRigidBody != null)
                attachedRigidBody.AddForceAtPosition(Gravity * (RB.mass / 2), point, ForceMode.Force);
        }

        internal virtual void CalculateSurfaceNormal()
        {
            if (Has_Pivot_Hip)
            {
                Vector3 TerrainNormal;

                if (Has_Pivot_Chest)
                {
                    Vector3 direction = (hit_Chest.point - hit_Hip.point).normalized;
                    Vector3 Side = Vector3.Cross(UpVector, direction).normalized;
                    SurfaceNormal = Vector3.Cross(direction, Side).normalized;

                    TerrainNormal = SurfaceNormal;
                    SlopeNormal = SurfaceNormal;

                    if (!MainRay && FrontRay)
                    {
                        SurfaceNormal = hit_Chest.normal;
                    }
                    else if (MainRay && !FrontRay)
                    {
                        //hit_Chest was copied from hit_Hip, so the direction was zero. Align with the Hip (e.g. landing facing downhill)
                        SurfaceNormal = hit_Hip.normal;
                    }
                }
                else
                {
                    SurfaceNormal = TerrainNormal = hit_Hip.normal;
                }

                TerrainSlope = Vector3.SignedAngle(TerrainNormal, UpVector, Right);
            }
            else
            {
                TerrainSlope = Vector3.SignedAngle(hit_Hip.normal, UpVector, Right);
                SurfaceNormal = UpVector;
            }
        }

        /// <summary>Align the Animal to Terrain</summary>
        /// <param name="align">True: Aling to Surface Normal, False: Align to Up Vector</param>
        public virtual void AlignRotation(bool align, float time, float smoothness)
        {
            AlignRotation(align ? SurfaceNormal : UpVector, time, smoothness);
        }

        /// <summary>Align the Animal to a Custom </summary>
        /// <param name="align">True: Aling to UP, False Align to Terrain</param>
        public virtual void AlignRotation(Vector3 alignNormal, float time, float Smoothness)
        {
            AlignRotLerpDelta = Mathf.Lerp(AlignRotLerpDelta, Smoothness, time * AlignRotDelta * 4);

            Quaternion AlignRot = Quaternion.FromToRotation(Up, alignNormal) * Rotation;  //Calculate the orientation to Terrain 
            Quaternion Inverse_Rot = Quaternion.Inverse(Rotation);
            Quaternion Target = Inverse_Rot * AlignRot;
            Quaternion Delta = Quaternion.Lerp(Quaternion.identity, Target, time * AlignRotLerpDelta); //Calculate the Delta Align Rotation

            Rotation *= Delta;
            //AdditiveRotation *= Delta;
        }

        public virtual void AlignRotation(Vector3 from, Vector3 to, float time, float Smoothness)
        {
            AlignRotLerpDelta = Mathf.Lerp(AlignRotLerpDelta, Smoothness, time * AlignRotDelta * 4);

            Quaternion AlignRot = Quaternion.FromToRotation(from, to) * Rotation;  //Calculate the orientation to Terrain 
            Quaternion Inverse_Rot = Quaternion.Inverse(Rotation);
            Quaternion Target = Inverse_Rot * AlignRot;
            Quaternion Delta = Quaternion.Lerp(Quaternion.identity, Target, time * AlignRotLerpDelta); //Calculate the Delta Align Rotation

            Rotation *= Delta;
            //AdditiveRotation *= Delta;
        }

        public virtual void AlignRotation(Vector3 from, Vector3 to)
        {
            Quaternion AlignRot = Quaternion.FromToRotation(from, to) * Rotation;  //Calculate the orientation to Terrain 
            Quaternion Inverse_Rot = Quaternion.Inverse(Rotation);
            Quaternion Target = Inverse_Rot * AlignRot;
            Rotation *= Target;
        }

        /// <summary>Snap to Ground with Smoothing</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: force optimize small method call
        internal void AlignPosition(float time)
        {
            if (!MainRay && !FrontRay) return;         //DO NOT ALIGN  IMPORTANT This caused the animals jumping upwards when falling down
            AlignPosition(hit_Hip.distance, time);
        }

        // private float difference;

        internal void AlignPosition(float distance, float time)
        {
            float difference = Height - distance;

            if (!Mathf.Approximately(distance, Height))
            {
                AlignPosLerpDelta = Mathf.Lerp(AlignPosLerpDelta, AlignPosLerp * 2, time * AlignPosDelta);

                var DeltaDifference = Mathf.Lerp(0, difference, time * AlignPosLerpDelta);
                Vector3 align = Rotation * new Vector3(0, DeltaDifference, 0); //Rotates with the Transform to better alignment
                Position += align; //WORKS WITH THIS!! 

                hit_Hip.distance += DeltaDifference; //REMOVE the difference (PERFORMANCE!!!!!)

            }
        }

        /// <summary> Slope movement when the slope is big or small and where there's a ground changer component  </summary>
        private void SlopeMovement()
        {
            SlopeAngleDifference = 0;

            float threshold;
            float slide;
            float slideDamp;

            if (InGroundChanger)
            {
                threshold = GroundChanger.SlideThreshold;
                slide = GroundChanger.SlideAmount;
                slideDamp = GroundChanger.SlideDamp;
            }
            else
            {
                //Restore the values
                threshold = slideThreshold;
                slide = slideAmount;
                slideDamp = this.slideDamp;
            }

            var Min = SlopeLimit - threshold;

            if (SlopeDirectionAngle > Min)
            {
                SlopeAngleDifference = (SlopeDirectionAngle - Min) / (SlopeLimit - Min);
                SlopeAngleDifference = Mathf.Clamp01(SlopeAngleDifference); //Clamp the Slope Movement so in higher angles does not get push that much
            }

            //Move in the direction of the Ground Normal, 
            if (Grounded)
                SlopeDirectionSmooth = Vector3.ProjectOnPlane(SlopeDirectionSmooth, SlopeNormal);

            SlopeDirectionSmooth = Vector3.SmoothDamp(
                    SlopeDirectionSmooth, slide * SlopeAngleDifference * SlopeDirection,
                    ref vectorSmoothDamp, DeltaTime * slideDamp);

            if (debugGizmos) MDebug.Draw_Arrow(Position + GizmoDeltaPos, SlopeDirectionSmooth * 2f, Color.yellow);

            var dot = Vector3.Dot(SlopeDirection, Move_Direction);

            if (dot < 0)
            {
                MovementAxis = Vector3.Lerp(MovementAxis, Vector3.zero, SlopeAngleDifference);
            }

            if (SlopeDirectionSmooth != Vector3.zero) Position += SlopeDirectionSmooth;
        }

        private Vector3 vectorSmoothDamp = Vector3.zero;

        /// <summary>Snap to Ground with no Smoothing</summary>
        internal virtual void AlignPosition_Distance(float distance)
        {
            float difference = Height - distance;
            AdditivePosition += Rotation * new Vector3(0, difference, 0); //Rotates with the Transform to better alignment
        }


        /// <summary>Snap to Ground with no Smoothing</summary>
        public virtual void AlignPosition()
        {
            if (hit_Hip.distance == 0) return;         //DO NOT ALIGN  IMPORTANT This caused the animals jumping upwards when falling down

            float difference = Height - hit_Hip.distance;
            //  Debug.Log($"Difference: {difference} - hit_Hip.distance {hit_Hip.distance} Height {Height}");
            Position += Rotation * new Vector3(0, difference, 0); //Rotates with the Transform to better alignment
            InertiaPositionSpeed = Vector3.ProjectOnPlane(RB.linearVelocity * DeltaTime, UpVector);
            ResetUPVector(); //IMPORTANT!
        }
        #endregion

        /// <summary> Try Activate all other states </summary>
        protected virtual void TryActivateState()
        {
            if (ActiveState.IsPersistent) return;        //If the State cannot be interrupted the ignored trying activating any other States
            if (Mode_PersistentState) return;             //The Modes are not allowing the States to Change
            if (JustActivateState) return;               //Do not try to activate a new state since there already a new one on Activation

            foreach (var trySt in states)
            {
                if (trySt == ActiveState) continue;      //Skip Re-Activating yourself

                if (ActiveState.IgnoreLowerStates && ActiveState.Priority > trySt.Priority) continue; //Do not check lower priority states

                if ((trySt.UniqueID + CurrentCycle) % trySt.TryLoop != 0) continue;     //Check the Performance Loop for the  trying state

                // Debug.Log($"trySt.name {trySt.name}");

                if (!ActiveState.IsPending && ActiveState.CanExit)                      //Means a new state can be activated
                {
                    if (trySt.Active &&
                        !trySt.OnEnterCoolDown &&
                        !trySt.IsSleep &&
                        !trySt.OnQueue &&
                        !trySt.OnHoldByReset &&
                         trySt.InternalTryActivate() && trySt.TryOverride
                         )
                    {
                        trySt.Activate();
                        break;
                    }
                }
            }
        }

        /// <summary>Check if the Active State can exit </summary>
        protected virtual void TryExitActiveState()
        {
            if (ActiveState.CanExit && !ActiveState.IsPersistent)
                ActiveState.TryExitState(DeltaTime);     //if is not in transition and is in the Main Tag try to Exit to lower States

            ActiveState.AutoExitConditions(); //Check the Auto Exit Conditions

        }


        //private bool JustAnimatorMove;
        protected virtual void OnAnimatorMove()
        {
            OnAnimalMove();
        }


        protected virtual void OnAnimalMove()
        {
            CurrentCycle = (CurrentCycle + 1) % 999999999;

            DeltaTime = Anim.updateMode == AnimatorUpdateMode.Fixed ?
              Time.fixedDeltaTime
                 : Time.deltaTime
                 ;

            DeltaPos = Position - LastPosition + DeltaPlatformPos;                    //DeltaPosition from the last frame

            if (defaultPlatform != null) DeltaPos -= DeltaPlatformPos; //Remove the Platform Movement if using a default platform

            //MWC: UpInertia_Apply writes straight to Position between this DeltaPos calculation and the LastPosition store,
            //so that movement was invisible to every DeltaPos — Fall counted gravity-only descent instead of net movement,
            //inflating FallCurrentDistance (and fall damage) 1.5-2x after wall runs. Re-add it, same pattern as DeltaPlatformPos.
            DeltaPos += LastUpInertiaMove;
            LastUpInertiaMove = Vector3.zero;

            //MWC: in Normal mode the controller moves the Transform INSIDE this method, so (Position - LastPosition)
            //never contains the character's own movement — Inertia/HorizontalSpeed/HorizontalVelocity were ~0 and
            //states inheriting momentum (JumpBasic forward movement, Fall) got nothing. Add the last frame's
            //self-movement so DeltaPos matches the Fixed-mode semantics (where physics moves the body between calls).
            if (Anim.updateMode == AnimatorUpdateMode.Normal)
            {
                DeltaPos += LastSelfMove;
                LastSelfMove = Vector3.zero;
            }

            // GizmoDeltaPos = DeltaPos;

            if (Sleep || InTimeline)
            {
                Anim.ApplyBuiltinRootMotion();
                return;
            }

            CacheAnimatorState();
            ResetValues();

            if (ActiveState == null) return;

            Anim.speed = AnimatorSpeed * TimeMultiplier;

            DeltaTime =
                Anim.updateMode == AnimatorUpdateMode.Fixed ?
                Time.fixedDeltaTime
                : Time.deltaTime
                ;


            PreInput?.Invoke(this);             //Check the Pre State Movement on External Scripts

            ActiveState.InputAxisUpdate();      //States will calculate the Input State, States can override the default values.
            ActiveState.SetCanExit();           //Check if the Active State can Exit to a new State (Was not Just Activated or is in transition)

            PreStateMovement?.Invoke(this);             //Check the Pre State Movement on External Scripts

            ActiveState.OnStatePreMove(DeltaTime);          //Call before the Target is calculated After the Input

            SetTargetSpeed();

            MoveRotator();

            AdditionalSpeed(DeltaTime);

            if (UseAdditiveRot)
                AdditionalRotation(DeltaTime);


            //Update the State Profile if is different
            if (ActiveState_Profile != ActiveState.StateProfile) Update_StateProfile();


            ActiveState.OnStateMove(DeltaTime);                                                     //UPDATE THE STATE BEHAVIOUR

            ApplyExternalForce();

            if (IsPlayingMode)
                ActiveMode.OnAnimatorMove(DeltaTime); //Do Charged Mode AND MODIFIERS
            else if (IsPreparingMode && Time.time > ModeActivationTime + 0.5f)
            {
                //Watchdog. A Mode was prepared (Animator parameters set, ModeOn raised) but the Animator never entered its
                //state, so Animation Tag Enter never ran and [Is Preparing Mode] would stay ON forever: every other Mode is
                //rejected and the layer keeps whatever state it was in. Seen when the same Ability is re-activated while
                //its state is still exiting and the transition cannot re-enter itself. Preparing normally resolves on the
                //next Animator update (TryActivate already treats 0.1s as stale), so half a second is a hard failure.
                if (debugModes) Debuging($"Preparing Mode [{ModeAbility}] timed out. The Animator never entered its state → Mode parameters reset");

                ModeAbility = 0;
                SetModeStatus(Int_ID.Interrupted); //[Interrupted] is the Empty transition that needs no ModeOn trigger
                IsPreparingMode = false;
            }


            var PosBeforePlatform = Position;

            PlatformMovement(); //This needs to be calculated first!!! 

            if (!GroundedLogic())
            {
                MainRay = FrontRay = false;
                SurfaceNormal = UpVector;

                //Use is also if there's a residual Slope movement
                SlopeMovement();

                //Reset the PosLerp
                AlignPosLerpDelta = 0;
                AlignRotLerpDelta = 0;

                if (!UseCustomRotation)
                    AlignRotation(false, DeltaTime, AlignRotLerp); //Align to the Gravity Normal
                TerrainSlope = 0;

                GravityLogic();
            }

            PostStateMovement?.Invoke(this); // Check the Post State Movement on External Scripts

            TryExitActiveState();
            TryActivateState();
            MovementSystem();

            if (float.IsNaN(AdditivePosition.x)) return;

            //Clear Y Movement
            if (ActiveMode != null && ActiveMode.ActiveAbility.NoYMovement)
            {
                AdditivePosition = Vector3.ProjectOnPlane(AdditivePosition, UpVector);
            }

            //MWC: compute the desired velocity for ALL update modes — it was only written on the Fixed(dynamic) branch,
            //so Fall's stuck-detection (and anything else reading it) saw stale data in Normal/Kinematic modes
            DesiredRBVelocity = DeltaTime > 0 ? (AdditivePosition / DeltaTime) * TimeMultiplier : Vector3.zero;

            if (!DisablePosition)
            {
                if (RB)
                {
                    if (Anim.updateMode == AnimatorUpdateMode.Normal)
                    {
                        var SelfMove = KinematicSweep();
                        Position += SelfMove;
                        LastSelfMove = SelfMove; //MWC: store the self-movement so the next DeltaPos includes it (momentum inheritance)
                        //Position += AdditivePosition * TimeMultiplier;

                        //MWC: Normal mode is transform-driven, but a dynamic body kept its residual velocity and PhysX
                        //moved it every physics step on top of the per-frame transform writes (= jitter). Clear the
                        //velocity so physics only depenetrates; the movement stays fully transform-driven.
                        if (!RB.isKinematic) RB.linearVelocity = Vector3.zero;
                    }
                    else if (Anim.updateMode == AnimatorUpdateMode.Fixed)
                    {
                        if (RB.isKinematic)
                        {
                            Position += KinematicSweep();
                            //Position += AdditivePosition * TimeMultiplier;
                        }
                        else
                        {
                            RB.linearVelocity = DesiredRBVelocity; //MWC: DesiredRBVelocity is now computed above for all modes
                        }
                    }
                }
                else
                {
                    Position += AdditivePosition * TimeMultiplier;
                }
            }

            if (!DisableRotation)
            {
                Rotation *= AdditiveRotation;
                Strafing_Rotation();
            }

            UpdateAnimatorParameters();              //Set all Animator Parameters


            additivePosition = Vector3.zero;
            additiveRotation = Quaternion.identity;


            //CustomPatch: Added linear & angular alignment velocity
            //MWC: in Normal mode the movement is transform-driven (velocity is cleared above) — apply the extra
            //linear/angular alignment directly to the transform instead of the physics body
            if (Anim.updateMode == AnimatorUpdateMode.Normal)
            {
                if (ExtraAdditiveLinearVelocity != Vector3.zero) Position += ExtraAdditiveLinearVelocity * DeltaTime;
                if (ExtraDeltaRotation != Quaternion.identity) Rotation *= ExtraDeltaRotation;
            }
            else if (RB && !RB.isKinematic) //MWC: added missing RB null guard
            {
                RB.linearVelocity += ExtraAdditiveLinearVelocity;
                RB.MoveRotation(RB.rotation * ExtraDeltaRotation);
            }





            LastPosition = Position;
            ExtraDeltaRotation = Quaternion.identity;
            ExtraAdditiveLinearVelocity = Vector3.zero;
        }

        private Vector3 KinematicSweep()
        {
            if (MainCollider == null || !MainCollider.enabled) return AdditivePosition * TimeMultiplier; //No collider, no sweep

            var remaining = AdditivePosition * TimeMultiplier;
            var movement = Vector3.zero;

            //MWC: capsule was built with an unrotated/unscaled center (center + position) and world-axis direction —
            //wrong whenever the character is rotated (slopes, orient-to-ground). Use TransformPoint and the rotated axis.
            var pos = t.TransformPoint(MainCollider.center);

            //Position += CollideAndSlide(AdditivePosition * TimeMultiplier, Main_Pivot_Point, 3, AdditivePosition); //COLIDE AND SLIDE AFTER

            var CapsuleDir = MainCollider.direction == 0 ? t.right : MainCollider.direction == 1 ? t.up : t.forward;

            //MWC: apply the character scale, and place the sphere centers at (height/2 - radius) so the swept capsule matches the collider
            var radius = MainCollider.radius * ScaleFactor;
            var halfHeight = Mathf.Max(0f, (MainCollider.height * ScaleFactor * 0.5f) - radius);
            var point1 = pos + CapsuleDir * halfHeight;
            var point2 = pos - CapsuleDir * halfHeight;

            // Work in displacements throughout. Multiplying the displacement by hit.distance
            // mixed metres with metres-squared, then added the entire vertical displacement
            // again. Landings could penetrate the floor and wall contacts changed the speed.
            // Sweep the remainder after sliding too, so corners cannot bypass a second wall.
            const float skin = .015f;
            for (int iteration = 0; iteration < 3 && remaining.sqrMagnitude > .00000001f; iteration++)
            {
                float distance = remaining.magnitude;
                var direction = remaining / distance;
                if (!Physics.CapsuleCast(point1 + movement, point2 + movement, radius, direction,
                    out var hit, distance, GroundLayer, QueryTriggerInteraction.Ignore))
                {
                    movement += remaining;
                    break;
                }
                if (iteration == 0 && hit.rigidbody != null && !hit.rigidbody.isKinematic)
                    hit.rigidbody.AddForceAtPosition(remaining * (RB ? RB.mass : 1), hit.point, ForceMode.Impulse);
                var travel = direction * Mathf.Max(0, hit.distance - skin);
                movement += travel;
                remaining = Vector3.ProjectOnPlane(remaining - travel, hit.normal);
            }
            return movement;
        }

        public Vector3 ExtraAdditiveLinearVelocity { get; set; }

        public Quaternion ExtraDeltaRotation { get; set; } = Quaternion.identity;

        internal bool GroundedLogic()
        {
            if (Grounded && !Mode_IgnoreGrounded)
            {
                SlopeMovement(); //Before Raycasting so the Raycast is calculated correctly

                if (AlignCycle.Value <= 1 || (AlignUniqueID + CurrentCycle) % AlignCycle.Value == 0)
                    AlignRayCasting();

                AlignPosition(DeltaTime);

                if (!UseCustomRotation)
                    AlignRotation(UseOrientToGround, DeltaTime, AlignRotLerp);
                return true;
            }
            return false;
        }

        /// <summary> Resets Additive Rotation and Additive Position to their default</summary>
        private void ResetValues()
        {
            //The animator might be set to UPDATE(0) due to the IK which breaks the RootMotion
            if (Anim.deltaPosition == Vector3.zero && Anim.deltaRotation == Quaternion.identity)
            {
                return;
            }

            var deltaTime = Anim.updateMode == AnimatorUpdateMode.Normal ? Time.deltaTime : Time.fixedDeltaTime;


            DeltaRootMotion = RootMotion && GroundRootPosition && !(UseInPlaceStrafe && Strafe && ActiveStateID.ID <= 1) ? (Anim.deltaPosition * CurrentSpeedSet.RootMotionPos) :
                Vector3.Lerp(DeltaRootMotion, Vector3.zero, currentSpeedModifier.lerpAnimator * deltaTime);

            // DeltaRootMotion = Vector3.zero;

            //IMPORTANT USE THE SLOPE IF the Animal uses only one Pivot
            if (Grounded && Has_Pivot_Chest && !Has_Pivot_Hip)
                DeltaRootMotion = Quaternion.FromToRotation(Up, SlopeNormal) * DeltaRootMotion;


            AdditivePosition = DeltaRootMotion * TimeMultiplier;


            // AdditivePosition = RootMotion ? Anim.deltaPosition : Vector3.zero;
            AdditiveRotation = RootMotionRotation ?
                Quaternion.Slerp(Quaternion.identity, Anim.deltaRotation, CurrentSpeedSet.RootMotionRot) :
                Quaternion.identity;

            //  DeltaPos = t.position - LastPos;                    //DeltaPosition from the last frame

            //  Debug.Log($"DeltaPos : {DeltaPos.magnitude/DeltaTime:F3} ");

            //CurrentCycle = (CurrentCycle + 1) % 999999999;

            if (RB)
            {
                //MWC: in Normal mode the body velocity is cleared every cycle (transform-driven movement) —
                //use DeltaPos there so DeltaVelocity stays meaningful (Swim, WallRun and others read it)
                DeltaVelocity = Anim.updateMode == AnimatorUpdateMode.Fixed ? RB.linearVelocity * DeltaTime : DeltaPos;
            }
            else DeltaVelocity = DeltaPos;
        }

        #region Inputs 
        /// <summary> Calculates the Movement Axis from the Input or Direction </summary>
        internal void InputAxisUpdate()
        {
            if (Rotate_at_Direction)
            {
                if (MainCamera && UseCameraInput)
                    MoveFromDirection(RawRotateDirAxis);
            }
            else if (UseRawInput)
            {
                //override the Forward Input if the State or Always Forward is set
                if (AlwaysForward || ActiveState.AlwaysForward.Value)
                    RawInputAxis.z = 1;

                var inputAxis = RawInputAxis;

                inputAxis.Scale(LockMovementAxis);

                if (LockMovement || Sleep)
                {
                    MovementAxis = Vector3.zero;
                    return;
                }

                if (worldSpacePlayerInput)
                {
                    MoveFromDirection(inputAxis);
                }
                else if (MainCamera && UseCameraInput)
                {
                    MoveWithCameraInput(inputAxis);
                }
                else
                {
                    MoveWorld(inputAxis);
                }
            }
            else //Means that is Using a Direction Instead 
            {
                MoveFromDirection(RawInputAxis);
            }
        }

        /// <summary> Convert the Camera View to Forward Direction </summary>
        private void MoveWithCameraInput(Vector3 inputAxis)
        {
            // if (MovementDone) return; //This was already called

            //Normalize the Camera Forward Depending the Up Vector IMPORTANT!
            var Cam_Forward = Vector3.ProjectOnPlane(MainCamera.forward, UpVector).normalized;
            var Cam_Right = Vector3.ProjectOnPlane(MainCamera.right, UpVector).normalized;

            Vector3 UpInput;

            if (!FreeMovement)
            {
                UpInput = Vector3.zero;            //Reset the UP Input in case is on the Ground
            }
            else
            {
                if (UseCameraUp)
                {
                    var angle = Vector3.SignedAngle(MainCamera.up, Vector3.up, MainCamera.right);

                    angle = Mathf.Clamp((angle / 90) * CurrentSpeedSet.UpDownMult.Value, -1, 1);
                    UpInput = (inputAxis.y * LockMovementAxis.y * UpVector); //Input addition
                    UpInput += angle * inputAxis.z * UpVector;

                }
                else
                {
                    UpInput = (inputAxis.y * LockMovementAxis.y * UpVector);
                }
            }

            var m_Move = (inputAxis.z * Cam_Forward) + (inputAxis.x * Cam_Right) + UpInput;

            MoveFromDirection(m_Move);
        }


        /// <summary>Get the Raw Input Axis from a source</summary>
        public virtual void SetInputAxis(Vector3 inputAxis)
        {
            worldSpacePlayerInput = false;
            UseRawInput = true;
            RawInputAxis = inputAxis;// + AdditiveRawInputAxis; // Store the last current use of the Input
            if (UsingUpDownExternal)
                RawInputAxis.y = UpDownAdditive; //Add the UPDown Additive from the Mobile.

            // Debug.Log("HERE");
        }

        public virtual void SetInputAxis(Vector2 inputAxis) => SetInputAxis(new Vector3(inputAxis.x, 0, inputAxis.y));

        public virtual void SetInputAxisXY(Vector2 inputAxis) => SetInputAxis(new Vector3(inputAxis.x, inputAxis.y, 0));

        public virtual void SetInputAxisYZ(Vector2 inputAxis) => SetInputAxis(new Vector3(0, inputAxis.x, inputAxis.y));

        private float UpDownAdditive;

        /// <summary> Up Down External Axis</summary>
        private bool UsingUpDownExternal;

        /// <summary>Use this for Custom UpDown Movement</summary>
        public virtual void SetUpDownAxis(float upDown)
        {
            UpDownAdditive = upDown;
            UsingUpDownExternal = true;
            SetInputAxis(RawInputAxis); //Call the Raw IMPORTANT
        }

        /// <summary>Gets the movement from the World Coordinates</summary>
        /// <param name="move">World Direction Vector</param>
        public virtual void MoveWorld(Vector3 move)
        {
            //  if (MovementDone) return; //This was already called

            UsingMoveWithDirection = false;

            if (!UseSmoothVertical && move.z > 0) move.z = 1;                   //It will remove slowing Stick push when rotating and going Forward

            Move_Direction = t.TransformDirection(move).normalized;    //Convert from world to relative IMPORTANT
            SetMovementAxis(move);
        }

        public virtual void SetMovementAxis(Vector3 move)
        {
            MovementAxisRaw = move;
            MovementAxis = MovementAxisRaw;
            MovementDetected = MovementAxisRaw != Vector3.zero;
            MovementAxis.Scale(ActiveState.MovementAxisMult);
        }

        /// <summary>Gets the movement values from a Direction</summary>
        /// <param name="move">Direction Vector</param>
        public virtual void MoveFromDirection(Vector3 move)
        {
            if (LockMovement)
            {
                MovementAxis = Vector3.zero;
                return;
            }

            //??
            if (LockForwardMovement) move = Vector3.Project(move, MainCamera.forward);
            if (LockHorizontalMovement) move = Vector3.Project(move, MainCamera.right);


            //If the State use KeepForward then ignore when the movement is Zero. Use the last one
            if (ActiveState.KeepForwardMovement && move == Vector3.zero)
            {
                move = Move_Direction;
            }


            UsingMoveWithDirection = true;

            if (move.magnitude > 1f) move.Normalize();

            var UpDown = FreeMovement ? move.y : 0; //Ignore UP Down Axis when the Animal is not on Free movement

            if (!FreeMovement)
                move = Quaternion.FromToRotation(UpVector, SlopeNormal) * move;    //Rotate with the ground Surface Normal. CORRECT!

            Move_Direction = move;

            if (debugGizmos)
            {
                MDebug.Draw_Arrow(Position + GizmoDeltaPos, Move_Direction.normalized * 2, Color.yellow);


                MDebug.DrawRay(Position, SlopeNormal, Color.black); //REAL TRANSFORM POS
                MDebug.DrawRay(Position + GizmoDeltaPos, SlopeNormal, Color.black);
            }
            move = t.InverseTransformDirection(move);               //Convert the move Input from world to Local  

            float turnAmount = Mathf.Atan2(move.x, move.z);                 //Convert it to Radians
            float forwardAmount = move.z < 0 ? 0 : move.z;

            if (!Strafe)
            {
                DeltaAngle = MovementDetected ? turnAmount * Mathf.Rad2Deg : 0;

                if (float.IsNaN(DeltaAngle)) DeltaAngle = 0f; //Remove the NAN Bug //MWC: Mathf.Approximately(x, NaN) is always false — the old check never worked

                if (Mathf.Abs(Vector3.Dot(Move_Direction, UpVector)) == 1)//Remove turn Mount when its going UP/Down
                {
                    turnAmount = 0;
                    DeltaAngle = 0f;
                }

                inTurnLimit = Mathf.Abs(DeltaAngle) > (TurnLimit); //Calculate if the Animal is in a turn Limit

                if (!UseRawInput && inTurnLimit) //Meaning is using Move from AI and not from Input
                {
                    forwardAmount = 0; //This will make the animal turn in place if it circling around a target
                }
                else if (!UseSmoothVertical) //It will remove slowing Stick push when rotating and going Forward
                {
                    forwardAmount = Mathf.Abs(move.z);
                    forwardAmount = forwardAmount > 0 ? 1 : forwardAmount;
                    inTurnLimit = false;
                }
                else
                {
                    if (!inTurnLimit || VerticalSmooth > 1) //If the animal is not moving allow him to turn around 
                    {
                        forwardAmount = Mathf.Clamp01(Move_Direction.magnitude);
                    }
                    else
                    {

                        if (MovementDetected && UpDownSmooth != 0)
                        {
                            forwardAmount = Mathf.Clamp01(Move_Direction.magnitude);
                        }
                    }
                }

                if (Rotate_at_Direction) forwardAmount = 0;

                var MovAxis = new Vector3(turnAmount, UpDown, forwardAmount);
                SetMovementAxis(MovAxis);
            }
            else
            {
                StrafeWithDirection(UpDown);
            }
        }

        private bool inTurnLimit;
        private void StrafeWithDirection(float UpDown)
        {
            var Dir = Vector3.ProjectOnPlane(Aimer.RawAimDirectionNoRayCast.normalized, UpVector).normalized;
            var M = Move_Direction;

            var cross = Quaternion.AngleAxis(90, UpVector) * Aimer.RawAimDirectionNoRayCast;
            cross = Vector3.ProjectOnPlane(cross, UpVector).normalized;

            var turnAmount = Vector3.Dot(cross, M);
            var forwardAmount = Vector3.Dot(Dir, M);

            if (debugGizmos)
            {
                MDebug.DrawRay(Position + GizmoDeltaPos, Dir * 2, Color.cyan);
                MDebug.DrawRay(Position + GizmoDeltaPos, cross * 5, Color.green);
            }

            DeltaAngle = Mathf.MoveTowards(DeltaAngle, 0f, DeltaTime * 2);

            // Normalizing made every nonzero stick deflection a full-speed strafe.
            var MovAxis = Vector3.ClampMagnitude(new Vector3(turnAmount, UpDown, forwardAmount), 1);


            SetMovementAxis(MovAxis);
        }

        /// <summary>Gets the movement from a Direction but it wont fo forward it will only rotate in place</summary>
        public virtual void RotateAtDirection(Vector3 direction)
        {
            if (IsPlayingMode && !ActiveMode.AllowRotation) return;
            RawRotateDirAxis = direction; // Store the last current use of the Input
            UseRawInput = false;
            Rotate_at_Direction = true;
        }

        #endregion

        private void Strafing_Rotation()
        {
            if (Strafe && Aimer)
            {
                Vector3 HorizontalDir = Vector3.ProjectOnPlane(Aimer.RawAimDirectionNoRayCast, UpVector).normalized;
                Vector3 ForwardDir = Vector3.ProjectOnPlane(Forward, UpVector).normalized;
                var HorizontalAngle_Raw = Vector3.SignedAngle(ForwardDir, HorizontalDir, UpVector); //Get the Normalized value for the look direction

                if (m_StrafeLerp > 0)
                {
                    StrafeDeltaValue = Mathf.Lerp(StrafeDeltaValue,
                    MovementDetected ? ActiveState.MovementStrafe * ActiveStance.MovementStrafe : ActiveState.IdleStrafe * ActiveStance.IdleStrafe,
                    DampingFactor(m_StrafeLerp, DeltaTime));
                    // Authored strafe weights were a per-frame fraction. Preserve their
                    // 60 Hz feel without doubling the turn speed on a 120 Hz display.
                    float turn = 1f - Mathf.Pow(1f - Mathf.Clamp01(StrafeDeltaValue), DeltaTime * 60f);
                    Rotation *= Quaternion.Euler(0, HorizontalAngle_Raw * turn, 0);
                }
                else
                {
                    Rotation *= Quaternion.Euler(0, HorizontalAngle_Raw, 0);
                }
            }
            else
            {
                StrafeDeltaValue = 0; //Reset Strafe Delta value
            }
        }



        /// <summary> Do the Gravity Logic </summary>
        public void GravityLogic()
        {
            if (UseGravity && !Mode_IgnoreGravity && !Grounded)
            {
                //MWC: integrate with the accumulated elapsed seconds instead of (current frame delta × frame count).
                //Identical result in Fixed mode; frame-rate independent in Normal mode (Fall speed was inconsistent there
                //because any frame-time fluctuation scaled the whole accumulated velocity quadratically).
                GravityStoredVelocity = StoredGravityVelocityTime(GravityElapsedTime);

                if (ClampGravitySpeed > 0)
                {
                    var ExternalForceGravity = Vector3.Project(CurrentExternalForce, Gravity); //Make sure to take into account the external force
                    var GoingDownMovement = Vector3.Project(DeltaPos, Gravity); //

                    var GravityAndExternal = GoingDownMovement + ExternalForceGravity;


                    bool GoingDown = Vector3.Dot(GravityAndExternal, Gravity) > 0;

                    if (GoingDown && (ClampGravitySpeed * ClampGravitySpeed) < GravityAndExternal.sqrMagnitude)
                    {
                        GravityElapsedTime -= DeltaTime; //Clamp the Gravity Speed //MWC: time-based (was GravityTime--)
                    }
                }

                AdditivePosition += (DeltaTime * GravityExtraPower * GravityStoredVelocity) //Add Gravity if is in use
                                     + GravityOffset * DeltaTime;            //Add Gravity Offset JUMP if is in use

                GravityElapsedTime += DeltaTime; //MWC: time-based accumulation (was GravityTime++)
            }
        }


        //MWC: seconds-based stored gravity velocity — single source of truth for the gravity math
        public float LinearGravityAcceleration { get; set; }
        public bool UseInPlaceStrafe { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Vector3 StoredGravityVelocityTime(float elapsedTime)
        {
            if (LinearGravityAcceleration > 0)
                return elapsedTime * LinearGravityAcceleration * ScaleFactor * Gravity;
            return (elapsedTime * elapsedTime * 0.5f) * GravityPower * ScaleFactor * TimeMultiplier * Gravity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Vector3 StoredGravityVelocity(float GravityTime)
        {
            //MWC: legacy frame-count API (JumpBasic trajectory preview, etc.) — counts are converted with the FIXED delta.
            //It previously used the current frame delta, which made the value frame-rate dependent in Normal mode.
            //Identical result in Fixed mode.
            return StoredGravityVelocityTime(Time.fixedDeltaTime * GravityTime);
        }
    }
}
