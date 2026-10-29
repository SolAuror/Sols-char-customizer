using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("General/Try Chance (0:1)")]
    public class C2_Weight : ConditionCore
    {
        public override string DynamicName => $"Random Chance if Value <= [{Weight.Value}]";

        protected override void _SetTarget(Object target) { }
        [Tooltip("Chance for checking a condition Set the value from  0 to 1")]
        public FloatReference Weight = new();

        protected override bool _Evaluate()
        {
            float prob = Random.Range(0f, 1f);
            if (prob <= Weight)
            {
                return true; //Do not Activate the Zone with low Probability.
            }
            return false;
        }
    }
}
