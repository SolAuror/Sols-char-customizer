// Last Change: 9/17/2026 6:05pm
using System;
using System.Globalization; //MWC — invariant parsing/formatting for the text serialization API
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    ///<summary>  Float Scriptable Variable. Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple  </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Variables/Float", order = 1000)]
    public class FloatVar : ScriptableVar
    {
        /// <summary>The current value</summary>
        [SerializeField] protected float value = 0;

        /// <summary> Invoked when the value changes</summary>
        public Action<float> OnValueChanged;

        /// <summary>Value of the Float Scriptable variable </summary>
        public virtual float Value
        {
            get => value;
            set
            {
                //  if (this.value != value)                                //If the value is different change it
                {
                    this.value = value;
                    OnValueChanged?.Invoke(value);         //If we are using OnChange event Invoked
#if UNITY_EDITOR
                    if (debug) Debug.Log($"<B>{name} -> [<color=red> {value:F3} </color>] </B>", this);
#endif
                }
            }
        }

        /// <summary>Set the Value using another FloatVar</summary>
        public virtual void SetValue(FloatVar var) => Value = var.Value;
        public virtual void SetValue(float var) => Value = var;

        /// <summary>Add or Remove the passed var value</summary>
        public virtual void Add(FloatVar var) => Value += var.Value;

        /// <summary>Add or Remove the passed var value</summary>
        public virtual void Add(float var) => Value += var;

        public virtual void Randomize01() => Value = UnityEngine.Random.Range(0f, 1f);

        public virtual void Randomize(FloatRangeVar range) => Value = range.Value;

        public static implicit operator float(FloatVar reference) => reference.Value;

        /// <summary>Float values can be stored as text.</summary>
        public override bool CanSerialize => true;

        /// <summary>Writes the stored value round-trip ("R") in the invariant culture, so a comma-decimal locale cannot corrupt it.</summary>
        public override string GetSerializedValue() => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>Restores the value from an invariant-culture float; assigns through [Value] so listeners are notified.</summary>
        public override bool SetSerializedValue(string data)
        {
            if (!float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) return false;
            Value = result;
            return true;
        }
    }

    [System.Serializable]
    public class FloatReference : ReferenceVar
    {
        public float ConstantValue;
        [RequiredField] public FloatVar Variable;

        public FloatReference() => Value = 0;

        public FloatReference(float value) => Value = value;

        public FloatReference(FloatVar value)
        {
            Variable = value;
            UseConstant = false;
        }

        public float Value
        {
            get => UseMember ? Member.GetFloat(ConstantValue) : //MWC — Use Reference mode reads the member; ConstantValue is the fallback
                UseConstant || Variable == null ? ConstantValue : Variable.Value;
            set
            {
                if (BlockedByMember()) return; //MWC — read-only while using a member reference

                Debug.Assert(UseConstant || !UseConstant && Variable != null, "Expected FloatVar object is not set when UseConstant is false!");

                if (UseConstant || Variable == null)
                    ConstantValue = value;
                else
                    Variable.Value = value;
            }
        }

        public static implicit operator float(FloatReference reference) => reference.Value;

        public static implicit operator FloatReference(float reference) => new(reference);

        public static implicit operator FloatReference(FloatVar reference) => new(reference);
    }

#if UNITY_EDITOR
    [UnityEditor.CanEditMultipleObjects, UnityEditor.CustomEditor(typeof(FloatVar))]
    public class FloatVarEditor : VariableEditor
    {
        public override void OnInspectorGUI() => PaintInspectorGUI("Float Variable");
    }
#endif
}