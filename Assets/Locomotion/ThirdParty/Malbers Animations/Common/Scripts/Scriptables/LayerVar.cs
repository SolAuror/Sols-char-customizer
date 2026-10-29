// Last Change: 9/17/2026 6:05pm
using System.Globalization; //MWC — invariant parsing/formatting for the text serialization API
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    ///<summary> Layer Scriptable Variable. Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple</summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Variables/Layer Mask", order = 2000)]
    public class LayerVar : ScriptableVar
    {
        /// <summary>The current value</summary>
        [SerializeField] private LayerMask value = 0;

        /// <summary>Value of the Layer Scriptable variable </summary>
        public virtual LayerMask Value
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

        public static implicit operator int(LayerVar reference) => reference.Value;

        /// <summary>Layer masks can be stored as text.</summary>
        public override bool CanSerialize => true;

        /// <summary>Writes the mask as an invariant-culture integer.</summary>
        public override string GetSerializedValue() => value.value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Restores the mask from an invariant-culture integer; assigns through [Value] so the debug log runs.</summary>
        public override bool SetSerializedValue(string data)
        {
            if (!int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return false;
            Value = result;
            return true;
        }
    }



    [System.Serializable]
    public class LayerReference : ReferenceVar
    { 
        public LayerMask ConstantValue = ~0;
        [RequiredField] public LayerVar Variable;

        public LayerReference() => Value = ~0;

        public LayerReference(LayerMask value)
        {
            UseConstant = true;
            Value = value;
        }

        public LayerReference(LayerVar value)
        {
            UseConstant = false;
            Value = value.Value;
        }

        public LayerMask Value
        {
            get => UseConstant || Variable == null ? ConstantValue : Variable.Value;
            set
            {
                if (UseConstant || Variable == null)
                    ConstantValue = value;
                else
                    Variable.Value = value;
            }
        }

        public static implicit operator int(LayerReference reference) => reference.Value;
        public static implicit operator LayerMask(LayerReference reference) => reference.Value;
    }

#if UNITY_EDITOR
    [UnityEditor.CanEditMultipleObjects, UnityEditor.CustomEditor(typeof(LayerVar))]
    public class LayerVarEditor : VariableEditor
    {
        public override void OnInspectorGUI() { PaintInspectorGUI("LayerMask Variable"); }
    }
#endif
}