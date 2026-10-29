// Last Change: 10/16/2026 9:45am
using System;
using UnityEngine;
using MalbersAnimations.Scriptables;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    // MWC — Full quadruped foot placement + body adjustment IK processor for generic rigs

    // ─────────────────────────────────────────────────────────────────────────
    // Per-leg data container (serialized inspector portion + runtime cache)
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    public class QuadLegEntry
    {
        [Tooltip("Tag identifying the upper limb bone of this leg (hip / shoulder joint). " +
                 "Intermediate bones and the foot bone are resolved automatically from the hierarchy.")]
        public IKTag UpperTag;

        [Range(2, 3)]
        [Tooltip("Number of limb bones in the chain before the foot:\n" +
                 "  2 = Upper → Mid → Foot  (e.g. simple limb)\n" +
                 "  3 = Upper → Mid → Lower → Foot  (e.g. dog/cat hock joint)\n" +
                 "The IK solver always bends at the LAST joint before the foot.")]
        public int LimbCount = 2;

        [Tooltip("Direct tag reference to the actual foot / paw bone at the END of the chain. " +
                 "Used for weight calculation (grounded vs lifted) and final foot rotation. " +
                 "If left empty the bone is auto-resolved by traversing LimbCount children from UpperBone.")]
        public IKTag FootTag;

        [Tooltip("Optional tag for a pole / hint transform that controls knee / hock bend direction.")]
        public IKTag HintTag;

        // ── Runtime-only (NonSerialized) ─────────────────────────────────────

        [NonSerialized] public Transform UpperBone;   // MWC — top of IK chain (thigh / shoulder)
        [NonSerialized] public Transform MidBone;     // MWC — first child (shin / forearm) — always resolved
        [NonSerialized] public Transform LowerBone;   // MWC — grandchild; only used when LimbCount == 3 (hock)
        [NonSerialized] public Transform FootBone;    // MWC — actual paw / foot (rotation weight + final rotation)

        /// <summary>
        /// The effective bend-joint for the IK solver.
        /// LimbCount == 2 → IKMidBone = MidBone
        /// LimbCount == 3 → IKMidBone = LowerBone  (solver spans Upper→LowerBone as the "upper segment")
        /// </summary>
        [NonSerialized] public Transform IKMidBone;

        [NonSerialized] public float UpperLen;        // MWC — UpperBone to IKMidBone (may span >1 physical bone)
        [NonSerialized] public float LowerLen;        // MWC — IKMidBone to FootBone
        [NonSerialized] public bool Valid;            // MWC — false if resolution failed at Start

        [NonSerialized] public bool HasHit;           // MWC — raycast found ground this frame
        [NonSerialized] public Vector3 HitPoint;      // MWC — ground hit position (+ NormalOffset applied)
        [NonSerialized] public Vector3 HitNormal;     // MWC — ground surface normal
        [NonSerialized] public float FootWeight;      // MWC — IK weight derived from foot rotation this frame
        [NonSerialized] public bool NeedsFloorFix;    // MWC — foot lifted but clipping into elevated terrain
        [NonSerialized] public float FloorFixWeight;  // MWC — penetration-based weight for floor correction
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Main Processor
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    [AddTypeMenu("Generic/Quadruped Foot")]
    public class IKProcessorQuadrupedFoot : IKProcessor
    {
        // ─── Bone Tags ────────────────────────────────────────────────────────

        [Tooltip("Tag identifying the Hip bone. Translated vertically on lateral slopes.")]
        public IKTag HipTag;

        [Tooltip("Tag identifying the Chest bone. Counter-rotates on lateral slopes to keep the upper body balanced. Optional.")]
        public IKTag ChestTag;

        [Tooltip("Front-Left leg: upper bone tag (knee and foot resolved automatically) + optional pole hint.")]
        public QuadLegEntry FrontLeftLeg  = new ();

        [Tooltip("Front-Right leg: upper bone tag (knee and foot resolved automatically) + optional pole hint.")]
        public QuadLegEntry FrontRightLeg = new ();

        [Tooltip("Back-Left leg: upper bone tag (knee and foot resolved automatically) + optional pole hint.")]
        public QuadLegEntry BackLeftLeg   = new ();

        [Tooltip("Back-Right leg: upper bone tag (knee and foot resolved automatically) + optional pole hint.")]
        public QuadLegEntry BackRightLeg  = new ();

        // MWC — internal flat array built at Start() for loop iteration; order: FL, FR, BL, BR
        private QuadLegEntry[] m_legs;

        // ─── Ground Raycast ───────────────────────────────────────────────────

        [Tooltip("Layer mask for ground surface detection.")]
        public LayerReference GroundLayer = new(1);

        [Tooltip("World-up offset above the foot bone from which the downward ray is fired.")]
        public float RaycastOriginOffset = 0.3f;

        [Tooltip("Total downward ray length (includes the origin offset above the foot).")]
        public float RaycastLength = 0.8f;

        [Tooltip("Height offset applied above the surface hit point along the surface normal.")]
        public float NormalOffset = 0f;

        [Range(0f, 1f)]
        [Tooltip("How strongly the foot bone aligns its orientation to the surface normal.")]
        public float RotationWeight = 1f;

        // ─── Foot Weight from Animation Rotation ──────────────────────────────

        [Tooltip("Local-space axis of the foot bone pointing toward the ground when the foot is flat. " +
                 "Vector3.down for Y-down rigs; -Vector3.forward for Z-forward rigs.")]
        public Vector3 FootDownAxis = Vector3.down;

        [Tooltip("Angle (°) between the foot's down axis and world-down at which IK weight reaches zero. " +
                 "Below this angle the foot is considered grounded; above it the foot is considered lifted.")]
        public float WeightFalloffAngle = 45f;

        [Tooltip("Curve mapping normalised foot-flatness (0 = lifted, 1 = flat) to an IK weight multiplier.")]
        public AnimationCurve WeightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        // ─── Hip Lateral Offset ───────────────────────────────────────────────

        [Range(0f, 1f)]
        [Tooltip("Scales the left–right foot-hit height delta applied as a vertical offset to the Hip bone. " +
                 "0 = no hip adjustment, 1 = full delta.")]
        public float HipLateralStrength = 0.5f;

        [Tooltip("Smoothing speed for the hip vertical offset (higher = snappier).")]
        public float HipLerpSpeed = 10f;

        // ─── Chest Counter-Rotation ───────────────────────────────────────────

        [Range(0f, 1f)]
        [Tooltip("How strongly the Chest bone rolls opposite to the hip lateral drop.")]
        public float ChestCounterStrength = 0.5f;

        [Tooltip("Smoothing speed for the chest counter-rotation.")]
        public float ChestLerpSpeed = 10f;

        // ─── Debug ────────────────────────────────────────────────────────────


        // ─── Private Runtime Cache ────────────────────────────────────────────

        private Transform m_hipBone;
        private Transform m_chestBone;
        private float m_smoothedHipOffset;      // MWC — smoothed lateral Y offset on hip
        private float m_smoothedChestAngle;     // MWC — smoothed chest roll angle

        public override bool RequireTargets => false; // MWC — all bones are tag-resolved, not from IKSet Targets

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        public override void Start(IKSet set, Animator anim, int index)
        {
            // MWC — build iteration array from named inspector fields (FL, FR, BL, BR)
            m_legs = new QuadLegEntry[] { FrontLeftLeg, FrontRightLeg, BackLeftLeg, BackRightLeg };

            // MWC — resolve hip and chest bones via tags
            if (HipTag == null)
            {
                Debug.LogWarning(
                    $"[QuadrupedFoot: <B>{name}</B>] HipTag is not assigned. " +
                    "Assign a Tag ScriptableObject pointing to the hip bone.", anim);
                Active = false;
                return;
            }

            m_hipBone = GetTarget(set, HipTag);
            if (m_hipBone == null)
            {
                Debug.LogWarning(
                    $"[QuadrupedFoot: <B>{name}</B>] No transform found for HipTag " +
                    $"<B><color=white>{HipTag.name}</color></B>. " +
                    "Ensure the hip bone has an MTags component with this tag assigned.", anim);
                Active = false;
                return;
            }

            // MWC — chest is optional; warn but continue if missing
            if (ChestTag != null)
            {
                m_chestBone = GetTarget(set, ChestTag);
                if (m_chestBone == null)
                    Debug.LogWarning(
                        $"[QuadrupedFoot: <B>{name}</B>] No transform found for ChestTag " +
                        $"<B><color=white>{ChestTag.name}</color></B>. Chest counter-rotation disabled.", anim);
            }

            // MWC — resolve each leg chain from its upper-bone tag
            for (int i = 0; i < m_legs.Length; i++)
            {
                var leg = m_legs[i];
                string slotName = LegSlotName(i);
                leg.Valid      = false;
                leg.LowerBone  = null;

                if (leg.UpperTag == null)
                {
                    Debug.LogWarning(
                        $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] has no UpperTag assigned.", anim);
                    continue;
                }

                leg.UpperBone = GetTarget(set, leg.UpperTag);
                if (leg.UpperBone == null)
                {
                    Debug.LogWarning(
                        $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] — no transform found for " +
                        $"UpperTag <B><color=white>{leg.UpperTag.name}</color></B>. " +
                        "Add an MTags component to the upper limb bone with this tag.", anim);
                    continue;
                }

                // MWC — resolve MidBone (always first child of upper)
                if (leg.UpperBone.childCount == 0)
                {
                    Debug.LogWarning(
                        $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] — upper bone " +
                        $"<B>{leg.UpperBone.name}</B> has no children (expected mid / knee bone).", anim);
                    continue;
                }
                leg.MidBone = leg.UpperBone.GetChild(0);

                if (leg.LimbCount == 3)
                {
                    // MWC — 3-bone chain: Upper → Mid → Lower → Foot
                    // IK solver bends at Lower (the last joint before the foot)
                    if (leg.MidBone.childCount == 0)
                    {
                        Debug.LogWarning(
                            $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] (LimbCount=3) — mid bone " +
                            $"<B>{leg.MidBone.name}</B> has no children (expected lower / hock bone).", anim);
                        continue;
                    }
                    leg.LowerBone = leg.MidBone.GetChild(0);
                    leg.IKMidBone = leg.LowerBone; // MWC — solver bends here

                    if (leg.LowerBone.childCount == 0 && leg.FootTag == null)
                    {
                        Debug.LogWarning(
                            $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] (LimbCount=3) — lower bone " +
                            $"<B>{leg.LowerBone.name}</B> has no children and no FootTag is set (expected foot / paw bone).", anim);
                        continue;
                    }
                    leg.FootBone = (leg.LowerBone.childCount > 0) ? leg.LowerBone.GetChild(0) : null;
                }
                else
                {
                    // MWC — 2-bone chain: Upper → Mid → Foot
                    // IK solver bends at Mid
                    if (leg.MidBone.childCount == 0 && leg.FootTag == null)
                    {
                        Debug.LogWarning(
                            $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] (LimbCount=2) — mid bone " +
                            $"<B>{leg.MidBone.name}</B> has no children and no FootTag is set (expected foot / paw bone).", anim);
                        continue;
                    }
                    leg.IKMidBone = leg.MidBone; // MWC — solver bends here
                    leg.FootBone  = (leg.MidBone.childCount > 0) ? leg.MidBone.GetChild(0) : null;
                }

                // MWC — FootTag overrides the auto-resolved foot bone (direct paw reference for weight + rotation)
                if (leg.FootTag != null)
                {
                    var taggedFoot = GetTarget(set, leg.FootTag);
                    if (taggedFoot != null)
                        leg.FootBone = taggedFoot;
                    else
                        Debug.LogWarning(
                            $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] — FootTag " +
                            $"<B><color=white>{leg.FootTag.name}</color></B> found no transform. " +
                            "Falling back to auto-resolved foot bone.", anim);
                }

                if (leg.FootBone == null)
                {
                    Debug.LogWarning(
                        $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] — foot bone could not be resolved. " +
                        "Set FootTag or ensure the hierarchy has enough children.", anim);
                    continue;
                }

                // MWC — cache IK segment lengths: UpperLen spans Upper→IKMid (may cover 1 or 2 physical bones)
                leg.UpperLen = Vector3.Distance(leg.UpperBone.position, leg.IKMidBone.position);
                leg.LowerLen = Vector3.Distance(leg.IKMidBone.position, leg.FootBone.position);

                if (leg.UpperLen < 0.0001f || leg.LowerLen < 0.0001f)
                {
                    Debug.LogWarning(
                        $"[QuadrupedFoot: <B>{name}</B>] Leg [{slotName}] — IK chain lengths are too small. " +
                        "Ensure the bones are distinct transforms.", anim);
                    continue;
                }

                leg.Valid = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // IK Update
        // ─────────────────────────────────────────────────────────────────────

        public override void LateUpdate(IKSet set, Animator anim, int index, float weight)
        {
            if (weight < 0.001f || m_hipBone == null) return;

            float dt = Time.deltaTime;

            // ── Step 1: Raycast all feet ──────────────────────────────────────
            // MWC — collect ground hits for every valid leg
            for (int i = 0; i < m_legs.Length; i++)
            {
                var leg = m_legs[i];
                leg.HasHit = false;
                leg.NeedsFloorFix = false;

                if (!leg.Valid || leg.FootBone == null) continue;

                float footWeight = GetFootRotationWeight(leg);
                Vector3 origin   = leg.FootBone.position + Vector3.up * RaycastOriginOffset;

                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, RaycastLength, GroundLayer))
                {
                    leg.HasHit    = true;
                    leg.HitPoint  = hit.point + hit.normal * (NormalOffset * OffsetScale(set)); // IK Manager Scale Multiplier
                    leg.HitNormal = hit.normal;
                    leg.FootWeight = footWeight;

                    // MWC — floor constraint: foot is lifted but ray is shorter than the origin offset,
                    // meaning the ground surface is above the current foot bone position → clipping.
                    // Apply correction IK weighted by how deeply it clips.
                    if (footWeight < 0.001f && hit.distance < RaycastOriginOffset)
                    {
                        leg.NeedsFloorFix  = true;
                        leg.FloorFixWeight = Mathf.Clamp01(
                            (RaycastOriginOffset - hit.distance) / RaycastOriginOffset);
                    }
                }
            }

            // ── Step 2: Hip lateral translation ──────────────────────────────
            // MWC — average Y of left pair (FL=0, BL=2) vs right pair (FR=1, BR=3)
            ApplyHipLateralOffset(weight, dt);

            // ── Step 3: Per-leg two-bone IK ───────────────────────────────────
            // MWC — solve IK for each leg after hip position is updated
            for (int i = 0; i < m_legs.Length; i++)
            {
                var leg = m_legs[i];
                if (!leg.HasHit) continue;

                Transform hint = (leg.HintTag != null) ? GetTarget(set, leg.HintTag) : null;

                if (leg.NeedsFloorFix)
                {
                    // MWC — foot clips into elevated terrain while lifted: push to ground surface
                    Solve(leg, leg.HitPoint, leg.FootBone.rotation, hint,
                        leg.FloorFixWeight * weight);
                }
                else
                {
                    float finalWeight = weight * leg.FootWeight;
                    if (finalWeight < 0.001f) continue;

                    Quaternion targetRot = GetFootTargetRotation(leg);
                    Solve(leg, leg.HitPoint, targetRot, hint, finalWeight);
                }
            }

            // ── Step 4: Chest counter-rotation ───────────────────────────────
            // MWC — rolls chest opposite to hip drop to keep upper body balanced
            if (m_chestBone != null && ChestCounterStrength > 0.001f)
                ApplyChestCounterRotation(weight, dt);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Hip Lateral Offset
        // ─────────────────────────────────────────────────────────────────────

        // MWC — translate hip in world Y by the left–right foot height delta
        private void ApplyHipLateralOffset(float weight, float dt)
        {
            // Collect valid left-pair (FL=0, BL=2) and right-pair (FR=1, BR=3) Y positions
            float leftY = 0f; int leftCount = 0;
            float rightY = 0f; int rightCount = 0;

            for (int i = 0; i < m_legs.Length; i++)
            {
                var leg = m_legs[i];
                if (!leg.HasHit) continue;

                if (i == 0 || i == 2) { leftY  += leg.HitPoint.y; leftCount++;  } // MWC — FL, BL
                else                  { rightY += leg.HitPoint.y; rightCount++; } // MWC — FR, BR
            }

            if (leftCount == 0 || rightCount == 0)
            {
                // MWC — can't compute lateral slope without hits on both sides — decay smoothly to zero
                m_smoothedHipOffset = Mathf.Lerp(m_smoothedHipOffset, 0f, dt * HipLerpSpeed);
            }
            else
            {
                float rawDelta        = (leftY / leftCount) - (rightY / rightCount); // MWC — + = left higher
                float targetOffset    = rawDelta * HipLateralStrength * weight;
                m_smoothedHipOffset   = Mathf.Lerp(m_smoothedHipOffset, targetOffset, dt * HipLerpSpeed);
            }

            m_hipBone.position += Vector3.up * m_smoothedHipOffset; // MWC — apply after animator update
        }

        // ─────────────────────────────────────────────────────────────────────
        // Chest Counter-Rotation
        // ─────────────────────────────────────────────────────────────────────

        // MWC — rolls chest on the character's forward axis opposite to hip drop
        private void ApplyChestCounterRotation(float weight, float dt)
        {
            float targetAngle       = -m_smoothedHipOffset * ChestCounterStrength * weight; // MWC — opposite direction
            m_smoothedChestAngle    = Mathf.Lerp(m_smoothedChestAngle, targetAngle, dt * ChestLerpSpeed);

            if (Mathf.Abs(m_smoothedChestAngle) < 0.001f) return;

            // MWC — rotate around the character's forward axis (roll)
            Quaternion rollRot = Quaternion.AngleAxis(m_smoothedChestAngle, m_chestBone.forward);
            m_chestBone.rotation = rollRot * m_chestBone.rotation;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Foot Rotation Weight
        // ─────────────────────────────────────────────────────────────────────

        // MWC — derives per-foot IK weight from how flat the foot is relative to world-down
        private float GetFootRotationWeight(QuadLegEntry leg)
        {
            if (leg.FootBone == null || WeightFalloffAngle < 0.001f) return 0f;

            Vector3 worldFootDown = leg.FootBone.TransformDirection(FootDownAxis).normalized;
            float dot   = Vector3.Dot(worldFootDown, Vector3.down); // 1 = foot pointing down (flat)
            float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;
            float t     = Mathf.Clamp01(1f - angle / WeightFalloffAngle); // 1 = flat, 0 = lifted

            return WeightCurve.Evaluate(t);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Foot Target Rotation
        // ─────────────────────────────────────────────────────────────────────

        // MWC — aligns foot's up axis with the surface normal, blended by RotationWeight
        private Quaternion GetFootTargetRotation(QuadLegEntry leg)
        {
            Vector3 worldFootUp  = leg.FootBone.TransformDirection(-FootDownAxis).normalized;
            Quaternion alignRot  = Quaternion.FromToRotation(worldFootUp, leg.HitNormal);
            Quaternion aligned   = alignRot * leg.FootBone.rotation;
            return Quaternion.Slerp(leg.FootBone.rotation, aligned, RotationWeight);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Two-Bone IK Solver (law of cosines — same algorithm as IKProcessorTwoBone)
        // ─────────────────────────────────────────────────────────────────────

        // MWC — solves the IK chain using Upper, IKMidBone (bend joint), and FootBone as the three points.
        //       For LimbCount=2: IKMidBone == MidBone.
        //       For LimbCount=3: IKMidBone == LowerBone; MidBone follows Upper's rotation implicitly.
        private void Solve(QuadLegEntry leg, Vector3 targetPos, Quaternion targetRot,
                           Transform hint, float weight)
        {
            var aPos = leg.UpperBone.position;
            var bPos = leg.IKMidBone.position;
            var cPos = leg.FootBone.position;

            float upperLen = leg.UpperLen > 0f ? leg.UpperLen : Vector3.Distance(aPos, bPos);
            float lowerLen = leg.LowerLen > 0f ? leg.LowerLen : Vector3.Distance(bPos, cPos);

            var toTarget = targetPos - aPos;
            float dist   = toTarget.magnitude;
            if (dist < 0.0001f) return;

            dist = Mathf.Clamp(dist,
                Mathf.Abs(upperLen - lowerLen) + 0.0001f,
                upperLen + lowerLen - 0.0001f);

            var dirAT = toTarget.normalized;

            // ── Bend-plane normal ─────────────────────────────────────────────
            Vector3 bendAxis;
            if (hint != null)
                bendAxis = Vector3.Cross(dirAT, hint.position - aPos);
            else
                bendAxis = Vector3.Cross(dirAT, bPos - aPos);

            if (bendAxis.sqrMagnitude < 0.0001f)
            {
                var fallback = Mathf.Abs(Vector3.Dot(dirAT, Vector3.up)) > 0.999f
                    ? Vector3.forward
                    : Vector3.up;
                bendAxis = Vector3.Cross(dirAT, fallback);
            }
            bendAxis.Normalize();

            var perpDir = Vector3.Cross(bendAxis, dirAT).normalized;

            // ── Law of cosines: angle at upper (root) bone ────────────────────
            float cosA = (upperLen * upperLen + dist * dist - lowerLen * lowerLen)
                         / (2f * upperLen * dist);
            cosA = Mathf.Clamp(cosA, -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);

            var bDesired = aPos + dirAT * (upperLen * cosA) + perpDir * (upperLen * sinA);

            // ── Rotate upper bone ─────────────────────────────────────────────
            var currentMidDir = (bPos - aPos).normalized;
            var desiredMidDir = (bDesired - aPos).normalized;

            if (Vector3.Dot(currentMidDir, desiredMidDir) < 0.9999f)
            {
                var rotA = Quaternion.FromToRotation(currentMidDir, desiredMidDir);
                if (weight < 0.9999f) rotA = Quaternion.Slerp(Quaternion.identity, rotA, weight);
                leg.UpperBone.rotation = rotA * leg.UpperBone.rotation;
            }

            // Re-read IKMidBone and foot positions — upper rotation has moved the whole chain
            bPos = leg.IKMidBone.position;
            cPos = leg.FootBone.position;

            // ── Rotate IKMid bone (bend joint) ────────────────────────────────
            var currentEndDir = (cPos - bPos).normalized;
            var desiredEndDir = (targetPos - bPos).normalized;

            if (Vector3.Dot(currentEndDir, desiredEndDir) < 0.9999f)
            {
                var rotB = Quaternion.FromToRotation(currentEndDir, desiredEndDir);
                if (weight < 0.9999f) rotB = Quaternion.Slerp(Quaternion.identity, rotB, weight);
                leg.IKMidBone.rotation = rotB * leg.IKMidBone.rotation; // MWC — bends at IKMidBone regardless of LimbCount
            }

            // ── Foot rotation ─────────────────────────────────────────────────
            if (RotationWeight > 0.001f)
                leg.FootBone.rotation = Quaternion.Slerp(leg.FootBone.rotation, targetRot, weight);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Verification
        // ─────────────────────────────────────────────────────────────────────

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            bool ok = true;

            // MWC — check hip tag
            if (HipTag == null)
            {
                Debug.LogWarning($"[QuadrupedFoot: <B>{name}</B>] HipTag is not assigned.", animator);
                ok = false;
            }
            else if (GetTarget(set, HipTag) == null)
            {
                Debug.LogWarning(
                    $"[QuadrupedFoot: <B>{name}</B>] No transform found for HipTag <B>{HipTag.name}</B>.", animator);
                ok = false;
            }

            // MWC — check chest tag (optional but warn)
            if (ChestTag == null)
                Debug.LogWarning($"[QuadrupedFoot: <B>{name}</B>] ChestTag is not assigned — chest counter-rotation disabled.", animator);

            // MWC — check each leg entry
            for (int i = 0; i < m_legs.Length; i++)
            {
                var leg = m_legs[i];
                string slot = LegSlotName(i);

                if (leg.UpperTag == null)
                {
                    Debug.LogWarning($"[QuadrupedFoot: <B>{name}</B>] Leg [{slot}] UpperTag is null.", animator);
                    ok = false;
                    continue;
                }

                var upper = GetTarget(set, leg.UpperTag);
                if (upper == null)
                {
                    Debug.LogWarning(
                        $"[QuadrupedFoot: <B>{name}</B>] Leg [{slot}] No transform for UpperTag <B>{leg.UpperTag.name}</B>.", animator);
                    ok = false;
                    continue;
                }

                // MWC — validate hierarchy depth matches LimbCount (skip child checks if FootTag is provided)
                if (upper.childCount == 0)
                {
                    Debug.LogWarning(
                        $"[QuadrupedFoot: <B>{name}</B>] Leg [{slot}] upper bone <B>{upper.name}</B> has no children (expected mid bone).", animator);
                    ok = false;
                }
                else
                {
                    var mid = upper.GetChild(0);
                    if (leg.LimbCount == 3)
                    {
                        // MWC — need: upper→mid→lower (→foot or via FootTag)
                        if (mid.childCount == 0)
                        {
                            Debug.LogWarning(
                                $"[QuadrupedFoot: <B>{name}</B>] Leg [{slot}] (LimbCount=3) mid bone <B>{mid.name}</B> has no children (expected lower/hock bone).", animator);
                            ok = false;
                        }
                        else if (mid.GetChild(0).childCount == 0 && leg.FootTag == null)
                        {
                            Debug.LogWarning(
                                $"[QuadrupedFoot: <B>{name}</B>] Leg [{slot}] (LimbCount=3) lower bone <B>{mid.GetChild(0).name}</B> has no children and FootTag is not set.", animator);
                            ok = false;
                        }
                    }
                    else
                    {
                        // MWC — need: upper→mid (→foot or via FootTag)
                        if (mid.childCount == 0 && leg.FootTag == null)
                        {
                            Debug.LogWarning(
                                $"[QuadrupedFoot: <B>{name}</B>] Leg [{slot}] (LimbCount=2) mid bone <B>{mid.name}</B> has no children and FootTag is not set.", animator);
                            ok = false;
                        }
                    }
                }
            }

            if (ok)
                Debug.Log($"<B>[IK Processor: {name}][Quadruped Foot]</B> <color=yellow>[OK]</color>", animator);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        // MWC — human-readable slot label matching inspector field names
        private static readonly string[] k_LegNames = { "Front-Left", "Front-Right", "Back-Left", "Back-Right" };
        private static string LegSlotName(int i) => i < k_LegNames.Length ? k_LegNames[i] : $"Leg{i}";

        // ─────────────────────────────────────────────────────────────────────
        // Gizmos
        // ─────────────────────────────────────────────────────────────────────

        public override void OnDrawGizmos(IKManager manager, IKSet set, Animator anim, float weight)
        {
            if (!set.Gizmos) return;

            // MWC — draw hip marker
            if (m_hipBone != null)
            {
               // Gizmos.color = Color.yellow;
                MDebug.GizmoCircle(m_hipBone.position,manager.transform.rotation, 0.07f, Color.yellow);
            }

            if (m_legs == null || m_legs.Length == 0) return;

            // MWC — draw each leg chain and ground hit
            for (int i = 0; i < m_legs.Length; i++)
            {
                var leg = m_legs[i];
                if (!leg.Valid || leg.UpperBone == null) continue;

                // MWC — draw all bones in chain; LowerBone is included for 3-bone legs
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(leg.UpperBone.position, leg.MidBone.position);
                if (leg.LowerBone != null)
                {
                    Gizmos.DrawLine(leg.MidBone.position, leg.LowerBone.position);
                    Gizmos.DrawLine(leg.LowerBone.position, leg.FootBone.position);
                    Gizmos.DrawWireSphere(leg.LowerBone.position, 0.015f);
                }
                else
                {
                    Gizmos.DrawLine(leg.MidBone.position, leg.FootBone.position);
                }
                Gizmos.DrawWireSphere(leg.UpperBone.position, 0.015f);
                Gizmos.DrawWireSphere(leg.MidBone.position, 0.015f);
                Gizmos.DrawWireSphere(leg.FootBone.position, 0.015f);

                if (leg.HasHit)
                {
                    Gizmos.color = leg.NeedsFloorFix ? Color.magenta : Color.green;
                    Gizmos.DrawWireSphere(leg.HitPoint, 0.025f);
                    Gizmos.DrawLine(leg.HitPoint, leg.HitPoint + leg.HitNormal * 0.12f);

                    Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.7f);
                    Gizmos.DrawLine(leg.FootBone.position, leg.HitPoint);
                }
            }
        }

#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator anim, UnityEngine.Object target, int index)
        {
            if (!Application.isPlaying || !set.Gizmos || !Active) return;

            // MWC — Handles-based visualization during play mode
            if (m_hipBone != null)
            {
                Handles.color = new Color(1f, 1f, 0f, 0.5f);
                Handles.DrawWireDisc(m_hipBone.position, Vector3.up, 0.07f);
            }

            for (int i = 0; i < m_legs.Length; i++)
            {
                var leg = m_legs[i];
                if (!leg.Valid || leg.UpperBone == null) continue;

                // MWC — draw full chain including LowerBone for 3-bone legs
                Handles.color = new Color(0f, 1f, 1f, 0.6f);
                Handles.DrawLine(leg.UpperBone.position, leg.MidBone.position, 2f);
                if (leg.LowerBone != null)
                {
                    Handles.DrawLine(leg.MidBone.position, leg.LowerBone.position, 2f);
                    Handles.DrawLine(leg.LowerBone.position, leg.FootBone.position, 2f);
                }
                else
                {
                    Handles.DrawLine(leg.MidBone.position, leg.FootBone.position, 2f);
                }

                if (leg.HasHit)
                {
                    Handles.color = leg.NeedsFloorFix
                        ? new Color(1f, 0f, 1f, 0.9f)
                        : new Color(0f, 1f, 0f, 0.8f);
                    Handles.DrawWireDisc(leg.HitPoint, leg.HitNormal, 0.03f);
                    Handles.DrawDottedLine(leg.FootBone.position, leg.HitPoint, 4f);
                }
            }
        }
#endif
    }
}
