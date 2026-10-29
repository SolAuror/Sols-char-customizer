// Last Change: 9/19/2026 4:05pm
using MalbersAnimations.Events;
using UnityEngine;
using UnityEngine.Events;

namespace MalbersAnimations
{
    /// <summary>Basic Entries needed to use Malbers Input Component</summary>
    public interface IInputSource
    {
        void Enable(bool val);

        /// <summary>Allows the Input to move the Character (Use Axis)</summary>
        bool MoveCharacter { get; set; }

        /// <summary>Returns the Input Action by its name</summary>
        IInputAction GetInput(string input);
        void EnableInput(string input);
        void DisableInput(string input);

        /// <summary>Changes the value of an Input using its name</summary>
        void SetInput(string input, bool value);

        /// <summary>Connects an input to a Bool Method</summary>
        void ConnectInput(string name, UnityAction<bool> action);

        /// <summary>Disconnect an input to a Bool Method</summary>
        void DisconnectInput(string name, UnityAction<bool> action);

        /// <summary> Reset the Input Value and Toggle value to false </summary> 
        void ResetInput(string name);

        /// <summary>BroadCast the Move Axis Value so any component can listen to it (x-> Horizontal, Y-> UpDown Z-> Forward-Vertical </summary> 
        System.Action<Vector3> OnMoveAxis { get; set; }

        Vector3 MoveAxis { get; set; }

        /// <summary>  Send the source to the Input so it can extract the values from it.
        /// The horse uses this to extract the Player Input Component from the Rider </summary>
        void PlayerInput(IInputSource player);

        /// <summary> Reference of the IInputSource Transform</summary>
        Transform transform { get; }
    }

    //MWC — [Fast Weapons] OPTIONAL companion to IInputSource. Several input rows can be bound to the SAME physical
    //control (e.g. 'Action1', 'Light Attack' and 'Heavy Attack' all on Left Click). Anything that has to arbitrate
    //between listeners of one press needs to know they are the same button; an input source that cannot tell simply
    //does not implement this, and the callers fall back to plain name comparison.
    /// <summary> Optional: exposes which physical control drives an input name </summary>
    public interface IInputPhysicalID
    {
        /// <summary> Identity of the physical control behind an input name. Null or empty when unknown </summary>
        string PhysicalInputID(string inputName);
    }

    /// <summary> Everything a UI needs to show which physical control drives an input name (see <see cref="IInputPrompt"/>) </summary>
    public struct InputPrompt
    {
        /// <summary> Short display name of the control on the active device. E.g. "LMB", "E", "RT", "Y" </summary>
        public string Display;
        /// <summary> Control path of the binding. E.g. "<Mouse>/leftButton", "<Gamepad>/buttonSouth". Used to look up an icon </summary>
        public string ControlPath;
        /// <summary> Device layout of the binding. E.g. "Mouse", "Keyboard", "Gamepad" </summary>
        public string Device;
        /// <summary> Name of the interaction the input row uses: Press, Down, Up, LongPress, DoubleTap, Toggle... </summary>
        public string Interaction;
    }

    //OPTIONAL companion to IInputSource. A UI that shows "press [E] to Interact" needs the binding behind an input
    //name on the CURRENT device. An input source that cannot tell simply does not implement this, and the UI falls
    //back to showing the input name itself.
    /// <summary> Optional: exposes the display name, control path and interaction behind an input name </summary>
    public interface IInputPrompt
    {
        /// <summary> Resolves the prompt of an input name on the active device. False when the input does not exist </summary>
        bool TryGetInputPrompt(string inputName, out InputPrompt prompt);
    }

    public interface IInputAction
    {
        /// <summary> Set/Get Input Action Active value</summary>
        bool Active { get; set; }

        /// <summary>Input Action Value True (Down/Pressed) False (Up/Released)</summary>
        bool GetValue { get; }

        string Name { get; }

        BoolEvent InputChanged { get; }
    }



    /// <summary> Common Entries for all Inputs on the Store </summary>
    public interface IInputSystem
    {
        float GetAxis(string Axis);
        float GetAxisRaw(string Axis);
        bool GetButtonDown(string button);
        bool GetButtonUp(string button);
        bool GetButton(string button);
    }

    /// <summary>Function Needed for moving Characters</summary>
    public interface ICharacterMove
    {
        /// <summary>Move the Character using a Direction</summary>
        void Move(Vector3 Direction);

        void RotateAtDirection(Vector3 Direction);

        /// <summary>Stop the Character from moving</summary>
        void StopMoving();

        /// <summary>Sends to the the Raw Input Axis </summary>
        void SetInputAxis(Vector3 inputAxis);

        /// <summary>Sends to the the Raw Input Axis </summary>
        void SetInputAxis(Vector2 inputAxis);

        bool MovementDetected { get; }

        Transform transform { get; }
    }


    /// <summary> Default Unity Input</summary>
    public class DefaultInput : IInputSystem
    {
        public float GetAxis(string Axis) =>
#if  !ENABLE_LEGACY_INPUT_MANAGER
            0;
#else
            Input.GetAxis(Axis);
#endif

        public float GetAxisRaw(string Axis) =>
#if !ENABLE_LEGACY_INPUT_MANAGER
            0;
#else
            Input.GetAxis(Axis);
#endif

        public bool GetButton(string button) =>
#if !ENABLE_LEGACY_INPUT_MANAGER
             false;
#else
             Input.GetButton(button);
#endif

        public bool GetButtonDown(string button) =>
#if !ENABLE_LEGACY_INPUT_MANAGER
            false;
#else
        Input.GetButtonDown(button);
#endif

        public bool GetButtonUp(string button) =>
#if !ENABLE_LEGACY_INPUT_MANAGER
            false;
#else
            Input.GetButtonUp(button);
#endif

        /// <summary> This Gets the Current Input System that is being used... Unity's, CrossPlatform or Rewired</summary>
        public static IInputSystem GetInputSystem(string PlayerID = "")
        {
            IInputSystem Input_System = null;
            Input_System = new DefaultInput();             //Set it as default the Unit Input System

#if REWIRED
           Rewired.Player player = Rewired.ReInput.players.GetPlayer(PlayerID);
            if (player != null)
                Input_System = new RewiredInput(player);
            else
                Debug.LogError("NO REWIRED PLAYER WITH THE ID:" + PlayerID + " was found");
#endif
            return Input_System;
        }
    }

#if REWIRED
    public class RewiredInput : IInputSystem
    {
        Rewired.Player player;

        public float GetAxis(string Axis)
        {
            return player.GetAxis(Axis);
        }

        public float GetAxisRaw(string Axis)
        {
            return player.GetAxisRaw(Axis);
        }

        public bool GetButton(string button)
        {
            return player.GetButton(button);
        }

        public bool GetButtonDown(string button)
        {
            return player.GetButtonDown(button);
        }

        public bool GetButtonUp(string button)
        {
            return player.GetButtonUp(button);
        }

        public RewiredInput(Rewired.Player player)
        {
            this.player = player;
        }
    }
#endif
}