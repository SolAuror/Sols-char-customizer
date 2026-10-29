using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking the Look At Angle of the Animator / </summary>
    [System.Serializable, AddTypeMenu("IK Manager/Target Index Exist")]
    public class WeightTarget : WeightProcessor
    {
        [Tooltip("Check if a transform exist. If it is null then the Weight will be zero")]
        [Min(0)] public int TargetIndex = 0;


        public override string DynamicName => $"Target Index Exist [{TargetIndex}]";

        public override float Process(IKSet set, Animator Anim, float weight)
        {
            return weight * (set.Targets[TargetIndex].Value != null ? 1f : 0f);
        }
    }
}
