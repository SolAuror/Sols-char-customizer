// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    public abstract class TransformCoreCondition : ConditionCore
    {
        [Tooltip("Target to check for the condition ")]
        [Hide(nameof(LocalTarget))] public TransformReference Target;
        protected override void _SetTarget(Object target) => Target.Value = MTools.VerifyComponent<Transform>(target, Target.Value);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target?.Value != null;
    }

    [System.Serializable, AddTypeMenu("Unity/Transform")]
    public class C2_Transform : TransformCoreCondition
    {

        public override string DynamicName
        {
            get
            {
                string extraCondition = Condition switch
                {
                    TransformCondition.Name => $"[Name: {checkName.Value}]",
                    TransformCondition.Equal => $"[Equal to: {GetValue()}]",
                    TransformCondition.Child => $"[Child of: {GetValue()}]",
                    TransformCondition.Parent => $"[Parent of: {GetValue()}]",
                    TransformCondition.GrandChild => $"[Grand Child of: {GetValue()}]",
                    TransformCondition.GrandParent => $"[Grand Parent of: {GetValue()}]",
                    TransformCondition.SameHierarchy => $"[Same Hierarchy as: {GetValue()}]",
                    _ => $"[{Condition}]"
                };
                return $"Transform [{(Target.Value ? Target.Value.name : "Dynamic")}] {extraCondition}";

                string GetValue() => $"{(Value.Value != null ? Value.Value.name : "None")}";
            }
        }

        public enum TransformCondition { Null, Equal, Child, Parent, GrandChild, GrandParent, SameHierarchy, Name }

        [Tooltip("Conditions types")]
        public TransformCondition Condition;
        [Tooltip("Transform Value to compare with")]
        [Hide(nameof(Condition), true, (int)TransformCondition.Null, (int)TransformCondition.Name)]
        public TransformReference Value;
        [Tooltip("Name to compare on a transform")]
        [Hide(nameof(Condition), (int)TransformCondition.Name)]
        public StringReference checkName;

        protected override bool _Evaluate()
        {
            if (Condition == TransformCondition.Null) return Target.Value == null;
            if (Target.Value == null) return false;

            return Condition switch
            {
                TransformCondition.Name => Target.Value.name.Contains(checkName.Value),
                TransformCondition.Child => Target.Value.IsChildOf(Value.Value),
                TransformCondition.Equal => Target.Value == Value.Value,
                TransformCondition.Parent => Value.Value.IsChildOf(Target.Value),
                TransformCondition.GrandChild => Target.Value.SameHierarchy(Value.Value),
                TransformCondition.GrandParent => Value.Value.SameHierarchy(Target.Value),
                TransformCondition.Null => false,
                TransformCondition.SameHierarchy => Value.Value.SameHierarchy(Target.Value) || Target.Value.SameHierarchy(Value.Value),
                _ => false,
            };
        }
    }

    [System.Serializable, AddTypeMenu("Unity/Transform Values")]
    public class C2_TransformValues : TransformCoreCondition
    {
        public override string DynamicName
        {
            get
            {
                if (OtherTransform)
                    return $"Transform {Condition} of [{(Target.Value ? Target.Value.name : "Dynamic")}] = [{(Value.Value != null ? Value.Value.name : "None")}]";

                return $"Transform {Condition} of [{(Target.Value ? Target.Value.name : "Dynamic")}] = {m_Value.Value}";
            }
        }

        public enum TransformValuesCondition { Position, Rotation, Scale }
        public TransformValuesCondition Condition;

        [Tooltip("Check if the values are equal to another Transform")]
        public bool OtherTransform = true;

        [Tooltip("Transform Value to compare with another Transform")]
        [Hide(nameof(OtherTransform))] public TransformReference Value;

        [Tooltip("Transform Value to compare with values")]
        [Hide(nameof(OtherTransform), true)] public Vector3Reference m_Value;

        protected override bool _Evaluate()
        {
            if (Target.Value == null) return false;

            if (OtherTransform)
            {
                return Condition switch
                {
                    TransformValuesCondition.Position => Target.Value.position == Value.Value.position,
                    TransformValuesCondition.Rotation => Target.Value.rotation == Value.Value.rotation,
                    TransformValuesCondition.Scale => Target.Value.localScale == m_Value.Value,
                    _ => true,
                };
            }
            else
            {
                return Condition switch
                {
                    TransformValuesCondition.Position => Target.Value.position == m_Value.Value,
                    TransformValuesCondition.Rotation => Target.Value.rotation.eulerAngles == m_Value.Value,
                    TransformValuesCondition.Scale => Target.Value.localScale == m_Value.Value,
                    _ => true,
                };
            }
        }
    }
}
