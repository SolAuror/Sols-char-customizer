using MalbersAnimations.Conditions;
using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking the Look At Angle of the Animator / </summary>
    [System.Serializable, AddTypeMenu("Check Conditions2")]
    public class WeightConditions2 : WeightProcessor
    {
        public override string DynamicName => $"Conditions: [{CheckIf.conditions?.Length ?? 0}]";

        [Tooltip("Evaluate conditions. If true then it will set the weight to 1 if false it will set to 0")]
        public Conditions2 CheckIf = new();

        //MWC — same per tick + phase cache as IKProcessor.GetProcessorConditionWeight: this runs once per Animator
        //IK-Pass layer plus once in LateUpdate (5x per frame on the Human v6 rig) and the answer cannot change
        //between two passes of the same phase. See that method for why the two phases are kept apart.
        private double m_CondTickIK = -1, m_CondTickLate = -1;
        private float m_CondWeightIK = 1, m_CondWeightLate = 1;

        public override float Process(IKSet set, Animator Anim, float weight)
        {
            if (set == null) return weight * (CheckIf.Evaluate(Anim) ? 1 : 0); //No tick to key the cache on

            var tick = set.CurrentTick;

            if (set.InAnimatorIKPass)
            {
                if (m_CondTickIK != tick)
                {
                    m_CondTickIK = tick;
                    m_CondWeightIK = CheckIf.Evaluate(Anim) ? 1 : 0;
                }
                return weight * m_CondWeightIK;
            }

            if (m_CondTickLate != tick)
            {
                m_CondTickLate = tick;
                m_CondWeightLate = CheckIf.Evaluate(Anim) ? 1 : 0;
            }
            return weight * m_CondWeightLate;
        }
    }
}
