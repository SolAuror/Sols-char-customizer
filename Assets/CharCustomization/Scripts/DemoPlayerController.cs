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

        public InputActionReference MoveAction => moveAction;
        public Animator Animator => animator;
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

            CurrentInput = ReadMoveInput();
            CurrentWorldMove = BuildWorldMove(CurrentInput);
            IsMoving = CurrentWorldMove.sqrMagnitude > movementDeadZone * movementDeadZone;

            if (IsMoving)
            {
                RotateTowards(CurrentWorldMove);
            }

            Move(CurrentWorldMove);
            UpdateAnimator(IsMoving);
        }

        public void SetGameplayInputEnabled(bool enabled)
        {
            gameplayInputEnabled = enabled;
            if (!enabled)
            {
                CurrentInput = Vector2.zero;
                CurrentWorldMove = Vector3.zero;
                IsMoving = false;
                verticalVelocity = 0f;
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

            Vector3 forward = cameraTransform != null ? cameraTransform.forward : transform.forward;
            Vector3 right = cameraTransform != null ? cameraTransform.right : transform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            Vector3 move = forward * input.y + right * input.x;
            return move.sqrMagnitude > 1f ? move.normalized : move;
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
