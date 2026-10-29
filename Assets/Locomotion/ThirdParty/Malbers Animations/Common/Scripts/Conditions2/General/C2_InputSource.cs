// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("General/Input Source")]
    public class C2_InputSource : ConditionCore
    {
        public override string DynamicName => $"Input Is Pressed: [{inputName.Value}]";

        private IInputSource inputSource;
        private IInputAction input;

        public StringReference inputName = new("input");

        protected override void _SetTarget(Object target)
        {
            inputSource ??= MTools.VerifyInterface(target, inputSource);
            if (inputSource != null)
                input = inputSource.GetInput(inputName.Value);
        }
        /// <summary>True while the Input Source of the Target is resolved.</summary>
        public override bool HasTarget => inputSource != null;

        public override void TargetHasChanged()
        {
            inputSource = MTools.VerifyInterface((Component)CacheTarget, inputSource);
        }

        protected override bool _Evaluate()
        {
            if (input == null)
            {
                // Debugging("No Input Source Found", false, null);
                return false;
            }
            else
            {
                // Debugging($"Input Found: {input != null}", input.GetValue, inputSource.transform);
                return input.GetValue;
            }
        }
    }
}
