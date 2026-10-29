// Last Change: 9/21/2026 9:40am
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MalbersAnimations.Conditions
{
    /// <summary>Conditions to Run on a Object </summary>
    [System.Serializable]
    public abstract class ConditionCore : ICloneable
    {
        /// <summary> Display name in the Condition</summary>
        public virtual string DynamicName => this.GetType().ToString() + " Condition";

        // MWC — per condition on/off switch, so one condition can be muted while testing instead of
        // being deleted. Defaults to true: conditions serialized before this field existed keep the
        // initializer value, so old data behaves exactly as it did.
        [Tooltip("Turn the Condition Off without removing it. Disabled Conditions are skipped on evaluation")]
        [HideInInspector] public bool active = true;

        [Tooltip("Description of the Condition")]
        [HideInInspector] public string desc = string.Empty;
        [HideInInspector] public bool invert;
        [HideInInspector] public bool OrAnd;
        [HideInInspector] public bool LocalTarget = false;
        protected Object CacheTarget;

        [SerializeField, HideInInspector] protected bool debug;

        public bool DebugCondition => debug;

        /// <summary>False when the Condition is switched Off in the Inspector, so it is skipped on evaluation.</summary>
        public bool Active { get => active; set => active = value; }

        //Call this on On Enable on any component
        public virtual void OnEnable() { }

        //Call this on On Disable on any component
        public virtual void OnDisable() { }

        /// <summary>Evaluate a condition using the Target</summary>
        protected abstract bool _Evaluate();

        /// <summary>Evaluate a condition using the Target</summary>
        public bool Evaluate(Object target)
        {
            SetTarget(target);
            return Evaluate();
        }

        // MWC — payload path. Routes the raw value into the condition, then goes through the
        // same Evaluate() as the Target path so [invert] and [debug] behave identically.
        /// <summary>Evaluate a condition using a basic value (int, float, string, bool...) </summary>
        public bool Evaluate(object value)
        {
            _SetValue(value);
            return Evaluate();
        }

        // MWC — override on conditions that read a value coming from a UnityEvent&lt;T&gt;
        /// <summary>Receive a raw value (int, float, string, bool...) sent by the caller.</summary>
        protected virtual void _SetValue(object value) { }


        /// <summary>Set target correct type on the on the Conditions</summary>
        protected abstract void _SetTarget(Object target);



        public virtual void VerifyComponent<T>(Object newTarget, ref T currentTarget) where T : Object
        {
            currentTarget = MTools.VerifyComponent<T>(newTarget, currentTarget);
        }

        // MWC — [Target] on most conditions is a SERIALIZED field used as a runtime cache. Anything that pushes the
        // serialized data back over the live instance (an Inspector apply whose SerializedObject snapshot still held
        // null, Clone(), an asset/domain reload) nulls it while [CacheTarget] keeps pointing at the same object, so
        // the guard in SetTarget short-circuits and the target is never resolved again: the condition stays false for
        // the rest of the session. Overriding this lets SetTarget notice the loss and re-resolve.
        /// <summary>True when the typed Target of this Condition is resolved. Override it on Conditions that own one.</summary>
        public virtual bool HasTarget => true;

        public virtual void SetTarget(Object target)
        {
            if (LocalTarget) return; //Do nothing if is Local Target

            //If the target is different from the last one
            if (CacheTarget != target || !HasTarget) //MWC — also re-resolve when the typed Target was lost
            {
                CacheTarget = target;
                _SetTarget(target);
                TargetHasChanged();
            }
        }


        // MWC — removed SetTarget(object)/_SetTarget(object): SetTarget(object) called itself
        // (overload resolution picked the same method) and blew the stack. Nothing used them;
        // the payload path is now _SetValue(object).

        /// <summary> Change internal variables here when a target has changed </summary>
        public virtual void TargetHasChanged() { }

        public bool Evaluate()
        {
            var result = invert ? !_Evaluate() : _Evaluate();

            Debugging(result);

            return result;
        }

        // protected override void _SetTarget(Object target) => Target = MTools.VerifyComponent(target, Target);

        /// <summary> Optional Method to Draw Gizmos on the Scene View </summary>
        public virtual void DrawGizmos(Component target) { }

        /// <summary> Optional Method to Draw GizmosOnSelected on the Scene View </summary>
        public virtual void DrawGizmosSelected(Component target) { }


        /// <summary>Console log with the Condition result, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("MALBERS_DEBUG")]
        public virtual void Debugging(bool result)
        {
            if (debug)
            {
                var color = result ? "cyan" : "orange";

                var inverted = invert ? "<B><color=red>[NOT]</color></B>" : string.Empty;

                // MWC — result already has [invert] applied; re-inverting here printed the raw
                // value while the color showed the final one.

                MDebug.Log($"<color=yellow><B>Condition: {inverted}[{DynamicName}] </B></color> Result: <color={color}> <B>{result}</B> </color> <b>Object: {(CacheTarget ? CacheTarget.name : "No Target")}</b> ", CacheTarget);
            }
        }

        /// <summary>Console log with the Condition result, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("MALBERS_DEBUG")]
        public virtual void Debugging(string data, bool result, Object target)
        {
            if (debug)
            {
                var color = result ? "cyan" : "orange";

                var inverted = invert ? "<B><color=red>[NOT]</color></B>" : string.Empty;

                // MWC — see Debugging(bool): do not re-invert an already inverted result

                if (string.IsNullOrEmpty(data)) data = DynamicName + " [.]"; // MWC — null-safe


                MDebug.Log($"<color=yellow><B>Condition: {inverted} [{data}] </B></color> Result: <color={color}> <B>{result}</B> </color> ", target);
            }
        }

        // MWC — clear the cached target on the copy, otherwise the clone skips _SetTarget()
        // for a target that only the original had already resolved.
        public object Clone()
        {
            var clone = (ConditionCore)this.MemberwiseClone();
            clone.CacheTarget = null;
            return clone;
        }
    }
}