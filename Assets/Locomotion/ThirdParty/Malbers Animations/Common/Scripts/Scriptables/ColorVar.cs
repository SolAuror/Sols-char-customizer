// Last Change: 9/17/2026 6:05pm
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    ///<summary>  Float Scriptable Variable. Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Variables/Color", order = 2000)]
    public class ColorVar : ScriptableVar
    {
        /// <summary>The current value </summary>
        [SerializeField] private Color value = Color.white;

        /// <summary>Value of the Float Scriptable variable</summary>
        public virtual Color Value
        {
            get => value;
            set
            {
                this.value = value;
#if UNITY_EDITOR
                if (debug) Debug.Log($"<B>{name} -> [<color=black> {value} </color>] </B>", this);
#endif
            }
        }

        public virtual void SetValue(ColorVar var) => Value = var.Value;

        public static implicit operator Color(ColorVar reference) => reference.Value;

        /// <summary>Color values can be stored as text.</summary>
        public override bool CanSerialize => true;

        /// <summary>Writes the stored value as JSON, which keeps HDR channels intact and is culture invariant.</summary>
        public override string GetSerializedValue() => JsonUtility.ToJson(value);

        /// <summary>Restores the value from JSON; assigns through [Value] so listeners are notified.</summary>
        public override bool SetSerializedValue(string data)
        {
            if (!TryFromJson<Color>(data, out var result)) return false;
            Value = result;
            return true;
        }
    }

    [System.Serializable]
    public class ColorReference : ReferenceVar
    {  
        public Color ConstantValue = Color.white;
        public ColorVar Variable;

        public ColorReference()
        {
            UseConstant = true;
            ConstantValue = Color.white;
        }

        public ColorReference(bool variable = false)
        {
            UseConstant = !variable;

            if (!variable)
            {
                ConstantValue = Color.white;
            }
            else
            {
                Variable = ScriptableObject.CreateInstance<ColorVar>();
                Variable.Value = Color.white;
            }
        }

        public ColorReference(Color value) => Value = value;

        public Color Value
        {
            get => UseMember ? Member.GetColor(ConstantValue) : //MWC — Use Reference mode reads the member; ConstantValue is the fallback
                UseConstant ? ConstantValue : Variable.Value;
            set
            {
                if (BlockedByMember()) return; //MWC — read-only while using a member reference

                if (UseConstant)
                    ConstantValue = value;
                else
                    Variable.Value = value;
            }
        }

        #region Operators
        public static implicit operator Color(ColorReference reference) => reference.Value;
        #endregion
    }

#if UNITY_EDITOR
    [UnityEditor.CanEditMultipleObjects, UnityEditor.CustomEditor(typeof(ColorVar))]
    public class ColorVarEditor : VariableEditor
    {
        public override void OnInspectorGUI() { PaintInspectorGUI("Color Variable"); }
    }
#endif


}