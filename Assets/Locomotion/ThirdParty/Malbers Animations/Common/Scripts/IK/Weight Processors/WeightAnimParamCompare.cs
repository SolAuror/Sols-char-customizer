using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking the Look At Angle of the Animator / </summary>
    [System.Serializable, AddTypeMenu("Animator/Parameter Compare")]
    public class WeightAnimParamCompare : WeightProcessor
    {
        public override string DynamicName => $"Parameter Compare [{parameter.Value}] {parameterType} {compare}";

        [Tooltip("Parameter to check in the animator ")]
        public StringReference parameter = new("Parameter Name");

        [Tooltip("Conditions types")]
        public AnimatorType parameterType;

        [Hide(nameof(parameterType), true, 2)]
        public ComparerNumber compare = ComparerNumber.Equal;

        [Hide(nameof(parameterType), false, 2)]
        public BoolReference m_isTrue;
        [Hide(nameof(parameterType), false, 0)]
        public FloatReference m_Value;
        [Hide(nameof(parameterType), false, 1)]
        public IntReference value;

        [HideInInspector] public int AnimParamHash;


        public override float Process(IKSet set, Animator Anim, float weight)
        {
            if (AnimParamHash == 0)
                AnimParamHash = Animator.StringToHash(parameter.Value);

            bool Result = false;
            switch (parameterType)
            {
                case AnimatorType.Float:
                    var Float = Anim.GetFloat(AnimParamHash);
                    Result = Float.MCompare(m_Value, compare);
                    break;
                case AnimatorType.Int:
                    var Int = Anim.GetInteger(AnimParamHash);
                    Result = Int.MCompare(value, compare); break;
                case AnimatorType.Bool:
                    var Bool = Anim.GetBool(AnimParamHash);
                    Result = Bool == m_isTrue.Value; break;
                default: break;
            }
            return Result ? weight : 0;
        }
    }
}
