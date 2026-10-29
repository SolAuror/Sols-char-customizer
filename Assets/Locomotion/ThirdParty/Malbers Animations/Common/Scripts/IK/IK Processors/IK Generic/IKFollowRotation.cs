// Last Change: 9/19/2026 3:44pm
using MalbersAnimations.Scriptables;
using System;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [Serializable]
    [AddTypeMenu("Generic/IK Follow Rotation")]
    public class IKFollowRotation : IKProcessor
    {
        public bool FollowRoot = true;

        [Tooltip("The Master Object that will override the rotation on the bone")]
        [Hide("FollowRoot", true)]
        public IKTag Master;

        public Vector3Reference Offset;

        public override bool RequireTargets => true;

        private Transform MasterTransform;


        public override void Start(IKSet IKSet, Animator anim, int index)
        {
            base.Start(IKSet, anim, index);

            if (FollowRoot) return; // If Follow Root is true, it will follow the root rotation, so no need to get the Master Transform

            if (Master != null)
            {
                MasterTransform = GetTarget(IKSet, Master);

                if (MasterTransform == null)
                {
                    Debug.LogWarning($"The IK Set <B>[{IKSet.Name}]</B> has no Transform set on the [Targets Global] " +
                        $" <B>[IK Processor: {name}]</B> Needs an a value in for the Master Index [{Master}]." +
                        $"Please add a reference for that index in the [Targets] array.", anim);
                }
            }
            else
            {
                Debug.LogWarning($"The IK Set <B>[{IKSet.Name}]</B> has no Tag set for Master Value [IK Processor: {name}]");
            }
        }

        public override void LateUpdate(IKSet IKSet, Animator anim, int index, float FinalWeight)
        {
            if (Bone == null) return;
            var master = FollowRoot || MasterTransform == null ? anim.transform : MasterTransform;
            if (master == null) return;

            var targetRotation = master.rotation * Quaternion.Euler(Offset);
            var deltaRotation = Quaternion.Lerp(Bone.rotation, targetRotation, FinalWeight);

            Bone.rotation = deltaRotation;
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int BoneIndex)
        {
            if (Tag == null)
            {
                Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> has no Tag set for the [IK Processor: {name}]. " +
                    $"Please add a Tag, or set the Target Index to get the reference from the [Targets] array.", animator);
            }
            if (!FollowRoot && Master == null || !manager.List_GlobalTarget_HasTag(Master))
            {
                Debug.LogWarning($"The IK Manager <B>[{set.Name}]</B> has no Master Tag {Master.name}] in the Global Targets, for the [IK Processor: {name}].");
            }
            else if (Tag != null && manager.List_GlobalTarget_HasTag(Tag))
            {
                Debug.LogWarning($"The IK Manager <B>[{set.Name}]</B> has no Tag {Tag.name}] in the Global Targets, for the [IK Processor: {name}].");
            }
            else
            {
                Debug.Log($"<B>[IK Processor: {name}][External Rotation]</B>  <color=yellow>[OK]</color>");
            }
        }


#if UNITY_EDITOR
        internal override void OnSceneGUI(IKSet set, Animator anim, UnityEngine.Object Target, int index)
        {
            if (Application.isPlaying)
            {
                if (set.Gizmos && Active && Bone != null)
                {
                    if (Tools.current == Tool.Rotate)
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            Vector3 Pos = Bone.position;

                            var master = FollowRoot ? anim.transform : MasterTransform;
                            if (master == null) return;

                            var startRotation = master.rotation;

                            Quaternion NewRotation = Handles.RotationHandle(startRotation * Quaternion.Euler(Offset), Pos);
                            NewRotation = Quaternion.Inverse(startRotation) * NewRotation;

                            if (cc.changed)
                            {
                                Undo.RecordObject(Target, "Change Rot");
                                Offset.Value = NewRotation.eulerAngles;
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
