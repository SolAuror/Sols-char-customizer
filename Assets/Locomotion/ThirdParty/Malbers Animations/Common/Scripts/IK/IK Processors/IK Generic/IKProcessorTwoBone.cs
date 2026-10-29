// Last Change: 9/19/2026 3:44pm
using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    [Serializable]
    [AddTypeMenu("Generic/Two Bone IK")]
    public class IKProcessorTwoBone : IKProcessor
    {
        [Tooltip("Root bone of the IK chain (shoulder / hip)")]
        public Transform Root;

        [Tooltip("Mid bone of the IK chain (elbow / knee)")]
        public Transform Mid;

        [Tooltip("Tip bone of the IK chain (hand / foot)")]
        public Transform Tip;

        [Tooltip("Index into the IKSet Targets array for the pole / hint transform. Set to -1 to disable.")]
        [Min(-1)] public int HintIndex = -1;

        [Tooltip("Tag to look up the pole / hint transform from Global Targets instead of the Targets array index.")]
        public IKTag HintTag;

        [Range(0f, 1f)]
        [Tooltip("How much the tip bone rotates to match the goal target's rotation.")]
        public float RotationWeight = 1f;


        public override bool RequireTargets => true;

        // Structural bone lengths, cached at Start so runtime avoids redundant Distance calls.
        private float m_UpperLen;
        private float m_LowerLen;

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        public override void Start(IKSet set, Animator anim, int index)
        {
            base.Start(set, anim, index); // sets Bone = goal target, validates RequireTargets+Tag warning

            if (Root == null || Mid == null || Tip == null)
            {
                Debug.LogWarning(
                    $"[TwoBoneIK: <B>{name}</B>] on IKSet <B>[{set.Name}]</B> is missing bone references (Root/Mid/Tip). " +
                    "Assign them in the inspector. Disabling processor.", anim);
                Active = false;
                return;
            }

            m_UpperLen = Vector3.Distance(Root.position, Mid.position);
            m_LowerLen = Vector3.Distance(Mid.position, Tip.position);

            if (m_UpperLen < 0.0001f || m_LowerLen < 0.0001f)
            {
                Debug.LogWarning(
                    $"[TwoBoneIK: <B>{name}</B>] Bone lengths are too small. " +
                    "Make sure Root→Mid and Mid→Tip are distinct transforms.", anim);
                Active = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // IK Update
        // ─────────────────────────────────────────────────────────────────────

        public override void LateUpdate(IKSet set, Animator anim, int index, float weight)
        {
            if (weight < 0.001f || Root == null || Mid == null || Tip == null) return;

            var goal = GetTarget(set);
            if (goal == null) return;

            Solve(goal.position, goal.rotation, GetHintTarget(set), weight);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Solver
        // ─────────────────────────────────────────────────────────────────────

        private void Solve(Vector3 targetPos, Quaternion targetRot, Transform hint, float weight)
        {
            var aPos = Root.position;
            var bPos = Mid.position;
            var cPos = Tip.position;

            float upperLen = m_UpperLen > 0f ? m_UpperLen : Vector3.Distance(aPos, bPos);
            float lowerLen = m_LowerLen > 0f ? m_LowerLen : Vector3.Distance(bPos, cPos);

            var toTarget = targetPos - aPos;
            float dist = toTarget.magnitude;
            if (dist < 0.0001f) return;

            // Clamp to the reachable range of the chain
            dist = Mathf.Clamp(dist,
                Mathf.Abs(upperLen - lowerLen) + 0.0001f,
                upperLen + lowerLen - 0.0001f);

            var dirAT = toTarget.normalized;

            // ── Bend-plane normal ──────────────────────────────────────────
            // Prefer the pole/hint to define which side the mid bone bends toward.
            Vector3 bendAxis;
            if (hint != null)
                bendAxis = Vector3.Cross(dirAT, hint.position - aPos);
            else
                bendAxis = Vector3.Cross(dirAT, bPos - aPos);

            if (bendAxis.sqrMagnitude < 0.0001f)
            {
                // Chain is collinear or hint is on the same line — pick an arbitrary fallback.
                var fallback = Mathf.Abs(Vector3.Dot(dirAT, Vector3.up)) > 0.999f
                    ? Vector3.forward
                    : Vector3.up;
                bendAxis = Vector3.Cross(dirAT, fallback);
            }
            bendAxis.Normalize();

            // Perpendicular direction in the bend plane (points toward mid bone)
            var perpDir = Vector3.Cross(bendAxis, dirAT).normalized;

            // ── Law of cosines: angle at the root ─────────────────────────
            float cosA = (upperLen * upperLen + dist * dist - lowerLen * lowerLen)
                         / (2f * upperLen * dist);
            cosA = Mathf.Clamp(cosA, -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);

            // Desired world position of the mid bone
            var bDesired = aPos + dirAT * (upperLen * cosA) + perpDir * (upperLen * sinA);

            // ── Rotate root bone ──────────────────────────────────────────
            var currentMidDir = (bPos - aPos).normalized;
            var desiredMidDir = (bDesired - aPos).normalized;

            if (Vector3.Dot(currentMidDir, desiredMidDir) < 0.9999f)
            {
                var rotA = Quaternion.FromToRotation(currentMidDir, desiredMidDir);
                if (weight < 0.9999f) rotA = Quaternion.Slerp(Quaternion.identity, rotA, weight);
                Root.rotation = rotA * Root.rotation;
            }

            // Re-read mid and tip positions — root rotation has moved them.
            bPos = Mid.position;
            cPos = Tip.position;

            // ── Rotate mid bone ───────────────────────────────────────────
            var currentEndDir = (cPos - bPos).normalized;
            var desiredEndDir = (targetPos - bPos).normalized;

            if (Vector3.Dot(currentEndDir, desiredEndDir) < 0.9999f)
            {
                var rotB = Quaternion.FromToRotation(currentEndDir, desiredEndDir);
                if (weight < 0.9999f) rotB = Quaternion.Slerp(Quaternion.identity, rotB, weight);
                Mid.rotation = rotB * Mid.rotation;
            }

            // ── Tip rotation ──────────────────────────────────────────────
            if (RotationWeight > 0.001f)
                Tip.rotation = Quaternion.Slerp(Tip.rotation, targetRot, RotationWeight * weight);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private Transform GetHintTarget(IKSet set)
        {
            if (HintTag != null) return GetTarget(set, HintTag);
            if (HintIndex >= 0 && HintIndex < set.Targets.Length) return set.Targets[HintIndex].Value;
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Verification
        // ─────────────────────────────────────────────────────────────────────

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            bool ok = true;

            if (Root == null)
            {
                Debug.LogWarning($"[TwoBoneIK: <B>{name}</B>] Root bone is not assigned.", animator);
                ok = false;
            }
            if (Mid == null)
            {
                Debug.LogWarning($"[TwoBoneIK: <B>{name}</B>] Mid bone is not assigned.", animator);
                ok = false;
            }
            if (Tip == null)
            {
                Debug.LogWarning($"[TwoBoneIK: <B>{name}</B>] Tip bone is not assigned.", animator);
                ok = false;
            }
            if (GetTarget(set) == null)
            {
                Debug.LogWarning(
                    $"[TwoBoneIK: <B>{name}</B>] No goal target found on IKSet <B>[{set.Name}]</B>. " +
                    "Assign a Target transform or set a Tag.", animator);
                ok = false;
            }

            if (ok)
                Debug.Log($"<B>[IK Processor: {name}][Two Bone IK]</B> <color=yellow>[OK]</color>", animator);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Gizmos
        // ─────────────────────────────────────────────────────────────────────

        public override void OnDrawGizmos(IKManager manager, IKSet set, Animator anim, float weight)
        {
            if (!set.Gizmos || Root == null || Mid == null || Tip == null) return;

            // Chain bones
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Root.position, Mid.position);
            Gizmos.DrawLine(Mid.position, Tip.position);
            Gizmos.DrawWireSphere(Root.position, 0.02f);
            Gizmos.DrawWireSphere(Mid.position, 0.02f);
            Gizmos.DrawWireSphere(Tip.position, 0.02f);

            // Goal target
            var goal = GetTarget(set);
            if (goal != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(Tip.position, goal.position);
                Gizmos.DrawWireSphere(goal.position, 0.03f);
            }

            // Pole / hint
            var hint = GetHintTarget(set);
            if (hint != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(Mid.position, hint.position);
                Gizmos.DrawWireSphere(hint.position, 0.02f);
            }
        }

#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator anim, UnityEngine.Object target, int index)
        {
            if (!Application.isPlaying || !set.Gizmos || !Active) return;
            if (Root == null || Mid == null || Tip == null) return;

            // Draw the chain in Scene view during play mode
            Handles.color = new Color(0f, 1f, 1f, 0.6f);
            Handles.DrawLine(Root.position, Mid.position, 2f);
            Handles.DrawLine(Mid.position, Tip.position, 2f);

            var goal = GetTarget(set);
            if (goal != null)
            {
                Handles.color = new Color(0f, 1f, 0f, 0.8f);
                Handles.DrawDottedLine(Tip.position, goal.position, 4f);
                Handles.DrawWireDisc(goal.position, anim.transform.up, 0.04f);
            }

            var hint = GetHintTarget(set);
            if (hint != null)
            {
                Handles.color = new Color(1f, 1f, 0f, 0.8f);
                Handles.DrawDottedLine(Mid.position, hint.position, 4f);
                Handles.DrawWireDisc(hint.position, anim.transform.up, 0.03f);
            }
        }
#endif
    }
}
