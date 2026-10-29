// Last Change: 9/17/2026 5:33pm
using UnityEngine;
using Object = UnityEngine.Object;

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("General/Member Compare (get)")]
    public class C2_MemberCompare : ConditionCore
    {
        public override string DynamicName => member.Description();

        [Tooltip("Target to check for the condition")]
        [Hide(nameof(LocalTarget))] public Object Target;

        public MemberValueCompare member = new();

        // ─── ConditionCore overrides ──────────────────────────────────────────────

        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
            member.SetTarget(target);
        }
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;

        protected override bool _Evaluate()
        {
            if (LocalTarget) member.SetTarget(Target);

            return member.Evaluate();
        }
    }
}