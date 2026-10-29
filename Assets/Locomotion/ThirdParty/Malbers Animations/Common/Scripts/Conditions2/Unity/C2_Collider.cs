// Last Change: 9/17/2026 5:33pm
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("Unity/Collider")]
    public class C2_Collider : ConditionCore
    {
        public override string DynamicName
        {
            get
            {
                var condition = Condition switch
                {
                    ColCondition.Equal => $"Equal to {Value}",
                    ColCondition.PhysicMaterial => $"Physic Material {Material}",
                    _ => $"{Condition}",
                };
                return $"Collider [{condition}]";
            }
        }

        public enum ColCondition { Enabled, Equal, isTrigger, PhysicMaterial, isBox, isCapsule, isSphere, isMeshCollider }

        [Tooltip("Target to check for the condition ")]
        [RequiredField, Hide(nameof(LocalTarget))] public Collider Target;

        public ColCondition Condition;

        [Hide(nameof(Condition), 1)] public Collider Value;
        [Hide(nameof(Condition), 3)] public PhysicsMaterial Material;

        protected override bool _Evaluate()
        {
            if (Target != null)
            {
                return Condition switch
                {
                    ColCondition.Enabled => Target.enabled,
                    ColCondition.Equal => Target == Value,
                    ColCondition.isTrigger => Target.isTrigger,
                    ColCondition.PhysicMaterial => Target.sharedMaterial == Material,
                    ColCondition.isBox => Target is BoxCollider,
                    ColCondition.isCapsule => Target is CapsuleCollider,
                    ColCondition.isSphere => Target is SphereCollider,
                    ColCondition.isMeshCollider => Target is MeshCollider,
                    _ => false,
                };
            }
            return false;
        }
        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }
}
