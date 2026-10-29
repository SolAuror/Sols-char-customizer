// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    // MWC — Payload conditions. These do NOT read a component: they compare the raw value that
    // was handed to Conditions2.EvaluateValue(), i.e. the argument of a UnityEvent<int/float/bool/string>.
    //
    // Wiring:  YourEvent(int) → MConditions2.Evaluate(int) → these conditions → Then / Else
    //
    // A UnityEvent can only bind methods whose parameter is bool, int, float, string, enum or
    // UnityEngine.Object, which is why MConditions2 exposes concrete Evaluate(int)/Evaluate(float)/
    // Evaluate(bool)/Evaluate(string) overloads instead of a single Evaluate(object).

    /// <summary>Base for conditions that compare the incoming value instead of a target component</summary>
    [System.Serializable]
    public abstract class C2_Payload : ConditionCore
    {
        /// <summary>True once a value has been received on this evaluation pass</summary>
        protected bool hasPayload;

        protected sealed override void _SetTarget(Object target) { } //Payload conditions have no target

        /// <summary>Fails loudly-ish when the condition is used on an evaluation that carries no value</summary>
        protected bool NoPayload()
        {
            if (debug) MDebug.Log($"<color=orange>[{DynamicName}]</color> received no value. Call MConditions2.Evaluate(value) instead of Evaluate().");
            return false;
        }
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Payload Int")]
    public class C2_PayloadInt : C2_Payload
    {
        public override string DynamicName => $"Value {MTools.CompareToString(Condition)} [{Value.Value}]";

        public ComparerNumber Condition = ComparerNumber.Equal;
        public IntReference Value = new();

        private int payload;

        protected override void _SetValue(object value)
        {
            hasPayload = true;

            switch (value)
            {
                case int i: payload = i; break;
                case float f: payload = Mathf.RoundToInt(f); break;
                case bool b: payload = b ? 1 : 0; break;
                case IDs id: payload = id.ID; break;
                case string s when int.TryParse(s, out var parsed): payload = parsed; break;
                default: hasPayload = false; break;
            }
        }

        protected override bool _Evaluate()
            => hasPayload ? payload.MCompare(Value.Value, Condition) : NoPayload();
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Payload Float")]
    public class C2_PayloadFloat : C2_Payload
    {
        public override string DynamicName => $"Value {MTools.CompareToString(Condition)} [{Value.Value}]";

        public ComparerNumber Condition = ComparerNumber.Equal;
        public FloatReference Value = new();

        private float payload;

        protected override void _SetValue(object value)
        {
            hasPayload = true;

            switch (value)
            {
                case float f: payload = f; break;
                case int i: payload = i; break;
                case bool b: payload = b ? 1 : 0; break;
                case string s when float.TryParse(s, out var parsed): payload = parsed; break;
                default: hasPayload = false; break;
            }
        }

        protected override bool _Evaluate()
            => hasPayload ? payload.MCompare(Value.Value, Condition) : NoPayload();
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Payload Bool")]
    public class C2_PayloadBool : C2_Payload
    {
        public override string DynamicName => $"Value == [{Value.Value}]";

        public BoolReference Value = new();

        private bool payload;

        protected override void _SetValue(object value)
        {
            hasPayload = true;

            switch (value)
            {
                case bool b: payload = b; break;
                case int i: payload = i != 0; break;
                case float f: payload = f != 0; break;
                case Object o: payload = o != null; break;
                default: hasPayload = false; break;
            }
        }

        protected override bool _Evaluate()
            => hasPayload ? payload == Value.Value : NoPayload();
    }

    //-------------------------------------------------------------------------------------------------------
    [System.Serializable, AddTypeMenu("Values/Payload String")]
    public class C2_PayloadString : C2_Payload
    {
        public override string DynamicName => $"Value {Condition} [{Value.Value}]";

        public enum StringCondition { Equal, Contains, ContainsLower, Empty }

        public StringCondition Condition = StringCondition.Equal;
        public StringReference Value = new();

        private string payload;

        protected override void _SetValue(object value)
        {
            hasPayload = value != null;
            payload = value?.ToString();
        }

        protected override bool _Evaluate()
        {
            if (!hasPayload) return NoPayload();

            return Condition switch
            {
                StringCondition.Equal => payload == Value.Value,
                StringCondition.Contains => payload.Contains(Value.Value),
                StringCondition.ContainsLower => payload.ToLower().Contains(Value.Value.ToLower()),
                StringCondition.Empty => string.IsNullOrEmpty(payload),
                _ => false,
            };
        }
    }
}
