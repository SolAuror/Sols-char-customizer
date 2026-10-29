using MalbersAnimations.Conditions;
using UnityEngine;


namespace MalbersAnimations.Controller.AI
{
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Malbers Conditions2", order = 201)]
    public class Condition2Decision : MAIDecision
    {
        public override string DisplayName => "General/Malbers Conditions2";
        public Affected checkOn = Affected.Self;
        public Conditions2 MCondition;

        public override bool Decide(MAnimalBrain brain, int Index)
        {
            return checkOn switch
            {
                Affected.Self => MCondition.Evaluate(brain.Animal),
                Affected.Target => MCondition.Evaluate(brain.TargetAnimal),
                _ => false,
            };
        }
    }
}
