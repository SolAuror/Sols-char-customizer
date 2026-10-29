using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MalbersAnimations.IK
{
    //MWC — New Weight Processor: drives the Set weight with an Animation Curve evaluated on an Animator Float
    //Parameter. [Animator/Parameter (Float)] can only divide the raw value by a constant, so any non linear response
    //(ease in, dead zone, fade out at the extremes of an aim angle) had to be baked into the Animator itself.
    /// <summary> Processes the weight by evaluating an Animator Float Parameter through an Animation Curve </summary>
    [System.Serializable, AddTypeMenu("Animator/Parameter Curve (Float)")]
    public class WeightAnimParamCurve : WeightProcessor
    {
        /// <summary> Name shown on the Processor header in the inspector </summary>
        public override string DynamicName => $"Anim Curve [{Parameter}]";

        [Tooltip("Name of the Animator Float Parameter to evaluate")]
        [AnimatorParam(AnimatorControllerParameterType.Float)]
        public string Parameter;

        [Tooltip("Remap the Parameter value from [Range] to [0-1] before evaluating the Curve.\n" +
            "Use it when the Parameter is not authored in the 0-1 space (an angle, a speed, a distance…)")]
        public bool Remap = false;

        [Tooltip("Parameter value that maps to the start (X:0) and the end (X:1) of the Curve")]
        [Hide(nameof(Remap))]
        public Vector2 Range = new(0, 1);

        [Tooltip("Weight (Y) returned for the Parameter value (X). The result is clamped to 0-1")]
        public AnimationCurve Curve = new(new Keyframe(0, 0), new Keyframe(1, 1));

        [HideInInspector] public int AnimParamHash;

        /// <summary> Caches the Parameter hash and disables the Processor when the Animator has no such Parameter </summary>
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

        /// <summary> Returns the Curve value for the current Parameter value, as a 0-1 multiplier </summary>
        public override float Process(IKSet set, Animator Anim, float weight)
        {
            if (AnimParamHash == 0) AnimParamHash = Animator.StringToHash(Parameter);

            if (AnimParamHash == 0 || Curve == null || Curve.length == 0) return 1; //Nothing to evaluate: do not touch the weight

            var value = Anim.GetFloat(AnimParamHash);

            //MWC — Remap keeps the Curve authored in the comfortable 0-1 space while the Parameter lives in its own
            //range. InverseLerp already clamps, so values outside [Range] hold the first/last key of the Curve.
            if (Remap) value = Mathf.InverseLerp(Range.x, Range.y, value);

            //MWC — the entry weight is deliberately NOT multiplied in here: IKSet.GetWeightProcessor already does
            //[preWeight *= result], so returning [weight * curve] would apply the Set weight twice. Returning the pure
            //0-1 factor is also what makes the [Invert] toggle (1 - result) meaningful.
            return Mathf.Clamp01(Curve.Evaluate(value));
        }
    }
}
