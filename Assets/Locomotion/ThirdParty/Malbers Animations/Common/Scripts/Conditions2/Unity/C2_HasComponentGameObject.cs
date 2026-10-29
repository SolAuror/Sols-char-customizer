// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    [AddTypeMenu("Unity/GameObject has Component")]
    public class C2_HasComponentGameObject : ConditionCore
    {
        public override string DynamicName =>
            $"[{(Target.Value ? Target.Value.name : "GameObject")}] Has Component [{component}]";

        [Tooltip("Target to check for the condition "), Hide(nameof(LocalTarget))]
        public GameObjectReference Target = new();

        [Tooltip("Type of Script-Component attached to the GameObject")]
        public StringReference component = new();

        protected override bool _Evaluate()
        {
            return Target.Value != null && Target.Value.GetComponent(component) != null;
        }

        protected override void _SetTarget(Object target) => Target.Value = MTools.VerifyComponent(target, Target.Value);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target?.Value != null;
    }
}
