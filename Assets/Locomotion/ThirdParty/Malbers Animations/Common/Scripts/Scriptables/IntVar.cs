// Last Change: 9/17/2026 6:05pm
using System;
using System.Globalization; //MWC — invariant parsing/formatting for the text serialization API
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    ///<summary>  Int Scriptable Variable. Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple  </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Variables/Integer", order = 1000)]
    public class IntVar : ScriptableVar
    {
        /// <summary> The current value</summary>
        [SerializeField] private int value = 0;

        /// <summary>Invoked when the value changes </summary>
        public Action<int> OnValueChanged;

        /// <summary>Value of the Int Scriptable variable</summary>
        public virtual int Value
        {
            get => value;
            set
            {
                // if (this.value != value)         //If the value is different change it
                {
                    this.value = value;
                    OnValueChanged?.Invoke(value);         //If we are using OnChange event Invoked
#if UNITY_EDITOR
                    if (debug) Debug.Log($"<B>{name} -> [<color=yellow> {value} </color>] </B>", this);
#endif
                }
            }
        }


        /// <summary>Set the Value using another IntVar</summary>
        public virtual void SetValue(IntVar var) => Value = var.Value;
        public virtual void SetValue(int var) => Value = var;

        /// <summary>Add or Remove the passed var value</summary>
        public virtual void Add(IntVar var) => Value += var.Value;

        /// <summary>Add or Remove the passed var value</summary>
        public virtual void Add(int var) => Value += var;
        public virtual void Multiply(int var) => Value *= var;
        public virtual void Multiply(IntVar var) => Value *= var;
        public virtual void Divide(IntVar var) => Value /= var;

        public static implicit operator int(IntVar reference) => reference.Value;

        public virtual void Randomize01() => Value = UnityEngine.Random.Range(0, 1);
        public virtual void Randomize(IntRangeVar range) => Value = range.Value;

        /// <summary>Int values can be stored as text.</summary>
        public override bool CanSerialize => true;

        /// <summary>Writes the stored value using the invariant culture.</summary>
        public override string GetSerializedValue() => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Restores the value from an invariant-culture integer; assigns through [Value] so listeners are notified.</summary>
        public override bool SetSerializedValue(string data)
        {
            if (!int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return false;
            Value = result;
            return true;
        }
    }


    [System.Serializable]
    public class IntReference : ReferenceVar
    {
        public int ConstantValue;
#pragma warning disable CA2235 // Mark all non-serializable fields
        [RequiredField] public IntVar Variable;
#pragma warning restore CA2235 // Mark all non-serializable fields

        public IntReference() => Value = 0;

        public IntReference(int value) => Value = value;

        public IntReference(IntVar value) => Value = value.Value;

        public int Value
        {
            get => UseMember ? Member.GetInt(ConstantValue) : //MWC — Use Reference mode reads the member; ConstantValue is the fallback
                UseConstant || Variable == null ? ConstantValue : Variable.Value;
            set
            {
                if (BlockedByMember()) return; //MWC — read-only while using a member reference

                if (UseConstant || Variable == null)
                    ConstantValue = value;
                else
                    Variable.Value = value;
            }
        }
        #region Operators
        public static implicit operator int(IntReference reference) => reference.Value;

        public static implicit operator IntReference(int reference) => new(reference);

        public static implicit operator IntReference(IntVar reference) => new(reference);
        #endregion
    }


#if UNITY_EDITOR
    [UnityEditor.CanEditMultipleObjects, UnityEditor.CustomEditor(typeof(IntVar))]
    public class IntVarEditor : VariableEditor
    {
        public override void OnInspectorGUI() => PaintInspectorGUI("Int Variable");
    }
#endif

}