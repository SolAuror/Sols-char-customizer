using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public enum TwistBoneSource
    {
        // The segment's end bone (hand, foot). The twist bone follows a share of its roll.
        DistalBone,

        // The segment bone itself (upper arm, thigh). The twist bone cancels a share of its roll.
        SegmentBone
    }

    [Serializable]
    public sealed class TwistBoneBinding
    {
        [SerializeField] private Transform twistBone;
        [Tooltip("The bone at the far end of the twist bone's segment, a direct child of the twist bone's parent.")]
        [SerializeField] private Transform distalBone;
        [SerializeField] private TwistBoneSource source;
        [Tooltip("Share of the source roll applied to the twist bone. Negative values counter-rotate.")]
        [SerializeField, Range(-1f, 1f)] private float weight;

        public TwistBoneBinding()
        {
        }

        public TwistBoneBinding(Transform twistBone, Transform distalBone, TwistBoneSource source, float weight)
        {
            this.twistBone = twistBone;
            this.distalBone = distalBone;
            this.source = source;
            this.weight = Mathf.Clamp(weight, -1f, 1f);
        }

        public Transform TwistBone => twistBone;
        public Transform DistalBone => distalBone;
        public TwistBoneSource Source => source;
        public float Weight => weight;
    }

    // Drives limb twist bones that humanoid animation leaves untouched. Exported rigs
    // such as Auto-Rig Pro skin the forearm, upper arm, thigh, and shin partly to
    // unmapped twist bones; without this, those bones stay at bind and the skin
    // candy-wraps at the wrists and ankles.
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class CharacterTwistBoneDriver : MonoBehaviour
    {
        // Counter-rotation applied at the root of an upper segment. The share fades
        // to zero at the segment's far end.
        public const float DefaultSegmentCounterWeight = -0.5f;

        private static readonly HumanBodyBones[,] LimbSegments =
        {
            { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm },
            { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm },
            { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
            { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg },
            { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot },
            { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg },
            { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }
        };

        [SerializeField] private Animator animator;
        [Tooltip("Finds unmapped child bones named '*twist*' on humanoid limb segments when no bindings are authored.")]
        [SerializeField] private bool autoDetectTwistBones = true;
        [SerializeField] private List<TwistBoneBinding> bindings = new();

        private readonly List<TwistState> states = new();
        private int initializeAttempts;

        public IReadOnlyList<TwistBoneBinding> Bindings => bindings;
        public int ActiveTwistBoneCount => states.Count;

        private void Awake()
        {
            Initialize();
        }

        private void LateUpdate()
        {
            ApplyNow();
        }

        private void OnDisable()
        {
            RestoreBindRotations();
        }

        // Returns false when nothing is driven. Safe to call again after the rig or
        // bindings change.
        public bool Initialize()
        {
            initializeAttempts++;
            states.Clear();
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (autoDetectTwistBones && bindings.Count == 0 && animator != null)
            {
                FindHumanoidTwistBones(animator, bindings);
            }

            Dictionary<Transform, Matrix4x4> bindPoses = CollectBindPoses(animator != null ? animator.transform : transform);
            foreach (TwistBoneBinding binding in bindings)
            {
                if (TwistState.TryCreate(binding, bindPoses, out TwistState state, out string error))
                {
                    states.Add(state);
                }
                else
                {
                    Debug.LogWarning($"Skipped twist bone binding on '{name}'. {error}", this);
                }
            }

            return states.Count > 0;
        }

        public void ApplyNow()
        {
            // An Animator that was inactive during Awake may not resolve humanoid bones
            // yet, so detection gets one retry once the character is running. Skinned
            // bones still read their rest rotation from bind poses at that point.
            if (initializeAttempts == 0 || (states.Count == 0 && initializeAttempts < 2))
            {
                Initialize();
            }

            foreach (TwistState state in states)
            {
                state.Apply();
            }
        }

        // Adds a binding for every unmapped child of a humanoid limb segment whose name
        // contains "twist". Weights follow the bone's position along the segment: lower
        // segments take that share of the hand or foot roll, upper segments counter
        // their own roll most strongly at the root.
        public static int FindHumanoidTwistBones(Animator animator, List<TwistBoneBinding> results)
        {
            if (animator == null || !animator.isHuman || results == null)
            {
                return 0;
            }

            var humanBones = new HashSet<Transform>();
            for (int bone = 0; bone < (int)HumanBodyBones.LastBone; bone++)
            {
                Transform boneTransform = animator.GetBoneTransform((HumanBodyBones)bone);
                if (boneTransform != null)
                {
                    humanBones.Add(boneTransform);
                }
            }

            int added = 0;
            for (int index = 0; index < LimbSegments.GetLength(0); index++)
            {
                Transform segment = animator.GetBoneTransform(LimbSegments[index, 0]);
                Transform distal = animator.GetBoneTransform(LimbSegments[index, 1]);
                if (segment == null || distal == null || distal.parent != segment)
                {
                    continue;
                }

                float length = distal.localPosition.magnitude;
                if (length <= 0.0001f)
                {
                    continue;
                }

                Vector3 axis = distal.localPosition / length;
                TwistBoneSource source = IsUpperSegment(LimbSegments[index, 0])
                    ? TwistBoneSource.SegmentBone
                    : TwistBoneSource.DistalBone;

                for (int child = 0; child < segment.childCount; child++)
                {
                    Transform candidate = segment.GetChild(child);
                    if (humanBones.Contains(candidate) ||
                        candidate.name.IndexOf("twist", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    float along = Mathf.Clamp01(Vector3.Dot(candidate.localPosition, axis) / length);
                    float weight = source == TwistBoneSource.DistalBone
                        ? along
                        : DefaultSegmentCounterWeight * (1f - along);
                    results.Add(new TwistBoneBinding(candidate, distal, source, weight));
                    added++;
                }
            }

            return added;
        }

        // Swing-twist decomposition: the signed angle, in degrees, of the rotation's
        // component about the given unit axis.
        public static float TwistAngle(Quaternion rotation, Vector3 axis)
        {
            float projection = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
            return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(projection, rotation.w) * Mathf.Rad2Deg);
        }

        private static bool IsUpperSegment(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.LeftUpperArm ||
                   bone == HumanBodyBones.RightUpperArm ||
                   bone == HumanBodyBones.LeftUpperLeg ||
                   bone == HumanBodyBones.RightUpperLeg;
        }

        private void RestoreBindRotations()
        {
            foreach (TwistState state in states)
            {
                state.Restore();
            }
        }

        // Skin bind poses give each bone's rest rotation regardless of the pose the
        // character is in when this runs. Keys map bones to their bind-space matrix.
        private static Dictionary<Transform, Matrix4x4> CollectBindPoses(Transform root)
        {
            var result = new Dictionary<Transform, Matrix4x4>();
            foreach (SkinnedMeshRenderer skinnedRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = skinnedRenderer.sharedMesh;
                Transform[] bones = skinnedRenderer.bones;
                if (mesh == null || bones == null)
                {
                    continue;
                }

                Matrix4x4[] bindposes = mesh.bindposes;
                int count = Mathf.Min(bones.Length, bindposes.Length);
                for (int index = 0; index < count; index++)
                {
                    if (bones[index] != null && !result.ContainsKey(bones[index]))
                    {
                        result.Add(bones[index], bindposes[index].inverse);
                    }
                }
            }

            return result;
        }

        private sealed class TwistState
        {
            private readonly Transform twistBone;
            private readonly Transform sourceBone;
            private readonly Quaternion twistBindLocal;
            private readonly Quaternion sourceBindLocal;
            private readonly Vector3 axisInSourceBind;
            private readonly Vector3 axisInSegment;
            private readonly float weight;

            private TwistState(
                Transform twistBone,
                Transform sourceBone,
                Quaternion twistBindLocal,
                Quaternion sourceBindLocal,
                Vector3 axisInSourceBind,
                Vector3 axisInSegment,
                float weight)
            {
                this.twistBone = twistBone;
                this.sourceBone = sourceBone;
                this.twistBindLocal = twistBindLocal;
                this.sourceBindLocal = sourceBindLocal;
                this.axisInSourceBind = axisInSourceBind;
                this.axisInSegment = axisInSegment;
                this.weight = weight;
            }

            public static bool TryCreate(
                TwistBoneBinding binding,
                Dictionary<Transform, Matrix4x4> bindPoses,
                out TwistState state,
                out string error)
            {
                state = null;
                if (binding == null || binding.TwistBone == null || binding.DistalBone == null)
                {
                    error = "The twist bone and distal bone must both be assigned.";
                    return false;
                }

                Transform segment = binding.TwistBone.parent;
                if (segment == null || binding.DistalBone.parent != segment)
                {
                    error = $"'{binding.DistalBone.name}' must be a direct child of '{binding.TwistBone.name}''s parent.";
                    return false;
                }

                Vector3 axisInSegment = binding.DistalBone.localPosition;
                if (axisInSegment.sqrMagnitude <= 0.00000001f)
                {
                    error = $"'{binding.DistalBone.name}' sits on its parent, so the segment has no direction.";
                    return false;
                }

                axisInSegment.Normalize();
                Transform sourceBone = binding.Source == TwistBoneSource.DistalBone ? binding.DistalBone : segment;
                Quaternion sourceBindLocal = GetBindLocalRotation(sourceBone, bindPoses);

                // The segment direction expressed in the source bone's rest frame. For the
                // distal bone it is its parent's segment; for the segment bone, its own.
                Vector3 axisInSourceBind = binding.Source == TwistBoneSource.DistalBone
                    ? Quaternion.Inverse(sourceBindLocal) * axisInSegment
                    : axisInSegment;

                state = new TwistState(
                    binding.TwistBone,
                    sourceBone,
                    GetBindLocalRotation(binding.TwistBone, bindPoses),
                    sourceBindLocal,
                    axisInSourceBind.normalized,
                    axisInSegment,
                    binding.Weight);
                error = null;
                return true;
            }

            public void Apply()
            {
                if (twistBone == null || sourceBone == null)
                {
                    return;
                }

                Quaternion delta = Quaternion.Inverse(sourceBindLocal) * sourceBone.localRotation;
                float angle = TwistAngle(delta, axisInSourceBind) * weight;
                twistBone.localRotation = Quaternion.AngleAxis(angle, axisInSegment) * twistBindLocal;
            }

            public void Restore()
            {
                if (twistBone != null)
                {
                    twistBone.localRotation = twistBindLocal;
                }
            }

            private static Quaternion GetBindLocalRotation(Transform bone, Dictionary<Transform, Matrix4x4> bindPoses)
            {
                if (bone.parent != null &&
                    bindPoses.TryGetValue(bone, out Matrix4x4 boneBind) &&
                    bindPoses.TryGetValue(bone.parent, out Matrix4x4 parentBind))
                {
                    return Quaternion.Inverse(parentBind.rotation) * boneBind.rotation;
                }

                // Unskinned bones fall back to their current rotation, which is the rest
                // pose as long as this runs before the Animator first evaluates.
                return bone.localRotation;
            }
        }
    }
}
