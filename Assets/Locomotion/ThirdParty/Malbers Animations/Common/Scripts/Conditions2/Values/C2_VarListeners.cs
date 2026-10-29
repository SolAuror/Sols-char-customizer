// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    // MWC — All four listeners shared the same operator precedence bug:
    //    ID.Value == Target.ID || ID.Value == -1 && Value1.Value == Target.Value
    // parses as (ID == Target.ID) || (ID == -1 && valueMatches), so a matching ID returned true
    // WITHOUT ever comparing the value. The ID is a filter, not an alternative to the comparison.

    [System.Serializable]
    public abstract class C2_VarListener : ConditionCore
    {
        [Tooltip("Which Var Listener to check when the target has more than one. Use -1 to accept any ID")]
        public IntReference ID = new(-1);

        /// <summary>The ID acts as a filter: -1 means "any listener", otherwise it must match</summary>
        protected bool IDMatch(IntReference targetID) => ID.Value == -1 || ID.Value == targetID;
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Bool Listener")]
    public class C2_BoolVarListener : C2_VarListener
    {
        public override string DynamicName => $"Bool Listener [ID:{ID.Value}] == [{Value1.Value}]";

        [Hide(nameof(LocalTarget))] public BoolVarListener Target;

        public BoolReference Value1;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;                    // MWC — no listener, no match
            if (!IDMatch(Target.ID)) return false;
            return Target.Value == Value1.Value;
        }

        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }


    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Int Listener")]
    public class C2_IntVarListener : C2_VarListener
    {
        public override string DynamicName => $"Int Listener [ID:{ID.Value}] {MTools.CompareToString(Condition)} [{Value1.Value}]";

        [Hide(nameof(LocalTarget))] public IntVarListener Target;

        public ComparerNumber Condition;
        public IntReference Value1;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;
            if (!IDMatch(Target.ID)) return false;
            // MWC — target on the left: reads as "the listener value is [Condition] than [Value1]"
            return Target.Value.MCompare(Value1.Value, Condition);
        }

        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Float Listener")]
    public class C2_FloatVarListener : C2_VarListener
    {
        public override string DynamicName => $"Float Listener [ID:{ID.Value}] {MTools.CompareToString(Condition)} [{Value1.Value}]";

        [Hide(nameof(LocalTarget))] public FloatVarListener Target;
        public ComparerNumber Condition;
        public FloatReference Value1;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;
            if (!IDMatch(Target.ID)) return false;
            return Target.Value.MCompare(Value1.Value, Condition);
        }

        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/String Listener")]
    public class C2_StringVarListener : C2_VarListener
    {
        public override string DynamicName => $"String Listener [ID:{ID.Value}] == [{Value1.Value}]";

        [Hide(nameof(LocalTarget))] public StringVarListener Target;
        public StringReference Value1;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;
            if (!IDMatch(Target.ID)) return false;
            return Target.Value == Value1.Value;
        }

        protected override void _SetTarget(Object target)
        {
            Target = MTools.VerifyComponent(target, Target);
        }
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }
}
