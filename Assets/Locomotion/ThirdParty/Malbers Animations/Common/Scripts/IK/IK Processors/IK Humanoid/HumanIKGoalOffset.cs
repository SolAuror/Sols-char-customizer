// Last Change: 10/16/2026 4:12pm
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Goal Offset")]
    public class HumanIKGoalOffset : IKProcessorOnAnimIK
    {
        public override bool RequireTargets => false;

        [Tooltip("Target to to lock any of the limbs ")]
        public AvatarIKGoal goal = AvatarIKGoal.RightHand;

        public bool RelativeToRoot = true;

        [Hide(nameof(RelativeToRoot), true), SearcheableEnum]
        public HumanBodyBones RelativeTo = HumanBodyBones.UpperChest;

        public Vector3 GoalOffset;
        public Vector3 GoalRotation;

        public bool FixHint = true;

        [Hide(nameof(FixHint), false)]
        public Vector3 HintOffset;

        public bool position = true;
        public bool rotation = true;

        /// <summary>Cached space the offsets are relative to (the Animator root or a Humanoid bone).</summary>
        private Transform space;
        /// <summary>Bone the cached space was resolved for (-1 = the Animator root).</summary>
        private int spaceKey = int.MinValue;

        /// <summary>Caches the space the offsets are relative to.</summary>
        public override void Start(IKSet set, Animator animator, int index)
        {
            spaceKey = int.MinValue;
            GetSpace(animator);
        }

        /// <summary>Returns the root or the RelativeTo bone; re-resolves when either option changes.</summary>
        private Transform GetSpace(Animator animator)
        {
            var key = RelativeToRoot ? -1 : (int)RelativeTo;

            if (space == null || key != spaceKey)
            {
                space = RelativeToRoot ? animator.transform : FindBone(animator, RelativeTo);
                spaceKey = key;
            }
            return space;
        }

        /// <summary>Returns a Humanoid bone, walking up to the closest mapped parent for optional bones (e.g. UpperChest).</summary>
        private static Transform FindBone(Animator animator, HumanBodyBones bone)
        {
            if (animator == null || !animator.isHuman) return null;

            var b = (int)bone;

            while (b >= 0 && b < (int)HumanBodyBones.LastBone)
            {
                var t = animator.GetBoneTransform((HumanBodyBones)b);
                if (t != null) return t;
                b = HumanTrait.GetParentBone(b);
            }
            return animator.transform;
        }


        private AvatarIKHint GetHint()
        {
            return goal switch
            {
                AvatarIKGoal.LeftFoot => AvatarIKHint.LeftKnee,
                AvatarIKGoal.RightFoot => AvatarIKHint.RightKnee,
                AvatarIKGoal.LeftHand => AvatarIKHint.LeftElbow,
                AvatarIKGoal.RightHand => AvatarIKHint.RightElbow,
                _ => AvatarIKHint.LeftKnee,
            };
        }



        /// <summary>Places the IK Goal (and Hint) at the offsets, relative to the root or the chosen bone.</summary>
        public override void OnAnimatorIK(IKSet set, Animator animator, int index, float weight)
        {
            var Root = GetSpace(animator);
            if (Root == null) return;

            var scale = OffsetScale(set); //IK Manager Scale Multiplier (position offsets only)
            var GoalPosition = Root.TransformPoint(GoalOffset * scale);
            var HintPosition = Root.TransformPoint(HintOffset * scale);

            MDebug.DrawWireSphere(GoalPosition, Color.red, 0.1f);
            MDebug.DrawWireSphere(HintPosition, Color.blue, 0.1f);

            if (position)
            {
                animator.SetIKPositionWeight(goal, weight);
                animator.SetIKPosition(goal, GoalPosition);
                MDebug.DrawWireSphere(GoalPosition, Color.green, 0.05f);

                if (FixHint)
                {
                    animator.SetIKHintPositionWeight(GetHint(), weight);
                    animator.SetIKHintPosition(GetHint(), HintPosition);
                    MDebug.DrawWireSphere(HintPosition, Color.green, 0.05f);
                }
            }
            if (rotation)
            {
                animator.SetIKRotationWeight(goal, weight);
                animator.SetIKRotation(goal, Root.rotation * Quaternion.Euler(GoalRotation));
            }
        }


        public override void OnDrawGizmos(IKManager manager, IKSet IKSet, Animator anim, float weight)
        {
            if (IKSet.Gizmos)
            {
                if (anim == null || !anim.isHuman) return;

                var RootBone = RelativeToRoot ? anim.transform : FindBone(anim, RelativeTo);

                var goalTransform = GetGoal(anim);
                if (RootBone == null || goalTransform == null) return;

                var scale = manager != null ? manager.ScaleMultiplier : OffsetScale(IKSet);

                var oldMatrix = Gizmos.matrix;
                Gizmos.matrix = RootBone.localToWorldMatrix;

                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(GoalOffset * scale, 0.05f);
                Gizmos.DrawSphere(GoalOffset * scale, 0.05f);

                Gizmos.matrix = oldMatrix;

                var off = RootBone.TransformPoint(GoalOffset * scale);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(goalTransform.position, off);
                Gizmos.color = Color.gray;
                Gizmos.DrawLine(RootBone.position, off);

                Gizmos.color = Color.green;
                Gizmos.DrawRay(off, RootBone.rotation * Quaternion.Euler(GoalRotation) * Vector3.up * 0.2f);
                Gizmos.color = Color.red;
                Gizmos.DrawRay(off, RootBone.rotation * Quaternion.Euler(GoalRotation) * Vector3.right * 0.2f);
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(off, RootBone.rotation * Quaternion.Euler(GoalRotation) * Vector3.forward * 0.2f);

                if (FixHint)
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawSphere(RootBone.TransformPoint(HintOffset * scale), 0.05f);
                }
            }
        }



        private Transform GetGoal(Animator ani)
        {
            return goal switch
            {
                AvatarIKGoal.LeftFoot => ani.GetBoneTransform(HumanBodyBones.LeftFoot),
                AvatarIKGoal.RightFoot => ani.GetBoneTransform(HumanBodyBones.RightFoot),
                AvatarIKGoal.LeftHand => ani.GetBoneTransform(HumanBodyBones.LeftHand),
                AvatarIKGoal.RightHand => ani.GetBoneTransform(HumanBodyBones.RightHand),
                _ => null,
            };
        }


