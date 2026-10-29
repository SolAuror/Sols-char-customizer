// Last Change: 10/16/2026 9:45am
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Goal RayCast")]
    public class HumanIKGoalRayCast : IKProcessorOnAnimIK
    {
        [Tooltip("Target to to lock any of the limbs ")]
        public AvatarIKGoal goal;

        public RangedFloat RayDistance = new(0.5f, 2);
        public LayerReference HitMask = new(1);
        public AxisDirection direction = AxisDirection.Forward;
        public override bool RequireTargets => false;

        [Tooltip("Use the Target assigned as the Origin of the RayCast")]
        public bool UseTagAsOrigin = false;

        [Hide(nameof(UseTagAsOrigin))]
        public IKTag Origin;

        public bool position = true;
        [Hide(nameof(position))]
        public float NormalOffset;
        public bool rotation = true;
        [Hide(nameof(rotation))]
        public Vector3 Offset;

        private Transform RootBone;

        public Vector3 Direction(Animator anim)
        {
            return direction switch
            {
                AxisDirection.None => Vector3.zero,
                AxisDirection.Right => anim.transform.right,
                AxisDirection.Left => -anim.transform.right,
                AxisDirection.Up => anim.transform.up,
                AxisDirection.Down => -anim.transform.up,
                AxisDirection.Forward => anim.transform.forward,
                AxisDirection.Backward => -anim.transform.forward,
                _ => Vector3.zero,
            };
        }

        public Vector3 NormalFromDirection(Animator anim)
        {
            return direction switch
            {
                AxisDirection.None => Vector3.up,
                AxisDirection.Right => anim.transform.forward,
                AxisDirection.Left => -anim.transform.forward,
                AxisDirection.Up => anim.transform.right,
                AxisDirection.Down => -anim.transform.right,
                AxisDirection.Forward => anim.transform.up,
                AxisDirection.Backward => -anim.transform.up,
                _ => Vector3.up,
            };
        }
        public override void Start(IKSet set, Animator anim, int index)
        {
            //Cache the RootBone in the Local Vars 
            switch (goal)
            {
                case AvatarIKGoal.LeftFoot:
                    Bone = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                    RootBone = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                    break;
                case AvatarIKGoal.RightFoot:
                    Bone = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                    RootBone = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    break;
                case AvatarIKGoal.LeftHand:
                    Bone = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                    RootBone = anim.GetBoneTransform(HumanBodyBones.LeftShoulder);
                    break;
                case AvatarIKGoal.RightHand:
                    Bone = anim.GetBoneTransform(HumanBodyBones.RightHand);
                    RootBone = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
                    break;
                default: break;
            }

            if (UseTagAsOrigin)
            {
                RootBone = GetTarget(set, Origin);
            }
        }


        public override void OnAnimatorIK(IKSet set, Animator anim, int index, float weight)
        {
            if (Bone == null || RootBone == null) return;

            var Dir = this.Direction(anim);
            var MinDistDir = Dir * RayDistance.Min;

            // var NormalV = NormalFromDirection(anim); //Get the Normal Vector

            var StartPoint = MTools.ClosestPointOnPlane(RootBone.position, Dir, Bone.position);
            if (UseTagAsOrigin) StartPoint = set.Targets[TargetIndex].position;

            MDebug.DrawWireSphere(StartPoint, Color.magenta, 0.025f);
            MDebug.DrawWireSphere(Bone.position, Color.white, 0.025f);
            MDebug.DrawRay(StartPoint, MinDistDir, Color.green);
            MDebug.DrawRay(StartPoint + MinDistDir, Dir * RayDistance.Difference, Color.red);

            Vector3 Hit;
            Quaternion Normal;

            if (Physics.Raycast(StartPoint, Dir, out var hit, RayDistance.maxValue, HitMask, QueryTriggerInteraction.Ignore))
            {
                Hit = hit.point;
                MDebug.DrawWireSphere(StartPoint, Color.green, 0.04f);

                weight *= hit.distance.CalculateRangeWeight(RayDistance.Min, RayDistance.Max); //Get the Average Hit

                MDebug.DrawRay(Hit, hit.normal * 0.2f, Color.yellow);

                Quaternion AlignRot = Quaternion.FromToRotation(-Dir, hit.normal);  //Calculate the orientation to Terrain 
                AlignRot = Quaternion.Inverse(Bone.rotation) * AlignRot; //Convert the rotation to Local
                                                                         // AlignRot = Quaternion.Inverse(AlignRot); //Convert the rotation to Local
                Quaternion Target = anim.rootRotation * Bone.rotation * AlignRot * Quaternion.Euler(Offset);


                Normal = Target;

                Hit += (hit.normal * (NormalOffset * OffsetScale(set))); //IK Manager Scale Multiplier
            }
            else
            {
                return;
            }

            if (position)
            {
                anim.SetIKPositionWeight(goal, weight);
                anim.SetIKPosition(goal, Hit);
            }
            if (rotation)
            {
                anim.SetIKRotationWeight(goal, weight);
                anim.SetIKRotation(goal, Normal);
            }
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (UseTagAsOrigin)
            {
                if (Origin == null)
                    Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> has no Origin Tag set for the [IK Processor: {name}]" +
                        $"Please add a Tag in Origin and a reference in the [Targets Global] list .", animator);
            }
            else
                Debug.Log($"<B>[IK Processor: {name}][HumanIK Goal RayCast]</B>  <color=yellow>[OK]</color>");
        }

        public override void OnDrawGizmos(IKManager manager, IKSet IKSet, Animator anim, float weight)
        {
            if (IKSet.Gizmos)
            {
                var Dir = this.Direction(anim);

                if (!Application.isPlaying)
                {
                    Transform bn = null;

                    if (UseTagAsOrigin)
                    {
                        bn = manager.List_GlobalTarget_GetTagValue(Origin);
                    }
                    else
                    {
                        //Cache the RootBone
                        switch (goal)
                        {
                            case AvatarIKGoal.LeftFoot:
                                bn = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                                break;
                            case AvatarIKGoal.RightFoot:
                                bn = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                                break;
                            case AvatarIKGoal.LeftHand:
                                bn = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                                break;
                            case AvatarIKGoal.RightHand:
                                bn = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
                                break;
                        }
                    }

                    if (bn == null) return;

                    var StartPosition = bn.position;

                    Gizmos.color = Color.green;
                    MDebug.GizmoRay(StartPosition, Dir * RayDistance.Min, 2);
                    Gizmos.DrawSphere(StartPosition, 0.02f);
                    Gizmos.color = Color.red;
                    MDebug.GizmoRay(StartPosition + Dir * RayDistance.Min, Dir * (RayDistance.Difference), 2);
                    Gizmos.DrawSphere(StartPosition + Dir * RayDistance.Max, 0.02f);
                }
            }
        }
    }
}
