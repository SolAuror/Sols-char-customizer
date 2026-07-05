using UnityEngine;
using UnityEngine.InputSystem;

namespace Sol.CharacterCustomization
{
    public sealed class DemoPlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private CharacterController characterController;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 2.5f;
        [SerializeField, Min(0f)] private float rotationSharpness = 14f;
        [SerializeField, Min(0f)] private float gravity = 20f;
        [SerializeField, Min(0f)] private float movementDeadZone = 0.01f;

        [Header("Animation")]
        [SerializeField] private string isMovingParameter = "isMoving";

        private float verticalVelocity;
        private bool gameplayInputEnabled;
        private bool hasLockedMovementBasis;
        private Vector3 lockedMovementForward;
        private Vector3 lockedMovementRight;

        public InputActionReference MoveAction => moveAction;
        public Animator Animator => animator;
        public bool GameplayInputEnabled => gameplayInputEnabled;
        public Vector2 CurrentInput { get; private set; }
        public Vector3 CurrentWorldMove { get; private set; }
        public bool IsMoving { get; private set; }

        private void Awake()
        {
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
        }

        private void OnEnable()
        {
            SetMoveActionEnabled(gameplayInputEnabled);
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

            LockFacingToMovementForward();
            CurrentInput = ReadMoveInput();
            CurrentWorldMove = BuildWorldMove(CurrentInput);
            IsMoving = CurrentWorldMove.sqrMagnitude > movementDeadZone * movementDeadZone;

            Move(CurrentWorldMove);
            UpdateAnimator(IsMoving);
        }

        public void SetGameplayInputEnabled(bool enabled)
        {
            gameplayInputEnabled = enabled;
            if (enabled)
            {
                CaptureMovementBasis();
                LockFacingToMovementForward();
            }

            if (!enabled)
            {
                CurrentInput = Vector2.zero;
                CurrentWorldMove = Vector3.zero;
                IsMoving = false;
                verticalVelocity = 0f;
                hasLockedMovementBasis = false;
                UpdateAnimator(false);
            }

            if (isActiveAndEnabled)
            {
                SetMoveActionEnabled(enabled);
            }
            else if (!enabled)
            {
                SetMoveActionEnabled(false);
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

        private Vector3 BuildWorldMove(Vector2 input)
        {
            if (input.sqrMagnitude <= 0f)
            {
                return Vector3.zero;
            }

            GetMovementBasis(out Vector3 forward, out Vector3 right);

            Vector3 move = forward * input.y + right * input.x;
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        private void CaptureMovementBasis()
        {
            if (!TryGetPlanarDirection(cameraTransform != null ? cameraTransform.forward : Vector3.zero, out lockedMovementForward) &&
                !TryGetPlanarDirection(animator != null ? animator.transform.forward : Vector3.zero, out lockedMovementForward) &&
                !TryGetPlanarDirection(transform.forward, out lockedMovementForward))
            {
                lockedMovementForward = Vector3.forward;
            }

            if (!TryGetPlanarDirection(cameraTransform != null ? cameraTransform.right : Vector3.zero, out lockedMovementRight) &&
                !TryGetPlanarDirection(animator != null ? animator.transform.right : Vector3.zero, out lockedMovementRight))
            {
                lockedMovementRight = Vector3.Cross(Vector3.up, lockedMovementForward);
            }

            hasLockedMovementBasis = true;
        }

        private void GetMovementBasis(out Vector3 forward, out Vector3 right)
        {
            if (!hasLockedMovementBasis)
            {
                CaptureMovementBasis();
            }

            forward = lockedMovementForward;
            right = lockedMovementRight;
        }

        private void LockFacingToMovementForward()
        {
            if (!hasLockedMovementBasis)
            {
                CaptureMovementBasis();
            }

            Quaternion targetBodyRotation = Quaternion.LookRotation(lockedMovementForward, Vector3.up);
            if (animator != null && animator.transform != transform && animator.transform.IsChildOf(transform))
            {
                Quaternion bodyRotationRelativeToPlayer = Quaternion.Inverse(transform.rotation) * animator.transform.rotation;
                transform.rotation = targetBodyRotation * Quaternion.Inverse(bodyRotationRelativeToPlayer);
                return;
            }

            transform.rotation = targetBodyRotation;
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

        private void RotateTowards(Vector3 moveDirection)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                1f - Mathf.Exp(-rotationSharpness * Time.deltaTime));
        }

        private void Move(Vector3 moveDirection)
        {
            Vector3 horizontalMotion = moveDirection * moveSpeed;
            if (characterController == null)
            {
                transform.position += horizontalMotion * Time.deltaTime;
                return;
            }

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -1f;
            }

            verticalVelocity -= gravity * Time.deltaTime;
            Vector3 velocity = horizontalMotion + Vector3.up * verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
        }

        private void UpdateAnimator(bool isMoving)
        {
            if (animator != null && !string.IsNullOrEmpty(isMovingParameter))
            {
                animator.SetBool(isMovingParameter, isMoving);
            }
        }

        private void SetMoveActionEnabled(bool enabled)
        {
            InputAction action = moveAction != null ? moveAction.action : null;
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
    }
}
