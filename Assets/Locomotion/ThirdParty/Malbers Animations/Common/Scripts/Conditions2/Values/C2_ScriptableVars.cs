// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Boolean")]
    public class C2_Bool : ConditionCore
    {
        public override string DynamicName => $"[{(!Value1.UseConstant && Value1.Variable != null ? Value1.Variable.name : Value1.Value)}] == [{(!Value2.UseConstant && Value2.Variable != null ? Value2.Variable.name : Value2.Value)}]";

        public BoolReference Value1 = new();
        public BoolReference Value2 = new();
        protected override bool _Evaluate() => Value1.Value == Value2.Value;
        protected override void _SetTarget(Object target) { } //null
    }
    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Integer")]
    public class C2_Integer : ConditionCore
    {
        public override string DynamicName => $"[{Value1.Value}] {MTools.CompareToString(Condition)} [{Value2.Value}]";


        public IntReference Value1;
        public ComparerNumber Condition;
        public IntReference Value2;
        protected override bool _Evaluate() => Value1.Value.MCompare(Value2.Value, Condition);
        protected override void _SetTarget(Object target) { } //null
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Float")]
    public class C2_Float : ConditionCore
    {
        public override string DynamicName => $"[{Value1.Value}] {MTools.CompareToString(Condition)} [{Value2.Value}]";

        public FloatReference Value1;
        public ComparerNumber Condition;
        public FloatReference Value2;
        protected override bool _Evaluate() => Value1.Value.MCompare(Value2.Value, Condition);
        protected override void _SetTarget(Object target) { } //null
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/String")]
    public class C2_String : ConditionCore
    {
        public override string DynamicName => $"[{Value1.Value}] {Condition} [{Value2.Value}]";

        public enum StringCondition { Equal, Contains, ContainsLower }
        public StringReference Value1;
        public StringCondition Condition;
        public StringReference Value2;


        protected override bool _Evaluate()
        {
            return Condition switch
            {
                StringCondition.Equal => Value1.Value == Value2.Value,
                StringCondition.Contains => Value1.Value.Contains(Value2.Value),
                StringCondition.ContainsLower => Value1.Value.ToLower().Contains(Value2.Value.ToLower()),
                _ => false,
            };
        }
        protected override void _SetTarget(Object target) { } //null
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Vector3")]
    public class C2_Vector3 : ConditionCore
    {
        public override string DynamicName
        {
            get
            {
                if (useTransform)
                {
                    return $"[{Target.Value} Position] = [{Value2.Value}]";
                }
                return $"[{Value1.Value}] = [{Value2.Value}]";
            }
        }

        [Tooltip("if True, compare the position of this transform to a Vector3 value")]
        public bool useTransform;
        [Hide("useTransform")]
        public TransformReference Target;
        [Hide("useTransform", true)]
        public Vector3Reference Value1;
        public Vector3Reference Value2;
        protected override bool _Evaluate() => useTransform ? Target.Value.position == Value2.Value : Value1.Value == Value2.Value;
        protected override void _SetTarget(Object target) { Target.Value = MTools.VerifyComponent<Transform>(target, Target.Value); }
        /// <summary>True while the typed Target is resolved (only needed when comparing a Transform).</summary>
        public override bool HasTarget => !useTransform || Target?.Value != null;
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Vector2")]
    public class C2_Vector2 : ConditionCore
    {
        public override string DynamicName => $"[{Value1.Value}] = [{Value2.Value}]";

        public Vector2Reference Value1;
        public Vector2Reference Value2;
        protected override bool _Evaluate() => Value1.Value == Value2.Value;
        protected override void _SetTarget(Object target) { } //null
    }
}
