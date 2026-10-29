using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Weapons
{
    [CreateAssetMenu(menuName = "Malbers Animations/Weapons/IK Profile")]
    public class IKProfile : ScriptableObject
    {
        [Tooltip("Use Animator.SetLookAtWeight() Function")]
        public bool LookAtIK = false;
        [Hide("LookAtIK")]
        [Tooltip("(0-1) the global weight of the LookAt, multiplier for other parameters.")]
        public float Weight = 1;
        [Hide("LookAtIK")]
        [Tooltip("(0-1) determines how much the body is involved in the LookAt.")]
        public float BodyWeight = 1;
        [Hide("LookAtIK")]
        [Tooltip("(0-1) determines how much the head is involved in the LookAt.")]
        public float HeadWeight = 1;
        [Hide("LookAtIK")]
        [Tooltip("(0-1) determines how much the eyes is involved in the LookAt.")]
        public float EyesWeight = 1;
        [Hide("LookAtIK")]
        [Tooltip("(0-1) 0.0 means the character is completely unrestrained in motion, 1.0 means he's completely clamped (look at becomes impossible), and 0.5 means he'll be able to move on half of the possible range (180 degrees).")]
        public float ClampWeight = 0f;
        [Hide("LookAtIK")]
        [Tooltip("(0-1) Distance to Determine the LookAtPosition")]
        public float Distance = 100f;
        [Hide("LookAtIK")]
        [Tooltip("Offset of the LookAt Ray Horizontally")]
        public float HorizontalOffset = 0;

        [Tooltip("Offset applied to the Horizontal Offset while aiming Up or Down")]
        [Hide("LookAtIK")]
        public AnimationCurve horizontalToVOffset =
            new(new Keyframe(-1, 0), new Keyframe(0, 0f), new Keyframe(1, 0));
        [Hide("LookAtIK")]
        [Tooltip("Offset of the LookAt Ray Vertically")]
        public float VerticalOffset = 0;

        [Tooltip("Offset applied to the Vertical Offset while aiming Up or Down")]
        [Hide("LookAtIK")]

        public AnimationCurve verticalToHOffset =
          new(new Keyframe(-1, 0), new Keyframe(0, 0f), new Keyframe(1, 0));
        //[Hide("LookAtIK")]
        //public HumanBodyBones AimOrigin = HumanBodyBones.Head;

        [Space]
        public List<BoneOfsset> offsets;
        public virtual void ApplyLookAt(Animator Anim, Vector3 origin, Vector3 Dir, float weight)
        {
            var offsetH = horizontalToVOffset.Evaluate(Dir.y);
            var offsetV = verticalToHOffset.Evaluate(Dir.y);

            // Debug.Log($"offsetH :{offsetH} : Dire: {Dir.y}");

            //  var origin = Anim.GetBoneTransform(AimOrigin);
            Dir = Quaternion.AngleAxis(HorizontalOffset + offsetH, Vector3.up) * Dir;

            var RightV = Vector3.Cross(Dir, Vector3.up);
            Dir = Quaternion.AngleAxis(VerticalOffset + offsetV, RightV) * Dir;

            var ray = new Ray(origin, Dir);
            var Point = ray.GetPoint(Distance);
            Debug.DrawLine(origin, Point, Color.cyan);
            Anim.SetLookAtWeight(Weight * weight, BodyWeight, HeadWeight, EyesWeight, ClampWeight);
            Anim.SetLookAtPosition(Point);
        }



        //MWC — RESTORED to the published (1.5.2b) behavior. This MUST be called from OnAnimatorIK and it writes the bones
        //through Animator.SetBoneLocalRotation, exactly like the store version. An intermediate change wrote the
        //Transforms directly from LateUpdate instead; that broke backwards compatibility: every shipped and user authored
        //IK Profile was tuned against SetBoneLocalRotation, so the same [Rotation Offset] values produced a different pose
        //(Pistol aim off), and the offsets no longer ran in the same pass as the free hand IK goal (two handed weapons like
        //the Bow jittered one frame behind). The LookAt runs here too, at the end, as it always did.
        /// <summary>Apply the bone offsets + LookAt of this profile. Call it ONLY from OnAnimatorIK. Legacy path: the weight is blended inside the pass, so it snaps on limbs as it goes to 0 (see WeightState)</summary>
        public virtual void ApplyOffsets(Animator Anim, Vector3 Origin, Vector3 Direction, float Weight, int layer)
        {
            var transform = Anim.transform;

            for (int i = 0; i < offsets.Count; i++)
            {
                if (Direction == Vector3.zero) continue;
                var offset = offsets[i];

                var bn = Anim.GetBoneTransform(offset.bone);
                if (bn == null) return;

                var BoneRotation = bn.localRotation;
                var finalRotation = GetBoneRotation(offset, bn, BoneRotation, transform, Direction);

                var ResWeight = offset.Weight * Weight;
                if (ResWeight > 0)
                {
                    var result = Quaternion.Lerp(BoneRotation, finalRotation, ResWeight);
                    Anim.SetBoneLocalRotation(offset.bone, result); //MWC — restored (1.5.2b), see the method comment
                }
            }

            if (LookAtIK) ApplyLookAt(Anim, Origin, Direction, Weight); //MWC — restored: LookAt in the same IK pass as the offsets
        }

        // Weight blending with a per character WeightState. Probed on 2026-09-08: Animator.SetBoneLocalRotation is an exact
        // Transform-space write on the spine chain, neck and head, but NOT on arms, legs and hips (the humanoid twist
        // redistribution re-solves those, so writing a limb its own rotation still moves it: 7° upper arm, ~40° hips), and
        // every IK-Pass layer sees what the previous layer wrote. Lerp(bone.localRotation, final, Weight) inside the pass
        // therefore never reached the animated pose as the Weight went to 0, and when it hit 0 the caller stopped applying
        // the Profile and the error vanished in one frame (the snap). Here the pass writes the full rotation (per offset
        // [Weight] included) and the Profile Weight is applied afterwards, in Transform space, by WeightState.ApplyWeight
        // from LateUpdate. The Additive type and the per offset [Weight] blend build from the cached animated rotation, so
        // the offset is applied once per tick instead of once per IK-Pass layer (the legacy path compounds 4x on the Human rig).

        /// <summary>Below this Profile weight the offsets are not written at all, so a LateUpdate that does not get to blend can never leave the full pose on screen. </summary>
        public const float MinWeight = 0.01f;

        /// <summary>Apply the bone offsets + LookAt of this profile, blending the Weight through [state] (call state.ApplyWeight from LateUpdate). Call it ONLY from OnAnimatorIK</summary>
        public virtual void ApplyOffsets(Animator Anim, Vector3 Origin, Vector3 Direction, float Weight, int layer, WeightState state)
        {
            var transform = Anim.transform;
            var tick = WeightState.CurrentTick(Anim);

            state.Begin(this, Anim, tick, Weight); //First IK-Pass layer of the tick: reads the animated pose of every bone

            if (Weight >= MinWeight)
            {
                for (int i = 0; i < offsets.Count; i++)
                {
                    if (Direction == Vector3.zero) continue;
                    var offset = offsets[i];

                    var bn = Anim.GetBoneTransform(offset.bone);
                    if (bn == null) return;

                    if (offset.Weight > 0)
                    {
                        //The ANIMATED rotation, not the live one: a later IK-Pass layer already holds this pass's write,
                        //so a live read compounded the offset once per layer. The Profile Weight is applied in state.ApplyWeight
                        var animated = state.AnimatedLocal(i);
                        var finalRotation = GetBoneRotation(offset, bn, animated, transform, Direction);

                        Anim.SetBoneLocalRotation(offset.bone, Quaternion.Lerp(animated, finalRotation, offset.Weight));
                        state.Wrote[i] = true;
                    }
                }
            }

            if (LookAtIK) ApplyLookAt(Anim, Origin, Direction, Weight); //Unity blends its own LookAt weight, no snap there
        }

        /// <summary>Target local rotation of [bn] for one offset, before any weight is applied. [boneLocal] is the rotation the Additive type adds to. </summary>
        private Quaternion GetBoneRotation(BoneOfsset offset, Transform bn, Quaternion boneLocal, Transform transform, Vector3 Direction)
        {
            var OffsetRot = Quaternion.Euler(offset.RotationOffset);
            var InverseRot = Quaternion.Inverse(bn.parent.rotation); //This is the Bone Rotation in world coordinates

            switch (offset.rotationType)
            {
                case BoneOfsset.IKType.AdditiveOffset:
                    return boneLocal * OffsetRot;
                case BoneOfsset.IKType.OffsetOnly:
                    return OffsetRot;
                case BoneOfsset.IKType.WorldRotation:
                    return InverseRot * OffsetRot;
                case BoneOfsset.IKType.LookAtDir:
                    return InverseRot * Quaternion.LookRotation(Direction, transform.up) * OffsetRot;
                case BoneOfsset.IKType.RootRotation:
                    return InverseRot * transform.rotation * OffsetRot;
                case BoneOfsset.IKType.LootAtYAxis:
                    Vector3 RotationAxis = Vector3.Cross(transform.up, Direction).normalized;
                    var VerticalAngle = (Vector3.Angle(transform.up, Direction) - 90);   //Get the Normalized value for the look direction
                    var finalRotation = Quaternion.AngleAxis(VerticalAngle, RotationAxis); // * transform.rotation;
                    Debug.DrawRay(bn.position, RotationAxis, Color.red);
                    Debug.DrawRay(bn.position, Direction, Color.green);
                    return InverseRot * finalRotation * OffsetRot;
                default:
                    return Quaternion.identity;
            }
        }

        /// <summary>Per character blending state of ApplyOffsets. A Profile asset is shared by every character that aims with it, so the state lives on the caller (Weapon Manager), never on the asset. </summary>
        public class WeightState
        {
            /// <summary>Profile that wrote the bones this tick. </summary>
            private IKProfile profile;

            /// <summary>Animated local rotation of each offset bone, read on the first IK Pass of the tick before anything wrote to it. </summary>
            private Quaternion[] animatedLocal = new Quaternion[0];

            /// <summary>Per offset: the IK Pass wrote this bone this tick. </summary>
            internal bool[] Wrote = new bool[0];

            /// <summary>Profile weight received by the IK Pass this tick; the one ApplyWeight blends with, so both passes agree. </summary>
            private float weight;

            private double tick = -1, blendTick = -1;

            /// <summary>Animated local rotation of offset [i], as read on the first IK Pass of the tick. </summary>
            internal Quaternion AnimatedLocal(int i) => animatedLocal[i];

            /// <summary>Id of the IK tick in progress: the render frame, or the physics step when the Animator is Fixed (same rule as IKSet.CurrentTick). </summary>
            public static double CurrentTick(Animator anim) => anim.updateMode == AnimatorUpdateMode.Fixed ? Time.fixedTimeAsDouble : Time.frameCount;

            /// <summary>Called by every IK Pass; only the first pass of a tick caches the animated pose and resets the written flags. </summary>
            internal void Begin(IKProfile p, Animator anim, double currentTick, float w)
            {
                var count = p.offsets.Count;

                if (animatedLocal.Length != count)
                {
                    animatedLocal = new Quaternion[count];
                    Wrote = new bool[count];
                }

                if (tick == currentTick && profile == p) return; //Later IK-Pass layer of the same tick

                tick = currentTick;
                profile = p;
                weight = w;

                for (int i = 0; i < count; i++)
                {
                    Wrote[i] = false;
                    var bn = anim.GetBoneTransform(p.offsets[i].bone);
                    if (bn != null) animatedLocal[i] = bn.localRotation;
                }
            }

            /// <summary>Applies the Profile weight in Transform space, between the animated pose and the one the IK Pass produced. Call it from LateUpdate</summary>
            public void ApplyWeight(Animator anim)
            {
                if (anim == null || profile == null || weight >= 1) return; //Weight 1 is the IK Pass result untouched

                var currentTick = CurrentTick(anim);

                if (tick != currentTick || blendTick == currentTick) return; //Nothing written this tick, or already blended

                blendTick = currentTick;

                for (int i = 0; i < Wrote.Length && i < profile.offsets.Count; i++)
                {
                    if (!Wrote[i]) continue;

                    var bn = anim.GetBoneTransform(profile.offsets[i].bone);
                    if (bn != null) bn.localRotation = Quaternion.Slerp(animatedLocal[i], bn.localRotation, weight);
                }
            }
        }

        void OnValidate()
        {
            Weight = Mathf.Clamp01(Weight);
            BodyWeight = Mathf.Clamp01(BodyWeight);
            HeadWeight = Mathf.Clamp01(HeadWeight);
            EyesWeight = Mathf.Clamp01(EyesWeight);
            ClampWeight = Mathf.Clamp01(ClampWeight);


            foreach (var item in offsets)
            {
                item.name = item.rotationType.ToString() + " [" + item.bone.ToString() + "]";
            }
        }
    }

    [System.Serializable]
    public class BoneOfsset
    {
        [HideInInspector]
        public string name;

        public IKType rotationType;
        [SearcheableEnum] public HumanBodyBones bone;
        public Vector3 RotationOffset;
        [Range(0, 1)]
        public float Weight;
        public Quaternion ParentBoneOffset { get; set; }


        public enum IKType
        {
            [InspectorName("Additive Local Rotation")]
            AdditiveOffset,
            [InspectorName("Override Local Rotation")]
            OffsetOnly,
            [InspectorName("World Rotation")]
            WorldRotation,
            [InspectorName("World Rotation Relative to Root")]
            RootRotation,
            [InspectorName("LookAt Aimer Direction")]
            LookAtDir,
            [InspectorName("LookAt Aimer Direction No Horizontal")]
            LootAtYAxis
        }
    }


}