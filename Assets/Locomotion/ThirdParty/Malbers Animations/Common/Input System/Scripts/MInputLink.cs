// Last Change: 10/8/2026 10:30am
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;

using UnityEngine;
using UnityEngine.Events;

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

#if ENABLE_INPUT_SYSTEM
using static UnityEngine.InputSystem.PlayerInputManager;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.Users;
using UnityEngine.InputSystem.Controls;
#endif

using UnityEngine.EventSystems;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace MalbersAnimations.InputSystem
{
    [AddComponentMenu("Input/MInput Link [New Input System]"), DisallowMultipleComponent]
    public partial class MInputLink : MonoBehaviour, IInputSource, IInputPhysicalID //MWC — [Fast Weapons] physical control identity
    {
        /// <summary> Identity of the physical control behind an input name: the Input Action several rows can share </summary>
        public string PhysicalInputID(string inputName)
        {
            if (m_MapButtons == null || string.IsNullOrEmpty(inputName)) return null;

            var key = inputName.Trim();

            foreach (var map in m_MapButtons)
            {
                if (map?.buttons == null) continue;

                foreach (var button in map.buttons)
                {
                    if (button == null || button.Name != key) continue;

                    //MWC — the runtime Action when it is resolved, the serialized reference before that
                    var action = button.Action != null ? button.Action
                               : (button.reference != null ? button.reference.action : null);

                    if (action != null) return action.id.ToString();
                }
            }
            return null;
        }

#if ENABLE_INPUT_SYSTEM
        ///// <summary> Current Active Action Map Index</summary>
        public static List<MInputLink> MInputLinks { get; protected set; }

        public readonly string versionInput = "Malbers New Unity Input System.";
        public Action<Vector3> OnMoveAxis { get; set; } = delegate { };

        [RequiredField]
        public PlayerInput playerInput;

        [Tooltip("Find the Reference from a Transform Hook if the Player Input is empty")]
        public TransformReference playerInputReference = new();

        [Tooltip("If the Input is disabled, clear the Player Input Reference (Useful for Mounts like the Horse)")]
        public bool clearPlayerInput = false;

        /// <summary> Check for a moveable character</summary>
        protected ICharacterMove character;
        public InputActionAsset InputActions;

        [Tooltip("Current Active Map to Activate on Enable\n if there are more Input links on the scene the last Input Link will set its Action Map as Active")]
        [SerializeField] internal int ActiveActionMapIndex;
        [HideInInspector][SerializeField] private int ShowMapIndex;


        public bool debug;
        /// <summary> Current Active Action Map Index</summary>
        public int ActiveMapIndex => ActiveActionMapIndex - 1;

        /// <summary> Current Active Action Map name</summary>
        public string ActiveMap { get; set; }

        /// <summary> Current Active Malbers Button Map (This have the List of buttons)</summary>
        // MWC — runtime cache only (set on enable, nulled on disable). Must NOT be serialized: MInputActionMap is a
        // plain [Serializable] class, so serializing this duplicated a whole button map into the component. Same
        // class of bug as Stats.PinnedStat.
        [System.NonSerialized] public MInputActionMap ActiveMActionMap;
        public MInputActionMap DefaultMap { get; protected set; }

        /// <summary>  Used to check if the Link is already connected to the Input Actions  </summary>
        public bool Connected { get; protected set; }


        /// <summary>Current Stored movement Axis</summary>
        public Vector3 MoveAxis { get; set; }

        // public void SetMoveAxis(Vector3 move) => MoveAxis = move;
        public bool MoveCharacter { set; get; }

        public Vector2Event OnMoveEvent;

        /// <summary> List of Malbers Input Row Buttons</summary>
        public List<MInputActionMap> m_MapButtons;


        [SerializeField, HideInInspector] private int Editor_Tabs1;

        public bool showInputEvents = false;
        public IntEvent OnInputEnabled = new();
        public IntEvent OnInputDisabled = new();

        public StringEvent OnActionMapChanged = new();
        public StringEvent CurrentControlScheme = new();

        public PlayerJoinedEvent OnControlsChanged = new();
        public PlayerJoinedEvent OnDeviceLost = new();
        public PlayerJoinedEvent OnDeviceRegained = new();


        [Tooltip("All Inputs will be ignored on Time.Scale = 0")]
        public BoolReference IgnoreOnPause = new(true);

        private void OnUserChange(InputUser user, InputUserChange change, InputDevice device)
        {
            if (user.index == playerInput.playerIndex)
            {
#if UNITY_EDITOR
                if (debug)
                    Debug.Log($" <color=cyan><B>{playerInput.name}</B> </color> - On User Changed Index: [{user.index}]");
#endif
                //??

                switch (change)
                {
                    case InputUserChange.DeviceLost:
                        //Do stuff when the Device is lost
                        break;
                    case InputUserChange.DeviceRegained:
                        //Do stuff when the Device is Regained
                        break;
                    case InputUserChange.ControlsChanged:
                        /*
                        CurrentControlScheme.Invoke(user.controlScheme.Value.name);
                        //   CurrentControlSchemeIndex = InputActions.FindControlSchemeIndex(user.controlScheme.Value.name);
                        */
                        if (user.controlScheme != null)
                            CurrentControlScheme.Invoke(user.controlScheme.Value.name);

                        break;
                }
            }
        }


        private void ControlsChanged(PlayerInput input)
        {
#if UNITY_EDITOR
            if (debug)
                Debug.Log($" <color=cyan><B>{playerInput.name}</B> </color> - Control Changed: <B>[{input.currentControlScheme}]</B>");
#endif
            OnControlsChanged.Invoke(input);
        }

        private void DeviceLost(PlayerInput input) => OnDeviceLost.Invoke(input);

        private void DeviceRegained(PlayerInput input) => OnDeviceRegained.Invoke(input);

        private void ValidateInputActions()
        {
            if (playerInput == null) return; //Do nothing if there's no Player Inp
            if (InputActions == null) return;

            //The Map Button is different or empty
            if (m_MapButtons == null || m_MapButtons.Count == 0 && InputActions != null)
            {
                m_MapButtons = new List<MInputActionMap>();

                for (int i = 0; i < InputActions.actionMaps.Count; i++)
                {
                    m_MapButtons.Add(new MInputActionMap(InputActions.actionMaps[i], i));
                }
                MTools.SetDirty(this);

                // Debug.Log("Validate Input Actions");
            }

            // UpdateActionMaps();

            if (!Application.isPlaying)
            {
                //Set the Active BtMap as the Default one IMPORTANT
                if (ActiveMapIndex >= 0 && m_MapButtons.Count < ActiveMapIndex)
                    ActiveMActionMap = m_MapButtons[ActiveMapIndex];
            }
        }


        protected virtual void Awake()
        {
            character = GetComponent<ICharacterMove>();     //Get the Animal Controller

            //  DefaultMap = m_MapButtons[ActiveMapIndex];      //Set the Active BtMap as the Default one IMPORTANT
        }

        /// <summary>Remove the Player Input Component</summary>
        public virtual void ClearPlayerInput() => playerInput = null;

        public void PlayerInput(IInputSource player)
        {
            if (player != null)
            {
                playerInput = player.transform.GetComponent<PlayerInput>(); //Get the Player Input from the character
            }

            CheckActivePlayerInput();
        }

        private void CheckActivePlayerInput()
        {
            if (playerInput != null && (!playerInput.enabled || !playerInput.gameObject.activeInHierarchy))
            {
                Debug.LogWarning("Player Input is Disabled or Disabled in Hierarchy. Please use a Valid Player Input. Disabling MInputLink", playerInput);
                enabled = false;
            }
        }

        /// <summary>Enable Disable the Input Script</summary>
        public virtual void Enable(bool val) => enabled = val;



        // Start is called before the first frame update
        private void OnEnable()
        {
            MInputLinks ??= new();
            MInputLinks.Add(this);                                              //Save the the Animal on the current List


            ActiveMActionMap = DefaultMap;    //Set the Active BtMap as the Default one IMPORTANT

            FindPlayerInput();

            //MWC — nothing on this component can work without a Map Buttons list. Bail out with a clear message
            //instead of throwing on the indexer below and on every foreach further down.
            if (m_MapButtons == null || m_MapButtons.Count == 0)
            {
                Debug.LogWarning($"[{name}]. MInputLink has no Action Maps configured. MInputLink component disabled.\n" +
                    $"<B><color=orange>Assign an [Input Actions] asset and press [Find Buttons]</color><B>", this);
                enabled = false;
                return;
            }

            if (playerInput != null && playerInput.enabled)
            {
                //Reset Player Input (BUG WHEN RE-ENABLING THE INPUT LINK)
                playerInput.enabled = false;
                playerInput.enabled = true;

                //MWC — ActiveActionMapIndex defaults to 0 ('<None>'), which makes ActiveMapIndex -1 and threw here on
                //any component added by hand instead of through Reset(). Not fatal: UpdateActiveMap() still resolves
                //the active map from the Player Input, so only DefaultMap (used by GetInput) is left unset.
                if (ActiveMapIndex >= 0 && ActiveMapIndex < m_MapButtons.Count)
                {
                    DefaultMap = m_MapButtons[ActiveMapIndex];      //Set the Active BtMap as the Default one IMPORTANT
                }
                else
                {
                    Debug.LogWarning($"[{name}]. [Connect Maps] is set to <None> or to a missing Action Map. " +
                        $"Please select a valid Action Map on the MInputLink component.", this);
                }

                ReplaceCloneRefAssets(); //Replace the Clone Reference Assets (NEW INPUT SYSTEM 1.14**))

                ConnectActionMap();
            }
            else
            {
                Debug.Log($"[{name}]. Player Input not found. MInputLink component disabled. Make Sure you add a Player Input to the scene.\n" +
                    $"<B><color=orange>Right Click on Hierarchy empty space: Add/Malbers Animations/Add Player Input</color><B>", this);

                //Disconnect because there's no Player Input
                enabled = false;
                return;
            }

            InputUser.onChange += OnUserChange;

            if (playerInput != null)
            {
                playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents; //   IMPORTANT IT NEEDS TO BE INVOKE CSHARP
                playerInput.onControlsChanged += ControlsChanged;
                playerInput.onDeviceLost += DeviceLost;
                playerInput.onDeviceRegained += DeviceRegained;
                ControlsChanged(playerInput); //Do the first call
            }

            UpdateActiveMap();
            OnInputEnabled.Invoke(playerInput.playerIndex);

            //verify every Input to their correct expected control
            foreach (var map in m_MapButtons)
            {
                if (map == null || map.buttons == null) continue; //MWC — NULL CHECK FIRST

                foreach (var button in map.buttons)
                {
                    if (button == null || button.Action == null || button.reference == null) continue; //Make sure is not null

                    //Debug.Log($"BUTTON: {button.action.name}, Expected: {button.action.expectedControlType}");
                    if (button.interaction != MInputInteraction.Vector2 && button.Action.expectedControlType == "Vector2")
                    {
                        Debug.LogWarning($"Button <{button.name}> has an action control Type of [{button.Action.expectedControlType}]. 'Vector2' is expected. Please change the Interaction Type to Vector2. Disabling Button");
                        button.Active = false;
                    }
                    //else if (button.interaction == MInputInteraction.Float && button.action.expectedControlType != "")
                    //{
                    //    Debug.LogWarning($"Button <{button.name}> has an action control Type of [{button.action.expectedControlType}]. 'Float' is expected. Please change the Interaction Type to Float. Disabling Button");
                    //    button.Active = false;
                    //}
                }
            }

            MoveAxis = Vector3.zero;

            //Invoke the first time the current control scheme

            if (playerInput.user.valid && playerInput.user.controlScheme.HasValue)
                CurrentControlScheme.Invoke(playerInput.user.controlScheme.Value.name);
        }

        private void FindPlayerInput()
        {
            if (playerInput == null)
            { playerInput = this.FindComponent<PlayerInput>(); } //Find Player Input inside the hierarchy (Rider Find)

            if (playerInput == null && playerInputReference.Value != null)
            { playerInput = playerInputReference.Value.FindComponent<PlayerInput>(); } //Find Player Input inside the hierarchy (Rider Find)

            //Second Try to find the Player Input
            if (playerInput == null)
            {
                var allLinks = this.GetComponentsInChildren<MInputLink>();

                foreach (var link in allLinks)
                {
                    if (link.playerInput != null)
                    {
                        playerInput = link.playerInput;
                        break;
                    }
                }
            }

            CheckActivePlayerInput();

        }

        private void ReplaceCloneRefAssets()
        {
            //Connect all MAPS!!! no matter which one is the Active one!! 
            foreach (var map in m_MapButtons)
            {
                if (map != null) //NULL CHECK FIRST!!
                {
                    var PlayerActionMap = playerInput.actions.FindActionMap(map.ActionMap.name); //Replace the Action Map Reference

                    if (PlayerActionMap != null)
                    {
                        map.ActionMap = PlayerActionMap; //Replace the Action Map Reference with the clone from the Player Input

                        //Replace the Move Action with the clone from the Player Input
                        if (map.Move != null)
                            map.MoveAction = PlayerActionMap.FindAction(map.Move.action.id);

                        //Replace the Up Down Action with the clone from the Player Input
                        if (map.UpDown != null)
                            map.UpDownAction = PlayerActionMap.FindAction(map.UpDown.action.id);



                        //Replace the Buttons Actions with the clone from the Player Input
                        foreach (var btn in map.buttons)
                        {
                            if (btn.reference != null)
                                btn.Action = PlayerActionMap.FindAction(btn.reference.action.id);
                        }
                    }
                }
            }
        }

        private void OnDisable()
        {
            //Remove all Links from the list
            MInputLinks.Remove(this);
            //I NEED TO CHECK IF ANY OTHER MLINK IS USING THE SAME ACTION MAP
            DisconnectActionMap();

            MoveAxis = Vector3.zero;
            InputUser.onChange -= OnUserChange; //Check User changes

            if (playerInput != null)
            {
                playerInput.onControlsChanged -= ControlsChanged;
                //MWC — these were '+=' (copy/paste): every Enable/Disable cycle added another subscription
                //instead of removing it, leaking delegates onto the shared PlayerInput and multiplying the callbacks.
                playerInput.onDeviceLost -= DeviceLost;
                playerInput.onDeviceRegained -= DeviceRegained;
                OnInputDisabled.Invoke(playerInput.playerIndex);
            }
            ActiveMActionMap = null;

            if (clearPlayerInput) playerInput = null; //Clear the Player Input Reference
        }

        /// <summary> Activate the Current Action Map! </summary>
        public virtual void ConnectActionMap()
        {
            if (playerInput == null || !playerInput.enabled) return;

            if (!Connected)
            {
                UpdateActiveMap();

                //Connect all MAPS!!! no matter which one is the Active one!! 
                foreach (var map in m_MapButtons)
                {
                    if (map != null) //NULL CHECK FIRST!!
                    {
                        map.InvalidateButtonLookup(); //MWC — rebuild the name lookup from the current buttons list

                        ConnectMove(map);
                        ConnectUpDown(map);
                        ConnectButtons(map);
                    }
                }
                Connected = true;//Update the Connection to true; This avoid to connect twice, which is bad!

                //Debug.Log("CONNECT INPTUTSSS");
            }
        }

        /// <summary> Update the Active Map to the current one  </summary>
        private void UpdateActiveMap()
        {
            if (playerInput.currentActionMap == null) return;

            if (ActiveMActionMap == null || ActiveMActionMap.ActionMap == null
                || ActiveMActionMap.ActionMap.id != playerInput.currentActionMap.id)
            {
                var currentID = playerInput.currentActionMap.id;

                //MWC — 'x.ActionMap' can be null on a half-configured entry, so null check inside the search too.
                var found = m_MapButtons.Find(x => x != null && x.ActionMap != null && x.ActionMap.id == currentID);

                //MWC — Find() returns null when the Player Input is using a map this component does not know about
                //(mismatched Input Action asset, or a map added after the buttons were configured).
                //Previously the next lines dereferenced it straight away and threw.
                if (found == null)
                {
                    Debugging($"Action Map <B>[{playerInput.currentActionMap.name}]</B> is not on the Map Buttons list. " +
                        $"Make sure the [Input Actions] asset matches the one on the Player Input.");
                    return;
                }

                ActiveMActionMap = found;

                OnActionMapChanged.Invoke(ActiveMActionMap.ActionMap.name); //Invoke the Active Action Map

                ActiveMActionMap.ActionMap.Enable(); //Enable the Action Map
                //Debug.Log("UpdateActiveMap");
            }
        }

        public virtual void SwitchActionMap(string map)
        {
            if (string.IsNullOrEmpty(map)) return;

            //Switch the Map in the Player Input if is not already switched
            if (playerInput != null && playerInput.currentActionMap.name != map)
            {
                //   DisconnectActionMap(); //Disconnect the current Action Map
                playerInput.SwitchCurrentActionMap(map);
                PlayerMap = playerInput.currentActionMap;
                playerInput.defaultActionMap = PlayerMap.id.ToString();
                OnActionMapChanged.Invoke(map);
                // UpdateActiveMap();
                // ConnectActionMap();
                Debugging($"Action Map Switched <B>[{map}]</B>");
            }
        }

        public virtual void DisconnectActionMap()
        {
            if (/*ActiveMActionMap != null && */Connected)
            {
                foreach (var map in m_MapButtons)
                {
                    if (map == null) continue; //MWC — NULL CHECK FIRST, same as ConnectActionMap()

                    DisconnectButtons(map);
                    DisconnectMove(map);
                    DisconnectUpDown(map);
                }
                Connected = false; //Update the Connection to false;

                Debugging("DisconnectActionMap");
            }
        }


        #region Connect/Disconnect Buttons
        private void ConnectButtons(MInputActionMap map)
        {
            foreach (var btn in map.buttons)
            {
                if (btn.reference != null) //Check that there's a valid input
                {
                    btn.Action = ResolveForPlayer(btn.Action, playerInput.playerIndex);
                    ConnectAction(btn.Action, btn);
                    btn.MCoroutine = this;                      //Set the Monobehaviour so I can use Coroutines
                                                                // Debug.Log($"Connected! {map.Name} btn {btn.Name}");
                }
            }
        }

        private void DisconnectButtons(MInputActionMap map)
        {
            foreach (var btn in map.buttons)
            {
                if (btn.Action != null) //Check that there's a valid input
                {
                    DisconnectAction(btn.Action, btn);
                    btn.MCoroutine = null; //remove the Monobehaviour for coroutines

                    if (btn.ResetOnDisable.Value)
                        btn.OnInputChanged.Invoke(btn.InputValue = false);  //Sent false to all Input listeners 
                }
            }
        }

        //MWC — name lookup instead of List.Find + closure per map. Also matches *every* button with that name
        //(the old Find stopped at the first one in each map), and null checks the map entries.
        public void ConnectInput(string name, UnityAction<bool> action)
        {
            if (m_MapButtons == null || string.IsNullOrEmpty(name)) return;

            var key = name.Trim();

            foreach (var maps in m_MapButtons)
            {
                var found = maps?.GetButtons(key);

                if (found != null)
                    foreach (var button in found) button.OnInputChanged.AddListener(action);
            }
        }

        //MWC — see ConnectInput
        public void DisconnectInput(string name, UnityAction<bool> action)
        {
            if (m_MapButtons == null || string.IsNullOrEmpty(name)) return;

            var key = name.Trim();

            foreach (var maps in m_MapButtons)
            {
                var found = maps?.GetButtons(key);

                if (found != null)
                    foreach (var button in found) button.OnInputChanged.RemoveListener(action);
            }
        }

        public void ConnectAction(InputAction action, MInputAction btn)
        {
            if (action == null) return;
            action.started += btn.TranslateInput;
            action.performed += btn.TranslateInput;
            action.canceled += btn.TranslateInput;
        }

        public void DisconnectAction(InputAction action, MInputAction btn)
        {
            if (action == null) return;
            action.started -= btn.TranslateInput;
            action.performed -= btn.TranslateInput;
            action.canceled -= btn.TranslateInput;
        }
        #endregion

        #region Connect/Disconnect movement

        /// <summary> Connects the Move Action from the character  </summary>
        private void ConnectMove(MInputActionMap map)
        {
            if (map.Move != null)
            {
                //Connect only for player Index!!!
                map.MoveAction = ResolveForPlayer(map.MoveAction, playerInput.playerIndex);

                if (map.MoveAction != null)
                {
                    // map.MoveAction.started += OnMove;
                    map.MoveAction.performed += OnMove;
                    map.MoveAction.canceled += OnMove;
                }
            }
        }
        private void DisconnectMove(MInputActionMap map)
        {
            //MWC — check the resolved action, not the reference asset. ResolveForPlayer() can return null,
            //so 'Move != null' did not guarantee 'MoveAction != null' and this threw on disable.
            if (map.MoveAction != null)
            {
                // map.MoveAction.started -= OnMove;
                map.MoveAction.performed -= OnMove;
                map.MoveAction.canceled -= OnMove;
            }

            character?.Move(Vector3.zero);       //When the Input is Disable make sure the character/animal is not moving.
        }

        /// <summary> Connects the Up Action to the character  </summary>
        private void ConnectUpDown(MInputActionMap map)
        {
            if (map.UpDown != null)
            {
                map.UpDownAction = ResolveForPlayer(map.UpDownAction, playerInput.playerIndex);

                if (map.UpDownAction != null)
                {
                    map.UpDownAction.performed += OnUpDown;
                    map.UpDownAction.canceled += OnUpDown;
                }
            }
        }

        private void DisconnectUpDown(MInputActionMap map)
        {
            //MWC — same as DisconnectMove: guard on the resolved action, not on the reference asset.
            if (map.UpDownAction != null)
            {
                // map.UpDownAction.started -= OnUpDown;
                map.UpDownAction.performed -= OnUpDown;
                map.UpDownAction.canceled -= OnUpDown;
            }

            //When the Input is Disable make sure the character/animal is not moving.
            character?.Move(Vector3.zero);
        }

        /// <summary>
        /// In a multi-player context, actions are associated with specific players
        /// This resolves the appropriate action reference for the specified player.
        /// 
        /// Because the resolution involves a search, we also cache the returned 
        /// action to make future resolutions faster.
        /// </summary>
        /// <param name="axis">Which input axis (0, 1, or 2)</param>
        /// <param name="action">Which action reference to resolve</param>
        /// <returns>The cached action for the player specified in PlayerIndex</returns>
        protected InputAction ResolveForPlayer(InputAction action, int PlayerIndex)
        {
            if (action == null) return null;

            //MWC — bail out when there are no paired InputUsers yet. Math.Clamp(x, 0, -1) throws
            //ArgumentException (min > max), it does not return a clamped value.
            if (PlayerIndex != -1 && InputUser.all.Count > 0)
            {
                PlayerIndex = Math.Clamp(PlayerIndex, 0, InputUser.all.Count - 1); //Make sure the Input Users does not exceed the list length
                action = GetFirstMatch(InputUser.all[PlayerIndex], action);
            }
            //if (AutoEnableInputs && actionRef != null && actionRef.action != null)
            //    actionRef.action.Enable();

            // Update enabled status
            if (action != null && action.enabled != action.enabled)
            {
                if (action.enabled)
                    action.Enable();
                else
                    action.Disable();
            }

            return action;

            // local function to wrap the lambda which otherwise causes a tiny gc
            static InputAction GetFirstMatch(in InputUser user, InputAction aRef)
            {
                foreach (var x in user.actions)
                {
                    if (x.id == aRef.id)
                        return x;
                }

                //  Debug.LogWarning($"Action Reference [{aRef.name}] Not Found. Make sure the Player is Using the Same Action MAP", this);
                return null;
            }
        }


        public void OnMove(InputAction.CallbackContext context)
        {
            if (ActiveMActionMap == null) return; //MWC — no resolved map means no MoveMult to read (see UpdateActiveMap)

            var move2D = context.ReadValue<Vector2>();

            Vector3 MoveAxis = this.MoveAxis;

            MoveAxis.x = move2D.x * ActiveMActionMap.MoveMult.x; //Catch the Horizontal Axis
            MoveAxis.z = move2D.y * ActiveMActionMap.MoveMult.z; //Catch the Forward Axis

            this.MoveAxis = MoveAxis;

            character?.SetInputAxis(this.MoveAxis);

            OnMoveAxis(MoveAxis);
            OnMoveEvent.Invoke(new Vector2(MoveAxis.x, MoveAxis.z));
        }

        public void OnUpDown(InputAction.CallbackContext context)
        {
            if (ActiveMActionMap == null) return; //MWC — no resolved map means no MoveMult to read (see UpdateActiveMap)

            if (context.valueType != typeof(float))
            {
                Debug.LogWarning("Up Down Input is not type float, Please set the correct type to the UpDown Input Action Reference");
                return;
            }

            var UpDown = context.ReadValue<float>();

            Vector3 MoveAxis = this.MoveAxis;
            MoveAxis.y = ActiveMActionMap.MoveMult.y * UpDown; //Catch the Up Down Axis
            this.MoveAxis = MoveAxis;

            character?.SetInputAxis(MoveAxis);

            OnMoveAxis(MoveAxis);
            OnMoveEvent.Invoke(new Vector2(MoveAxis.x, MoveAxis.z));

        }

        #endregion

        #region Player Input Code I can use  easier for me to do the Local MultiPlayer thingy (Not Implemented yet)

        /// <summary>  Player Input methods I can use too  
        /// IT SEEMS FOR MULTIPLE LOCAL PLAYERS THE ACTION ASSET NEEDS TO BE DUPLICATED.... Tricky guys!!!
        /// 
        /// </summary>


        //[NonSerialized] private Action<InputDevice, InputDeviceChange> m_DeviceChangeDelegate;
        //[NonSerialized] private bool m_OnDeviceChangeHooked;
        //[NonSerialized] private InputUser m_InputUser;
        //internal static int s_AllActivePlayersCount;
        //public static bool isSinglePlayer =>
        //  s_AllActivePlayersCount <= 1 &&
        //  (PlayerInputManager.instance == null || !PlayerInputManager.instance.joiningEnabled);


        //private void StartListeningForDeviceChanges()
        //{
        //    if (m_OnDeviceChangeHooked)
        //        return;
        //    if (m_DeviceChangeDelegate == null)
        //        m_DeviceChangeDelegate = OnDeviceChange;
        //    InputSystem.onDeviceChange += m_DeviceChangeDelegate;
        //    m_OnDeviceChangeHooked = true;
        //}

        //private void StopListeningForDeviceChanges()
        //{
        //    if (!m_OnDeviceChangeHooked)
        //        return;
        //    InputSystem.onDeviceChange -= m_DeviceChangeDelegate;
        //    m_OnDeviceChangeHooked = false;  
        //} 

        //private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        //{
        //    // If a device was added and we have no control schemes in the actions and we're in
        //    // single-player mode, pair the device to the player if it works with the bindings we have.
        //    if (change == InputDeviceChange.Added && isSinglePlayer &&
        //        InputActions != null && InputActions.controlSchemes.Count == 0 &&
        //        HaveBindingForDevice(device) && m_InputUser.valid)
        //    {
        //        InputUser.PerformPairingWithDevice(device, user: m_InputUser);
        //    }
        //}

        //private bool HaveBindingForDevice(InputDevice device)
        //{
        //    if (InputActions == null)
        //        return false;

        //    var actionMaps = InputActions.actionMaps;
        //    for (var i = 0; i < actionMaps.Count; ++i)
        //    {
        //        var actionMap = actionMaps[i];
        //        if (actionMap.IsUsableWithDevice(device))
        //            return true;
        //    }

        //    return false;
        //}
        #endregion
        private void Update()
        {
            if (playerInput == null || !playerInput.enabled)
            {
                Debug.Log("Player Input is Null or Disabled. Disabling MInputLink.", this);
                enabled = false;
                return;
            }

            if (IgnoreOnPause && Time.timeScale == 0) return;

            //Update player Map
            if (PlayerMap != playerInput.currentActionMap)
            {
                UpdateActiveMap();
                PlayerMap = playerInput.currentActionMap;
            }


            character?.SetInputAxis(MoveAxis);
            OnMoveEvent.Invoke(new Vector2(MoveAxis.x, MoveAxis.z));
        }

        private InputActionMap PlayerMap;

        //MWC — name lookup instead of List.Find + closure. DefaultMap can legitimately stay null when
        //[Connect Maps] is <None> (see OnEnable), so null check here too.
        public IInputAction GetInput(string name)
        {
            if (DefaultMap == null || string.IsNullOrEmpty(name)) return null;

            var found = DefaultMap.GetButtons(name.Trim());

            return found != null && found.Count > 0 ? found[0] : null;
        }

        #region Public Methods IInputSource Interface


        public virtual void SetMoveMultX(float value) => ActiveMActionMap.MoveMult.x = value;
        public virtual void SetMoveMultY(float value) => ActiveMActionMap.MoveMult.y = value;
        public virtual void SetMoveMultZ(float value) => ActiveMActionMap.MoveMult.z = value;


        /// <summary>Enable an Input Row</summary>
        public virtual void EnableInput(string name) => EnableInput(name, true);


        /// <summary> Disable an Input Row </summary>
        public virtual void DisableInput(string name) => EnableInput(name, false);


        /// <summary>Enable/Disable an Input Row</summary>
        public virtual void EnableInput(string input_name, bool value) => ApplyToInputs(input_name, value, true);


        /// <summary>Set a Value of an Input internally without calling the Events</summary>
        public virtual void SetInput(string input_name, bool value) => ApplyToInputs(input_name, value, false);


        /// <summary>Shared lookup for EnableInput/SetInput. Accepts one name or a comma separated list</summary>
        //MWC — one code path for both, backed by the map's name lookup instead of FindAll + closure + Trim per
        //button. 'setActive' selects which value is written: the Active property, or the raw InputValue.
        private void ApplyToInputs(string input_name, bool value, bool setActive)
        {
            if (ActiveMActionMap == null || string.IsNullOrEmpty(input_name)) return;

            //MWC — single name is by far the common case (States/Modes pass one): skip Split() entirely.
            //string.Trim() returns the same instance when there is nothing to trim, so this does not allocate.
            if (input_name.IndexOf(',') < 0)
            {
                Apply(ActiveMActionMap.GetButtons(input_name.Trim()));
                return;
            }

            foreach (var n in SplitInputNames(input_name)) Apply(ActiveMActionMap.GetButtons(n));

            void Apply(List<MInputAction> found)
            {
                if (found == null) return;

                foreach (var item in found)
                {
                    //MWC — 'Active' property, not 'active.Value': the property is what enables/disables the
                    //underlying InputAction and fires OnInputEnabled/OnInputDisabled.
                    if (setActive) item.Active = value;
                    else item.InputValue = value;
                }
            }
        }

        //MWC — cached separator so Split() does not allocate a new char[] on every call
        private static readonly char[] InputNameSeparator = { ',' };

        /// <summary>Splits a comma separated list ("Jump, Attack") into trimmed names</summary>
        //MWC — every entry is trimmed now. EnableInput used to trim only the whole string before splitting, so in
        //"Jump, Attack" the second entry kept its leading space and never matched a button.
        private static string[] SplitInputNames(string input_name)
        {
            var names = input_name.Split(InputNameSeparator, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < names.Length; i++) names[i] = names[i].Trim();

            return names;
        }

        public void ResetInput(string name) => SetInput(name, false);

        internal void ResetButtonMap()
        {
            m_MapButtons = null;
            ActiveActionMapIndex = 0;
        }

        public virtual void PlayerInput_Set(GameObject gameObject) => playerInput = gameObject.GetComponent<PlayerInput>();

        public virtual void PlayerInput_Set(Component component) => playerInput = component.GetComponent<PlayerInput>();

        public virtual void PlayerInput_Set(TransformVar var) => playerInput = var.Value.GetComponent<PlayerInput>();

        public virtual void PlayerInput_Set(GameObjectVar var) => playerInput = var.Value.GetComponent<PlayerInput>();

        public virtual void PlayerInput_Set(PlayerInput player) => playerInput = player;
        #endregion

        /// <summary>Editor only console log for this Input Link, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debugging(string val)
        {
#if UNITY_EDITOR
            if (debug && playerInput) MDebug.Log($"<color=cyan><B>{playerInput.name}</B> </color> - {val}");
#endif
        }



#if UNITY_EDITOR
        private void CheckActionMaps()
        {
            if (InputActions != null)
            {
                m_MapButtons = new(); //Make sure is not null

                //Check when a new Input Map has been added
                if (m_MapButtons.Count < InputActions.actionMaps.Count)
                {
                    for (int i = m_MapButtons.Count; i < InputActions.actionMaps.Count; i++)
                    {
                        m_MapButtons.Add(new MInputActionMap(InputActions.actionMaps[i], i) { MoveMult = new(Vector3.one) });
                    }
                }
                EditorUtility.SetDirty(this);
            }
        }


        private void Reset()
        {
            playerInputReference.UseConstant = false;
            playerInputReference.Variable = MTools.GetInstance<TransformVar>("Player Input");

            var inputActions = MTools.GetInstance<InputActionAsset>("Malbers Inputs");
            InputActions = inputActions;

            ValidateInputActions();

            SetDevicesEvents();

            CheckActionMaps();

            ActiveActionMapIndex = 1;
            FindButtons();

            var findPlayerInput = GameObject.FindAnyObjectByType<PlayerInput>();

            if (findPlayerInput != null)
            {
                playerInput = findPlayerInput;
                playerInput.actions = inputActions;
                playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
                Debug.Log("Player Input Found on the Scene and assigned to the MInputLink. Input Actions set to [Malbers Input]");
            }
            else
            {
                var PlayerInputPath = "Assets/Malbers Animations/Common/Prefabs/Game Logic/Player Input.prefab";
                var PI = AssetDatabase.LoadAssetAtPath(PlayerInputPath, typeof(GameObject)) as GameObject;
                PrefabUtility.InstantiatePrefab(PI);
                Debug.Log("Player Input Prefab added to the scene. (Unity6 now requires that just 1 Player Input component needs to be in the scene for Single player games)");
            }
        }

        [ContextMenu("Set Device Sprint Events")]
        private void SetDevicesEvents()
        {
            var currentDevice = MTools.GetInstance<StringVar>("Current Device");
            if (currentDevice != null)
            {
                UnityEditor.Events.UnityEventTools.AddPersistentListener(CurrentControlScheme, currentDevice.SetValue);
            }

            var ActionMapChanged = MTools.GetInstance<StringVar>("ActionMap Changed");
            if (ActionMapChanged != null)
            {
                UnityEditor.Events.UnityEventTools.AddPersistentListener(OnActionMapChanged, ActionMapChanged.SetValue);
            }

            MTools.SetDirty(this);
        }

        private void OnValidate()
        {
            //MWC — buttons may have been renamed/added/removed in the inspector: drop the cached name lookups
            if (m_MapButtons != null)
                foreach (var map in m_MapButtons) map?.InvalidateButtonLookup();

            ValidateInputActions();

            //IMPORTANT IT NEEDS TO BE INVOKE CSHARP
            if (playerInput != null && playerInput.notificationBehavior != PlayerNotifications.InvokeCSharpEvents)
            {
                playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
                MTools.SetDirty(playerInput);
            }
        }

        public virtual void FindButtons()
        {
            if (InputActions == null) { m_MapButtons = null; { Debug.Log("NO INPUT ACTION"); } return; }

            m_MapButtons ??= new(InputActions.actionMaps.Count);


            if (ActiveActionMapIndex - 1 < 0) return; //skip

            //Find All the Action Input References
            var AllActionRef = GetAllActionsFromAsset(InputActions);

            var ActiveActionMap = InputActions.actionMaps[ActiveActionMapIndex - 1];

            var buttonMap = m_MapButtons[ActiveActionMapIndex - 1];

            //Find 
            if (buttonMap.Move == null)// || string.Compare( CurrentButtonMap.Move.name,"Move") != 0)
                buttonMap.Move = GetActionReferenceFromAssets(AllActionRef, ActiveActionMap, "Move");
            if (buttonMap.UpDown == null)
                buttonMap.UpDown = GetActionReferenceFromAssets(AllActionRef, ActiveActionMap, "UpDown");


            //Find Missing Buttons first
            for (int i = buttonMap.buttons.Count - 1; i >= 0; i--)
            {
                var button = buttonMap.buttons[i];
                //meaning the button is empty
                if (button != null && button.reference == null)
                {
                    var actionRef = GetActionReferenceFromAssets(AllActionRef, ActiveActionMap, button.name);
                    button.reference = actionRef;
                }

                if (button.reference == null)
                {
                    //Remove button??
                    buttonMap.buttons.Remove(button);
                }
            }

            //Add new buttons
            foreach (var action in ActiveActionMap)
            {
                if (action.type == InputActionType.Button
                    && !buttonMap.buttons.Exists(x => x.reference != null && x.reference.action.id == action.id))
                {
                    var actionRef = GetActionReferenceFromAssets(AllActionRef, ActiveActionMap, action.name);

                    //Do not add it if the name already exist
                    if (buttonMap.buttons.Exists(x => x.name == action.name) == false)
                    {
                        buttonMap.buttons.Add(new MInputAction(action.name, actionRef, MInputInteraction.Press));
                    }
                }
            }

            buttonMap.InvalidateButtonLookup(); //MWC — buttons were added/removed above, drop the cached name lookup

            MTools.SetDirty(this); //Make it Dirty (Ctrl+z)
        }

        private static InputActionReference[] GetAllActionsFromAsset(InputActionAsset actions)
        {
            if (actions != null)
            {
                var path = AssetDatabase.GetAssetPath(actions);
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                return assets.Where(asset => asset is InputActionReference).Cast<InputActionReference>().OrderBy(x => x.name).ToArray();
            }
            return null;
        }

        private static InputActionReference GetActionReferenceFromAssets(InputActionReference[] actions, InputActionMap map, params string[] actionNames)
        {
            foreach (var actionName in actionNames)
            {
                foreach (var action in actions)
                {
                    if (action.action != null && action.action.actionMap == map)
                        if (string.Compare(action.action.name, actionName, StringComparison.InvariantCultureIgnoreCase) == 0)
                            return action;
                }
            }
            return null;
        }
#endif
#endif
    }

    public enum MInputInteraction { Press = 0, Down = 1, Up = 2, LongPress = 3, DoubleTap = 4, Toggle = 5, Float = 6, Vector2 = 7 }

    /// <summary>  Wrapper to separate the Unity System Action Maps</summary>
    [System.Serializable]
    public class MInputActionMap
    {
        public int Index;
        public string Name => ActionMap != null ? ActionMap.name : "<None>"; //MWC — ActionMap is null on an unconfigured entry
        [Tooltip("Action for Moving the Character\n(X:Horizontal; Y:Forward)")]
        public InputActionReference Move;
        [Tooltip("Action for Moving Up and Down the Character")]
        public InputActionReference UpDown;

        [Tooltip("Multiplier for the Move value (X: Horizontal, Y: UpDown, Z:Forward/Vertical")]
        public Vector3Reference MoveMult = new(Vector3.one);



        internal InputAction MoveAction;
        internal InputAction UpDownAction;
        public InputActionMap ActionMap;

        // public string name;
        public List<MInputAction> buttons;

        //MWC — name -> buttons lookup, built once instead of a linear List.Find/FindAll (plus a closure, plus a
        //Trim() per button) on every EnableInput/DisableInput/SetInput call. States and Modes hit those constantly.
        //MUST stay [NonSerialized]: this is a runtime cache, not data (same rule as Stats.PinnedStat).
        //Names are not unique (two rows can share one), so each entry holds a list.
        [System.NonSerialized] private Dictionary<string, List<MInputAction>> buttonsByName;

        /// <summary>Buttons matching an already trimmed name. Returns null when there is no match</summary>
        public List<MInputAction> GetButtons(string trimmedName)
        {
            buttonsByName ??= BuildButtonLookup();

            return buttonsByName.TryGetValue(trimmedName, out var found) ? found : null;
        }

        /// <summary>Drops the name lookup so it is rebuilt on the next query</summary>
        //MWC — call whenever the buttons list changes (inspector edits, Find Buttons, connect)
        public void InvalidateButtonLookup() => buttonsByName = null;

        private Dictionary<string, List<MInputAction>> BuildButtonLookup()
        {
            //MWC — fully qualified: 'StringComparer' is ambiguous here, MalbersAnimations.Scriptables has one too
            var lookup = new Dictionary<string, List<MInputAction>>(buttons?.Count ?? 0, System.StringComparer.Ordinal);

            if (buttons != null)
            {
                foreach (var button in buttons)
                {
                    if (button == null || string.IsNullOrEmpty(button.Name)) continue;

                    var key = button.Name.Trim(); //Trim once here instead of once per button per call

                    if (!lookup.TryGetValue(key, out var list))
                        lookup[key] = list = new List<MInputAction>(1);

                    list.Add(button);
                }
            }

            return lookup;
        }

        public MInputActionMap(InputActionMap map, int index)
        {

            Index = index;
            buttons = new List<MInputAction>();
            ActionMap = map;
            // Name = map.name;
        }
    }

    /// <summary> Input Class. Translate Unity Input System Callback to Malbers Input System -> Bool values </summary>
    [System.Serializable]
    public class MInputAction : IInputAction
    {
        public InputActionReference reference;
        public InputAction Action { get; set; }

        public BoolReference active = new(true);

        [Tooltip("If the Input Component gets disable the Input will be set to false and it will send false to all its listeners")]
        public BoolReference ResetOnDisable = new(true);


        [Tooltip("Input will not work on Time.Scale = 0")]
        public BoolReference ignoreOnPause = new();

        [Tooltip("Ignore Mouse, Touch and Pen presses that start over an interactable UI element (Button, Toggle, Slider, Scroll View...). " +
            "Clicking the UI will not trigger this Input. Keyboard and Gamepad are never blocked")]
        public BoolReference blockOverUI = new(true);

        /// <summary>Type of interaction the button has</summary>
        public MInputInteraction interaction = MInputInteraction.Press;


        public MonoBehaviour MCoroutine { get; set; }
        private IEnumerator C_Press;
        private IEnumerator C_LongPress;

        #region LONG PRESS and Double Tap
        public FloatReference DoubleTapTime = new(0.3f);                          //Double Tap Time

        [Tooltip("Time the Input Should be Pressed to be considered a Long Press")]
        public FloatReference LongPressTime = new(0.5f);

        [Tooltip("Delay before the Long Press is considered as a Long Press. If the Input Up is released within this time, Long press will be ignored")]
        public FloatReference LongPressDelay = new(0f);

        public FloatReference PressThreshold = new(0.5f);

        [Tooltip("If the Input Up is not released within this time, it will be considered as a failed input")]
        public FloatReference InputUpFailed = new(0.1f);
        public FloatReference Vector2Mult = new(1f);

        private bool FirstInputPress = false;
        private bool InputCompleted = false;

        private bool LongPressTempValue { get; set; }

        private float InputStartTime { get; set; }

        public bool debug;

        #endregion

        /// <summary>  Name of the Action  </summary>
        public string name = "InputName";
        public string Name => name;

        /// <summary> Enable Disable the Input Action </summary>
        public bool Active
        {
            get => active.Value;
            set
            {
                var changed = active.Value != value;

                active.Value = value;

                if (Application.isPlaying)
                {
                    //MWC — never Enable()/Disable() the underlying InputAction here. Several rows share one action
                    //(Mount/Dismount/CallMount all map to 'Interact'), so the last row toggled decided whether the
                    //physical action was alive for all of them: MRider restored Mount = on, then Dismount = off, and
                    //Interact stayed disabled. TranslateInput() already gates on 'Active', which is the per-row switch.
                    if (!value) ResetInputState(); //MWC — or the Input stays 'pressed' forever

                    //MWC — these are edge events: fire them only when the value actually changed. EnableInput() can
                    //be called repeatedly by States/Modes with the same value, which would otherwise spam listeners.
                    if (changed)
                    {
                        if (value) OnInputEnabled.Invoke();
                        else OnInputDisabled.Invoke();
                    }
                }
            }
        }

        /// <summary>Current Input Value</summary>
        private bool inputValue = false;
        public bool InputValue
        {
            get => inputValue;
            set
            {
                if (inputValue != value)
                {
                    inputValue = value;
                    DebugInput(value);
                }
            }
        }

        /// <summary>Editor only console log for this Input row, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void DebugInput(bool value)
        {
#if UNITY_EDITOR

            if (debug)
                Debug.Log($"<color=yellow> <B>[Input {name} - {interaction} : {value}]</B>. Map [{reference.action.actionMap.name}]  </color>", reference);
#endif
        }
        public virtual bool GetValue { get => inputValue; set => inputValue = value; }

        public UnityEvent OnInputDown = new();

        public UnityEvent OnInputEnabled = new();
        public UnityEvent OnInputDisabled = new();

        public UnityEvent OnInputUp = new();
        public UnityEvent OnLongPress = new();
        public UnityEvent OnDoubleTap = new();
        public BoolEvent OnInputChanged = new();
        public UnityEvent OnInputPressed = new();
        public FloatEvent OnInputFloatValue = new();
        public Vector2Event OnInputV2Value = new();

        public UnityEvent InputDown => this.OnInputDown;
        public UnityEvent InputUp => this.OnInputUp;
        public BoolEvent InputChanged => this.OnInputChanged;


        public void TranslateInput(InputAction.CallbackContext context)
        {
            if (!Active) return; //Do nothing if the Local Active is false;
            if (ignoreOnPause && Time.timeScale == 0) return; //Do nothing if TimeScale = 0;
            if (BlockedByUI(context)) return;

            //  Debug.Log($"{name}: {context.phase}");

            bool OldValue = InputValue;            //Store the Old Value first
            bool NewValue = context.performed || context.started;

            var Performed = context.phase == InputActionPhase.Performed;
            var Started = context.phase == InputActionPhase.Started;
            var Canceled = context.phase == InputActionPhase.Canceled;

            switch (interaction)
            {
                #region Press Interation
                case MInputInteraction.Press:

                    InputValue = NewValue; //Update the value for LongPress IMPORTANT!

                    //MWC — OnInputChanged used to be invoked outside this check, on every phase. 'Started' and
                    //'Performed' both produce NewValue == true, so every listener received 'true' twice per press.
                    //It is a *changed* event, so it now only fires when the value actually changed.
                    if (OldValue != InputValue)
                    {
                        if (InputValue)
                        {
                            OnInputDown.Invoke();
                            DoPress();
                        }
                        else
                        {
                            OnInputUp.Invoke();
                        }

                        OnInputChanged.Invoke(InputValue); //MWC — same order as before: Down/Up first, then Changed
                    }
                    break;
                #endregion

                #region Down Interaction
                //-------------------------------------------------------------------------------------------------------
                case MInputInteraction.Down:

                    if (context.phase == InputActionPhase.Started)
                    {
                        OnInputDown.Invoke();
                        OnInputChanged.Invoke(InputValue = true);
                    }
                    else if (Performed)
                    {
                        OnInputChanged.Invoke(InputValue = false);
                    }
                    break;
                #endregion

                #region Up Interaction
                //-------------------------------------------------------------------------------------------------------
                case MInputInteraction.Up:

                    if (Performed)
                    {
                        inputValue = true;
                        InputStartTime = Time.time; //Store the time when the Input was released
                    }
                    else if (Canceled)
                    {
                        //MWC — parenthesis fix. '&&' binds tighter than '||', so this used to read
                        //(inputValue && InputUpFailed == 0) || (!Elapsed), which bypassed the 'inputValue' guard
                        //entirely for any non-zero InputUpFailed (0.1 by default): a Cancel with no preceding
                        //Performed still fired OnInputUp. Intent: it must have been pressed, AND either there is no
                        //fail window (0) or it was released inside that window.
                        if (inputValue && (InputUpFailed.Value == 0 || !MTools.ElapsedTime(InputStartTime, InputUpFailed)))
                        {
                            inputValue = false; //make the value false so Input value gets executed
                            OnInputUp.Invoke();
                            OnInputChanged.Invoke(InputValue = true);

                            //Release the Input Value after a frame
                            MCoroutine.Delay_Action(() => { OnInputChanged.Invoke(InputValue = false); });
                        }
                    }
                    break;
                #endregion

                #region Long Press
                //-------------------------------------------------------------------------------------------------------
                case MInputInteraction.LongPress:

                    if (Performed)
                    {
                        LongPressTempValue = true;
                        if (C_LongPress != null)
                            MCoroutine.StopCoroutine(C_LongPress); //Clear the Long Press Coroutine

                        C_LongPress = IEnum_LongPress();
                        MCoroutine.StartCoroutine(C_LongPress);
                    }
                    else if (Canceled)
                    {
                        LongPressTempValue = false;

                        //If the Input was released before the LongPress was completed ... take it as Interrupted 
                        //(ON INPUT UP serves as Interrupted)
                        if (!InputCompleted)
                        {
                            if (C_LongPress != null) MCoroutine.StopCoroutine(C_LongPress); //Call Interruption
                        }

                        OnInputUp.Invoke(); //Invoke no mater what invoke this

                        InputCompleted = false;  //Reset the Long Press
                        OnInputChanged.Invoke(InputValue = false);
                    }
                    break;
                #endregion

                #region Double Tap
                //-------------------------------------------------------------------------------------------------------
                case MInputInteraction.DoubleTap:
                    InputValue = NewValue; //Update the value for LongPress IMPORTANT!


                    if (OldValue != InputValue)
                    {
                        OnInputChanged.Invoke(InputValue); //Just to make sure the Input is Pressed

                        if (InputValue)
                        {
                            if (InputStartTime != 0 && MTools.ElapsedTime(InputStartTime, DoubleTapTime))
                            {
                                FirstInputPress = false;    //This is in case it was just one Click/Tap this will reset it
                            }

                            if (!FirstInputPress)
                            {
                                OnInputDown.Invoke();
                                InputStartTime = Time.time;
                                FirstInputPress = true;
                            }
                            else
                            {
                                if ((Time.time - InputStartTime) <= DoubleTapTime)
                                {
                                    FirstInputPress = false;
                                    InputStartTime = 0;
                                    OnDoubleTap.Invoke();       //Successful Double tap
                                }
                                else
                                {
                                    FirstInputPress = false;
                                }
                            }
                        }
                    }
                    break;
                #endregion

                #region Toggle
                //-------------------------------------------------------------------------------------------------------
                case MInputInteraction.Toggle:

                    if (Started)
                    {
                        InputValue = !InputValue;

                        OnInputChanged.Invoke(InputValue);

                        if (InputValue)
                            OnInputDown.Invoke();
                        else
                            OnInputUp.Invoke();

                    }
                    break;

                #endregion

                case MInputInteraction.Float:
                    {
                        var value = context.action.ReadValue<float>();

                        InputValue = value != 0;

                        if (OldValue != InputValue)
                        {
                            OnInputChanged.Invoke(InputValue);

                            if (InputValue) OnInputDown.Invoke();
                            else OnInputUp.Invoke();
                        }


                        if (value > PressThreshold.Value) OnInputPressed.Invoke();
                        if (debug) Debug.Log($"<color=cyan><B>[Input {name} : {value}]</B></color>");


                        OnInputFloatValue.Invoke(value);
                    }
                    break;


                case MInputInteraction.Vector2:

                    var v2 = context.action.ReadValue<Vector2>();

                    InputValue = v2 != Vector2.zero;

                    if (OldValue != InputValue)
                    {
                        OnInputChanged.Invoke(InputValue);

                        if (InputValue) OnInputDown.Invoke();
                        else OnInputUp.Invoke();
                    }

                    if (InputValue) OnInputPressed.Invoke();
                    if (debug) Debug.Log($"<color=cyan><B>[Input {name} : {v2}]</B></color>");

                    OnInputV2Value.Invoke(v2 * Vector2Mult);
                    break;

                default: break;
            }
        }

        /// <summary>Clears the runtime state of a disabled Input so it can be pressed again once it is re-enabled</summary>
        //MWC — Active = false blocks TranslateInput(), so the Canceled phase of a press that was interrupted by the
        //disable never arrives and InputValue stays true. On re-enable the next press found OldValue == true, so
        //'changed' was false and no event fired at all: MRider's Mount Input worked once, then was dead.
        private void ResetInputState()
        {
            if (MCoroutine != null)
            {
                if (C_Press != null) MCoroutine.StopCoroutine(C_Press);
                if (C_LongPress != null) MCoroutine.StopCoroutine(C_LongPress);
            }

            C_Press = C_LongPress = null;

            LongPressTempValue = false;
            FirstInputPress = false;
            InputCompleted = false;
            InputStartTime = 0;
            PressStarted = PressBlockedByUI = false;

            if (!inputValue) return;                 //It was not pressed: there's nothing to release

            inputValue = false;
            DebugInput(false);

            //MWC — same contract as DisconnectButtons(): the listeners are told the Input dropped to false
            if (ResetOnDisable.Value) OnInputChanged.Invoke(false);
        }

        /// <summary>True while a press is in progress: its UI verdict was already taken</summary>
        private bool PressStarted;

        /// <summary>True when the current press started over an interactable UI element</summary>
        private bool PressBlockedByUI;

        private static readonly List<RaycastResult> UIHits = new();

        /// <summary>Swallows every phase of a Pointer press that started over an interactable UI element</summary>
        private bool BlockedByUI(InputAction.CallbackContext context)
        {
            //The verdict is taken once, when the press begins, so Started, Performed and the release all agree.
            //Dragging off the Button before releasing must not let the release (or a Hold's Performed) through.
            if (context.started || (context.performed && !PressStarted))
            {
                PressStarted = true;
                PressBlockedByUI = blockOverUI.Value && IsPointerOverInteractableUI(context.control);
            }

            var blocked = PressBlockedByUI;

            //Tap/PassThrough actions end on Performed with no Canceled: a released button also closes the press
            if (context.canceled || (context.control is ButtonControl button && !button.isPressed))
                PressStarted = PressBlockedByUI = false;

            return blocked;
        }

        /// <summary>True when the control is a Mouse, Touch or Pen button and the pointer is over an interactable UI element</summary>
        public static bool IsPointerOverInteractableUI(InputControl control)
        {
            if (control is not ButtonControl || control.device is not Pointer pointer) return false;

            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            //Raycast now instead of EventSystem.IsPointerOverGameObject(): that one reads last frame's
            //state and logs a warning every time it is called from inside an Input System callback
            var position = control.parent is TouchControl touch ? touch.position.ReadValue() : pointer.position.ReadValue();
            var data = new PointerEventData(eventSystem) { position = position };

            UIHits.Clear();
            eventSystem.RaycastAll(data, UIHits);

            if (UIHits.Count == 0) return false;

            var top = UIHits[0]; //RaycastAll sorts the hits front to back
            UIHits.Clear();

            if (top.module is PhysicsRaycaster) return false;                  //A world object (3D or 2D), not UI
            if (top.module is not UnityEngine.UI.GraphicRaycaster) return true; //UI Toolkit panel

            var handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top.gameObject);
            if (handler == null) handler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(top.gameObject);
            if (handler == null) handler = ExecuteEvents.GetEventHandler<IBeginDragHandler>(top.gameObject);

            if (handler == null) return false; //Decorative UI: labels, panels, HUD images

            //A disabled Button (or one inside a non interactable Canvas Group) does not eat the click
            return !handler.TryGetComponent(out UnityEngine.UI.Selectable selectable) || selectable.IsInteractable();
        }

        private void DoPress()
        {
            if (MCoroutine == null) return;

            if (C_Press != null)
                MCoroutine.StopCoroutine(C_Press);

            C_Press = IEnum_Press();
            MCoroutine.StartCoroutine(C_Press);
        }

        #region Coroutines

        private IEnumerator IEnum_Press()
        {
            while (InputValue)
            {
                OnInputPressed.Invoke();
                yield return null;
            }
        }

        ///// <summary> This is called when the Input Up is released Set Input Value to false next frame </summary>
        //IEnumerator IEnum_UpReleased()
        //{
        //    yield return null;
        //    OnInputChanged.Invoke(InputValue = false);
        //}


        private IEnumerator IEnum_LongPress()
        {
            if (LongPressDelay.Value > 0)
            {
                yield return new WaitForSeconds(LongPressDelay.Value);
            }

            if (!LongPressTempValue) { yield break; } //Skip the code if the Long Press was not started


            InputStartTime = Time.time;
            InputCompleted = false;
            OnInputDown.Invoke();
            OnInputChanged.Invoke(InputValue = true);

            float elapsed;

            while (!InputCompleted)
            {
                elapsed = (Time.time - InputStartTime) / LongPressTime;
                OnInputFloatValue.Invoke(elapsed);

                if (elapsed >= 1f)
                {
                    OnInputFloatValue.Invoke(1);
                    OnLongPress.Invoke();
                    InputCompleted = true;                     //This will avoid the long pressed being pressed just one time
                    InputValue = true;
                    break;
                }
                yield return null;
            }
        }

        #endregion

        #region Constructors


        public MInputAction(string name)
        {
            active.Value = true;
            this.name = name;
            interaction = MInputInteraction.Down;
            reference = null;
            Action = null;
            DoubleTapTime = new FloatReference(0.3f);                          //Double Tap Time
            LongPressTime = new FloatReference(0.5f);
        }

        public MInputAction(string name, MInputInteraction pressed)
        {
            this.name = name;
            active.Value = true;
            interaction = pressed;
            reference = null;
            DoubleTapTime = new FloatReference(0.3f);                          //Double Tap Time
            LongPressTime = new FloatReference(0.5f);
        }


        public MInputAction(string name, InputActionReference reference)
        {
            this.name = name;
            active.Value = true;
            interaction = MInputInteraction.Down;
            this.reference = reference;
            DoubleTapTime = new FloatReference(0.3f);                          //Double Tap Time
            LongPressTime = new FloatReference(0.5f);
        }

        public MInputAction(string name, InputActionReference reference, MInputInteraction pressed)
        {
            this.name = name;
            active.Value = true;
            interaction = pressed;
            this.reference = reference;
            DoubleTapTime = new FloatReference(0.3f);                          //Double Tap Time
            LongPressTime = new FloatReference(0.5f);
        }

        public MInputAction(bool active, string name, MInputInteraction pressed)
        {
            this.name = name;
            this.active.Value = active;
            interaction = pressed;
            DoubleTapTime = new FloatReference(0.3f);                          //Double Tap Time
            LongPressTime = new FloatReference(0.5f);
        }

        public MInputAction()
        {
            active.Value = true;
            name = "InputName";
            interaction = MInputInteraction.Press;
            reference = null;
            DoubleTapTime = new FloatReference(0.3f);                          //Double Tap Time
            LongPressTime = new FloatReference(0.5f);
        }
        #endregion
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(MInputLink)), CanEditMultipleObjects]
    public class MInputLinkEditor : Editor
    {
        private bool ShowButtons;

        //protected ReorderableList list;
        protected SerializedProperty
            m_Buttons, InputActions, m_MapButtons, showInputEvents, playerInput, clearPlayerInput, Editor_Tabs1, playerInputReference,
            UpDown, Move, r_Move, PlayerIndex, DefaultScheme, ActiveActionMap, ShowMapIndex,
            IgnoreOnPause, OnInputEnabled, OnMoveEvent, OnInputDisabled, OnActionMapChanged, CurrentControlScheme,
            OnControlsChanged, OnDeviceLost, OnDeviceRegained,
            debug;
        private MInputLink M;
        protected MonoScript script;


        private readonly Dictionary<string, ReorderableList> innerListDict = new();

        private string[] ActionMapsNames;


        protected virtual void OnEnable()
        {
            M = ((MInputLink)target);
            script = MonoScript.FromMonoBehaviour(target as MonoBehaviour);

            playerInput = serializedObject.FindProperty("playerInput");
            playerInputReference = serializedObject.FindProperty("playerInputReference");
            clearPlayerInput = serializedObject.FindProperty("clearPlayerInput");
            m_Buttons = serializedObject.FindProperty("m_Buttons");
            m_MapButtons = serializedObject.FindProperty("m_MapButtons");

            OnInputEnabled = serializedObject.FindProperty("OnInputEnabled");
            OnMoveEvent = serializedObject.FindProperty("OnMoveEvent");
            OnInputDisabled = serializedObject.FindProperty("OnInputDisabled");
            OnActionMapChanged = serializedObject.FindProperty("OnActionMapChanged");

            OnControlsChanged = serializedObject.FindProperty("OnControlsChanged");
            OnDeviceLost = serializedObject.FindProperty("OnDeviceLost");
            OnDeviceRegained = serializedObject.FindProperty("OnDeviceRegained");



            CurrentControlScheme = serializedObject.FindProperty("CurrentControlScheme");
            showInputEvents = serializedObject.FindProperty("showInputEvents");

            IgnoreOnPause = serializedObject.FindProperty("IgnoreOnPause");
            debug = serializedObject.FindProperty("debug");

            Move = serializedObject.FindProperty("Move");
            UpDown = serializedObject.FindProperty("UpDown");
            InputActions = serializedObject.FindProperty("InputActions");

            DefaultScheme = serializedObject.FindProperty("DefaultScheme");
            ActiveActionMap = serializedObject.FindProperty("ActiveActionMapIndex");
            ShowMapIndex = serializedObject.FindProperty("ShowMapIndex");
            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");



            if (InputActions.objectReferenceValue == null)
            {
                M.ResetButtonMap();
                EditorUtility.SetDirty(target);
            }
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

            if (M.playerInput == null && prefabStage == null)
            {
                M.playerInput = GameObject.FindAnyObjectByType<PlayerInput>();
                EditorUtility.SetDirty(target);
            }
        }

        private void CheckActionMaps(bool DifferentMapButton)
        {
            if (M.InputActions != null)
            {
                var count = M.InputActions.actionMaps.Count; //Store the Amount of Action Maps

                ActionMapsNames = new string[count + 1]; // Set the first one as NONE  
                ActionMapsNames[0] = "<None>";

                for (int i = 0; i < count; i++)
                {
                    var nme = M.InputActions.actionMaps[i].name;
                    ActionMapsNames[i + 1] = nme;
                    // Debug.Log($"MAP Name = <{MInp.InputActions.actionMaps[i].name}>");
                }

                M.m_MapButtons ??= new(); //Make sure is not null

                //Check when a new Input Map has been added
                if (M.m_MapButtons.Count < count)
                {
                    for (int i = M.m_MapButtons.Count; i < count; i++)
                    {
                        M.m_MapButtons.Add(new MInputActionMap(M.InputActions.actionMaps[i], i));
                    }
                }

                if (M.m_MapButtons == null || DifferentMapButton) //The Map Button is different or empty
                {
                    M.m_MapButtons = new List<MInputActionMap>();
                    if (debug.boolValue)
                        Debug.Log($"{target.name} <MapButton list reset>");


                    for (int i = 0; i < count; i++)
                    {
                        M.m_MapButtons.Add(new MInputActionMap(M.InputActions.actionMaps[i], i));
                    }
                    EditorUtility.SetDirty(target);
                }
            }
            else
            {
                ActionMapsNames = null;
            }
        }

        private readonly string[] tabs = new string[] { "Inputs", "Events" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            //MWC — ActiveMActionMap is [NonSerialized] and is cleared on OnDisable(), so it is null whenever the
            //link is disabled or not connected yet. Selecting the component in Play Mode used to throw here.
            var ActiveActionMapName = Application.isPlaying && M.ActiveMActionMap != null
                ? $" Active Map: [{M.ActiveMActionMap.Name}]" : "";

            MalbersEditor.DrawDescription(M.versionInput + ActiveActionMapName);

            if (Application.isPlaying && M.playerInput && M.playerInput.enabled && M.playerInput.currentActionMap != null)
            {
                EditorGUILayout.LabelField($"Scheme: [{M.playerInput.currentControlScheme}] ActionMap [{M.playerInput.currentActionMap.name}] Active {M.playerInput.currentActionMap.enabled}");
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (var CA = new EditorGUI.ChangeCheckScope())
                {
                    var OldInpSource = InputActions.objectReferenceValue;
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(playerInput);
                        MalbersEditor.DrawDebugIcon(debug);
                    }

                    if (playerInput.objectReferenceValue == null)
                        EditorGUILayout.PropertyField(playerInputReference);

                    if (CA.changed)
                    {
                        //Check if the Player Input is found
                        if (playerInput.objectReferenceValue != null)
                        {
                            var pI = playerInput.objectReferenceValue as PlayerInput;

                            if (pI != null)
                            {
                                InputActions.objectReferenceValue = pI.actions;
                                InputActions.serializedObject.ApplyModifiedProperties();
                                serializedObject.ApplyModifiedProperties(); //Update the new Input Source

                                //  serializedObject.Update();
                            }
                        }

                        var DifferentINPSource = OldInpSource != InputActions.objectReferenceValue;


                        //IMPORTANT clean everything!
                        if (OldInpSource != InputActions.objectReferenceValue)
                            ActiveActionMap.intValue = 0;


                        serializedObject.ApplyModifiedProperties(); //Update the new Input Source


                        CheckActionMaps(DifferentINPSource);
                    }
                }

                using (new EditorGUI.DisabledGroupScope(playerInput.objectReferenceValue != null))
                    EditorGUILayout.PropertyField(InputActions);

                if (!Application.isPlaying)
                {
                    if (M.playerInput != null && M.playerInput.actions != M.InputActions)
                    {
                        // EditorGUILayout.HelpBox("[Input Actions] does not match with the Player Input -> [Input Actions].\nUse the same asset", MessageType.Error);
                        InputActions.objectReferenceValue = M.playerInput.actions;
                        serializedObject.ApplyModifiedProperties(); //Update the new Input Source
                        M.ResetButtonMap();
                        //Debug.Log("Button Cleared and Disconnected. The Input Action Asset has changed");
                        EditorUtility.SetDirty(target);
                    }
                }

                EditorGUILayout.PropertyField(clearPlayerInput);
                EditorGUILayout.PropertyField(IgnoreOnPause);

                if (InputActions.objectReferenceValue != null)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        if (ActionMapsNames == null)
                            CheckActionMaps(false);

                        //LINK the Player Input with the Map in this component! TODO 
                        if (ActiveActionMap != null && ActionMapsNames != null)
                        {
                            ActiveActionMap.intValue =
                            EditorGUILayout.Popup(new GUIContent("Connect Maps (" + (ActiveActionMap.intValue - 1) + ")",
                            "Action Map connected to the Animal Controller at start"), ActiveActionMap.intValue, ActionMapsNames);
                        }

                        using (new EditorGUI.DisabledGroupScope(Application.isPlaying))
                        {
                            if (GUILayout.Button(new GUIContent("Find Buttons",
                                "Search for Actions set as buttons on the Action Map. New Buttons will be added automatically to the button list"), GUILayout.MaxWidth(100), GUILayout.MinWidth(50)))
                            {
                                M.FindButtons();
                                EditorUtility.SetDirty(target);
                            }
                        }
                    }

                    if (Application.isPlaying)
                        EditorGUILayout.HelpBox("To Change Action maps use SwitchActionMap(string actionMap)", MessageType.Info);

                }

            }

            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, tabs);

            switch (Editor_Tabs1.intValue)
            {
                case 0: DrawButtons(); break;
                case 1: DrawEvents(); break;
                default: break;

            }


            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        EditorGUILayout.LabelField($"[Move Forward: {M.MoveAxis.z}] [Move Right: {M.MoveAxis.x}] [Move Up: {M.MoveAxis.y}]");
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
        private void DrawButtons()
        {
            if (Application.isPlaying && !ShowButtons)
            {
                if (GUILayout.Button("Show Buttons"))
                {
                    ShowButtons = true;
                }
                EditorGUILayout.HelpBox("Buttons Hidden to make inspector faster on Play Mode", MessageType.Info);
            }


            if (InputActions.objectReferenceValue != null && (ShowButtons || !Application.isPlaying))
            {
                ReorderableList Reo_AbilityList;

                var index = ActiveActionMap.intValue - 1;

                if (ActiveActionMap.intValue <= 0) return;

                if (M.m_MapButtons == null || m_MapButtons == null || M.m_MapButtons.Count <= index) return; //Null Checking IMPORTANT!

                // Debug.Log(index);
                SerializedProperty actionMap = m_MapButtons.GetArrayElementAtIndex(index);

                if (actionMap == null) return;


                var ButtonList = actionMap.FindPropertyRelative("buttons");
                var Move = actionMap.FindPropertyRelative("Move");
                var UpDown = actionMap.FindPropertyRelative("UpDown");
                var id = actionMap.FindPropertyRelative("id");
                var map = actionMap.FindPropertyRelative("ActionMap");
                var m_Name = map.FindPropertyRelative("m_Name");
                var MoveMult = actionMap.FindPropertyRelative("MoveMult");


                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(m_Name, new GUIContent("Action Map", "This must match the same name as the Player Input"));
                    EditorGUILayout.PropertyField(Move);
                    EditorGUILayout.PropertyField(UpDown);
                    EditorGUILayout.PropertyField(MoveMult);




                    string listKey = actionMap.propertyPath;

                    if (innerListDict.ContainsKey(listKey))
                    {
                        // fetch the reorderable list in dict
                        Reo_AbilityList = innerListDict[listKey];
                    }
                    else
                    {
                        Reo_AbilityList = new ReorderableList(actionMap.serializedObject, ButtonList, true, true, true, true)
                        {
                            drawElementCallback = (rect, ele_index, isActive, isFocused) =>
                            {
                                if (M.m_MapButtons == null || M.m_MapButtons.Count <= index) return; //Nul Check!! IMPORTANT
                                if (M.m_MapButtons[index].buttons == null || M.m_MapButtons[index].buttons.Count <= ele_index) return; //Nul Check!! IMPORTANT

                                var element = M.m_MapButtons[index].buttons[ele_index];

                                // if (element.action.action.actionMap != MInp.InputActions.actionMaps[MInp.ActiveActionMap]) return;

                                var elementSer = actionMap.FindPropertyRelative("buttons").GetArrayElementAtIndex(ele_index);

                                rect.y += 2;


                                var splitter = (rect.width - 20) / 3;
                                var height = EditorGUIUtility.singleLineHeight;

                                Rect R_0 = new(rect.x, rect.y, 20, height);
                                Rect R_1 = new(rect.x + 20, rect.y, splitter - 75, height);
                                Rect R_2 = new(rect.x + 20 + splitter - 70, rect.y, splitter + 75, height);
                                Rect R_4 = new(rect.x + (splitter * 2) + 30, rect.y, splitter - 5, height);



                                var name = elementSer.FindPropertyRelative("name");
                                var GetPressed = elementSer.FindPropertyRelative("interaction");
                                var action = elementSer.FindPropertyRelative("reference");
                                var active = elementSer.FindPropertyRelative("active");

                                var dbC = GUI.backgroundColor;
                                GUI.backgroundColor = isActive ? MTools.MBlue : dbC;



                                var result = EditorGUI.Toggle(R_0, element.active.Value);

                                if (result != element.active.Value)
                                {
                                    element.active.Value = result;
                                    EditorUtility.SetDirty(target); //UPDATE THE VALUE ON THE ACTIVE INPUT
                                }

                                // EditorGUI.PropertyField(R_0, active, GUIContent.none);
                                EditorGUI.PropertyField(R_1, name, GUIContent.none);

                                if (action != null && action.objectReferenceValue == null) GUI.backgroundColor = Color.red * 1.5f;
                                EditorGUI.PropertyField(R_2, action, GUIContent.none);
                                GUI.backgroundColor = isActive ? MTools.MBlue : dbC;
                                EditorGUI.PropertyField(R_4, GetPressed, GUIContent.none);
                                GUI.backgroundColor = dbC;
                            },

                            drawHeaderCallback = HeaderCallbackDelegate,
                        };


                        innerListDict.Add(listKey, Reo_AbilityList);  //Store it on the Editor
                    }

                    //using (var X = new GUILayout.ScrollViewScope(Scroll, GUILayout.MaxHeight(200)))
                    //{
                    //  Scroll = X.scrollPosition;
                    Reo_AbilityList.DoLayoutList();
                    //  }

                    var buttonIndex = Reo_AbilityList.index;

                    if (buttonIndex != -1 && Reo_AbilityList.count > buttonIndex)
                    {
                        var elem = ButtonList.GetArrayElementAtIndex(buttonIndex);
                        DrawInputEvents(elem, buttonIndex);
                    }
                }
            }


        }
        //private Vector2 Scroll;

        private void DrawEvents()
        {
            EditorGUILayout.PropertyField(OnMoveEvent);
            EditorGUILayout.PropertyField(OnInputEnabled);
            EditorGUILayout.PropertyField(OnInputDisabled);
            EditorGUILayout.PropertyField(OnActionMapChanged);
            EditorGUILayout.PropertyField(CurrentControlScheme);

            EditorGUILayout.PropertyField(OnControlsChanged);
            EditorGUILayout.PropertyField(OnDeviceLost);
            EditorGUILayout.PropertyField(OnDeviceRegained);
        }

        private Vector2 ScrollEvents;

        protected void DrawInputEvents(SerializedProperty Element, int index)
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {

                EditorGUI.indentLevel++;

                var InputName = Element.FindPropertyRelative("name").stringValue;

                Element.isExpanded = EditorGUILayout.Foldout(Element.isExpanded,
                    new GUIContent($"[{InputName} Properties]"));
                EditorGUI.indentLevel--;
                if (Element.isExpanded)
                {
                    var active = Element.FindPropertyRelative("active");
                    var debug = Element.FindPropertyRelative("debug");
                    var ignoreOnPause = Element.FindPropertyRelative("ignoreOnPause");
                    var blockOverUI = Element.FindPropertyRelative("blockOverUI");
                    var ResetOnDisable = Element.FindPropertyRelative("ResetOnDisable");


                    var OnInputChanged = Element.FindPropertyRelative("OnInputChanged");
                    var OnInputDown = Element.FindPropertyRelative("OnInputDown");
                    var OnInputUp = Element.FindPropertyRelative("OnInputUp");


                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(active);
                        MalbersEditor.DrawDebugIcon(debug);
                    }

                    EditorGUILayout.PropertyField(ResetOnDisable);
                    EditorGUILayout.PropertyField(ignoreOnPause);
                    EditorGUILayout.PropertyField(blockOverUI);

                    if (Element.FindPropertyRelative("reference").objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox("Please set an Input Action Reference.", MessageType.Warning);
                        return;
                    }


                    var guiColor = GUI.backgroundColor;
                    GUI.backgroundColor = MTools.MGreen * 2;

                    using (new GUILayout.HorizontalScope())
                    {

                        var reference = Element.FindPropertyRelative("reference").objectReferenceValue as InputActionReference;
                        var style = EditorStyles.toolbarButton;


                        using (new GUILayout.VerticalScope())
                            for (int i = 0; i < reference.action.bindings.Count; i += 2)
                            {
                                EditorGUILayout.LabelField($"{reference.action.bindings[i].path}", style, GUILayout.MinWidth(50));
                            }

                        using (new GUILayout.VerticalScope())
                            for (int i = 1; i < reference.action.bindings.Count; i += 2)
                            {
                                EditorGUILayout.LabelField($"{reference.action.bindings[i].path}", style, GUILayout.MinWidth(50));
                            }
                    }
                    GUI.backgroundColor = guiColor;


                    MalbersEditor.DrawSplitter();
                    EditorGUILayout.LabelField("Events", EditorStyles.toolbarButton);

                    using (var X = new GUILayout.ScrollViewScope(ScrollEvents, GUILayout.MaxHeight(300)))
                    {
                        ScrollEvents = X.scrollPosition;

                        MInputInteraction interaction = (MInputInteraction)Element.FindPropertyRelative("interaction").enumValueIndex;


                        switch (interaction)
                        {
                            case MInputInteraction.Press:
                                EditorGUILayout.PropertyField(OnInputChanged);
                                EditorGUILayout.PropertyField(OnInputDown);
                                EditorGUILayout.PropertyField(OnInputUp);
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnInputPressed"));
                                break;
                            case MInputInteraction.Down:
                                EditorGUILayout.PropertyField(OnInputDown);
                                EditorGUILayout.PropertyField(OnInputChanged);
                                break;
                            case MInputInteraction.Up:
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("InputUpFailed"));
                                EditorGUILayout.PropertyField(OnInputUp);
                                EditorGUILayout.PropertyField(OnInputChanged);
                                break;
                            case MInputInteraction.LongPress:
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("LongPressTime"));
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("LongPressDelay"));
                                EditorGUILayout.Space();
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnLongPress"), new GUIContent("On Long Press Completed"));
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnInputFloatValue"), new GUIContent("On Pressed Time Normalized"));
                                EditorGUILayout.PropertyField(OnInputDown, new GUIContent("On Input Down"));
                                EditorGUILayout.PropertyField(OnInputUp, new GUIContent("On Pressed Interrupted (On Input Up)"));
                                EditorGUILayout.PropertyField(OnInputChanged);
                                break;
                            case MInputInteraction.DoubleTap:
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("DoubleTapTime"));
                                EditorGUILayout.Space();
                                EditorGUILayout.PropertyField(OnInputDown, new GUIContent("On First Tap"));
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnDoubleTap"));
                                EditorGUILayout.PropertyField(OnInputChanged);
                                break;
                            case MInputInteraction.Toggle:
                                EditorGUILayout.PropertyField(OnInputChanged, new GUIContent("On Input Toggle"));
                                EditorGUILayout.PropertyField(OnInputDown, new GUIContent("On Toggle On"));
                                EditorGUILayout.PropertyField(OnInputUp, new GUIContent("On Toggle Off"));
                                break;
                            case MInputInteraction.Float:
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("PressThreshold"));
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnInputFloatValue"), new GUIContent("On Input Float"));
                                EditorGUILayout.PropertyField(OnInputDown);
                                EditorGUILayout.PropertyField(OnInputUp);
                                EditorGUILayout.PropertyField(OnInputChanged);
                                break;

                            case MInputInteraction.Vector2:
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("Vector2Mult"), new GUIContent("Vector2 Multiplier", "Multiply the Vector2 value by this value"));
                                EditorGUILayout.PropertyField(Element.FindPropertyRelative("OnInputV2Value"), new GUIContent("On Vector2 Value"));


                                EditorGUILayout.PropertyField(OnInputDown);
                                EditorGUILayout.PropertyField(OnInputUp);
                                EditorGUILayout.PropertyField(OnInputChanged);
                                break;
                            default:
                                EditorGUILayout.PropertyField(OnInputChanged);
                                break;
                        }

                        var OnInputEnabled = Element.FindPropertyRelative("OnInputEnabled");
                        var OnInputDisabled = Element.FindPropertyRelative("OnInputDisabled");
                        EditorGUILayout.Space();

                        EditorGUILayout.PropertyField(OnInputEnabled, new GUIContent($"On [{InputName}] Enabled"));
                        EditorGUILayout.PropertyField(OnInputDisabled, new GUIContent($"On [{InputName}] Disabled"));
                    }
                }
            }
        }

        protected void HeaderCallbackDelegate(Rect rect)
        {
            var height = EditorGUIUtility.singleLineHeight;

            var splitter = (rect.width - 20) / 3;
            Rect R_1 = new(rect.x + 20, rect.y, splitter - 75, height);
            Rect R_2 = new(rect.x + 20 + splitter - 70, rect.y, splitter + 75, height);
            Rect R_4 = new(rect.x + (splitter * 2) + 30, rect.y, splitter - 5, height);

            EditorGUI.LabelField(R_1, "   Name", EditorStyles.boldLabel);
            EditorGUI.LabelField(R_2, "  Input Action Reference", EditorStyles.boldLabel);
            EditorGUI.LabelField(R_4, "  Interaction", EditorStyles.boldLabel);
        }
    }
#endif
}