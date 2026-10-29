using Sol.CharacterCustomization;
using MalbersAnimations;
using MalbersAnimations.Controller;
using MalbersAnimations.IK;
using MalbersAnimations.Utilities;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sol.Locomotion
{
    // Malbers owns movement, gravity,
    // root motion and animation state; this component owns input and camera routing.
    [DefaultExecutionOrder(100)]
    public sealed class HumanLocomotionController : MonoBehaviour, ICharacterGameplayHandoff
    {
        [Header("References")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference sprintAction;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Transform cameraYawRoot;
        [SerializeField] private Transform cameraPitchRoot;
        [SerializeField] private CinemachineOrbitalFollow cameraOrbit;
        [SerializeField] private CharacterMorphController character;
        [SerializeField] private bool startInGameplay;

        [Header("Look")]
        [SerializeField, Min(0)] private float mouseLookSensitivity = .12f;
        [SerializeField, Min(0)] private float gamepadLookSpeed = 180;
        [SerializeField] private float minimumPitch = -30;
        [SerializeField] private float maximumPitch = 60;
        [SerializeField] private bool lockCursorDuringGameplay = true;

        [Header("Jump")]
        [Tooltip("Remembers a jump pressed just before landing, in seconds.")]
        [SerializeField, Range(0, .25f)] private float jumpInputBuffer = .12f;
        [SerializeField, Min(.1f)] private float jumpHeight = 1.25f;
        [SerializeField, Min(1)] private float gravityAcceleration = 25f;

        MAnimal motor;
        bool gameplayInputEnabled;
        bool crouching;
        bool jumpHeld;
        bool ready;
        float cameraPitch;
        float standingHeight;
        float jumpBufferedUntil = -1;
        bool jumpWasPressed;
        InputAction moveInput, lookInput, jumpInput, sprintInput, crouchInput;
        JumpBasic jumpState;
        Transform activeCharacterRoot;
        Avatar fittedAvatar;
        CinemachineCamera gameplayCamera;
        CinemachineRotationComposer cameraComposer;
        Vector3 spawnPosition;
        Quaternion spawnRotation;

        public InputActionReference MoveAction => moveAction;
        public InputActionReference LookAction => lookAction;
        public InputActionReference JumpAction => jumpAction;
        public InputActionReference SprintAction => sprintAction;
        public Animator Animator => animator;
        public MAnimal Motor => motor;
        public bool GameplayInputEnabled => gameplayInputEnabled;
        public Vector2 CurrentInput { get; private set; }
        public Vector3 CurrentWorldMove { get; private set; }
        public bool IsMoving => CurrentWorldMove.sqrMagnitude > .0001f;
        public bool IsDead => motor && motor.ActiveStateID && motor.ActiveStateID.ID == 10;

        void Awake()
        {
            if (!character) character = GetComponentInChildren<CharacterMorphController>(true);
            if (!cameraOrbit) cameraOrbit = GetComponentInChildren<CinemachineOrbitalFollow>(true);
            moveInput = moveAction ? moveAction.action : null;
            lookInput = lookAction ? lookAction.action : null;
            jumpInput = jumpAction ? jumpAction.action : null;
            sprintInput = sprintAction ? sprintAction.action : null;
            crouchInput = moveInput?.actionMap.FindAction("Crouch");
            if (cameraOrbit)
            {
                gameplayCamera = cameraOrbit.GetComponent<CinemachineCamera>();
                cameraComposer = cameraOrbit.GetComponent<CinemachineRotationComposer>();
                // Movement turns the body toward the orbit heading. Inheriting the
                // body's rotation here creates a positive feedback loop on forward input.
                cameraOrbit.TrackerSettings.BindingMode = BindingMode.WorldSpace;
                cameraOrbit.HorizontalAxis.Recentering.Enabled = false;
                cameraOrbit.VerticalAxis.Recentering.Enabled = false;
                // This component is the sole owner of look input, including on old instances.
                foreach (var input in cameraOrbit.GetComponents<CinemachineInputAxisController>())
                    input.enabled = false;
            }
            // A legacy scene override may still contain the old collider. Never run
            // a CharacterController and a Malbers Rigidbody motor together.
            if (TryGetComponent<CharacterController>(out var legacy)) legacy.enabled = false;
            foreach (var animal in GetComponentsInChildren<MAnimal>(true)) SetMotorActive(animal, false);
            ready = true;
        }

        void Start()
        {
            if (!startInGameplay) return;
            BindAnimator(character ? character.ActiveAnimator : animator);
            SetGameplayInputEnabled(true);
        }

        void OnEnable()
        {
            if (!ready) return;
            EnableActions(gameplayInputEnabled);
            SetMotorActive(motor, gameplayInputEnabled);
        }

        void OnDisable() => SetGameplayInputEnabled(false);

        public void BindAnimator(Animator activeAnimator)
        {
            if (!activeAnimator) throw new System.ArgumentNullException(nameof(activeAnimator));
            var nextMotor = activeAnimator.GetComponent<MAnimal>();
            if (!nextMotor) throw new System.InvalidOperationException("The customized rig needs its Malbers locomotion components. Run Install Human Locomotion Slice.");
            bool changingRig = gameplayInputEnabled && motor && motor != nextMotor;
            var position = changingRig ? motor.transform.position : activeAnimator.transform.position;
            var rotation = changingRig ? motor.transform.rotation : activeAnimator.transform.rotation;
            ClearHeldInput();
            SetMotorActive(motor, false);
            animator = activeAnimator;
            motor = nextMotor;
            var aim = activeAnimator.GetComponent<Aim>();
            activeCharacterRoot = character ? character.ActiveCharacterRoot : animator.transform;
            animator.transform.SetPositionAndRotation(position, rotation);
            if (!changingRig) { spawnPosition = position; spawnRotation = rotation; }
            crouching = false;
            animator.enabled = true;
            motor.UseCameraInput = false;
            motor.UseInPlaceStrafe = true;
            motor.m_MainCamera = new MalbersAnimations.Scriptables.TransformReference(cameraTransform);
            if (aim) { aim.MainCamera = cameraTransform; aim.UseCamera = true; }
            if (gameplayCamera)
            {
                gameplayCamera.Follow = animator.transform;
                gameplayCamera.LookAt = animator.transform;
            }
            FitCapsule();
            SetMotorActive(motor, gameplayInputEnabled);
            jumpState = motor.State_Get(2) as JumpBasic;
            motor.LinearGravityAcceleration = gravityAcceleration;
            if (jumpState) jumpState.BallisticHeight = jumpHeight;
            if (changingRig) motor.Reset_Movement();
        }

        public void SetGameplayInputEnabled(bool value)
        {
            gameplayInputEnabled = value;
            ClearHeldInput();
            if (motor && crouching) motor.Stance_Reset();
            crouching = false;
            SetMotorActive(motor, value);
            if (value && motor)
            {
                jumpState = motor.State_Get(2) as JumpBasic;
                motor.LinearGravityAcceleration = gravityAcceleration;
                if (jumpState) jumpState.BallisticHeight = jumpHeight;
            }
            EnableActions(value);
            if (lockCursorDuringGameplay)
            {
                Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !value;
            }
        }

        static void SetMotorActive(MAnimal animal, bool value)
        {
            if (!animal) return;
            var a = animal.GetComponent<Animator>();
            var body = animal.GetComponent<Rigidbody>();
            if (!value) animal.enabled = false;
            if (body) { body.isKinematic = !value; body.useGravity = false; }
            if (animal.MainCollider) animal.MainCollider.enabled = value;
            if (a) a.applyRootMotion = value;
            if (animal.TryGetComponent<Aim>(out var aimer)) aimer.enabled = value;
            if (animal.TryGetComponent<IKManager>(out var manager)) manager.enabled = value;
            if (value) animal.enabled = true;
        }

        void FitCapsule()
        {
            if (motor.MainCollider is not CapsuleCollider capsule || !animator.isHuman) return;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (!head) return;
            // Read the rebuilt Avatar's rest skeleton, never an animated/crouching
            // head position. This runs only on bind or an actual Avatar replacement.
            var skeleton = animator.avatar.humanDescription.skeleton;
            var headPosition = Vector3.zero;
            for (var bone = head; bone && bone != animator.transform; bone = bone.parent)
            {
                var localPosition = bone.localPosition;
                var localRotation = bone.localRotation;
                var localScale = bone.localScale;
                foreach (var rest in skeleton)
                    if (rest.name == bone.name)
                    { localPosition = rest.position; localRotation = rest.rotation; localScale = rest.scale; break; }
                headPosition = Matrix4x4.TRS(localPosition, localRotation, localScale).MultiplyPoint3x4(headPosition);
            }
            float height = Mathf.Clamp(headPosition.y + .2f, .8f, 3.5f);
            capsule.height = height;
            capsule.radius = Mathf.Clamp(height * .17f, .15f, .45f);
            capsule.center = Vector3.up * (height * .5f);
            standingHeight = height;
            bool colliderEnabled = capsule.enabled;
            // The prefab starts disabled. Stance reset must restore a live collider,
            // not that disabled authoring state.
            capsule.enabled = true;
            motor.SetDefaultMainColliderValues();
            foreach (var stance in motor.Stances)
                if (stance.ID && stance.ID.ID == 7)
                {
                    stance.OverrideCapsule = true;
                    stance.newCapsule.height = Mathf.Max(capsule.radius * 2, height * .62f);
                    stance.newCapsule.center = Vector3.up * (stance.newCapsule.height * .5f);
                    stance.newCapsule.radius = capsule.radius;
                    stance.newCapsule.modify = CapsuleModifier.height | CapsuleModifier.center | CapsuleModifier.radius;
                }
            motor.Restore_MainCollider();
            capsule.enabled = colliderEnabled;
            var origin = animator.transform.Find("Locomotion Aim Origin");
            if (origin) origin.localPosition = headPosition;
            fittedAvatar = animator.avatar;
            if (cameraOrbit) cameraOrbit.TargetOffset = Vector3.up * (height * .78f);
            if (cameraComposer) cameraComposer.TargetOffset = Vector3.up * (height * .78f);
        }

        void Update()
        {
            if (!gameplayInputEnabled || !motor) return;
            // ActiveAnimator searches the hierarchy. Only resolve it when the root changes.
            if (character && character.ActiveCharacterRoot != activeCharacterRoot) BindAnimator(character.ActiveAnimator);
            if (animator.avatar != fittedAvatar) FitCapsule();
            if (Time.timeScale <= 0) { ClearHeldInput(); return; }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (lockCursorDuringGameplay && Cursor.lockState != CursorLockMode.Locked)
            {
                ClearHeldInput();
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
                return;
            }
            UpdateCameraLook();
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) { Respawn(); return; }
            if (keyboard != null && keyboard.kKey.wasPressedThisFrame) { TestDeath(); return; }
            if (IsDead) { ClearHeldInput(); return; }
            CurrentInput = Vector2.ClampMagnitude(moveInput?.ReadValue<Vector2>() ?? Vector2.zero, 1);
            // Use the player's orbit heading, independent of positional/composer damping.
            // Reading the rendered camera here would feed camera lag back into steering.
            var forward = cameraOrbit
                ? Quaternion.Euler(0, cameraOrbit.HorizontalAxis.Value, 0) * Vector3.forward
                : Vector3.ProjectOnPlane(cameraTransform ? cameraTransform.forward : Vector3.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            CurrentWorldMove = forward * CurrentInput.y + right * CurrentInput.x;
            motor.SetWorldMovementInput(CurrentWorldMove);
            motor.Sprint = sprintInput != null && sprintInput.IsPressed();
            UpdateJumpInput();
            if (crouchInput != null && crouchInput.WasPressedThisFrame())
            {
                if (motor.Stance.ID == 7) { if (CanStand()) motor.Stance_Reset(); }
                else motor.Stance_Set(7);
                crouching = motor.Stance.ID == 7;
            }
            if (keyboard != null)
            {
                if (keyboard.tabKey.wasPressedThisFrame) motor.Strafe_Toggle();
                if (keyboard.digit1Key.wasPressedThisFrame) motor.SpeedDown();
                if (keyboard.digit2Key.wasPressedThisFrame) motor.SpeedUp();
            }
            if (animator.transform.position.y < -15) Respawn();
        }

        bool CanStand()
        {
            var capsule = motor.MainCollider;
            float scale = motor.ScaleFactor;
            float radius = capsule.radius * scale;
            var bottom = motor.transform.position + motor.transform.up * radius;
            var top = motor.transform.position + motor.transform.up * (standingHeight * scale - radius);
            return !Physics.CheckCapsule(bottom, top, Mathf.Max(.01f, radius - .015f), motor.GroundLayer, QueryTriggerInteraction.Ignore);
        }

        [ContextMenu("Test Death (Play Mode)")]
        public void TestDeath()
        {
            if (!gameplayInputEnabled || !motor || IsDead) return;
            ClearHeldInput();
            motor.Stance_Reset();
            crouching = false;
            motor.State_Force(10);
        }

        [ContextMenu("Respawn (Play Mode)")]
        public void Respawn()
        {
            if (!gameplayInputEnabled || !motor) return;
            ClearHeldInput();
            var displacement = spawnPosition - motor.transform.position;
            // Cancel delayed death cleanup before restarting the state machine.
            motor.StopAllCoroutines();
            motor.Reset_Movement();
            motor.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            motor.ResetController();
            motor.Stance_Reset();
            motor.Restore_MainCollider();
            crouching = false;
            SetMotorActive(motor, true);
            if (motor.RB) { motor.RB.position = spawnPosition; motor.RB.rotation = spawnRotation; motor.RB.linearVelocity = Vector3.zero; }
            if (gameplayCamera) gameplayCamera.OnTargetObjectWarped(motor.transform, displacement);
        }

        void UpdateJumpInput()
        {
            bool pressed = jumpInput != null && jumpInput.IsPressed();
            if (pressed && !jumpWasPressed) jumpBufferedUntil = Time.time + jumpInputBuffer;
            jumpWasPressed = pressed;
            bool activeJump = motor.ActiveStateID.ID == 2;
            bool request = activeJump ? jumpHeld && pressed
                : jumpBufferedUntil >= Time.time && (motor.Grounded || (jumpState && jumpState.InCoyoteTime));
            if (request != jumpHeld)
            { motor.State_Activate_by_Input(2, request); jumpHeld = request; }
            if (motor.ActiveStateID.ID == 2) jumpBufferedUntil = -1;
        }

        void ClearHeldInput()
        {
            CurrentInput = Vector2.zero;
            CurrentWorldMove = Vector3.zero;
            if (motor)
            {
                motor.StopMoving();
                motor.Sprint = false;
                if (jumpHeld) motor.State_Activate_by_Input(2, false);
            }
            jumpHeld = false;
            jumpWasPressed = false;
            jumpBufferedUntil = -1;
        }

        void UpdateCameraLook()
        {
            var action = lookInput;
            if (action == null) return;
            Vector2 input = action.ReadValue<Vector2>();
            float factor = action.activeControl?.device is Pointer ? mouseLookSensitivity : gamepadLookSpeed * Time.deltaTime;
            if (cameraOrbit)
            {
                cameraOrbit.HorizontalAxis.Value = cameraOrbit.HorizontalAxis.ClampValue(cameraOrbit.HorizontalAxis.Value + input.x * factor);
                cameraOrbit.VerticalAxis.Value = cameraOrbit.VerticalAxis.ClampValue(cameraOrbit.VerticalAxis.Value - input.y * factor);
            }
            else if (cameraYawRoot && cameraPitchRoot)
            {
                cameraYawRoot.Rotate(Vector3.up, input.x * factor, Space.World);
                cameraPitch = Mathf.Clamp(cameraPitch - input.y * factor, minimumPitch, maximumPitch);
                cameraPitchRoot.localRotation = Quaternion.Euler(cameraPitch, 0, 0);
            }
        }

        void EnableActions(bool value)
        {
            SetActionEnabled(moveInput, value);
            SetActionEnabled(lookInput, value);
            SetActionEnabled(jumpInput, value);
            SetActionEnabled(sprintInput, value);
            SetActionEnabled(crouchInput, value);
        }

        static void SetActionEnabled(InputAction action, bool value)
        {
            if (action == null) return;
            if (value) action.Enable(); else action.Disable();
        }
    }
}