#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator animator, UnityEngine.Object Target, int index)
        {
            if (set.Gizmos)
            {
                if (animator == null || !animator.isHuman) return;
                var RootBone = RelativeToRoot ? animator.transform : FindBone(animator, RelativeTo);
                var goalTransform = GetGoal(animator);

                if (RootBone == null || goalTransform == null) return;

                //The Handles show the scaled offset and a drag is divided back, so the stored value stays unscaled
                var scale = OffsetScale(set);

                if (Tools.current == Tool.Move)
                {
                    //Follow the Scene view Global/Local toggle: Local uses the Goal rotation for the Goal and the space bone for the Hint
                    var local = Tools.pivotRotation == PivotRotation.Local;
                    var goalHandleRot = local ? RootBone.rotation * Quaternion.Euler(GoalRotation) : Quaternion.identity;
                    var hintHandleRot = local ? RootBone.rotation : Quaternion.identity;

                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        Vector3 piv = RootBone.TransformPoint(GoalOffset * scale);
                        Vector3 NewPivPosition = Handles.PositionHandle(piv, goalHandleRot);

                        if (cc.changed)
                        {
                            Undo.RecordObject(Target, "Change Pos Goal");
                            GoalOffset = RootBone.InverseTransformPoint(NewPivPosition) / scale;
                            EditorUtility.SetDirty(Target);
                        }
                    }

                    using (var dd = new EditorGUI.ChangeCheckScope())
                    {
                        Vector3 HandlePos = RootBone.TransformPoint(HintOffset * scale);
                        Vector3 NewHintPos = Handles.PositionHandle(HandlePos, hintHandleRot);

                        if (dd.changed)
                        {
                            Undo.RecordObject(Target, "Change Pos Hint");
                            HintOffset = RootBone.InverseTransformPoint(NewHintPos) / scale;
                            EditorUtility.SetDirty(Target);
                        }
                    }
                }
                else if (Tools.current == Tool.Rotate)
                {
                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        Vector3 Pos = RootBone.TransformPoint(GoalOffset * scale);

                        Quaternion oldQ = RootBone.rotation * Quaternion.Euler(GoalRotation);

                        Quaternion NewRotation = Handles.RotationHandle(oldQ, Pos);

                        NewRotation = Quaternion.Inverse(RootBone.rotation) * NewRotation;

                        if (cc.changed)
                        {
                            Undo.RecordObject(Target, "Change Rot");
                            GoalRotation = NewRotation.eulerAngles;
                            EditorUtility.SetDirty(Target);
                        }
                    }
                }
            }
        }

#endif
        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            Debug.Log($"<B>[IK Processor: {name}][HumanIK Goal]</B>  <color=yellow>[OK]</color>");
        }
    }
}
