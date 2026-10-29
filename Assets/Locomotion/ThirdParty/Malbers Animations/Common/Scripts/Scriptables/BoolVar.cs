// Last Change: 9/17/2026 6:05pm
using System;
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    ///<summary>  Bool Scriptable Variable. Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Variables/Bool", order = 1000)]
    public class BoolVar : ScriptableVar
    {
        [SerializeField] private bool value;

        /// <summary>Invoked when the value changes </summary>
        public Action<bool> OnValueChanged;

        /// <summary> Value of the Bool variable</summary>
        public virtual bool Value
        {
            get => value;
            set
            {
                //if (this.value != value)                  //If the value is diferent change it
                {
                    this.value = value;
                    OnValueChanged?.Invoke(value);         //If we are using OnChange event Invoked
#if UNITY_EDITOR
                    if (debug) Debug.Log($"<B>{name} -> [<color=blue> {value} </color>] </B>", this);
#endif
                }
            }
        }

        public virtual void SetValue(BoolVar var) => SetValue(var.Value);

        public virtual void SetValue(bool var) => Value = var;
        public virtual void SetValueInverted(bool var) => Value = !var;
        public virtual void Toggle() => Value ^= true;
        public virtual void UpdateValue() => OnValueChanged?.Invoke(value);

        public static implicit operator bool(BoolVar reference) => reference.Value;

        /// <summary>Bool values can be stored as text.</summary>
        public override bool CanSerialize => true;

        /// <summary>Writes the stored value as "1" or "0".</summary>
        public override string GetSerializedValue() => value ? "1" : "0";

        /// <summary>Restores the value from "1"/"0", or from the legacy "True"/"False" text written by older versions.</summary>
        public override bool SetSerializedValue(string data)
        {
            if (string.IsNullOrEmpty(data)) return false;

            if (data == "1") { Value = true; return true; }
            if (data == "0") { Value = false; return true; }

            //MWC — legacy format: bools used to be saved with bool.ToString(). TryParse is culture independent.
            if (!bool.TryParse(data, out var result)) return false;

            Value = result;
            return true;
        }
    }

    [System.Serializable]
    public class BoolReference : ReferenceVar
    {
        public bool ConstantValue;
        [RequiredField] public BoolVar Variable;

        public BoolReference() => Value = false;

        public BoolReference(bool value) => Value = value;

        public BoolReference(BoolVar value) => Value = value.Value;

        public bool Value
        {
            get => UseMember ? Member.GetBool(ConstantValue) : //MWC — Use Reference mode reads the member; ConstantValue is the fallback
                UseConstant || Variable == null ? ConstantValue : Variable.Value;
            set
            {
                if (BlockedByMember()) return; //MWC — read-only while using a member reference

                // Debug.Log(value);
                if (UseConstant || Variable == null)
                    ConstantValue = value;
                else
                    Variable.Value = value;
            }
        }

        #region Operators
        public static implicit operator bool(BoolReference reference) => reference.Value;
        //: fixed bug here: caused StackOverflow (circular implicit operator usage because of wrong return value) =>
        //HIGHLY recommended to NOT "mask" reference types u     sage with value types usage by using implicit conversion
        // from value types to reference types. This is an anti-coding pattern that can easily cause performance impact
        // (redundant memory allocations with each implicit conversion),  unexpected behaviour (think about what happens if you accidently assign a value type to a reference
        // , but you stored the previous reference to propagate a value change in multiple places and the value type you just assigned creates a whole
        // new reference and overwrites the one you registered to?) =>
        // also code readability is heavily affected because of this implicit conversion from a value type to a reference type
        public static implicit operator BoolReference(bool value) => new(value);
        #endregion
    }

#if UNITY_EDITOR
    [UnityEditor.CanEditMultipleObjects, UnityEditor.CustomEditor(typeof(BoolVar))]
    public class BoolVarEditor : VariableEditor
    {
        public override void OnInspectorGUI() => PaintInspectorGUI("Bool Variable");
    }
#endif
}
