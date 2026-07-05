using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

namespace Sol.CharacterCustomization
{
    public sealed class DemoPlayerController : MonoBehaviour
    {
        private const int PlayerLayerIndex = 3;
        private const int PlayerLayerMask = 1 << PlayerLayerIndex;
        private const int AllLayersExceptPlayer = ~PlayerLayerMask;

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
        [SerializeField] private CharacterController characterController;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 4f;
        [SerializeField, Min(0f)] private float sprintSpeed = 6f;
        [SerializeField, Min(0f)] private float rotationSpeed = 720f;
        [SerializeField, Min(0f)] private float movementDeadZone = 0.01f;

        [Header("Look")]
        [SerializeField, Min(0f)] private float mouseLookSensitivity = 0.12f;
        [SerializeField, Min(0f)] private float gamepadLookSpeed = 180f;
        [SerializeField] private float minimumPitch = -30f;
        [SerializeField] private float maximumPitch = 60f;
        [SerializeField] private bool lockCursorDuringGameplay = true;

        [Header("Gravity")]
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float groundedVerticalSpeed = -2f;

        [Header("Grounding")]
        [SerializeField] private LayerMask groundLayers = AllLayersExceptPlayer;
        [SerializeField, Min(0f)] private float groundCheckDistance = 0.08f;
        [SerializeField, Range(0.5f, 1f)] private float groundCheckRadiusScale = 0.9f;

        [Header("Jump")]
        [SerializeField, Min(0f)] private float jumpHeight = 1.2f;

        [Header("Animation")]
        [SerializeField] private string isMovingParameter = "isMoving";

        private float verticalSpeed;
        private float cameraPitch;
        private bool gameplayInputEnabled;
        private bool isGrounded;

        public InputActionReference MoveAction => moveAction;
        public InputActionReference LookAction => lookAction;
        public InputActionReference JumpAction => jumpAction;
        public InputActionReference SprintAction => sprintAction;
        public Animator Animator => animator;
        public bool GameplayInputEnabled => gameplayInputEnabled;
        public Vector2 CurrentInput { get; private set; }
        public Vector3 CurrentWorldMove { get; private set; }
        public bool IsMoving { get; private set; }

        private void Awake()
        {
            if (cameraTransform == null)
            {
                Camera childCamera = GetComponentInChildren<Camera>(true);
                cameraTransform = childCamera != null ? childCamera.transform : null;
            }

            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (cameraOrbit == null)
            {
                cameraOrbit = GetComponentInChildren<CinemachineOrbitalFollow>(true);
            }

            if (cameraYawRoot == null)
            {
                cameraYawRoot = cameraTransform != null ? cameraTransform.parent : null;
            }

            if (cameraPitchRoot == null)
            {
                cameraPitchRoot = cameraYawRoot;
            }

            cameraPitch = NormalizePitch(cameraPitchRoot != null ? cameraPitchRoot.localEulerAngles.x : 0f);
            if (cameraOrbit != null)
            {
                cameraPitch = cameraOrbit.VerticalAxis.Value;
            }

            isGrounded = CheckGrounded();
        }

        private void OnEnable()
        {
            SetInputActionsEnabled(gameplayInputEnabled);
            UpdateAnimator(false);
        }

        private void OnDisable()
        {
            SetGameplayInputEnabled(false);
        }

        private void Update()
        {
            if (!gameplayInputEnabled)
            {
                return;
            }

            UpdateCameraLook();
            CurrentInput = ReadMoveInput();
            UpdateMovement(CurrentInput);

            UpdateAnimator(IsMoving);
        }

        public void SetGameplayInputEnabled(bool enabled)
        {
            gameplayInputEnabled = enabled;
            ApplyCursorLock(enabled);

            if (!enabled)
            {
                CurrentInput = Vector2.zero;
                CurrentWorldMove = Vector3.zero;
                IsMoving = false;
                verticalSpeed = 0f;
                UpdateAnimator(false);
            }

            if (isActiveAndEnabled)
            {
                SetInputActionsEnabled(enabled);
            }
            else if (!enabled)
            {
                SetInputActionsEnabled(false);
            }
        }

        public void BindAnimator(Animator activeAnimator)
        {
            animator = activeAnimator;
            if (animator != null)
            {
                animator.enabled = true;
            }

            UpdateAnimator(false);
        }

        private void UpdateCameraLook()
        {
            InputAction action = lookAction != null ? lookAction.action : null;
            if (action == null || (cameraOrbit == null && cameraYawRoot == null))
            {
                return;
            }

            Vector2 lookInput = action.ReadValue<Vector2>();
            if (lookInput.sqrMagnitude <= 0f)
            {
                return;
            }

            bool isPointerInput = action.activeControl?.device is Pointer;
            float yawDelta = isPointerInput
                ? lookInput.x * mouseLookSensitivity
                : lookInput.x * gamepadLookSpeed * Time.deltaTime;
            float pitchDelta = isPointerInput
                ? -lookInput.y * mouseLookSensitivity
                : -lookInput.y * gamepadLookSpeed * Time.deltaTime;

            if (cameraOrbit != null)
            {
                cameraOrbit.HorizontalAxis.Value = cameraOrbit.HorizontalAxis.ClampValue(cameraOrbit.HorizontalAxis.Value + yawDelta);
                cameraOrbit.VerticalAxis.Value = cameraOrbit.VerticalAxis.ClampValue(cameraOrbit.VerticalAxis.Value + pitchDelta);
                cameraPitch = cameraOrbit.VerticalAxis.Value;
                return;
            }

            cameraYawRoot.Rotate(Vector3.up, yawDelta, Space.World);

            if (cameraPitchRoot != null)
            {
                cameraPitch = Mathf.Clamp(cameraPitch + pitchDelta, minimumPitch, maximumPitch);
                cameraPitchRoot.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            }
        }

