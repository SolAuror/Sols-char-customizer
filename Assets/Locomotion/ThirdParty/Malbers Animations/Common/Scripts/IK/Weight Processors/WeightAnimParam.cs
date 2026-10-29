using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>  Process the weight by checking the Look At Angle of the Animator / </summary>
    [System.Serializable, AddTypeMenu("Animator/Parameter (Float)")]
    public class WeightAnimParam : WeightProcessor
    {
        public override string DynamicName => $"Anim Float Parameter [{Parameter}]";

        [Tooltip("Name of the Animator Parameter to check")]
        [AnimatorParam(AnimatorControllerParameterType.Float)]
        public string Parameter;
        [Tooltip("Normalize the weight by this value")]
        public float normalizedBy = 1;

        [HideInInspector] public int AnimParamHash;

        public override void OnEnable(IKSet set, Animator Anim)
        {
            AnimParamHash = Animator.StringToHash(Parameter);

            HashSet<int> animatorHashParams = new(Anim.parameters.Select(p => p.nameHash)); // Cache all Animator Parameters Hashes

            if (!animatorHashParams.Contains(AnimParamHash))
            {
                Debug.LogWarning($"<b><color=orange> '{Anim.name}' Animator does not have '{Parameter}' parameter. Disabling Weight Processor </color> </b>", Anim);
                Active = false;
            }
        }

        public override float Process(IKSet set, Animator Anim, float weight)
        {
            // if (!Anim) return weight;

            if (AnimParamHash == 0)
                AnimParamHash = Animator.StringToHash(Parameter);

            var animWeight = 1f;

            if (AnimParamHash != 0)
            {
                animWeight = Anim.GetFloat(AnimParamHash) / normalizedBy;
            }
            return Mathf.Min(weight, animWeight);
        }
    }
}
