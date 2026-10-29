// Last Change: 10/12/2026 1:45pm
using System;
using UnityEngine;

namespace MalbersAnimations.SA
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [AddComponentMenu("Malbers/Utilities/Standard Asset/Rigidbody FPS Controller")]
    public class MRigidbodyFPSController : MonoBehaviour, IObjectCore, ICharacterMove
    {
        public Camera cam;
        [Tooltip("Keep the cursor locked to the center of the screen and hidden. It is locked again if something releases it (e.g. Escape in the Editor)")]
        public bool LockCursor;

        public bool lockMovement = false;
        public MMovementSettings movementSettings = new();
        public MMouseLook mouseLook = new();
        public MAdvancedSettings advancedSettings = new();


        private Rigidbody m_RigidBody;
        private CapsuleCollider m_Capsule;
        private Vector3 m_GroundContactNormal;
        private bool m_Jump, m_PreviouslyGrounded, m_Jumping, m_IsGrounded;
        private float oldYRotation;

        public bool LockMovement { get => lockMovement; set => lockMovement = value; }
        public Vector3 Velocity => m_RigidBody.linearVelocity;

        public bool Grounded => m_IsGrounded;

        public bool Jumping => m_Jumping;

        public bool Running => movementSettings.Running;

        public bool MovementDetected => throw new NotImplementedException();

        /// <summary>Cache the components, apply the cursor lock and reset the mouse look</summary>
        private void Start()
        {
            m_RigidBody = GetComponent<Rigidbody>();
            m_Capsule = GetComponent<CapsuleCollider>();

            ApplyCursorLock();
            RestartMouseLook();
        }

        /// <summary>Lock or unlock the cursor and keep it that way</summary>
        public virtual void SetCursorLock(bool value)
        {
            LockCursor = value;
            ApplyCursorLock();
        }

        /// <summary>Apply the Lock Cursor value to the cursor</summary>
        protected virtual void ApplyCursorLock()
        {
            Cursor.lockState = LockCursor ? CursorLockMode.Locked : CursorLockMode.None;  // Lock or unlock the cursor.
            Cursor.visible = !LockCursor;
        }

        /// <summary>Keep the cursor lock, rotate the view and read the jump input</summary>
        private void Update()
        {
            //The cursor can be released by something else (Escape in the Editor, losing focus). Lock it again
            if (LockCursor && Cursor.lockState != CursorLockMode.Locked) ApplyCursorLock();

            RotateView();

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetButtonDown("Jump") && !m_Jump) m_Jump = true;
#endif
        }

        public void RestartMouseLook()
        {
            mouseLook.Init(transform, cam.transform);
        }


        public virtual void Jump(bool value) => m_Jump = value;


        private void FixedUpdate()
        {
            if (lockMovement) return;

            GroundCheck();
            Vector2 input = GetInput();

            if ((Mathf.Abs(input.x) > float.Epsilon || Mathf.Abs(input.y) > float.Epsilon) && (advancedSettings.airControl || m_IsGrounded))
            {
                // always move along the camera forward as it is the direction that it being aimed at
                Vector3 desiredMove = cam.transform.forward * input.y + cam.transform.right * input.x;
                desiredMove = Vector3.ProjectOnPlane(desiredMove, m_GroundContactNormal).normalized;

                desiredMove.x *= movementSettings.CurrentTargetSpeed;
                desiredMove.z *= movementSettings.CurrentTargetSpeed;
                desiredMove.y *= movementSettings.CurrentTargetSpeed;
                if (m_RigidBody.linearVelocity.sqrMagnitude <
                    (movementSettings.CurrentTargetSpeed * movementSettings.CurrentTargetSpeed))
                {
                    m_RigidBody.AddForce(desiredMove * SlopeMultiplier(), ForceMode.Impulse);
                }
            }

            if (m_IsGrounded)
            {
                m_RigidBody.linearDamping = 5f;

                if (m_Jump)
                {
                    m_RigidBody.linearDamping = 0f;
                    m_RigidBody.linearVelocity = new Vector3(m_RigidBody.linearVelocity.x, 0f, m_RigidBody.linearVelocity.z);
                    m_RigidBody.AddForce(new Vector3(0f, movementSettings.JumpForce, 0f), ForceMode.Impulse);
                    m_Jumping = true;
                }

                if (!m_Jumping && Mathf.Abs(input.x) < float.Epsilon && Mathf.Abs(input.y) < float.Epsilon && m_RigidBody.linearVelocity.magnitude < 1f)
                {
                    m_RigidBody.Sleep();
                }
            }
            else
            {
                m_RigidBody.linearDamping = 0f;
                if (m_PreviouslyGrounded && !m_Jumping)
                {
                    StickToGroundHelper();
                }
            }
            m_Jump = false;
        }

        private float SlopeMultiplier()
        {
            float angle = Vector3.Angle(m_GroundContactNormal, Vector3.up);
            return movementSettings.SlopeCurveModifier.Evaluate(angle);
        }

        private void StickToGroundHelper()
        {
            RaycastHit hitInfo;
            if (Physics.SphereCast(transform.position, m_Capsule.radius, Vector3.down, out hitInfo,
                                   ((m_Capsule.height / 2f) - m_Capsule.radius) +
                                   advancedSettings.stickToGroundHelperDistance))
            {
                if (Mathf.Abs(Vector3.Angle(hitInfo.normal, Vector3.up)) < 85f)
                {
                    if (!m_RigidBody.isKinematic) m_RigidBody.linearVelocity = Vector3.ProjectOnPlane(m_RigidBody.linearVelocity, hitInfo.normal);
                }
            }
        }

        private Vector2 GetInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            Vector2 input = new Vector2
            {
                x = Input.GetAxis("Horizontal"),
                y = Input.GetAxis("Vertical")
            };