        private Vector2 ReadMoveInput()
        {
            InputAction action = moveAction != null ? moveAction.action : null;
            if (action == null)
            {
                return Vector2.zero;
            }

            Vector2 input = action.ReadValue<Vector2>();
            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        private bool IsSprintPressed()
        {
            InputAction action = sprintAction != null ? sprintAction.action : null;
            return action != null && action.IsPressed();
        }

        private bool WasJumpPressedThisFrame()
        {
            InputAction action = jumpAction != null ? jumpAction.action : null;
            return action != null && action.WasPressedThisFrame();
        }

        private void UpdateMovement(Vector2 movementInput)
        {
            isGrounded = CheckGrounded();
            if (isGrounded && verticalSpeed < 0f)
            {
                verticalSpeed = groundedVerticalSpeed;
            }

            if (isGrounded && WasJumpPressedThisFrame())
            {
                verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            bool isSprinting = IsSprintPressed() && movementInput.y > movementDeadZone;
            float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;
            CurrentWorldMove = BuildWorldMove(movementInput);

            if (CurrentWorldMove.sqrMagnitude > movementDeadZone * movementDeadZone)
            {
                RotateTowards(CurrentWorldMove);
            }

            verticalSpeed += gravity * Time.deltaTime;
            Move(CurrentWorldMove * currentSpeed + Vector3.up * verticalSpeed);
            IsMoving = CurrentWorldMove.sqrMagnitude > movementDeadZone * movementDeadZone;
        }

        private Vector3 BuildWorldMove(Vector2 input)
        {
            if (input.sqrMagnitude <= movementDeadZone * movementDeadZone)
            {
                return Vector3.zero;
            }

            GetMovementBasis(out Vector3 right, out Vector3 forward);

            Vector3 move = forward * input.y + right * input.x;
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        private void GetMovementBasis(out Vector3 right, out Vector3 forward)
        {
            Transform basis = cameraTransform != null ? cameraTransform : cameraYawRoot;
            if (!TryGetPlanarDirection(basis != null ? basis.forward : Vector3.zero, out forward) &&
                !TryGetPlanarDirection(cameraYawRoot != null ? cameraYawRoot.forward : Vector3.zero, out forward) &&
                !TryGetPlanarDirection(transform.forward, out forward))
            {
                forward = Vector3.forward;
            }

            if (!TryGetPlanarDirection(basis != null ? basis.right : Vector3.zero, out right) &&
                !TryGetPlanarDirection(cameraYawRoot != null ? cameraYawRoot.right : Vector3.zero, out right))
            {
                right = Vector3.Cross(Vector3.up, forward);
            }
        }

        private static bool TryGetPlanarDirection(Vector3 direction, out Vector3 planarDirection)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                planarDirection = Vector3.zero;
                return false;
            }

            planarDirection = direction.normalized;
            return true;
        }

        private bool CheckGrounded()
        {
            if (characterController == null)
            {
                return true;
            }

            float groundCheckRadius = characterController.radius * groundCheckRadiusScale;
            Vector3 groundCheckPosition = characterController.bounds.center +
                                          Vector3.down * (characterController.bounds.extents.y - groundCheckRadius + groundCheckDistance);

            return Physics.CheckSphere(groundCheckPosition, groundCheckRadius, groundLayers, QueryTriggerInteraction.Ignore);
        }

        private void RotateTowards(Vector3 moveDirection)
        {
            if (moveDirection.sqrMagnitude <= 0f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            Transform rotationTarget = animator != null && animator.transform.IsChildOf(transform)
                ? animator.transform
                : transform;

            rotationTarget.rotation = Quaternion.RotateTowards(
                rotationTarget.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        private void Move(Vector3 velocity)
        {
            if (characterController == null)
            {
                transform.position += velocity * Time.deltaTime;
                return;
            }

            characterController.Move(velocity * Time.deltaTime);
        }

        private void UpdateAnimator(bool isMoving)
        {
            if (animator != null && !string.IsNullOrEmpty(isMovingParameter))
            {
                animator.SetBool(isMovingParameter, isMoving);
            }
        }

        private void SetInputActionsEnabled(bool enabled)
        {
            SetActionEnabled(moveAction, enabled);
            SetActionEnabled(lookAction, enabled);
            SetActionEnabled(jumpAction, enabled);
            SetActionEnabled(sprintAction, enabled);
        }

        private void ApplyCursorLock(bool enabled)
        {
            if (!lockCursorDuringGameplay)
            {
                return;
            }

            Cursor.lockState = enabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !enabled;
        }

        private static void SetActionEnabled(InputActionReference actionReference, bool enabled)
        {
            InputAction action = actionReference != null ? actionReference.action : null;
            if (action == null)
            {
                return;
            }

            if (enabled)
            {
                action.Enable();
            }
            else
            {
                action.Disable();
            }
        }

        private static float NormalizePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}
