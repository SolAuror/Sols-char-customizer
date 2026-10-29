using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking  if the Aimer component has a target/ </summary>
    [System.Serializable, AddTypeMenu("Aimer has Target")]
    public class WeightAimTarget : WeightProcessor
    {
        public override string DynamicName => "Aimer has Target";

        public override float Process(IKSet set, Animator Anim, float weight)
        {
            var newWeight = weight * (set.aimer != null && set.aimer.AimTarget ? 1f : 0f);

            return newWeight;
        }
    }
}
