// Last Change: 10/16/2026 9:45am
using MalbersAnimations.Scriptables;
using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    // MWC — Humanoid foot grounding: per foot ground solve + pelvis vertical adjustment + knee hints.
    //
    // The solve is expressed entirely in the character's own axes (transform.up / .forward), never world up,
    // so it stays correct under custom gravity, slope alignment and wall running.
    //
    // Three ideas carry most of the quality, see the comments at each site:
    //   1. The cast anchor uses the animated ankle's HORIZONTAL position at the ROOT's height, so the cast
    //      window never moves with the swing and its length is constant.       -> FootAnchor()
    //   2. The ground solution is never allowed to push a foot BELOW where the animation put it. That single
    //      asymmetric clamp is the whole step / swing logic.                   -> SolveFoot(), "swing clamp"
    //   3. The pelvis takes a signed min/max of the two feet, never an average, otherwise every slope and
    //      stair half sinks one foot.                                          -> SolveBody()

    /// <summary> Humanoid foot placement on the ground, with pelvis adjustment and knee hints. </summary>
    [Serializable]
    [AddTypeMenu("Humanoid/Foot Ground Placement")]
    public class HumanIKFootGround : IKProcessorOnAnimIK
    {
        // ─────────────────────────────────────────────────────────────────────
        // Foot
        // ─────────────────────────────────────────────────────────────────────

        [Tooltip("Measure the foot and leg sizes from the rig on Start, so the Processor works on any humanoid " +
                 "with nothing to tune. Turn it off to set them by hand")]
        public bool AutoCalibrate = true; // MWC — hides the eight measured fields below

        [Hide(nameof(AutoCalibrate), true)]
        [Tooltip("Distance from the ankle bone down to the sole. Also used as the radius of the secondary sphere cast")]
        [Min(0)] public float FootHeight; // MWC — 0 still falls back to the measured value, see Calibrate()

        [Hide(nameof(AutoCalibrate), true)]
        [Tooltip("Length of the foot, used as the Z size of the foot shaped box cast")]
        [Min(0)] public float FootLength;

        [Hide(nameof(AutoCalibrate), true)]
        [Tooltip("Width of the foot, used as the X size of the foot shaped box cast")]
        [Min(0)] public float FootWidth;

        [Hide(nameof(AutoCalibrate), true)]
        [Tooltip("Forward shift of the cast origin. Biases the contact point towards the ball of the foot, " +
                 "which is what makes the foot pivot on slopes")]
        [Min(0)] public float FootForwardOffset;

        [Hide(nameof(AutoCalibrate), true)]
        [Tooltip("Maximum distance a foot may be raised above its animated position")]
        [Min(0)] public float MaxFootLift;

        [Tooltip("Raises (+) or lowers (-) both feet on top of the foot height. Use a small negative value when " +
                 "the animations plant the ankle lower than the rig's rest pose does, which shows as feet " +
                 "hovering just above the ground")]
        public float SoleOffset = 0f;

        [Tooltip("Extra length added to every cast so the solve keeps working right at its limits")]
        [Min(0)] public float RayMargin = 0.05f;

        [Tooltip("How fast the foot position follows the ground solution. Higher is snappier")]
        [Range(1, 50)] public float FootSmooth = 8f;

        [Tooltip("How fast the ankle rotation follows the surface normal. Higher is snappier")]
        [Range(1, 50)] public float RotationSmooth = 8f;

        [Tooltip("Maximum angle the ankle may tilt away from the character's up axis")]
        [Range(0, 90)] public float MaxAnkleAngle = 90f;

        [Tooltip("Overshoot applied while the character moves up, so the feet keep up with a rising body")]
        [Range(0, 5)] public float RiseOvershoot = 1f;

        [Tooltip("Overshoot applied while the character moves down. This is what makes a landing read as a landing")]
        [Range(0, 10)] public float FallOvershoot = 2.5f;

        [Tooltip("How hard a foot that overshot below the ground is pulled back. Near instant by design")]
        [Range(0, 250)] public float PenetrationRecovery = 50f;

        [Tooltip("Align the ankle to the surface only while the foot is planted. Turn off to make the feet " +
                 "chase the ground normal during the swing as well")]
        public bool RotateOnlyWhenPlanted = true;

        [Tooltip("A foot whose animated goal moved less than this since the last solve is not cast again. " +
                 "Keeps a standing character at one cast per frame")]
        [Min(0)] public float MinMoveThreshold = 0.003f;

        // ─────────────────────────────────────────────────────────────────────
        // Ground
        // ─────────────────────────────────────────────────────────────────────

        [Tooltip("Layers treated as ground")]
        public LayerReference GroundLayer = new(1);

        [Tooltip("How the casts treat trigger colliders")]
        public QueryTriggerInteraction TriggerInteraction = QueryTriggerInteraction.Ignore;

        [Tooltip("Surfaces steeper than this are not ground at all and the hit is discarded")]
        [Range(0, 90)] public float MaxGroundAngle = 81f;

        [Tooltip("Upper limit for the slope angle used by the math. Kept below [Max Ground Angle] so a valid " +
                 "but very steep surface still grounds the foot without tilting it all the way over")]
        [Range(0, 90)] public float MaxSlopeAngle = 79f;

        [Hide(nameof(AutoCalibrate), true)]
        [Tooltip("Half length of the grounded check sphere cast")]
        [Min(0)] public float GroundedDistance;

        [Hide(nameof(AutoCalibrate), true)]
        [Tooltip("Radius of the grounded check sphere cast")]
        [Min(0)] public float GroundedRadius;

        // ─────────────────────────────────────────────────────────────────────
        // Body
        // ─────────────────────────────────────────────────────────────────────

        [Tooltip("Weight of the pelvis vertical adjustment. Set to 0 to solve the feet only")]
        [Range(0, 1)] public float BodyWeight = 1f;

        [Tooltip("How fast the pelvis follows its target offset. Higher is snappier")]
        [Range(1, 50)] public float BodySmooth = 8f;

        [Hide(nameof(AutoCalibrate), true)]
        [Tooltip("Maximum distance the pelvis may drop to let the lower foot reach the ground")]
        [Min(0)] public float MaxBodyDrop;

        [Tooltip("Dead band around [Max Body Drop], as a fraction of it. Stops the one frame on/off flicker " +
                 "when the correction sits exactly on the limit")]
        [Range(0, 0.3f)] public float BodyTolerance = 0.05f;

        [Tooltip("Let the pelvis rise when both feet end up above the root, so the character can step up onto " +
                 "a ledge its collider does not know about")]
        public bool AllowBodyRise = true;

        [Tooltip("How much a foot the animation is holding up in the air still pulls the pelvis down towards " +
                 "the ground under it. At 0 only the feet near the ground move the pelvis, so running up a " +
                 "slope keeps the hips up and the front knee relaxed. At 1 the pelvis always drops far " +
                 "enough for the lifted foot to land the moment the animation brings it down, at the cost " +
                 "of a deep knee bend on the other leg")]
        [Range(0, 1)] public float SwingFootPull = 0f;

        [Tooltip("Constant vertical shift applied to the pelvis on top of the solve. Does not move the IK goals")]
        public float BodyHeightOffset = 0f;

        // ─────────────────────────────────────────────────────────────────────
        // Knee
        // ─────────────────────────────────────────────────────────────────────

        [Tooltip("Place the knee hints analytically for the corrected ankle position. Without this the knee " +
                 "snaps straight or inverts on large corrections")]
        public bool SolveKneeHints = true;

        [Hide(nameof(SolveKneeHints))]
        [Tooltip("Weight of the knee hints")]
        [Range(0, 1)] public float KneeHintWeight = 1f;

        // ─────────────────────────────────────────────────────────────────────
        // Debug
        // ─────────────────────────────────────────────────────────────────────

        [Tooltip("Draw the live solve with debug lines while playing. Unlike the gizmos this does not need " +
                 "the Processor to stay selected")]
        public bool ShowRuntimeDebug = false;

        public override bool RequireTargets => false; // MWC — every bone comes from the Avatar, not from [Targets]

        // ─────────────────────────────────────────────────────────────────────
        // Runtime state — private only. Never serialize any of this: an IK Set can live inside a shared
        // IKSetProfile asset, and a public cache field would be written straight back into the asset.
        // ─────────────────────────────────────────────────────────────────────

        [NonSerialized] private Transform m_Root;
        [NonSerialized] private Transform m_LeftFoot, m_RightFoot;
        [NonSerialized] private Transform m_LeftUpperLeg, m_RightUpperLeg;
        [NonSerialized] private Transform m_LeftLowerLeg, m_RightLowerLeg;
        [NonSerialized] private bool m_Ready;

        // Calibrated values. These mirror the serialized fields, except that a serialized 0 means
        // "measure it from the rig" and the measured value lands here instead of in the asset.
        [NonSerialized] private float m_FootHeight, m_FootLength, m_FootWidth, m_FootForwardOffset;
        [NonSerialized] private float m_MaxFootLift, m_MaxBodyDrop, m_GroundedDistance, m_GroundedRadius;
        [NonSerialized] private float m_UpperLegLength, m_LowerLegLength;
        [NonSerialized] private float m_SwingFadeHeight; // lift at which a foot in the air stops pulling the pelvis
        [NonSerialized] private float m_OffsetScale = 1; // IK Manager Scale Multiplier, cached by OnAnimatorIK / Gizmos / Handles

        /// <summary>[SoleOffset] multiplied by the IK Manager Scale Multiplier</summary>
        private float ScaledSole => SoleOffset * m_OffsetScale;

        [NonSerialized] private Vector3 m_OffsetL, m_OffsetR;          // smoothed foot offsets
        [NonSerialized] private Vector3 m_ExtrapolationL, m_ExtrapolationR;
        [NonSerialized] private Quaternion m_RotationL = Quaternion.identity, m_RotationR = Quaternion.identity;
        [NonSerialized] private Vector3 m_BodyOffset;                  // smoothed pelvis offset
        [NonSerialized] private Vector3 m_RawBodyOffset;
        [NonSerialized] private float m_GroundBlend, m_BodyBlend;      // grounded fade, 0..1
        [NonSerialized] private bool m_WasGrounded;

        [NonSerialized] private FootSolve m_SolveL, m_SolveR;
        [NonSerialized] private Vector3 m_LastGoalL, m_LastGoalR;      // for the movement threshold
        [NonSerialized] private Vector3 m_LastRootPos;
        [NonSerialized] private Vector3 m_HintL, m_HintR;
        [NonSerialized] private bool m_HasHints;

        // Final absolute goals, resolved once per tick. They are stored rather than rebuilt from
        // GetIKPosition on every pass because several Animator layers can run an IK Pass, and a later pass
        // reads back whatever the previous one wrote, which would add the offset once per layer.
        [NonSerialized] private Vector3 m_FinalGoalL, m_FinalGoalR;
        [NonSerialized] private Quaternion m_FinalRotL = Quaternion.identity, m_FinalRotR = Quaternion.identity;

        [NonSerialized] private double m_LastTick = -1;
        [NonSerialized] private float m_PeakBodyOffset, m_PeakFootLift;

        /// <summary> Vertical offset currently applied to the pelvis, in world space. </summary>
        public Vector3 BodyOffset => m_BodyOffset;

        /// <summary> True while the grounded sphere cast is finding ground under the character. </summary>
        public bool IsGrounded { get; private set; }

        /// <summary> Hit of the grounded sphere cast of this frame. </summary>
        public RaycastHit GroundHit { get; private set; }

        /// <summary> Largest pelvis drop seen so far. Useful to calibrate [Max Body Drop]. </summary>
        public float PeakBodyOffset => m_PeakBodyOffset;

        /// <summary> Largest foot correction seen so far. Useful to calibrate [Max Foot Lift]. </summary>
        public float PeakFootLift => m_PeakFootLift;

        // ─────────────────────────────────────────────────────────────────────
        // One foot's solve result
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Result of one foot's ground solve for the current tick. </summary>
        private struct FootSolve
        {
            public Vector3 Goal;          // ankle target after every clamp
            public Vector3 PlantedPos;    // the pure ground solution, kept as a floor for the pelvis pass
            public Vector3 SurfacePoint;  // contact point below the ankle
            public Vector3 Normal;        // chosen surface normal
            public Transform Ground;      // collider transform, so a moving platform can force a re-solve
            public Vector3 GroundPos;     // its pose at the time of the solve
            public Quaternion GroundRot;
            public bool Planted;          // false while the animation is holding the foot above the ground
            public bool Hit;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Resolves the leg bones from the Avatar and calibrates every field left at 0. </summary>
        public override void Start(IKSet set, Animator anim, int index)
        {
            m_Ready = false;

            if (anim == null) return;

            if (!anim.isHuman)
            {
                Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] The Animator is not Humanoid. " +
                    "This Processor needs an Avatar with feet, legs and a body. Use [Generic/Quadruped Foot] instead.", anim);
                Active = false;
                return;
            }

            m_Root = anim.transform;

            m_LeftFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            m_RightFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            m_LeftUpperLeg = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            m_RightUpperLeg = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            m_LeftLowerLeg = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            m_RightLowerLeg = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);

            if (m_LeftFoot == null || m_RightFoot == null || m_LeftUpperLeg == null || m_RightUpperLeg == null)
            {
                Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] The Avatar is missing leg or foot bones.", anim);
                Active = false;
                return;
            }

            Bone = m_LeftFoot; // base class reference, purely informational here

            Calibrate(anim);

            m_LastRootPos = m_Root.position;
            m_LastGoalL = m_LeftFoot.position;
            m_LastGoalR = m_RightFoot.position;
            m_RotationL = m_RotationR = Quaternion.identity;
            m_Ready = true;
        }

        /// <summary> Clears the fade so a re-enabled Set does not pop back in at full strength. </summary>
        public override void OnEnable(IKSet set, Animator anim, int index)
        {
            m_GroundBlend = m_BodyBlend = 0f;
            m_OffsetL = m_OffsetR = m_BodyOffset = m_RawBodyOffset = Vector3.zero;
            m_ExtrapolationL = m_ExtrapolationR = Vector3.zero;
            m_RotationL = m_RotationR = Quaternion.identity;
            m_WasGrounded = false;
            m_HasHints = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Calibration — every field left at 0 is measured from the rig, so the Processor is correct out of
        // the box on any humanoid whatever its scale. The measured value stays in a private field on
        // purpose: writing it back into the serialized field would dirty the asset and silently turn an
        // "auto" field into a fixed one that no longer follows the rig.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Fills the private working values, measuring from the rig whatever was left at 0. </summary>
        private void Calibrate(Animator anim)
        {
            var up = m_Root.up;

            m_UpperLegLength = Vector3.Distance(m_LeftUpperLeg.position,
                m_LeftLowerLeg != null ? m_LeftLowerLeg.position : m_LeftFoot.position);

            m_LowerLegLength = m_LeftLowerLeg != null
                ? Vector3.Distance(m_LeftLowerLeg.position, m_LeftFoot.position)
                : 0f;

            var legLength = m_UpperLegLength + m_LowerLegLength;
            if (legLength < 0.0001f) legLength = Vector3.Distance(m_LeftUpperLeg.position, m_LeftFoot.position);
            if (legLength < 0.0001f) legLength = 0.9f; // last resort, a human sized leg

            // Ankle height above the root plane. On a standard humanoid the root sits on the ground, so
            // this is exactly the sole to ankle distance.
            var measuredHeight = Mathf.Abs(Vector3.Dot(m_LeftFoot.position - m_Root.position, up));
            if (measuredHeight < 0.001f) measuredHeight = legLength * 0.11f;

            m_FootHeight = FootHeight > 0 ? FootHeight : measuredHeight;

            if (FootLength > 0) m_FootLength = FootLength;
            else
            {
                var toes = anim.GetBoneTransform(HumanBodyBones.LeftToes);
                m_FootLength = toes != null
                    ? Vector3.Distance(m_LeftFoot.position, toes.position) * 1.5f
                    : m_FootHeight * 2.4f;
            }

            m_FootWidth = FootWidth > 0 ? FootWidth : m_FootLength * 0.35f;
            m_FootForwardOffset = FootForwardOffset > 0 ? FootForwardOffset : m_FootLength * 0.27f;
            m_MaxFootLift = MaxFootLift > 0 ? MaxFootLift : legLength * 0.55f;
            m_MaxBodyDrop = MaxBodyDrop > 0 ? MaxBodyDrop : legLength * 0.78f;
            m_GroundedDistance = GroundedDistance > 0 ? GroundedDistance : legLength * 0.78f;
            m_GroundedRadius = GroundedRadius > 0 ? GroundedRadius : legLength * 0.22f;

            // A walking swing lifts the ankle a few centimetres, a running one a good part of the leg.
            // A fifth of the leg puts the fade between the two, so a walk keeps most of its pull and a run none.
            m_SwingFadeHeight = legLength * 0.2f;

            // A zero foot height would collapse the secondary sphere cast and divide by zero in the slope math
            m_FootHeight = Mathf.Max(m_FootHeight, 0.001f);
            m_FootLength = Mathf.Max(m_FootLength, 0.001f);
            m_FootWidth = Mathf.Max(m_FootWidth, 0.001f);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Main pass
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Solves both feet, the pelvis and the knee hints, then writes them to the Animator. </summary>
        public override void OnAnimatorIK(IKSet set, Animator anim, int index, float weight)
        {
            m_OffsetScale = OffsetScale(set);
            if (!m_Ready || anim == null) return;

            // OnAnimatorIK runs once per Animator layer that has its IK Pass on. The solve must advance only
            // once per tick, otherwise the smoothing integrates N times and the pelvis offset is applied N
            // times. The goals are still written on every pass, the pelvis only on the first one.
            var tick = anim.updateMode == AnimatorUpdateMode.Fixed ? Time.fixedTimeAsDouble : Time.frameCount;
            var firstPass = m_LastTick != tick;

            if (firstPass)
            {
                m_LastTick = tick;
                var dt = anim.updateMode == AnimatorUpdateMode.Fixed ? Time.fixedDeltaTime : Time.deltaTime;
                Solve(anim, dt);
            }

            Apply(anim, weight, firstPass);
        }

        /// <summary> Advances the whole solve for this tick: grounded check, both feet, pelvis. </summary>
        private void Solve(Animator anim, float dt)
        {
            var up = m_Root.up;
            var forward = m_Root.forward;
            var rootPos = m_Root.position;
            var movingUp = Vector3.Dot(rootPos - m_LastRootPos, up) > 0.0001f;

            IsGrounded = CheckGrounded(rootPos, up);

            var goalL = anim.GetIKPosition(AvatarIKGoal.LeftFoot);
            var goalR = anim.GetIKPosition(AvatarIKGoal.RightFoot);

            if (!IsGrounded)
            {
                // Airborne: no casts at all, just fade everything still applied back to zero
                FadeOut(dt);
                m_WasGrounded = false;
                m_LastRootPos = rootPos;
                m_LastGoalL = goalL;
                m_LastGoalR = goalR;
                Finalize(anim, goalL, goalR);
                return;
            }

            // Soft re-entry: scale what survived the fade instead of snapping back to full strength
            if (!m_WasGrounded)
            {
                m_OffsetL *= m_GroundBlend;
                m_OffsetR *= m_GroundBlend;
                m_BodyOffset *= m_BodyBlend;
                m_WasGrounded = true;
            }

            m_GroundBlend = Mathf.Lerp(m_GroundBlend, 1f, Damp(FootSmooth, dt));
            m_BodyBlend = Mathf.Lerp(m_BodyBlend, 1f, Damp(BodySmooth, dt));

            var anchorL = FootAnchor(goalL, rootPos, up);
            var anchorR = FootAnchor(goalR, rootPos, up);

            // Re-solve only when the goal actually moved, or when the ground under the foot did. A standing
            // character on static ground therefore costs a single cast per frame.
            var threshold = MinMoveThreshold * MinMoveThreshold;

            if (!m_SolveL.Hit || (goalL - m_LastGoalL).sqrMagnitude > threshold || GroundMoved(m_SolveL))
                m_SolveL = SolveFoot(anchorL, goalL, up, forward);

            if (!m_SolveR.Hit || (goalR - m_LastGoalR).sqrMagnitude > threshold || GroundMoved(m_SolveR))
                m_SolveR = SolveFoot(anchorR, goalR, up, forward);

            m_LastGoalL = goalL;
            m_LastGoalR = goalR;

            SolveBody(rootPos, up, goalL, goalR, dt); // the goals gate the pull of a lifted foot

            SmoothFoot(ref m_OffsetL, ref m_ExtrapolationL, m_SolveL, goalL, up, movingUp, dt);
            SmoothFoot(ref m_OffsetR, ref m_ExtrapolationR, m_SolveR, goalR, up, movingUp, dt);

            SmoothRotation(ref m_RotationL, m_SolveL, up, dt);
            SmoothRotation(ref m_RotationR, m_SolveR, up, dt);

            m_PeakBodyOffset = Mathf.Max(m_PeakBodyOffset, m_BodyOffset.magnitude);
            m_PeakFootLift = Mathf.Max(m_PeakFootLift, Mathf.Max(m_OffsetL.magnitude, m_OffsetR.magnitude));

            m_LastRootPos = rootPos;

            Finalize(anim, goalL, goalR);
        }

        /// <summary> Turns this tick's offsets into the absolute goals, rotations and hints to be written. </summary>
        private void Finalize(Animator anim, Vector3 goalL, Vector3 goalR)
        {
            m_FinalGoalL = goalL + m_OffsetL;
            m_FinalGoalR = goalR + m_OffsetR;

            m_FinalRotL = m_RotationL * anim.GetIKRotation(AvatarIKGoal.LeftFoot);
            m_FinalRotR = m_RotationR * anim.GetIKRotation(AvatarIKGoal.RightFoot);

            // Hints need the knee bones. An Avatar without them keeps working, just without hints.
            if (SolveKneeHints && m_LeftLowerLeg != null && m_RightLowerLeg != null)
            {
                m_HintL = SolveKneeHint(m_LeftUpperLeg, m_LeftLowerLeg, m_LeftFoot, m_FinalGoalL);
                m_HintR = SolveKneeHint(m_RightUpperLeg, m_RightLowerLeg, m_RightFoot, m_FinalGoalR);
                m_HasHints = true;
            }
            else m_HasHints = false;
        }

        /// <summary> Writes the solved pelvis, goals, rotations and hints into the Animator. </summary>
        private void Apply(Animator anim, float weight, bool applyBody)
        {
            var footWeight = weight * m_GroundBlend;

            // The pelvis is a read modify write on a value shared by every IK pass, so it goes in once
            if (applyBody && BodyWeight > 0)
                anim.bodyPosition += (m_BodyOffset + m_Root.up * (BodyHeightOffset * m_OffsetScale)) * (BodyWeight * weight * m_BodyBlend);

            anim.SetIKPositionWeight(AvatarIKGoal.LeftFoot, footWeight);
            anim.SetIKPosition(AvatarIKGoal.LeftFoot, m_FinalGoalL);
            anim.SetIKPositionWeight(AvatarIKGoal.RightFoot, footWeight);
            anim.SetIKPosition(AvatarIKGoal.RightFoot, m_FinalGoalR);

            anim.SetIKRotationWeight(AvatarIKGoal.LeftFoot, footWeight);
            anim.SetIKRotation(AvatarIKGoal.LeftFoot, m_FinalRotL);
            anim.SetIKRotationWeight(AvatarIKGoal.RightFoot, footWeight);
            anim.SetIKRotation(AvatarIKGoal.RightFoot, m_FinalRotR);

            if (m_HasHints)
            {
                var hintWeight = footWeight * KneeHintWeight;
                anim.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, hintWeight);
                anim.SetIKHintPosition(AvatarIKHint.LeftKnee, m_HintL);
                anim.SetIKHintPositionWeight(AvatarIKHint.RightKnee, hintWeight);
                anim.SetIKHintPosition(AvatarIKHint.RightKnee, m_HintR);
            }

            if (ShowRuntimeDebug) DrawRuntimeDebug();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Grounded check — one sphere cast, the only physics done while airborne
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Single sphere cast straight down from above the root. </summary>
        private bool CheckGrounded(Vector3 rootPos, Vector3 up)
        {
            var radius = m_GroundedRadius;
            var originOffset = radius + m_GroundedDistance;
            var distance = originOffset + m_GroundedDistance - radius;
            var origin = rootPos + up * originOffset;

            if (Physics.SphereCast(origin, radius, -up, out var hit, distance, GroundLayer, TriggerInteraction))
            {
                GroundHit = hit;
                return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Foot ground solve
        // ─────────────────────────────────────────────────────────────────────

        // The cast anchor. This is the single most important line of the whole Processor: it keeps the
        // animated ankle's HORIZONTAL position but drops it to the ROOT's height. The cast therefore always
        // starts at a fixed height above the ground plane no matter how high the animation lifted the foot,
        // so its length is constant and a high lift animation can never make the cast miss.
        /// <summary> Animated ankle position, projected down to the root's height along the up axis. </summary>
        private static Vector3 FootAnchor(Vector3 goal, Vector3 rootPos, Vector3 up)
            => goal - Vector3.Project(goal - rootPos, up);

        /// <summary> Casts the ground under one foot and returns the clamped ankle target. </summary>
        private FootSolve SolveFoot(Vector3 anchor, Vector3 goal, Vector3 up, Vector3 forward)
        {
            var result = new FootSolve
            {
                Goal = goal,
                PlantedPos = goal,
                SurfacePoint = anchor,
                Normal = up,
                Planted = false,
                Hit = false,
                Ground = null
            };

            var down = -up;
            var boxHeight = Mathf.Max(m_FootHeight * 0.01f, 0.01f);
            var reach = Mathf.Max(m_MaxBodyDrop, m_GroundedDistance);

            var originPoint = anchor + up * (m_MaxFootLift + boxHeight + RayMargin);
            var boxOrigin = originPoint + forward * m_FootForwardOffset;
            var castDistance = m_MaxFootLift + reach + RayMargin;

            // Cast 1: a foot shaped slab, oriented by the character. A single ray would drop into the crack
            // between two stair treads, a footprint sized box spans it.
            var halfExtents = new Vector3(m_FootWidth * 0.5f, boxHeight, m_FootLength * 0.5f);

            if (!Physics.BoxCast(boxOrigin, halfExtents, down, out var boxHit, m_Root.rotation,
                                 castDistance, GroundLayer, TriggerInteraction))
                return result;

            var boxNormal = boxHit.normal;

            // BoxCast returns NaN normals against near degenerate box colliders
            if (float.IsNaN(boxNormal.x) || float.IsNaN(boxNormal.y) || float.IsNaN(boxNormal.z) ||
                boxNormal.sqrMagnitude < 0.0001f)
            {
                boxNormal = up;
#if UNITY_EDITOR
                WarnDegenerate(boxHit.collider);
#endif
            }

            var contactPoint = boxOrigin + down * (boxHit.distance + boxHeight);   // bottom face of the slab
            var bottomPoint = contactPoint - forward * m_FootForwardOffset;        // undo the forward bias

            var normal = boxNormal;
            var alpha = Vector3.Angle(up, normal);

            // Cast 2: a sphere whose radius IS the foot height, so its centre at contact is by construction
            // an ankle position that cannot penetrate the surface. It clamps the box on convex edges and
            // gives a second normal sample.
            var sphereOrigin = originPoint + up * m_FootHeight;
            var sphereAnkle = Vector3.zero;

            var hasSphere = Physics.SphereCast(sphereOrigin, m_FootHeight, down, out var sphereHit,
                                               castDistance, GroundLayer, TriggerInteraction);
            if (hasSphere)
            {
                sphereAnkle = sphereOrigin + down * sphereHit.distance + up * ScaledSole;

                var beta = Vector3.Angle(up, sphereHit.normal);

                // When the two samples disagree, synthesize a third normal from the line between the two
                // contact points and prefer the flattest plausible one. This is what removes the spurious
                // near vertical normals a box cast reports on a step lip, which otherwise flip the foot
                // sideways for a single frame.
                if (Mathf.Abs(beta - alpha) > 1f)
                {
                    var span = sphereHit.point - boxHit.point;

                    if (span.sqrMagnitude > 0.000001f)
                    {
                        span.Normalize();
                        var side = Vector3.Cross(span, up);

                        if (side.sqrMagnitude > 0.000001f)
                        {
                            var synthetic = Vector3.Cross(side.normalized, span).normalized;
                            var syntheticAngle = Vector3.Angle(up, synthetic);

                            if (syntheticAngle < alpha)
                            {
                                normal = synthetic;
                                alpha = syntheticAngle;

                                if (beta > alpha) { alpha = beta; normal = sphereHit.normal; }
                            }
                        }
                    }
                }
            }

            // Two separate limits on purpose: past [Max Ground Angle] the surface is not ground at all and
            // the hit is thrown away, while [Max Slope Angle] only caps the angle fed to the math so a very
            // steep but valid surface still grounds the foot without tipping it all the way over.
            if (alpha > MaxGroundAngle) return result;

            alpha = Mathf.Min(alpha, MaxSlopeAngle);

            // Slope correction. Two cosines: the contact point walks along the slope plane to undo the
            // forward bias landing at the wrong spot, and a flat sole at angle a needs footHeight / cos(a)
            // of clearance rather than footHeight. Skipping this sinks the heel into every ramp.
            var cosAlpha = Mathf.Cos(alpha * Mathf.Deg2Rad);
            if (cosAlpha < 0.01f) cosAlpha = 0.01f;

            var toContact = bottomPoint - boxHit.point;
            var contactDistance = toContact.magnitude;

            if (contactDistance > 0.0001f)
            {
                var side = Vector3.Cross(toContact / contactDistance, normal);

                if (side.sqrMagnitude > 0.000001f)
                {
                    var tangent = Vector3.Cross(normal, side.normalized).normalized;
                    bottomPoint += Vector3.Project(tangent * (contactDistance / cosAlpha), up);
                }
            }

            var soleHeight = m_FootHeight + ScaledSole;                          // user shift on top of the measured height
            var ankle = bottomPoint + up * (soleHeight / cosAlpha);
            var surfacePoint = ankle - up * soleHeight;

            // The sphere never penetrates, so when the box wants to lift the foot and the sphere says lower,
            // the sphere wins.
            if (hasSphere && Vector3.Dot(ankle - anchor, up) > 0)
            {
                var delta = Vector3.Dot(sphereAnkle - ankle, up);

                if (delta < 0)
                {
                    var shift = up * delta;
                    ankle += shift;
                    surfacePoint += shift;
                    bottomPoint += shift;
                }
            }

            // The swing clamp, and the whole step logic in three lines: the ground solution is never allowed
            // to push a foot BELOW where the animation put it. While the leg swings the animated ankle is
            // above the ground, so the animation simply wins and the foot stays lifted. Only once the
            // animation brings the ankle down to the ground does the ground solution take over. The pure
            // ground answer is still kept in PlantedPos, where the pelvis pass uses it as a floor.
            var planted = true;
            var plantedPos = ankle;
            var toAnkle = ankle - goal;

            if (Vector3.Dot(toAnkle, up) < 0)
            {
                ankle = goal;
                planted = false;

                var depth = toAnkle.magnitude;

                if (depth > m_MaxFootLift)
                {
                    var shift = up * (depth - m_MaxFootLift);
                    surfacePoint += shift;
                    bottomPoint += shift;
                }
            }
            else
            {
                var lift = ankle - anchor;
                var maxLift = m_MaxFootLift + m_FootHeight;

                if (lift.magnitude > maxLift)
                {
                    ankle = anchor + lift.normalized * maxLift;
                    plantedPos = ankle;
                }
            }

            result.Goal = ankle;
            result.PlantedPos = plantedPos;
            result.SurfacePoint = surfacePoint;
            result.Normal = normal;
            result.Planted = planted;
            result.Hit = true;
            result.Ground = boxHit.transform;

            if (result.Ground != null)
            {
                result.GroundPos = result.Ground.position;
                result.GroundRot = result.Ground.rotation;
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Pelvis
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Picks the pelvis offset from both feet by signed min/max and smooths it. </summary>
        private void SolveBody(Vector3 rootPos, Vector3 up, Vector3 goalL, Vector3 goalR, float dt)
        {
            // Hysteresis around the limit, so a correction sitting exactly on [Max Body Drop] does not
            // flicker on and off every other frame.
            var limit = m_MaxBodyDrop;
            if (m_BodyOffset.sqrMagnitude < 0.000001f) limit *= (1f - BodyTolerance);

            var left = m_SolveL.Hit ? Vector3.Dot(m_SolveL.SurfacePoint - rootPos, up) * FootPull(m_SolveL, goalL, rootPos, up) : 0f;
            var right = m_SolveR.Hit ? Vector3.Dot(m_SolveR.SurfacePoint - rootPos, up) * FootPull(m_SolveR, goalR, rootPos, up) : 0f;

            // Spread rejection: rather than doing the splits over a hole, drop the lower foot's result
            // entirely when the two solutions are further apart vertically than the pelvis may travel.
            if (m_SolveL.Hit && m_SolveR.Hit && Mathf.Abs(left - right) > limit)
            {
                if (left < right) { m_SolveL = Invalidate(m_SolveL, up); left = 0f; }
                else { m_SolveR = Invalidate(m_SolveR, up); right = 0f; }
            }

            float target;

            if (left < 0 && right < 0) target = Mathf.Min(left, right);   // both below: reach the deeper one
            else if (left < 0) target = left;
            else if (right < 0) target = right;
            else target = AllowBodyRise ? Mathf.Min(left, right) : 0f;    // both above: rise to the lower one

            target = Mathf.Clamp(target, -limit, limit);

            m_RawBodyOffset = up * target;
            m_BodyOffset = Vector3.Lerp(m_BodyOffset, m_RawBodyOffset, Damp(BodySmooth, dt));

            // The pelvis has moved, so the feet that hang off it have to move with it
            FollowBody(ref m_SolveL, up);
            FollowBody(ref m_SolveR, up);
        }

        // A foot the animation is holding in the air does not need the ground yet. Letting it drag the pelvis
        // down anyway is what folds the front knee when running up a slope: the trailing foot is high in the
        // air, its ground is far below the root, and the pelvis dives to meet a foot that is not coming down
        // for a while. So the pull of an un-planted foot fades with its animated lift above the sole height.
        // A planted foot always pulls in full, it is standing on that ground.
        /// <summary> 0..1 weight of one foot's pull on the pelvis, fading as the animation lifts it. </summary>
        private float FootPull(in FootSolve solve, Vector3 goal, Vector3 rootPos, Vector3 up)
        {
            if (solve.Planted || SwingFootPull >= 1f) return 1f;

            var lift = Vector3.Dot(goal - rootPos, up) - (m_FootHeight + ScaledSole);
            if (lift <= 0f || m_SwingFadeHeight <= 0.0001f) return 1f;

            return Mathf.Lerp(1f, SwingFootPull, Mathf.Clamp01(lift / m_SwingFadeHeight));
        }

        /// <summary> Discards a foot's ground answer, leaving the animation in charge of it. </summary>
        private FootSolve Invalidate(FootSolve solve, Vector3 up)
        {
            solve.Hit = false;
            solve.Planted = false;
            solve.Normal = up;
            solve.Ground = null;
            solve.Goal = solve.PlantedPos = solve.SurfacePoint;
            return solve;
        }

        // The IK goal is an absolute world position, so it does not inherit the pelvis shift. A foot the
        // animation is still placing was authored relative to the body: if the pelvis drops and the goal
        // stays pinned where the animation put it, the leg stretches and the foot hangs in mid air. That is
        // very visible on a strongly curved or stepped surface, where the pelvis drop is large.
        //
        // So an UN-PLANTED foot follows the pelvis, then gets floored on the ground answer. A PLANTED foot
        // never follows: it is pinned to the surface, which is the entire point of planting it.
        /// <summary> Re-hangs an un-planted foot off the shifted pelvis, then floors it on the ground. </summary>
        private void FollowBody(ref FootSolve solve, Vector3 up)
        {
            if (!solve.Hit || solve.Planted) return;

            var shifted = solve.Goal + m_BodyOffset * BodyWeight;

            // Whichever is higher wins. When the ground wins, the foot has just landed and is planted.
            if (Vector3.Dot(shifted - solve.PlantedPos, up) <= 0)
            {
                solve.Goal = solve.PlantedPos;
                solve.Planted = true;
            }
            else solve.Goal = shifted;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Smoothing
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Frame rate independent damping factor for a Lerp or Slerp. </summary>
        private static float Damp(float speed, float dt) => 1f - Mathf.Exp(-speed * dt);

        /// <summary> Smooths one foot's offset, with directional overshoot and a penetration clamp. </summary>
        private void SmoothFoot(ref Vector3 offset, ref Vector3 extrapolation, FootSolve solve,
                                Vector3 goal, Vector3 up, bool movingUp, float dt)
        {
            var raw = solve.Hit ? solve.Goal - goal : Vector3.zero;
            var diff = raw - offset;

            // Overshoot, asymmetric on purpose: a foot that has to come up while the body is falling needs
            // to move much faster than one that follows a rising body. This is what makes a landing land.
            if (solve.Hit && diff.sqrMagnitude > 0.0001f && Vector3.Dot(diff, up) > 0)
                extrapolation = diff * (movingUp ? RiseOvershoot : FallOvershoot);
            else
                extrapolation = Vector3.zero;

            offset = Vector3.Lerp(offset, raw + extrapolation, Damp(FootSmooth, dt));

            // Never let the overshoot carry the foot below the ground answer
            if (Vector3.Dot(offset - raw, up) < 0)
            {
                offset = Vector3.Lerp(offset, raw, Mathf.Min(dt * PenetrationRecovery, 1f));
                extrapolation = Vector3.zero;
            }
        }

        /// <summary> Smooths one ankle's delta rotation towards the surface normal, clamped. </summary>
        private void SmoothRotation(ref Quaternion rotation, FootSolve solve, Vector3 up, float dt)
        {
            var target = Quaternion.identity;

            if (solve.Hit && (solve.Planted || !RotateOnlyWhenPlanted))
            {
                var axis = Vector3.Cross(up, solve.Normal);

                if (axis.sqrMagnitude > 0.000001f)
                {
                    axis.Normalize();
                    var angle = Vector3.SignedAngle(up, solve.Normal, axis);
                    angle = Mathf.Clamp(angle, -MaxAnkleAngle, MaxAnkleAngle);
                    target = Quaternion.AngleAxis(angle, axis);
                }
            }

            rotation = Quaternion.Slerp(rotation, target, Damp(RotationSmooth, dt));
        }

        /// <summary> Airborne pass: no casts, everything still applied fades back to zero. </summary>
        private void FadeOut(float dt)
        {
            var footT = Damp(FootSmooth, dt);
            var bodyT = Damp(BodySmooth, dt);
            var rotT = Damp(RotationSmooth, dt);

            m_GroundBlend = Mathf.Lerp(m_GroundBlend, 0f, footT);
            m_BodyBlend = Mathf.Lerp(m_BodyBlend, 0f, bodyT);

            m_OffsetL = Vector3.Lerp(m_OffsetL, Vector3.zero, footT);
            m_OffsetR = Vector3.Lerp(m_OffsetR, Vector3.zero, footT);
            m_BodyOffset = Vector3.Lerp(m_BodyOffset, Vector3.zero, bodyT);
            m_RawBodyOffset = Vector3.zero;

            m_ExtrapolationL = m_ExtrapolationR = Vector3.zero;

            m_RotationL = Quaternion.Slerp(m_RotationL, Quaternion.identity, rotT);
            m_RotationR = Quaternion.Slerp(m_RotationR, Quaternion.identity, rotT);

            m_SolveL.Hit = m_SolveR.Hit = false;
            m_SolveL.Ground = m_SolveR.Ground = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Knee hint — analytic pole placement
        // ─────────────────────────────────────────────────────────────────────

        // The bend reference is the ANIMATED KNEE BONE, never Animator.GetIKHintPosition. Reading the hint
        // back would return this Processor's own output from the previous frame, a feedback loop seeded by
        // whatever the Animator happened to hold before anything ever set a hint, which drives the knees off
        // in a direction that has nothing to do with the pose. The knee bone is authoritative and stable.
        /// <summary> Places the knee pole for the corrected ankle using the law of cosines. </summary>
        private Vector3 SolveKneeHint(Transform upperLeg, Transform lowerLeg, Transform foot, Vector3 newAnkle)
        {
            var hipPos = upperLeg.position;
            var originalAnkle = foot.position;
            var kneePos = lowerLeg.position;

            var toHip = hipPos - newAnkle;
            var c = toHip.magnitude;
            if (c < 0.0001f) return kneePos;

            var toHipDir = toHip / c;

            var a = m_UpperLegLength;                                          // hip to knee
            var b = m_LowerLegLength > 0.0001f ? m_LowerLegLength : c * 0.5f;  // knee to ankle
            var legLength = a + b;

            // Keep the triangle solvable when the leg is stretched or folded past its own segment lengths
            if (b > a && b > c) b = Mathf.Min(b, (c + a) * 0.995f);
            else if (a > b && a > c) a = Mathf.Min(a, (c + b) * 0.995f);
            else c = Mathf.Min(c, (a + b) * 0.995f);

            var cosAnkle = Mathf.Clamp((b * b + c * c - a * a) / (2f * b * c), -1f, 1f);
            var ankleAngle = Mathf.Acos(cosAnkle);

            var along = Mathf.Cos(ankleAngle) * b;
            var perpendicular = Mathf.Sin(ankleAngle) * b;

            // Carry the animated bend direction over to the new leg axis instead of re deriving it
            var originalAxis = hipPos - originalAnkle;

            var bendDir = Vector3.zero;

            if (originalAxis.sqrMagnitude > 0.000001f)
            {
                var swing = Quaternion.FromToRotation(originalAxis.normalized, toHipDir);
                bendDir = swing * (kneePos - originalAnkle);
                bendDir -= Vector3.Project(bendDir, toHipDir);
            }

            // A straight leg leaves nothing perpendicular to read a direction from. Knees bend forwards,
            // so the character's own forward axis is the right answer whenever the pose cannot supply one.
            if (bendDir.sqrMagnitude < 0.000001f)
            {
                bendDir = m_Root.forward - Vector3.Project(m_Root.forward, toHipDir);

                if (bendDir.sqrMagnitude < 0.000001f) return kneePos; // leg points along forward, give up
            }

            // The hint is a POLE: only its direction off the leg axis matters, never its distance. Placing it
            // exactly at the analytic knee collapses it onto the axis as the leg straightens, and it collapses
            // completely once the goal is further away than the leg can reach (c >= a + b), which happens on
            // every long stride. A pole sitting on the axis has no bend plane, so the solver picks a side from
            // rounding noise and the knee flips. Holding it a fixed distance out keeps the plane well defined.
            var poleDistance = Mathf.Max(perpendicular, legLength * 0.25f);

            return newAnkle + toHipDir * along + bendDir.normalized * poleDistance;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        // Transform.hasChanged is not usable here: nothing ever resets it, so one moving platform would force
        // a re-solve on every frame for the rest of the session. The pose is compared against the snapshot
        // taken when this foot was last solved instead.
        /// <summary> True when the collider a foot is standing on has moved since that foot was last solved. </summary>
        private static bool GroundMoved(in FootSolve solve)
        {
            if (solve.Ground == null) return false;

            return (solve.Ground.position - solve.GroundPos).sqrMagnitude > 0.000001f
                || Quaternion.Angle(solve.Ground.rotation, solve.GroundRot) > 0.01f;
        }

#if UNITY_EDITOR
        [NonSerialized] private HashSet<Collider> m_ReportedDegenerate;

        /// <summary> Editor only: reports each collider that makes the box cast return a NaN normal, once. </summary>
        private void WarnDegenerate(Collider collider)
        {
            if (collider == null) return;

            m_ReportedDegenerate ??= new HashSet<Collider>();
            if (!m_ReportedDegenerate.Add(collider)) return;

            Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] The collider " +
                $"<B><color=white>{collider.name}</color></B> returned an invalid surface normal. " +
                "A Box Collider with a near zero size on one axis does this. Give it some thickness.", collider);
        }
#endif

        // ─────────────────────────────────────────────────────────────────────
        // Verify
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Checks the rig and the Animator setup, and reports the calibrated values. </summary>
        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (animator == null)
            {
                Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] No Animator assigned to the IK Manager.");
                return;
            }

            if (!animator.isHuman)
            {
                Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] The Animator is not Humanoid. " +
                    "Use [Generic/Quadruped Foot] for generic rigs.", animator);
                return;
            }

            var ok = true;

            var required = new (HumanBodyBones bone, string label)[]
            {
                (HumanBodyBones.LeftFoot,      "Left Foot"),
                (HumanBodyBones.RightFoot,     "Right Foot"),
                (HumanBodyBones.LeftUpperLeg,  "Left Upper Leg"),
                (HumanBodyBones.RightUpperLeg, "Right Upper Leg"),
                (HumanBodyBones.LeftLowerLeg,  "Left Lower Leg"),
                (HumanBodyBones.RightLowerLeg, "Right Lower Leg"),
            };

            foreach (var (bone, label) in required)
            {
                if (animator.GetBoneTransform(bone) == null)
                {
                    Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] The Avatar has no <B>{label}</B> bone.", animator);
                    ok = false;
                }
            }

            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] The Animator has no Controller, " +
                    "so no layer can run an IK Pass.", animator);
                ok = false;
            }

            if (GroundLayer.Value == 0)
            {
                Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] [Ground Layer] is empty, nothing will be detected.", animator);
                ok = false;
            }

            if (MaxSlopeAngle > MaxGroundAngle)
            {
                Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] [Max Slope Angle] ({MaxSlopeAngle}) is above " +
                    $"[Max Ground Angle] ({MaxGroundAngle}), so it can never take effect.", animator);
                ok = false;
            }

            // Only one Processor may own the pelvis, several would fight over animator.bodyPosition
            if (BodyWeight > 0 && manager != null && manager.sets != null)
            {
                var owners = 0;

                foreach (var s in manager.sets)
                {
                    if (s?.Processors == null) continue;

                    foreach (var p in s.Processors)
                        if (p is HumanIKFootGround other && other.Active && other.BodyWeight > 0) owners++;
                }

                if (owners > 1)
                {
                    Debug.LogWarning($"[Foot Ground Placement: <B>{name}</B>] {owners} Foot Ground Placement Processors " +
                        "have a [Body Weight] above zero on this character. They all write the pelvis and will fight. " +
                        "Keep one, or set [Body Weight] to 0 on the others.", animator);
                    ok = false;
                }
            }

            if (Application.isPlaying && m_Ready)
            {
                Debug.Log($"<B>[IK Processor: {name}][Foot Ground Placement]</B> calibrated: " +
                    $"Foot Height {m_FootHeight:0.###}, Length {m_FootLength:0.###}, Width {m_FootWidth:0.###}, " +
                    $"Forward {m_FootForwardOffset:0.###} | Max Foot Lift {m_MaxFootLift:0.###}, " +
                    $"Max Body Drop {m_MaxBodyDrop:0.###} | Grounded dist {m_GroundedDistance:0.###} radius {m_GroundedRadius:0.###}", animator);
            }

            if (ok)
                Debug.Log($"<B>[IK Processor: {name}][Foot Ground Placement]</B> <color=yellow>[OK]</color>", animator);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Gizmos
        //
        // OnDrawGizmos is called by the IK Manager only while it is selected, its inspector is expanded and
        // THIS Processor's row is the selected one. It also runs outside Play Mode, where none of the Start()
        // cache exists, so everything below resolves its own bones and calibrates locally. Nothing here may
        // read the runtime cache without checking m_Ready first.
        //
        // The shapes are drawn as the FOOT the values describe, hanging off the ankle bone, not as the casts:
        // a line for the height, a footprint for the length and width, an arrow for the forward offset and
        // vertical bars for the lift and drop limits. Sizing the footprint to the shoe mesh in the Scene view
        // is the whole calibration workflow when [Auto Calibrate] is off.
        // ─────────────────────────────────────────────────────────────────────

        /// <summary> Working sizes for the gizmos, measured or typed, so both modes draw from one set. </summary>
        private struct FootSizes
        {
            public float Height, Length, Width, ForwardOffset, MaxLift, BodyDrop, GroundedDistance, GroundedRadius;
        }

        // One colour per value, shared by the shape and its label so the two are matched at a glance.
        // Picked to survive both a dark and a bright, saturated scene.
        private static readonly Color ColHeight = new(1f, 0.85f, 0.15f);     // Foot Height (yellow)
        private static readonly Color ColFootprint = new(0.15f, 1f, 0.3f);   // Foot Length / Width (green)
        private static readonly Color ColForward = new(1f, 0.45f, 0.05f);    // Foot Forward Offset (orange)
        private static readonly Color ColLift = new(0.35f, 0.55f, 1f);       // Max Foot Lift (blue)
        private static readonly Color ColDrop = new(1f, 0.25f, 0.25f);       // Max Body Drop (red)
        private static readonly Color ColSole = new(1f, 0.25f, 1f);          // Sole Offset (magenta)
        private static readonly Color ColGrounded = new(1f, 1f, 1f, 0.45f);  // Grounded Distance / Radius (white)

        private const float LineWidth = 3f;   // thick anti aliased lines read on any background

        /// <summary> Draws every size as the foot shape it describes, and the live solve while playing. </summary>
        public override void OnDrawGizmos(IKManager manager, IKSet IKSet, Animator anim, float weight)
        {
            m_OffsetScale = OffsetScale(IKSet);
            if (!IKSet.Gizmos || anim == null || !anim.isHuman) return;

            var leftFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rightFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            var leftUpper = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var rightUpper = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);

            if (leftFoot == null || rightFoot == null || leftUpper == null || rightUpper == null) return;

            // Edit Mode has no calibration yet, so measure it here from the same rig
            var sizes = GizmoValues(anim, leftFoot, leftUpper);

            var root = anim.transform;
            var up = root.up;
            var forward = root.forward;
            var rootPos = root.position;

            var prevColor = Gizmos.color;
            var prevMatrix = Gizmos.matrix;

            // try/finally, so that even a partial draw can never leave our colour and matrix behind for
            // every gizmo drawn after this one in the same frame
            try
            {
                // Grounded check: the sphere cast at both ends of its sweep
                var groundOrigin = rootPos + up * (sizes.GroundedRadius + sizes.GroundedDistance);
                var groundLength = sizes.GroundedDistance * 2f;

                Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : ColGrounded;
                Gizmos.DrawWireSphere(groundOrigin, sizes.GroundedRadius);
                Gizmos.DrawWireSphere(groundOrigin - up * groundLength, sizes.GroundedRadius);
                MDebug.GizmoRay(groundOrigin, -up * groundLength);

                DrawFootGizmo(anim, leftFoot, sizes, up, forward, rootPos, true);
                DrawFootGizmo(anim, rightFoot, sizes, up, forward, rootPos, false);

                // Max Body Drop: how far the pelvis may sink, hanging from the hips
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);

                if (hips != null && BodyWeight > 0)
                {
                    var hipPos = hips.position;
                    var dropEnd = hipPos - up * sizes.BodyDrop;

                    ThickLine(hipPos, dropEnd, ColDrop);
                    Disc(dropEnd, up, sizes.Width * 0.5f, ColDrop);

                    // The live pelvis offset, only meaningful once the solve has run
                    if (Application.isPlaying && m_Ready)
                    {
                        MDebug.GizmoCircle(hipPos, root.rotation, sizes.Length * 0.35f, Color.yellow);
                        Gizmos.color = Color.yellow;
                        MDebug.GizmoRay(hipPos, m_BodyOffset);
                    }
                }
            }
            finally
            {
                Gizmos.color = prevColor;
                Gizmos.matrix = prevMatrix;
            }
        }

        /// <summary> Draws one foot: height, sole, footprint, forward offset, lift limit, and its solve while playing. </summary>
        private void DrawFootGizmo(Animator anim, Transform foot, FootSizes sizes, Vector3 up, Vector3 forward,
                                   Vector3 rootPos, bool isLeft)
        {
            // Outside Play Mode there is no animated goal to read, the bone itself is the best stand in.
            // While playing, the goal cached by the last solve is used rather than GetIKPosition, which
            // outside the IK pass returns the corrected value this Processor already wrote.
            var goal = Application.isPlaying && m_Ready
                ? (isLeft ? m_LastGoalL : m_LastGoalR)
                : foot.position;

            var rotation = anim.transform.rotation;
            var anchor = FootAnchor(goal, rootPos, up);
            var soleHeight = sizes.Height + ScaledSole;
            var sole = goal - up * soleHeight;                         // where the sole plane sits under the ankle
            var print = sole + forward * sizes.ForwardOffset;          // centre of the footprint

            var right = rotation * Vector3.right;

            // Foot Height: the ankle bone straight down to the sole, with a cross where the sole plane sits
            Gizmos.color = ColHeight;
            Gizmos.DrawWireSphere(goal, sizes.Height * 0.12f);
            ThickLine(goal, sole, ColHeight);
            Cross(sole, right, forward, sizes.Width * 0.35f, ColHeight);

            // Sole Offset: the measured sole against the shifted one, only when there is a shift to see
            if (Mathf.Abs(SoleOffset) > 0.0005f)
            {
                var measured = goal - up * sizes.Height;
                ThickLine(measured, sole, ColSole);
                Cross(measured, right, forward, sizes.Width * 0.35f, ColSole);
            }

            // Foot Length / Width: the footprint on the sole plane, oriented by the character. Match this
            // rectangle to the shoe mesh and the box cast spans exactly what the foot covers.
            Footprint(print, right * (sizes.Width * 0.5f), forward * (sizes.Length * 0.5f), ColFootprint);

            // Foot Forward Offset: an arrow from under the ankle to the footprint centre
            if (sizes.ForwardOffset > 0.0005f)
            {
                ThickLine(sole, print, ColForward);
                Gizmos.color = ColForward;
                Gizmos.DrawWireSphere(print, sizes.Height * 0.08f);
            }

            // The secondary sphere cast, radius equal to the foot height: at contact its centre IS the ankle,
            // so it is drawn resting on the sole plane
            Gizmos.color = new Color(ColHeight.r, ColHeight.g, ColHeight.b, 0.2f);
            Gizmos.DrawWireSphere(sole + up * sizes.Height, sizes.Height);

            // Max Foot Lift: the sole may be raised this far above the root plane. Measured from the anchor,
            // the animated ankle dropped to the root's height, which is also where every cast starts.
            var liftEnd = anchor + up * sizes.MaxLift;
            Gizmos.color = ColLift;
            Gizmos.DrawWireSphere(anchor, sizes.Height * 0.1f);
            ThickLine(anchor, liftEnd, ColLift);
            Disc(liftEnd, up, sizes.Width * 0.5f, ColLift);

            if (!Application.isPlaying || !m_Ready) return;

            var solve = isLeft ? m_SolveL : m_SolveR;
            if (!solve.Hit) return;

            Gizmos.color = solve.Planted ? Color.green : Color.magenta;
            Gizmos.DrawWireSphere(solve.SurfacePoint, sizes.Height * 0.2f);
            Gizmos.DrawLine(solve.SurfacePoint, solve.SurfacePoint + solve.Normal * (sizes.Length * 0.5f));
            Gizmos.DrawLine(goal, isLeft ? m_FinalGoalL : m_FinalGoalR);

            if (m_HasHints)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(isLeft ? m_HintL : m_HintR, sizes.Height * 0.15f);
            }
        }

        // Drawing helpers. In the Editor they use Handles, whose anti aliased lines take a pixel width and
        // stay readable on a bright scene where a one pixel Gizmos line disappears. Handles are legal inside
        // OnDrawGizmos. The fallbacks keep the file compiling in a player build.

        /// <summary> Thick line between two points. </summary>
        private static void ThickLine(Vector3 from, Vector3 to, Color color)
        {
#if UNITY_EDITOR
            Handles.color = color;
            Handles.DrawAAPolyLine(LineWidth, from, to);
#else
            Gizmos.color = color;
            Gizmos.DrawLine(from, to);
#endif
        }

        /// <summary> Thick circle around an axis. </summary>
        private static void Disc(Vector3 center, Vector3 normal, float radius, Color color)
        {
#if UNITY_EDITOR
            Handles.color = color;
            Handles.DrawWireDisc(center, normal, radius, LineWidth);
#else
            MDebug.GizmoCircle(center, Quaternion.LookRotation(normal), radius, color);
#endif
        }

        /// <summary> Small cross on a plane, along the two given directions. </summary>
        private static void Cross(Vector3 center, Vector3 right, Vector3 forward, float halfSize, Color color)
        {
            ThickLine(center - right * halfSize, center + right * halfSize, color);
            ThickLine(center - forward * halfSize, center + forward * halfSize, color);
        }

        /// <summary> Translucent filled rectangle with a thick outline, from its centre and two half extents. </summary>
        private static void Footprint(Vector3 center, Vector3 halfRight, Vector3 halfForward, Color color)
        {
            var a = center - halfRight - halfForward;
            var b = center - halfRight + halfForward;
            var c = center + halfRight + halfForward;
            var d = center + halfRight - halfForward;
#if UNITY_EDITOR
            Handles.color = color;
            Handles.DrawSolidRectangleWithOutline(new[] { a, b, c, d },
                new Color(color.r, color.g, color.b, 0.15f), Color.clear);
            Handles.DrawAAPolyLine(LineWidth, a, b, c, d, a);
#else
            Gizmos.color = color;
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
#endif
        }

        /// <summary> Resolves the working sizes for the gizmos, which also run outside Play Mode. </summary>
        private FootSizes GizmoValues(Animator anim, Transform foot, Transform upperLeg)
        {
            if (Application.isPlaying && m_Ready)
            {
                return new FootSizes
                {
                    Height = m_FootHeight,
                    Length = m_FootLength,
                    Width = m_FootWidth,
                    ForwardOffset = m_FootForwardOffset,
                    MaxLift = m_MaxFootLift,
                    BodyDrop = m_MaxBodyDrop,
                    GroundedDistance = m_GroundedDistance,
                    GroundedRadius = m_GroundedRadius
                };
            }

            // Same rules as Calibrate(), applied to whatever pose the rig is in right now
            var root = anim.transform;
            var lowerLeg = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);

            var legLength = lowerLeg != null
                ? Vector3.Distance(upperLeg.position, lowerLeg.position) + Vector3.Distance(lowerLeg.position, foot.position)
                : Vector3.Distance(upperLeg.position, foot.position);

            if (legLength < 0.0001f) legLength = 0.9f;

            var measuredHeight = Mathf.Abs(Vector3.Dot(foot.position - root.position, root.up));
            if (measuredHeight < 0.001f) measuredHeight = legLength * 0.11f;

            var sizes = new FootSizes { Height = Mathf.Max(FootHeight > 0 ? FootHeight : measuredHeight, 0.001f) };

            if (FootLength > 0) sizes.Length = FootLength;
            else
            {
                var toes = anim.GetBoneTransform(HumanBodyBones.LeftToes);
                sizes.Length = toes != null ? Vector3.Distance(foot.position, toes.position) * 1.5f : sizes.Height * 2.4f;
            }

            sizes.Length = Mathf.Max(sizes.Length, 0.001f);
            sizes.Width = Mathf.Max(FootWidth > 0 ? FootWidth : sizes.Length * 0.35f, 0.001f);
            sizes.ForwardOffset = FootForwardOffset > 0 ? FootForwardOffset : sizes.Length * 0.27f;
            sizes.MaxLift = MaxFootLift > 0 ? MaxFootLift : legLength * 0.55f;
            sizes.BodyDrop = MaxBodyDrop > 0 ? MaxBodyDrop : legLength * 0.78f;
            sizes.GroundedDistance = GroundedDistance > 0 ? GroundedDistance : legLength * 0.78f;
            sizes.GroundedRadius = GroundedRadius > 0 ? GroundedRadius : legLength * 0.22f;
            return sizes;
        }

        /// <summary> Debug lines while playing, so a character can be checked without keeping the row selected. </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void DrawRuntimeDebug()
        {
            MDebug.DrawWireSphere(m_FinalGoalL, Color.cyan, m_FootHeight * 0.2f);
            MDebug.DrawWireSphere(m_FinalGoalR, Color.cyan, m_FootHeight * 0.2f);

            DrawRuntimeFoot(m_SolveL);
            DrawRuntimeFoot(m_SolveR);

            if (m_HasHints)
            {
                MDebug.DrawWireSphere(m_HintL, Color.red, m_FootHeight * 0.15f);
                MDebug.DrawWireSphere(m_HintR, Color.red, m_FootHeight * 0.15f);
            }

            if (m_Root != null) MDebug.DrawRay(m_Root.position, m_BodyOffset, Color.yellow);
        }

        /// <summary> Debug lines for one foot's solve result. </summary>
        private void DrawRuntimeFoot(FootSolve solve)
        {
            if (!solve.Hit) return;

            MDebug.DrawWireSphere(solve.SurfacePoint, solve.Planted ? Color.green : Color.magenta, m_FootHeight * 0.2f);
            MDebug.DrawRay(solve.SurfacePoint, solve.Normal * (m_FootLength * 0.5f), Color.yellow);
        }

