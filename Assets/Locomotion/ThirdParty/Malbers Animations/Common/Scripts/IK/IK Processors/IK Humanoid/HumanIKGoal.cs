// Last Change: 10/16/2026 9:45am
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor; //MWC — was an unguarded using, which does not exist in a Player build
#endif

namespace MalbersAnimations.IK
{
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Goal")]
    public class HumanIKGoal : IKProcessorOnAnimIK
    {
        public override bool RequireTargets => true;
        [Tooltip("Target to to lock any of the limbs ")]

        [SearcheableEnum]
        public AvatarIKGoal goal;

        public bool position = true;
        [Hide("position")]
        public Vector3 OffsetP;
        public bool rotation = true;
        [Hide("rotation")]
        public Vector3 OffsetR;

        //MWC — Elbow/Knee Hint. The Hint of this limb (Left|Right Elbow for the hands, Left|Right Knee for the feet)
        //is placed at an offset from ANY Human Bone, so the elbow can be pinned to the chest, the hips, the opposite
        //hand… and it follows that bone as it animates. No Target transform is needed for it.
        [Tooltip("Set the Elbow/Knee Hint of this limb, placed at an offset from a Human Bone")]
        public bool hint = false;

        [Tooltip("Human Bone the Hint offset is relative to. The Hint follows this bone as it animates")]
        [Hide(nameof(hint)), SearcheableEnum]
        public HumanBodyBones HintBone = HumanBodyBones.Hips;

        [Tooltip("Hint position, in the LOCAL space of [Hint Bone]")]
        [Hide(nameof(hint))]
        public Vector3 OffsetH;

        [Tooltip("Min and Max Distance to the Goal to modify the weight. Id the distance is lower than the Min the weight is 1. If is greater than the max then the weight is zero")]
        public RangedFloat Distance = new();

        private Transform TargetGoal;
        private Transform TargetBone;

        /// <summary>Elbow/Knee Hint of the limb the Goal belongs to </summary>
        private AvatarIKHint HintGoal;

        /// <summary>Transform of the [Hint Bone] the offset is relative to </summary>
        private Transform HintBoneT;

        /// <summary>Human Bone [HintBoneT] was resolved from, to catch a change made at runtime </summary>
        private HumanBodyBones HintBoneCached = (HumanBodyBones)(-1);

        // The IK Pass sees the ANIMATED pose, but the Scene View handles run after LateUpdate, where the LookAt/Generic
        // Processors have already rotated the spine. The Hint Bone then sits somewhere else, and with a 0.01 scaled rig
        // the local offset is 100x, so the Hint handle and its gizmo end up meters apart. The pose used by the last IK
        // Pass is cached here so the Hint handle is drawn (and inverted) from the same frame as the gizmo.

        /// <summary>Local to world matrix of [HintBoneT] as seen by the last IK Pass. </summary>
        private Matrix4x4 HintIKPose;

        /// <summary>True once the IK Pass has cached [HintIKPose]. </summary>
        private bool IKPoseCached;

        public override void Start(IKSet set, Animator animator, int index)
        {
            //Cache the Bone
            switch (goal)
            {
                case AvatarIKGoal.LeftFoot:
                    Bone = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    TargetBone = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    HintGoal = AvatarIKHint.LeftKnee;
                    break;
                case AvatarIKGoal.RightFoot:
                    Bone = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    TargetBone = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    HintGoal = AvatarIKHint.RightKnee;
                    break;
                case AvatarIKGoal.LeftHand:
                    Bone = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    TargetBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    HintGoal = AvatarIKHint.LeftElbow;
                    break;
                case AvatarIKGoal.RightHand:
                    Bone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    TargetBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    HintGoal = AvatarIKHint.RightElbow;
                    break;
                default:
                    break;
            }

            HintBoneT = GetHintBone(animator);

            TargetGoal = GetTarget(set, Tag);

            IKPoseCached = false; //A new character: the poses cached from the previous one are meaningless
        }

