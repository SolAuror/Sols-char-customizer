// Last Change: 9/19/2026 3:44pm
// MWC — New IK Processor. Keeps a bone's axis aligned to an Up reference using Quaternion.FromToRotation,
// which is a MINIMAL ARC rotation: it kills the tilt (pitch/roll) the lower layers push into the bone and
// leaves the twist AROUND that axis (the aiming yaw authored on the Upper Body layer) completely untouched.
using System;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [Serializable]
    [AddTypeMenu("Generic/Keep Bone Upright (From To)")]
    public class IKKeepUpright : IKProcessor
    {
        /// <summary> Where the Up reference direction comes from </summary>
        public enum UpReferenceType
        {
            [InspectorName("World Up (0,1,0)")]
            World,
            [InspectorName("Root Up (Animator Transform)")]
            Root,
            [InspectorName("Gravity Up (IGravity)")]
            Gravity,
            [InspectorName("Target Up (Tagged Transform)")]
            Target
        }

        [Tooltip("Direction the Bone Axis will be aligned to.\n" +
            "Root: uses the Animator Transform Up (best for characters that can walk on slopes).\n" +
            "World: always straight up.\n" +
            "Gravity: uses the IGravity component Up Vector (MAnimal).\n" +
            "Target: uses the Up vector of a Global Tagged transform.")]
        public UpReferenceType UpReference = UpReferenceType.Root;

        [Tooltip("Global Tagged transform used as the Up reference")]
        [Hide(nameof(UpReference), (int)UpReferenceType.Target)]
        public IKTag UpTag;

        [Tooltip("Find the Bone Axis automatically: at Start it stores which LOCAL axis of the Bone is pointing " +
            "towards the Up reference on the Bind Pose. Keeping it ON is the safe option for any rig")]
        public bool AutoAxis = true;

        [Tooltip("Local axis of the Bone treated as its 'Up'. Usually (0,1,0) but it depends on how the rig was built")]
        [Hide(nameof(AutoAxis), true)]
        public Vector3 BoneAxis = Vector3.up;

        [Tooltip("Extra lean around the Root Right axis, applied to the Up reference. Positive leans the Bone FORWARD")]
        [Range(-90f, 90f)] public float PitchOffset = 0;

        [Tooltip("Extra lean around the Root Forward axis, applied to the Up reference. Positive leans the Bone LEFT")]
        [Range(-90f, 90f)] public float RollOffset = 0;

        [Tooltip("Maximum correction the Processor is allowed to apply, in degrees. Zero means no limit")]
        [Min(0)] public float MaxAngle = 0;

        [Tooltip("Restore the Child bones' world rotations after the correction is applied to the Bone. " +
            "Leave it OFF so the whole chain above the Bone (Chest, Arms, Weapon) inherits the correction")]
        public bool KeepChildrenRotation = false;


        /// <summary> Resolved local axis used as the Bone 'Up'. Set by AutoAxis or copied from [BoneAxis] </summary>
        private Vector3 m_Axis = Vector3.up;

        /// <summary> Cached IGravity source used by the [Gravity] Up reference </summary>
        private IGravity m_Gravity;

        /// <summary> Cached transform used by the [Target] Up reference </summary>
        private Transform m_UpTarget;

        public override bool RequireTargets => true;

        /// <summary> Resolves the Bone, the Bone Axis and the Up reference source </summary>
        public override void Start(IKSet set, Animator anim, int index)
        {
            base.Start(set, anim, index);

            if (Bone == null) return;

            // MWC — Bind Pose sampling. Start() runs before the Animator writes its first pose, so the Bone is
            // still on its imported orientation: the local direction pointing to the Root Up IS the bone's Up axis.
            if (AutoAxis)
            {
                m_Axis = Bone.InverseTransformDirection(anim.transform.up).normalized;
            }
            else
            {
                m_Axis = BoneAxis.normalized;
            }

            // MWC — a zeroed axis would NaN the FromToRotation and destroy the pose
            if (m_Axis == Vector3.zero) m_Axis = Vector3.up;

            if (UpReference == UpReferenceType.Gravity)
            {
                m_Gravity = anim.GetComponentInParent<IGravity>();

                if (m_Gravity == null)
                    Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> [IK Processor: {name}] uses [Gravity Up] " +
                        $"but there's no IGravity component (MAnimal) on the hierarchy. Falling back to [World Up]", anim);
            }
            else if (UpReference == UpReferenceType.Target)
            {
                m_UpTarget = GetTarget(set, UpTag);

                if (m_UpTarget == null)
                    Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> [IK Processor: {name}] uses [Target Up] " +
                        $"but the Tag [{(UpTag != null ? UpTag.name : "NULL")}] is not on the [Targets Global] list", anim);
            }
        }

        /// <summary> World space direction the Bone Axis has to be aligned to, including the Pitch/Roll offsets </summary>
        private Vector3 GetUpReference(Animator anim)
        {
            var up = UpReference switch
            {
                UpReferenceType.Root => anim.transform.up,
                UpReferenceType.Gravity => m_Gravity != null ? m_Gravity.UpVector : Vector3.up,
                UpReferenceType.Target => m_UpTarget != null ? m_UpTarget.up : Vector3.up,
                _ => Vector3.up,
            };

            // MWC — Offsets are applied on the ROOT axes, not the world ones, so the lean follows the character
            if (PitchOffset != 0) up = Quaternion.AngleAxis(PitchOffset, anim.transform.right) * up;
            if (RollOffset != 0) up = Quaternion.AngleAxis(RollOffset, anim.transform.forward) * up;

            return up;
        }

        /// <summary> Applies the minimal arc correction that puts the Bone Axis back on the Up reference </summary>
        public override void LateUpdate(IKSet set, Animator anim, int index, float weight)
        {
            if (Bone == null || weight <= 0) return;

            var upRef = GetUpReference(anim);
            if (upRef == Vector3.zero) return;

            var boneUp = Bone.TransformDirection(m_Axis); // Where the Bone Axis is pointing AFTER the animation

            // MWC — THE core of this Processor. FromToRotation gives the shortest rotation between both directions,
            // so it carries no twist around the Up axis: the horizontal aiming rotation survives untouched.
            var delta = Quaternion.FromToRotation(boneUp, upRef);

            if (MaxAngle > 0) delta = Quaternion.RotateTowards(Quaternion.identity, delta, MaxAngle);

            delta = Quaternion.Slerp(Quaternion.identity, delta, weight);

            delta.ToAngleAxis(out var angle, out var axis);

            // MWC — ToAngleAxis returns a garbage/NaN axis when the rotation is (almost) identity. Skipping the
            // write also skips the child rotation caching, so a Processor at rest costs nothing.
            if (angle < 0.01f || float.IsNaN(axis.x)) return;

            // World space pre-multiply (rotation = delta * rotation) with the optional child lock
            Bone.RotateAroundAxisLockChildRotations(axis, angle, KeepChildrenRotation);

            if (set.Gizmos) //Blue = Bone Axis, Green = Up Reference
            {
                MDebug.Draw_Arrow(Bone.position, boneUp * 0.5f, Color.blue);
                MDebug.Draw_Arrow(Bone.position, upRef * 0.5f, Color.green);
            }
        }

        /// <summary> Editor validation: reports missing Bone references and bad Up reference setups </summary>
        public override void Verify(IKManager manager, IKSet set, Animator animator, int BoneIndex)
        {
            var isValid = true;

            if (Tag != null)
            {
                if (!manager.List_GlobalTarget_HasTag(Tag))
                {
                    Debug.LogWarning($"There's no Global Target with the Tag [{Tag.name}] on the IK Manager. " +
                        $"<B>[IK Processor: {name}]</B> needs it to find the Bone", manager);
                    isValid = false;
                }
            }
            else if (set.Targets.Length <= TargetIndex || set.Targets[TargetIndex].Value == null)
            {
                Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> has no Transform on the [Targets] array - Index [{TargetIndex}]. " +
                    $"<B>[IK Processor: {name}]</B> needs the Bone to align", animator);
                isValid = false;
            }

            if (UpReference == UpReferenceType.Target && (UpTag == null || !manager.List_GlobalTarget_HasTag(UpTag)))
            {
                Debug.LogWarning($"<B>[IK Processor: {name}]</B> uses [Target Up] but the Tag " +
                    $"[{(UpTag != null ? UpTag.name : "NULL")}] is not on the [Targets Global] list", manager);
                isValid = false;
            }

            if (UpReference == UpReferenceType.Gravity && animator.GetComponentInParent<IGravity>() == null)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}]</B> uses [Gravity Up] but there's no IGravity " +
                    $"component (MAnimal) on the hierarchy", animator);
                isValid = false;
            }

            if (!AutoAxis && BoneAxis == Vector3.zero)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}]</B> has a zeroed [Bone Axis]. Use (0,1,0) or turn [Auto Axis] on", animator);
                isValid = false;
            }

            if (isValid)
            {
                Debug.Log($"<B>[IK Processor: {name}][IK Keep Upright]</B>  <color=yellow>[OK]</color>");
            }
        }
    }
}