#if UNITY_EDITOR
        /// <summary> Labels every size next to its shape on the left foot, plus the live readouts while playing. </summary>
        internal override void OnSceneGUI(IKSet set, Animator animator, UnityEngine.Object target, int index)
        {
            m_OffsetScale = OffsetScale(set);
            if (!set.Gizmos || !Active || animator == null || !animator.isHuman) return;

            var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            if (leftFoot == null || leftUpper == null) return;

            var playing = Application.isPlaying && m_Ready;
            var sizes = GizmoValues(animator, leftFoot, leftUpper);
            var root = animator.transform;
            var up = root.up;
            var forward = root.forward;
            var outward = -root.right;   // away from the body, so the labels sit outside the left foot

            var goal = playing ? m_LastGoalL : leftFoot.position;
            var anchor = FootAnchor(goal, root.position, up);
            var sole = goal - up * (sizes.Height + ScaledSole);
            var print = sole + forward * sizes.ForwardOffset;

            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 10, richText = true };

            // A typed value reads as is, a measured one is marked so the user knows the number came from the rig
            static string Auto(float field) => field > 0 ? "" : " <i>(auto)</i>";

            // Each label carries its shape's colour, so the number and the line it describes are matched at a glance
            static string Tag(Color color, string text) => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{text}</b></color>";

            var height = $"{Tag(ColHeight, "Foot Height")} {sizes.Height:0.###}{Auto(FootHeight)}";
            if (Mathf.Abs(SoleOffset) > 0.0005f) height += $"\n{Tag(ColSole, "Sole Offset")} {SoleOffset:0.###}";
            Handles.Label((goal + sole) * 0.5f + outward * sizes.Width, height, style);

            Handles.Label(print + forward * (sizes.Length * 0.5f),
                $"{Tag(ColFootprint, "Foot Length")} {sizes.Length:0.###}{Auto(FootLength)}\n" +
                $"{Tag(ColFootprint, "Foot Width")} {sizes.Width:0.###}{Auto(FootWidth)}", style);

            if (sizes.ForwardOffset > 0.0005f)
                Handles.Label(print - outward * sizes.Width,
                    $"{Tag(ColForward, "Forward Offset")} {sizes.ForwardOffset:0.###}{Auto(FootForwardOffset)}", style);

            Handles.Label(anchor + up * sizes.MaxLift + outward * sizes.Width,
                $"{Tag(ColLift, "Max Foot Lift")} {sizes.MaxLift:0.###}{Auto(MaxFootLift)}", style);

            var groundTop = root.position + up * (sizes.GroundedDistance + sizes.GroundedRadius * 2f);
            Handles.Label(groundTop,
                $"{Tag(ColGrounded, "Grounded Radius")} {sizes.GroundedRadius:0.###}{Auto(GroundedRadius)}\n" +
                $"{Tag(ColGrounded, "Grounded Distance")} {sizes.GroundedDistance:0.###}{Auto(GroundedDistance)}", style);

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;

            if (BodyWeight > 0)
                Handles.Label(hips.position - up * sizes.BodyDrop + outward * sizes.Width,
                    $"{Tag(ColDrop, "Max Body Drop")} {sizes.BodyDrop:0.###}{Auto(MaxBodyDrop)}", style);

            if (!playing) return;

            Handles.Label(hips.position + up * (sizes.Height * 2f),
                $"Grounded: <b>{IsGrounded}</b>\n" +
                $"Body Offset: <b>{Vector3.Dot(m_BodyOffset, up):0.###}</b>   peak {m_PeakBodyOffset:0.###} / max {m_MaxBodyDrop:0.###}\n" +
                $"Foot Lift: <b>{Mathf.Max(m_OffsetL.magnitude, m_OffsetR.magnitude):0.###}</b>   peak {m_PeakFootLift:0.###} / max {m_MaxFootLift:0.###}\n" +
                $"Planted:  L <b>{m_SolveL.Planted}</b>   R <b>{m_SolveR.Planted}</b>", style);
        }
#endif
    }
}