        public override void OnAnimatorIK(IKSet set, Animator animator, int index, float weight)
        {
            if (TargetGoal == null)
            {
                TargetGoal = GetTarget(set, Tag);
                return; //If there's no target skip
            }

            //Check Max and Min Distance if is greater than Zero
            if (Distance.Min != 0 && Distance.Max != 0)
            {
                var DistanceFromRoot = Vector3.Distance(Bone.position, TargetGoal.position);
                weight *= DistanceFromRoot.CalculateRangeWeight(Distance.Min, Distance.Max);

                if (set.Gizmos)
                {
                    var dir = (TargetGoal.position - Bone.position).normalized;
                    MDebug.DrawRay(Bone.position, dir * Distance.Max, Color.gray);
                    MDebug.DrawRay(Bone.position, dir * Distance.Min, Color.green);
                }
            }

            if (position)
            {
                animator.SetIKPositionWeight(goal, weight);
                animator.SetIKPosition(goal, TargetGoal.TransformPoint(OffsetP * OffsetScale(set))); //IK Manager Scale Multiplier
            }
            if (rotation)
            {
                animator.SetIKRotationWeight(goal, weight);
                animator.SetIKRotation(goal, TargetGoal.rotation * Quaternion.Euler(OffsetR));
            }

            //MWC — Elbow/Knee Hint, resolved from the Human Bone it is relative to (re-resolved when the Bone is
            //changed at runtime from the inspector, or when it was not found the first time)
            if (hint)
            {
                if (HintBoneT == null || HintBoneCached != HintBone) HintBoneT = GetHintBone(animator);

                if (HintBoneT != null)
                {
                    HintIKPose = HintBoneT.localToWorldMatrix; //Pose the Scene View handle must use (see IKPoseCached)
                    IKPoseCached = true;

                    var HintPosition = HintIKPose.MultiplyPoint3x4(OffsetH * OffsetScale(set));

                    animator.SetIKHintPositionWeight(HintGoal, weight);
                    animator.SetIKHintPosition(HintGoal, HintPosition);

                    if (set.Gizmos)
                    {
                        MDebug.DrawLine(HintBoneT.position, HintPosition, Color.cyan);
                        MDebug.DrawWireSphere(HintPosition, 0.02f, Color.cyan);
                    }
                }
            }
        }

        /// <summary>Transform of the Human Bone the Hint offset is relative to. Null when the Avatar has no such bone </summary>
        private Transform GetHintBone(Animator animator)
        {
            HintBoneCached = HintBone; //Remember which bone the cached Transform belongs to, so a runtime change re-resolves it
            return hint && animator != null && animator.isHuman ? animator.GetBoneTransform(HintBone) : null;
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (Tag == null)
            {
                Debug.LogError($"The IK Set <B>[{set.Name}]</B> has no Tag set for the [IK Processor: {name}]. " +
                    $"Please add a reference in the [Targets Global] list .", animator);
                return;
            }
            else
            {
                Debug.Log($"<B>[IK Processor: {name}][HumanIK Goal]</B>  <color=yellow>[OK]</color>");
            }

            //MWC — the Hint offset is relative to a Human Bone, which is optional on some Avatars (Chest, Shoulders, Toes…)
            if (hint && animator.isHuman && animator.GetBoneTransform(HintBone) == null)
            {
                Debug.LogWarning($"The [IK Processor: {name}] uses the Hint Bone <B>[{HintBone}]</B>, " +
                    $"but the Avatar of <B>[{animator.name}]</B> does not have it. The Elbow/Knee Hint will be skipped.", animator);
            }
        }


#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator anim, UnityEngine.Object Target, int index)
        {
            if (!Application.isPlaying) return;

            if (!set.Gizmos || !Active || TargetBone == null || TargetGoal == null) return;

            // The Goal handles use the LIVE target: the solved hand and its target ride together on whatever the spine does
            // after the IK Pass, so the final pose is where the hand is seen. The Hint handle instead uses the pose cached
            // by the IK Pass (see IKPoseCached) so it matches the cyan gizmo.
            if (Tools.current == Tool.Rotate)
            {
                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    var startRotation = TargetGoal.rotation;
                    var NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(OffsetR), TargetBone.position);
                    NewRotation = Quaternion.Inverse(startRotation) * NewRotation;

                    if (cc.changed)
                    {
                        Undo.RecordObject(Target, "Change Rot");
                        OffsetR = NewRotation.eulerAngles;
                        EditorUtility.SetDirty(Target);
                    }
                }
            }

            //The Handles show the scaled offsets and a drag is divided back, so the stored values stay unscaled
            var scale = OffsetScale(set);

            if (Tools.current == Tool.Move)
            {
                var Rotation = Tools.pivotRotation == PivotRotation.Local ? TargetGoal.rotation : Quaternion.identity;

                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    Vector3 piv = TargetGoal.TransformPoint(OffsetP * scale);
                    Vector3 NewPos = Handles.PositionHandle(piv, Rotation);

                    if (cc.changed)
                    {
                        Undo.RecordObject(Target, "Change Pos");
                        OffsetP = TargetGoal.InverseTransformPoint(NewPos) / scale;
                        EditorUtility.SetDirty(Target);
                    }
                }

                //MWC — second Move handle for the Elbow/Knee Hint. It sits at the elbow/knee, far from the Goal handle,
                //so both can live under the Move tool without fighting each other.
                if (hint && HintBoneT != null)
                {
                    var hintPose = IKPoseCached ? HintIKPose : HintBoneT.localToWorldMatrix;
                    var HintRotation = Tools.pivotRotation == PivotRotation.Local ? hintPose.rotation : Quaternion.identity;

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        Vector3 HintPiv = hintPose.MultiplyPoint3x4(OffsetH * scale);
                        Vector3 NewHint = Handles.PositionHandle(HintPiv, HintRotation);

                        if (cc.changed)
                        {
                            Undo.RecordObject(Target, "Change Hint");
                            OffsetH = hintPose.inverse.MultiplyPoint3x4(NewHint) / scale;
                            EditorUtility.SetDirty(Target);
                        }
                    }
                }
            }
        }
#endif
    }
}

