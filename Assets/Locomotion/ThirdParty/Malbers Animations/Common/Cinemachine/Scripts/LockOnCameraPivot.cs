// Last Change: 10/8/2026 6:40pm
using MalbersAnimations.Scriptables;
using Unity.Cinemachine;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary>Pivot for a Lock On camera: sits on the Follow target and turns toward the Locked target, so a Third Person Follow body keeps the camera behind the player relative to the enemy</summary>
    [AddComponentMenu("Malbers/Camera/Lock On Camera Pivot (Cinemachine)")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/utilities/lock-on-target")]
    public class LockOnCameraPivot : MonoBehaviour
    {
        [Tooltip("Transform the pivot sits on (the Player Camera Target). Set by the [Camera Target] variable or the [Set Camera Settings] event")]
        public TransformReference Follow = new();

        [Tooltip("Locked target the pivot faces. Use the [Locked Target] variable set by the Lock On Target component")]
        public TransformReference Target = new();

        [Tooltip("Optional. Transform placed between the Follow and the Target. Use it as the camera LookAt so both stay on screen")]
        public Transform AimPoint;

        [Tooltip("Where the Aim Point sits between the Follow (0) and the Target (1)")]
        [Range(0f, 1f)] public FloatReference AimPointBias = new(0.5f);

        [Tooltip("Height added to the Follow position when placing the Aim Point")]
        public FloatReference AimPointHeight = new(0f);

        [Tooltip("Seconds the pivot takes to turn toward the target direction. Zero turns instantly")]
        public FloatReference RotationSmoothTime = new(0.15f);

        [Tooltip("Keep facing the last locked position after the lock is released, so the camera does not swing around while it blends out")]
        public BoolReference HoldLastTarget = new(true);

        [Tooltip("Ignore the height difference so the pivot only turns around the Up axis")]
        public BoolReference OnlyYaw = new(true);

        [Tooltip("Unparent the pivot on Awake. Needed when the pivot is a child of the Cinemachine Camera, since the camera moves its own transform")]
        public bool DetachOnAwake = true;

        [Tooltip("Cinemachine Brain used to match its update method (Late or Fixed Update). Found automatically when empty")]
        public CinemachineBrain Brain;

        /// <summary>Target the pivot faced on the last update, used to snap when a new lock starts</summary>
        private Transform lastTarget;

        /// <summary>Last position of the locked target, used while the lock is released</summary>
        private Vector3 heldTargetPosition;

        /// <summary>True when there is a held target position to face</summary>
        private bool hasHeldTarget;

        /// <summary>Unparents the pivot so the Cinemachine Camera transform does not drag it around</summary>
        private void Awake()
        {
            if (DetachOnAwake && transform.parent != null)
            {
                transform.SetParent(null, true);
                if (AimPoint != null && AimPoint.parent != transform) AimPoint.SetParent(transform, true);
            }
        }

        /// <summary>Snaps the pivot to the current targets so the first frame is already correct</summary>
        private void OnEnable() => Snap();

        /// <summary>Updates the pivot in Fixed Update when the Brain also updates there</summary>
        private void FixedUpdate()
        {
            if (UsingFixedUpdate) Tick(Time.fixedDeltaTime);
        }

        /// <summary>Updates the pivot after the characters moved, before the Brain reads it</summary>
        private void LateUpdate()
        {
            if (!UsingFixedUpdate) Tick(Time.deltaTime);
        }

        /// <summary>True when the Brain updates its cameras in Fixed Update</summary>
        private bool UsingFixedUpdate
        {
            get
            {
                if (Brain == null && CinemachineBrain.ActiveBrainCount > 0) Brain = CinemachineBrain.GetActiveBrain(0);
                return Brain != null && Brain.UpdateMethod == CinemachineBrain.UpdateMethods.FixedUpdate;
            }
        }

        /// <summary>Sets the transform the pivot sits on</summary>
        public void SetFollow(Transform value) => Follow.Value = value;

        /// <summary>Sets the transform the pivot faces. Null returns the pivot to the Follow facing</summary>
        public void SetTarget(Transform value) => Target.Value = value;

        /// <summary>Snaps the pivot to its desired position and rotation without smoothing</summary>
        public void Snap()
        {
            var follow = Follow.Value;
            if (follow == null) return;

            var target = Target.Value;
            hasHeldTarget = target != null;
            if (hasHeldTarget) heldTargetPosition = target.position;

            transform.SetPositionAndRotation(follow.position, DesiredRotation(follow));
            PlaceAimPoint(follow);
            lastTarget = target;
        }

        /// <summary>Moves the pivot onto the Follow and turns it toward the Target, smoothing the rotation over time</summary>
        private void Tick(float deltaTime)
        {
            var follow = Follow.Value;
            if (follow == null) return;

            var target = Target.Value;

            if (target != lastTarget && lastTarget == null) //A new lock started: snap so the camera blend starts from the right place
            {
                Snap();
                return;
            }

            lastTarget = target;

            if (target != null)
            {
                heldTargetPosition = target.position;
                hasHeldTarget = true;
            }
            else if (!HoldLastTarget.Value)
            {
                hasHeldTarget = false;
            }

            var desired = DesiredRotation(follow);
            var smooth = RotationSmoothTime.Value;

            var rotation = smooth <= 0f ? desired : Quaternion.Slerp(transform.rotation, desired, 1f - Mathf.Exp(-deltaTime / smooth));

            transform.SetPositionAndRotation(follow.position, rotation);
            PlaceAimPoint(follow);
        }

        /// <summary>Rotation looking from the Follow toward the Target (or the held position), or the Follow facing when there is none</summary>
        private Quaternion DesiredRotation(Transform follow)
        {
            var direction = hasHeldTarget ? heldTargetPosition - follow.position : follow.forward;

            if (OnlyYaw.Value) direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f) return transform.rotation; //Target on top of the Follow: keep the current facing

            return Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>Places the Aim Point between the Follow and the Target (or the held position)</summary>
        private void PlaceAimPoint(Transform follow)
        {
            if (AimPoint == null) return;

            var origin = follow.position + Vector3.up * AimPointHeight.Value;
            AimPoint.position = hasHeldTarget ? Vector3.Lerp(origin, heldTargetPosition, AimPointBias.Value) : origin;
        }

#if UNITY_EDITOR
        /// <summary>Draws the pivot facing and the Aim Point</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, transform.forward);

            if (AimPoint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(AimPoint.position, 0.1f);
                Gizmos.DrawLine(transform.position, AimPoint.position);
            }
        }

        /// <summary>Wires the default Camera Target and Locked Target variables</summary>
        private void Reset()
        {
            var cameraTarget = MTools.GetInstance<TransformVar>("Camera Target");
            var lockedTarget = MTools.GetInstance<TransformVar>("Locked Target");

            if (cameraTarget != null) Follow = new TransformReference(cameraTarget);
            if (lockedTarget != null) Target = new TransformReference(lockedTarget);
        }
#endif
    }
}
