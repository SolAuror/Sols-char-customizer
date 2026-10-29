// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("General/Check Distance")]

    public class C2_Distance : ConditionCore
    {
        public override string DynamicName =>
            $"Distance from [{(Target.Value ? Target.Value.name : "Dynamic")}] to [{(Target2.Value ? Target2.Value.name : "Target2")}] " +
            $"{MTools.CompareToString(Condition)} [{Distance.Value}]";

        protected override void _SetTarget(Object target) => Target.Value = MTools.VerifyComponent(target, Target.Value);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target?.Value != null;

        [Tooltip("Target to check for the condition")]
        [Hide(nameof(LocalTarget))] public GameObjectReference Target = new();

        public GameObjectReference Target2 = new();
        public ComparerNumber Condition = ComparerNumber.Less;

        public FloatReference Distance = new(5);

        protected override bool _Evaluate()
        {
            if (Target == null) return false;
            return Vector3.Distance(Target.Value.transform.position, Target2.Value.transform.position).MCompare(Distance.Value, Condition);

        }
    }
}
