// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("General/Malbers Tag")]
    public class C2_MalbersTag : ConditionCore
    {
        public override string DynamicName
        {
            get
            {
                var tagsToString = string.Empty;

                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i] != null) tagsToString += tags[i].name;
                    if (i < tags.Length - 1) tagsToString += ", ";
                }

                return $"Malbers Tag [{Condition}] [{tagsToString}]";
            }
        }

        public enum MalbersTagCondition { HasAnyTag, HasAnyTagInParent, HasAllTags, HasTagInChildren }

        [Tooltip("Target to check for the condition ")]
        [Hide(nameof(LocalTarget))] public GameObjectReference Target = new();

        public MalbersTagCondition Condition;

        public MTags tags;


        protected override bool _Evaluate()
        {
            if (Target != null)
            {
                return Condition switch
                {
                    MalbersTagCondition.HasAnyTag => Target.Value.HasMalbersTag(tags),
                    MalbersTagCondition.HasAnyTagInParent => Target.Value.HasMalbersTagInParent(tags),
                    MalbersTagCondition.HasAllTags => Target.Value.HasAllMalbersTag(tags),
                    MalbersTagCondition.HasTagInChildren => Target.Value.HasAllMalbersTagInChildren(tags),
                    _ => false,
                };
            }
            return false;
        }
        protected override void _SetTarget(Object target) => Target.Value = MTools.VerifyComponent(target, Target.Value);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target?.Value != null;
    }
}