#else
            //Using the new Input System to get Horizontal and Vertical axis
            Vector2 input = new Vector2
            {
                x = UnityEngine.InputSystem.Keyboard.current.aKey.isPressed ? -1 :
                    UnityEngine.InputSystem.Keyboard.current.dKey.isPressed ? 1 : 0,
                y = UnityEngine.InputSystem.Keyboard.current.sKey.isPressed ? -1 :
                    UnityEngine.InputSystem.Keyboard.current.wKey.isPressed ? 1 : 0
            };
#endif
            movementSettings.UpdateDesiredTargetSpeed(input);
            return input;
        }



        public virtual void RotateView()
        {
            //avoids the mouse looking if the game is effectively paused
            if (Mathf.Abs(Time.timeScale) < float.Epsilon) return;

            mouseLook.Init(transform, cam.transform); //Restart because its needed

            // get the rotation before it's changed
            oldYRotation = transform.eulerAngles.y;

            mouseLook.LookRotation(transform, cam.transform);

            if (m_IsGrounded || advancedSettings.airControl)
            {
                // Rotate the rigidbody velocity to match the new direction that the character is looking
                Quaternion velRotation = Quaternion.AngleAxis(transform.eulerAngles.y - oldYRotation, Vector3.up);
                if (!m_RigidBody.isKinematic)
                    m_RigidBody.linearVelocity = velRotation * m_RigidBody.linearVelocity;
            }
        }

        /// sphere cast down just beyond the bottom of the capsule to see if the capsule is colliding round the bottom
        private void GroundCheck()
        {
            m_PreviouslyGrounded = m_IsGrounded;
            RaycastHit hitInfo;
            if (Physics.SphereCast(transform.position, m_Capsule.radius, Vector3.down, out hitInfo,
                                   ((m_Capsule.height / 2f) - m_Capsule.radius) + advancedSettings.groundCheckDistance))
            {
                m_IsGrounded = true;
                m_GroundContactNormal = hitInfo.normal;
            }
            else
            {
                m_IsGrounded = false;
                m_GroundContactNormal = Vector3.up;
            }
            if (!m_PreviouslyGrounded && m_IsGrounded && m_Jumping)
            {
                m_Jumping = false;
            }
        }

        private Vector3 ExternalInput;

        public void Move(Vector3 Direction) => ExternalInput = Direction;

        public void RotateAtDirection(Vector3 Direction) => ExternalInput = Direction;

        public void StopMoving() => ExternalInput = Vector3.zero;

        public void SetInputAxis(Vector3 inputAxis) => ExternalInput = inputAxis;

        public void SetInputAxis(Vector2 inputAxis) => ExternalInput = inputAxis;

    }

    [Serializable]
    public class MMovementSettings
    {
        public float ForwardSpeed = 8.0f;   // Speed when walking forward
        public float BackwardSpeed = 4.0f;  // Speed when walking backwards
        public float StrafeSpeed = 4.0f;    // Speed when walking sideways
        public float RunMultiplier = 2.0f;   // Speed when sprinting

#if ENABLE_LEGACY_INPUT_MANAGER
        public KeyCode RunKey = KeyCode.LeftShift;
#else
        public UnityEngine.InputSystem.Key RunKey = UnityEngine.InputSystem.Key.LeftShift;
#endif
        public float JumpForce = 30f;
        public AnimationCurve SlopeCurveModifier = new(new Keyframe(-90.0f, 1.0f), new Keyframe(0.0f, 1.0f), new Keyframe(90.0f, 0.0f));
        [HideInInspector]
        public float CurrentTargetSpeed = 8f;

        private bool m_Running;

        public void UpdateDesiredTargetSpeed(Vector2 input)
        {
            if (input == Vector2.zero) return;

            if (input.x > 0 || input.x < 0)
            {
                //strafe
                CurrentTargetSpeed = StrafeSpeed;
            }
            if (input.y < 0)
            {
                //backwards
                CurrentTargetSpeed = BackwardSpeed;
            }
            if (input.y > 0)
            {
                //forwards
                //handled last as if strafing and moving forward at the same time forwards speed should take precedence
                CurrentTargetSpeed = ForwardSpeed;
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(RunKey))
#else
            //Find the Left Shift key state using the new Input System
            if (UnityEngine.InputSystem.Keyboard.current[RunKey].isPressed)
#endif

            {
                CurrentTargetSpeed *= RunMultiplier;
                m_Running = true;
            }
            else
            {
                m_Running = false;
            }
        }

        public bool Running
        {
            get { return m_Running; }
        }
    }

    [Serializable]
    public class MAdvancedSettings
    {
        public float groundCheckDistance = 0.01f;               // distance for checking if the controller is grounded ( 0.01f seems to work best for this )
        public float stickToGroundHelperDistance = 0.5f;        // stops the character
        public float slowDownRate = 20f;                        // rate at which the controller comes to a stop when there is no input
        public bool airControl;                                 // can the user control the direction that is being moved in the air
    }
}
