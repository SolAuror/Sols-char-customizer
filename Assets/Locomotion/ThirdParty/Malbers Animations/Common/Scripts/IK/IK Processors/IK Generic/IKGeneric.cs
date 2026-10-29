// Last Change: 9/19/2026 3:44pm
using System;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    [Serializable]
    [AddTypeMenu("Generic/IK Offset Rotation")]
    public class IKGeneric : IKProcessor
    {
        public enum IKRotationType
        {
            [InspectorName("Local Rotation Additive")]
            RotationAdditive,
            [InspectorName("Local Rotation Override (Relative to Animator)")]
            RotationOverride
        }

        public IKRotationType IK;
        public Vector3 Offset;

        [Tooltip("Restore the Child bone's rotations after the IK is applied to the bone")]
        public bool KeepChildrenRotation;

        // private (Transform, Quaternion)[] ChildInitialRotations;


        public override bool RequireTargets => true;

        public override void Start(IKSet IKSet, Animator anim, int index)
        {
            base.Start(IKSet, anim, index);
        }


        public override void LateUpdate(IKSet IKSet, Animator anim, int index, float FinalWeight)
        {
            if (Bone == null) return;

            var boneRot = Bone.rotation;

            var TargetRotation = (IK) switch
            {
                IKRotationType.RotationAdditive => boneRot * Quaternion.Euler(Offset),
                IKRotationType.RotationOverride => anim.transform.rotation * Quaternion.Euler(Offset),
                _ => Quaternion.identity,
            };

            var FinalRotation = Quaternion.Lerp(boneRot, TargetRotation, FinalWeight);

            //MWC — was RotateLockChildren(), which right-multiplies its argument as a DELTA (rotation *= value).
            //[FinalRotation] is the final WORLD rotation, so the bone ended up at (boneRot * boneRot * Offset):
            //the Offset was applied on top of a doubled bone rotation and the Weight did not behave as a blend.
            Bone.SetRotationLockChildren(FinalRotation, KeepChildrenRotation);
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

#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator animator, UnityEngine.Object Target, int index)
        {
            if (Application.isPlaying)
            {
                if (set.Gizmos && Active && Bone != null)
                {
                    Quaternion startRotation;

                    if (Tools.current == Tool.Rotate)
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            Vector3 Pos = Bone.position;
                            Quaternion NewRotation = Quaternion.identity;

                            switch (IK)
                            {
                                case IKRotationType.RotationAdditive:
                                    startRotation = Bone.parent.rotation; //Get the Rotation before IK 
                                    NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(Offset), Pos);
                                    NewRotation = Quaternion.Inverse(startRotation) * NewRotation;
                                    break;

                                case IKRotationType.RotationOverride:
                                    startRotation = animator.transform.rotation;
                                    NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(Offset), Pos);
                                    NewRotation = Quaternion.Inverse(startRotation) * NewRotation;
                                    break;
                                default:
                                    break;
                            }

                            if (cc.changed)
                            {
                                Undo.RecordObject(Target, "Change Rot");
                                Offset = NewRotation.eulerAngles;
                                EditorUtility.SetDirty(Target);
                            }
                        }
                    }
                }
            }
        }
#endif
    }
}
