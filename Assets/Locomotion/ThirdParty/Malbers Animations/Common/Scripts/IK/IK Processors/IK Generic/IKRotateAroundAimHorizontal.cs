// Last Change: 9/19/2026 3:44pm
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;


//#if UNITY_EDITOR
//using UnityEditor;
//#endif

namespace MalbersAnimations.IK
{
    [Serializable]
    [AddTypeMenu("Generic/Rotate Around Aim Horizontal")]
    public class IKRotateAroundAimHorizontal : IKProcessor
    {
        public enum RotateAroundType
        {
            [InspectorName("Horizontal Aim (Green)")]
            Horizontal,
            [InspectorName("Vertical Aim (Red)")]
            Vertical
        }

        public RotateAroundType RotateAround = RotateAroundType.Horizontal;

        public float multiplier = 1;

        public Vector3Reference Offset;

        [Tooltip("Use the Raw Aim Direction instead of the Smoothed Aim Direction")]
        public bool RawAim = false;

        [Tooltip("Restore the Child bone's rotations after the IK is applied to the bone")]
        public bool KeepChildRot;



        public override bool RequireTargets => true;

        public override void LateUpdate(IKSet IKSet, Animator anim, int index, float FinalWeight)
        {
            if (Bone == null) return;   //Missing Bone

            var UpVector = anim.transform.up;

            var AimDirection = RawAim ? IKSet.aimer.RawAimDirection : IKSet.aimer.AimDirection;
            var VerticalAngle = RawAim ? IKSet.aimer.VerticalAngle_Raw : IKSet.aimer.VerticalAngle;
            var HorizontalAngle = RawAim ? IKSet.aimer.HorizontalAngle_Raw : IKSet.aimer.HorizontalAngle;

            if (AimDirection == Vector3.zero) return; //Do nothing if the Aim Direction is zero

            var HorizontalRotationAxis = Vector3.Cross(UpVector, AimDirection).normalized;

            switch (RotateAround)
            {
                case RotateAroundType.Horizontal:
                    // Bone.RotateAround(Bone.position, HorizontalRotationAxis, VerticalAngle * multiplier * -FinalWeight);
                    Bone.RotateAroundAxisLockChildRotations(HorizontalRotationAxis, VerticalAngle * multiplier * -FinalWeight, KeepChildRot);
                    break;
                case RotateAroundType.Vertical:
                    //Bone.RotateAround(Bone.position, UpVector, HorizontalAngle * multiplier * FinalWeight);
                    Bone.RotateAroundAxisLockChildRotations(UpVector, HorizontalAngle * multiplier * FinalWeight, KeepChildRot);
                    break;
                default:
                    break;
            }

            // Bone.rotation *= Quaternion.Euler(Offset.Value * FinalWeight);
            Bone.RotateLockChildren(Quaternion.Euler(Offset.Value * FinalWeight), KeepChildRot);

            if (IKSet.Gizmos)
            {
                MDebug.Draw_Arrow(Bone.position, HorizontalRotationAxis * 2, Color.red);
                MDebug.Draw_Arrow(Bone.position, AimDirection * 2, Color.blue);
                MDebug.Draw_Arrow(Bone.position, UpVector * 2, Color.green);
            }
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int BoneIndex)
        {
            var isValid = true;

            if (set.aimer == null)
            {
                Debug.LogWarning($"There's no Aimer on the IK Set. <B>[IK Processor: {name}]</B> needs an Aimer to get the Aim Direction", animator);
                isValid = false;
            }
            else
            {
                //Check for errors and Null references
                // foreach (var bn in Bones)
                {
                    if (set.Targets.Length < BoneIndex || set.Targets[BoneIndex] == null)
                    {
                        Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> has no Transform set on the [Targets] array - Index [{BoneIndex}]." +
                            $" <B>[IK Processor: {name}]</B> Needs an a value in Index {BoneIndex}." +
                            $"Please add a reference for that index in the [Targets] array.", animator);
                        // set.active = false;

                        isValid = false;
                    }
                }
            }

            if (isValid)
            {
                Debug.Log($"<B>[IK Processor: {name}][IKGeneric]</B>  <color=yellow>[OK]</color>");
            }
        }
    }
}
