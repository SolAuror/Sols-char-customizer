using UnityEngine;

namespace MalbersAnimations.IK
{
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Hint")]
    public class HumanIKHint : IKProcessorOnAnimIK
    {
        public override bool RequireTargets => true;
        public AvatarIKHint hint;

        public override void OnAnimatorIK(IKSet set, Animator animator, int index, float weight)
        {
            animator.SetIKHintPositionWeight(hint, weight);
            animator.SetIKHintPosition(hint, Bone.position);
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int BoneIndex)
        {
            if (Tag == null)
            {
                Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> has no Tag set for the [IK Processor: {name}]. " +
                    $"Please add a Tag, or set the Target Index to get the reference from the [Targets] array.", animator);
            }
            if (Tag != null && manager.List_GlobalTarget_HasTag(Tag))
            {
                Debug.LogWarning($"The IK Manager <B>[{set.Name}]</B> has no Tag {Tag.name}] in the Global Targets, for the [IK Processor: {name}].");
            }
            else
            {
                Debug.Log($"<B>[IK Processor: {name}][IK Hint]</B>  <color=yellow>[OK]</color>");
            }
        }

    }
}
