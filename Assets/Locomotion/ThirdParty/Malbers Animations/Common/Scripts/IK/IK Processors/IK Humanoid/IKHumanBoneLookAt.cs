// Last Change: 9/19/2026 3:44pm
using MalbersAnimations.Scriptables;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    //MWC — moved off IKProcessorOnAnimIK (the OnAnimatorIK pass) onto the LateUpdate pass, same as HumanIKBoneRotation.
    //On a Humanoid, Unity applies the retargeted pose to the Transforms AFTER OnAnimatorIK returns, so what is read
    //there is not the current animation pose and what is written to a bone is discarded. SetBoneLocalRotation is the
    //way to write during that pass, but it takes the rotation in the Avatar's humanoid bone space rather than the
    //Transform's, so every rotation built here landed in the wrong frame — a constant error the authored Offsets were
    //silently absorbing. In LateUpdate the Transforms hold the finished pose: the read is real and the write sticks.
    //Trade-off: this now runs after Unity's built-in Goal IK, and the [Layer Index] filter is gone.
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Human Bone Rotation <LookAt>")]
    public class IKHumanBoneLookAt : IKProcessor
    {
        public enum IKRotationType
        {
            [InspectorName("Local Look At (Aim)")]
            LookAt,
            LookAtUpDown,
            [InspectorName("Local Rotation Additive")]
            AdditiveOffset,
            [InspectorName("Local Rotation Override")]
            RotationOverride
        }

        public override bool RequireTargets => false;

        public enum UpVectorType { VectorUp, Local, Global }

        [SearcheableEnum] public HumanBodyBones humanBone;

        [Tooltip("Rotation Offset applied to the bone")]
        public Vector3 offset;


        [Tooltip("Use the Aimer Direction to calculate the LookAt Direction")]
        [Hide("IK", (int)IKRotationType.LookAt)]
        public bool UseAimDirection = true;

        [Hide("IK", (int)IKRotationType.LookAt)]
        public UpVectorType upVector = UpVectorType.VectorUp;
        [Hide("upVector", (int)UpVectorType.Local)]
        public Vector3 LocalUp = new(0, 1, 0);
        [Hide("upVector", (int)UpVectorType.Global)]
        public Vector3Var WorldUp;


        public Vector3 UpVector
        {
            get
            {
                return upVector switch
                {
                    UpVectorType.Local => LocalUp,
                    UpVectorType.Global => (Vector3)WorldUp,
                    _ => Vector3.up,
                };
            }
        }

        /// <summary>  LookAt the Aimer Direction</summary>
        public override void LateUpdate(IKSet set, Animator anim, int index, float weight)
        {
            var Direction = set.aimer.AimDirection; //Get the Direction from the Aimer
            var Bone = anim.GetBoneTransform(humanBone); //Get the Bone


            var angle = Vector3.Angle(anim.transform.forward, Direction);

            //if (LookAtLimit.maxValue != 0 && LookAtLimit.minValue != 0) //Check the Limit in case there is a limit
            //    weight *= angle.CalculateRangeWeight(LookAtLimit.minValue, LookAtLimit.maxValue);

            if (set.Gizmos) MDebug.DrawRay(Bone.transform.position, Direction.normalized, Color.Lerp(Color.black, Color.green, weight));

            if (weight == 0) return;


            var OffsetRot = Quaternion.Euler(offset);
            var InverseRot = Quaternion.Inverse(Bone.parent.rotation); //This is the Bone Rotation in world coordinates

            var BoneRotation = Bone.localRotation;



            Quaternion targetRot = InverseRot * Quaternion.LookRotation(Direction, UpVector) * OffsetRot;

            var localRotation = Quaternion.Slerp(BoneRotation, targetRot, weight); //Covert the Look Rotation to Local

            Bone.localRotation = localRotation; //MWC — direct Transform write, see the class comment
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (set.aimer == null)
            {
                Debug.LogError($"The IK Set <B>[{set.Name}]</B> has no Aimer set on the [Aimer] field." +
                    $" <B>[IK Processor: {name}]</B> Needs an Aimer to work." +
                    $" Please add a reference for that index in the [Aimer] field", animator);
            }
            else
            {
                Debug.Log($"<B>[IK Processor: {name}][IKHuman - BoneLookAt]</B>  <color=yellow>[OK]</color>");
            }
        }

#if UNITY_EDITOR && MALBERS_DEBUG

        internal override void OnSceneGUI(IKSet set, Animator animator, Object target, int index)
        {
            if (set.Gizmos && animator && animator.avatar)
            {
                var Bone = animator.GetBoneTransform(humanBone);

                if (Bone)
                {
                    Bone.GetPositionAndRotation(out Vector3 pos, out var rot);

                    Handles.color = Color.green;
                    Handles.SphereHandleCap(0, pos, rot, 0.02f, EventType.Repaint);


                    if (Application.isPlaying)
                    {

                        if (Tools.current == Tool.Rotate)
                        {
                            using (var cc = new EditorGUI.ChangeCheckScope())
                            {
                                Quaternion oldQ = animator.transform.rotation * Quaternion.Euler(offset);

                                Quaternion NewRotation = Handles.RotationHandle(oldQ, pos);

                                NewRotation = Quaternion.Inverse(animator.transform.rotation) * NewRotation;

                                if (cc.changed)
                                {
                                    Undo.RecordObject(target, "Change Offset");
                                    offset = NewRotation.eulerAngles;
                                    EditorUtility.SetDirty(target);
                                }
                            }
                        }
                    }
                }
            }
        }
#endif
    }
}
