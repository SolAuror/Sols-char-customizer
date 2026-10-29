// Last Change: 9/19/2026 3:44pm
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    [Serializable, AddTypeMenu("Generic/LookAt")]
    public class IKGenericLookAt : IKProcessor
    {
        public override bool RequireTargets => true;
        public enum UpVectorType { VectorUp, Local, Global }

        public Vector3 Offset;
        public UpVectorType upVector;
        [Hide(nameof(upVector), (int)UpVectorType.Local)]
        public Vector3 LocalUp = Vector3.up;
        [Hide(nameof(upVector), (int)UpVectorType.Global)]
        public Vector3Var WorldUp;

        // MWC — replaced RotationType enum with a 0-1 blend: 0 = additive (delta on top of anim), 1 = override (lerp from anim to look-at)
        [Range(0f, 1f)]
        [Tooltip("0 = Additive: applies the LookAt rotation ON TOP of the animation pose.\n1 = Override: lerps FROM the animation pose TO the LookAt target.")]
        public float rotationBlend = 1f;

        public bool RawAim = false;

        private Quaternion m_AnimatedRotation;
        private Quaternion m_BindRotation;

        public Vector3 UpVector(Animator anim) => upVector switch
        {
            UpVectorType.Local => anim.transform.TransformDirection(LocalUp),
            UpVectorType.Global => WorldUp != null ? WorldUp.Value : Vector3.up,
            _ => Vector3.up,
        };

        public override void Start(IKSet IKSet, Animator anim, int index)
        {
            base.Start(IKSet, anim, index);

            if (Bone != null)
            {
                m_BindRotation = IKSet.Owner.BindPose.TryGetValue(Bone, out var bindRotation) ? bindRotation : Quaternion.identity; // Cache rest/bind pose in local space before any animation runs

                //MWC — seed the animated pose too: default(Quaternion) is (0,0,0,0), NOT identity, and every blend here
                //starts from it. A caller that forgets CacheValue then silently snapped the bone to the full LookAt
                //(Weight and Rotation Blend both dead) instead of degrading to a one frame stale pose.
                m_AnimatedRotation = Bone.localRotation;
            }

            if (IKSet.aimer == null)
            {
                Debug.LogWarning($"There's no Aimer on the IK Set. Generic IK needs an Aimer");
                Active = false; // Disable this processor
                return;
            }
        }

        /// <summary>
        /// Caches the current rotation value of the associated bone, if available.
        /// </summary>
        /// <param name="IKSet">The inverse kinematics set that provides context for the caching operation.</param>
        /// <param name="anim">The animator instance used to evaluate the animation state.</param>
        /// <param name="index">The index of the target element within the IK set.</param>
        public override void CacheValue(IKSet IKSet, Animator anim, int index)
        {
            if (Bone != null) m_AnimatedRotation = Bone.localRotation; // MWC — always cache local rotation; used by both additive and override blend paths
        }

        public override void LateUpdate(IKSet IKSet, Animator anim, int index, float weight)
        {
            if (weight == 0) return; //Do nothing if the weight is zero
            if (Bone == null) return;

            var AimDirection = RawAim ? IKSet.aimer.RawAimDirection : IKSet.aimer.AimDirection;

            if (AimDirection == Vector3.zero) return; //Do nothing if the Aim Direction is zero

            var TargetRotation = Quaternion.LookRotation(AimDirection, UpVector(anim)) * Quaternion.Euler(Offset);
            var parentRot = Bone.parent != null ? Bone.parent.rotation : Quaternion.identity;

            // Additive path (rotationBlend = 0): apply look-at delta on top of the animated pose  
            var targetLocal = Quaternion.Inverse(parentRot) * TargetRotation;
            var delta = targetLocal * Quaternion.Inverse(m_BindRotation);
            var localResult = Quaternion.Slerp(Quaternion.identity, delta, weight) * m_AnimatedRotation;
            var additiveResult = parentRot * localResult;

            // Override path (rotationBlend = 1): lerp from animated pose to look-at target  
            var currentWorldRot = parentRot * m_AnimatedRotation;
            var overrideResult = Quaternion.Slerp(currentWorldRot, TargetRotation, weight);

            // Blend between additive and override, then write world rotation  
            Bone.rotation = Quaternion.Slerp(additiveResult, overrideResult, rotationBlend);
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (Tag != null)
            {
                if (!manager.List_GlobalTarget_HasTag(Tag))
                {
                    Debug.LogWarning($"There's no Global Targets with the Tag {Tag.name} on the IK Manager. Please Add the Tag to your Global Targets and add a Transform value", manager);
                }
                else
                {
                    Debug.Log($"<B>[IK Processor: {name}]</B>  <color=yellow>[OK]</color>");
                }
            }
            else if (set.Targets.Length <= 0)
            {
                Debug.LogWarning($"There's no Targets on the IK Set. Generic IK needs a Target on on Index [{TargetIndex}]");
            }
            else if (set.Targets.Length <= TargetIndex)
            {
                Debug.LogWarning($"The Target Index [{TargetIndex}] is out of range on the IK Set. The IK Set has only {set.Targets.Length} targets");
            }
            else if (set.Targets[TargetIndex].Value == null)
            {
                Debug.LogWarning($"The Target in Index [{TargetIndex}] is Empty. Make sure you set a proper value. in the Editor, or at Runtime");
            }
            else
            {
                Debug.Log($"<B>[IK Processor: {name}]</B>  <color=yellow>[OK]</color>");
            }
        }

#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator anim, UnityEngine.Object target, int index)
        {
            if (!Application.isPlaying || !set.Gizmos || !Bone || !Active) return;

            if (Tools.current == Tool.Rotate)
            {
                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    var TargetRotation = Quaternion.LookRotation(set.aimer.RawAimDirection, UpVector(anim));
                    var NewRotation = Handles.RotationHandle(TargetRotation * Quaternion.Euler(Offset), Bone.position);
                    NewRotation = Quaternion.Inverse(TargetRotation) * NewRotation; //Get the Local Rotation

                    if (cc.changed)
                    {
                        Undo.RecordObject(target, "Change Rot");
                        Offset = NewRotation.eulerAngles;
                        EditorUtility.SetDirty(target);
                    }
                }
            }
        }
#endif
    }
}
