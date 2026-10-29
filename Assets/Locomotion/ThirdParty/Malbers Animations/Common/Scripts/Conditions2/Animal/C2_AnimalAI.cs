// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Controller.AI;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    [AddComponentMenu("Malbers/Animal Controller/Conditions/Animal AI")]
    [AddTypeMenu("Animal/Animal AI")]
    public class C2_AnimalAI : ConditionCore
    {

        public override string DynamicName
        {
            get
            {
                var displayName = $"Animal AI [{Condition}]";

                if (Condition == AnimalAICondition.CurrentTarget || Condition == AnimalAICondition.NextTarget)
                {
                    if (Value.Value != null)
                    {
                        displayName += $" [{(Value.Value != null ? Value.Value.name : "None")}]";
                    }
                }


                return displayName;
            }
        }

        [RequiredField, Hide(nameof(LocalTarget))]
        public MAnimalAIControl Target;

        public enum AnimalAICondition { enabled, HasTarget, HasNextTarget, Arrived, Waiting, InOffMesh, CurrentTarget, NextTarget }
        public AnimalAICondition Condition;
        [Hide("Condition", 6, 7)]
        public TransformReference Value;

        protected override bool _Evaluate()
        {
            if (!Target) return false;

            return Condition switch
            {
                AnimalAICondition.enabled => Target.enabled,
                AnimalAICondition.HasTarget => Target.Target != null,
                AnimalAICondition.HasNextTarget => Target.NextTarget != null,
                AnimalAICondition.Arrived => Target.HasArrived,
                AnimalAICondition.InOffMesh => Target.InOffMeshLink,
                AnimalAICondition.CurrentTarget => Target.Target == Value.Value,
                AnimalAICondition.Waiting => Target.IsWaiting,
                AnimalAICondition.NextTarget => Target.NextTarget == Value.Value,
                _ => false,
            };
        }

        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;

    }
}
