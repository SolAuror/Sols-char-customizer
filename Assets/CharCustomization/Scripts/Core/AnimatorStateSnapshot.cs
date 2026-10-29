using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Carries an Animator's states, layer weights, and parameters across a Rebind,
    // which Unity performs when the Avatar or bound meshes change.
    internal readonly struct AnimatorStateSnapshot
    {
        private readonly LayerState[] layers;
        private readonly ParameterState[] parameters;

        private AnimatorStateSnapshot(LayerState[] layers, ParameterState[] parameters)
        {
            this.layers = layers;
            this.parameters = parameters;
        }

        public static AnimatorStateSnapshot Capture(Animator animator)
        {
            if (!CanSnapshot(animator))
            {
                return default;
            }

            var layers = new LayerState[animator.layerCount];
            for (int index = 0; index < layers.Length; index++)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(index);
                float normalizedTime = state.loop
                    ? Mathf.Repeat(state.normalizedTime, 1f)
                    : Mathf.Clamp01(state.normalizedTime);
                layers[index] = new LayerState(state.fullPathHash, normalizedTime, animator.GetLayerWeight(index));
            }

            AnimatorControllerParameter[] sourceParameters = animator.parameters;
            var parameters = new ParameterState[sourceParameters.Length];
            for (int index = 0; index < sourceParameters.Length; index++)
            {
                AnimatorControllerParameter parameter = sourceParameters[index];
                parameters[index] = parameter.type switch
                {
                    AnimatorControllerParameterType.Float => new ParameterState(
                        parameter.nameHash, parameter.type, animator.GetFloat(parameter.nameHash), 0, false),
                    AnimatorControllerParameterType.Int => new ParameterState(
                        parameter.nameHash, parameter.type, 0f, animator.GetInteger(parameter.nameHash), false),
                    AnimatorControllerParameterType.Bool => new ParameterState(
                        parameter.nameHash, parameter.type, 0f, 0, animator.GetBool(parameter.nameHash)),
                    _ => new ParameterState(parameter.nameHash, parameter.type, 0f, 0, false)
                };
            }

            return new AnimatorStateSnapshot(layers, parameters);
        }

        public void Restore(Animator animator)
        {
            if (layers == null || !CanSnapshot(animator))
            {
                return;
            }

            foreach (ParameterState parameter in parameters)
            {
                if (animator.IsParameterControlledByCurve(parameter.NameHash)) continue;
                switch (parameter.Type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(parameter.NameHash, parameter.FloatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(parameter.NameHash, parameter.IntValue);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(parameter.NameHash, parameter.BoolValue);
                        break;
                }
            }

            int layerCount = Mathf.Min(layers.Length, animator.layerCount);
            for (int index = 0; index < layerCount; index++)
            {
                animator.SetLayerWeight(index, layers[index].Weight);
                animator.Play(layers[index].StateHash, index, layers[index].NormalizedTime);
            }

            // Evaluate immediately so the rebind does not show a frame of bind pose.
            animator.Update(0f);
        }

        private static bool CanSnapshot(Animator animator)
        {
            return animator != null &&
                   animator.isActiveAndEnabled &&
                   animator.isInitialized &&
                   animator.runtimeAnimatorController != null;
        }

        private readonly struct LayerState
        {
            public LayerState(int stateHash, float normalizedTime, float weight)
            {
                StateHash = stateHash;
                NormalizedTime = normalizedTime;
                Weight = weight;
            }

            public int StateHash { get; }
            public float NormalizedTime { get; }
            public float Weight { get; }
        }

        private readonly struct ParameterState
        {
            public ParameterState(
                int nameHash,
                AnimatorControllerParameterType type,
                float floatValue,
                int intValue,
                bool boolValue)
            {
                NameHash = nameHash;
                Type = type;
                FloatValue = floatValue;
                IntValue = intValue;
                BoolValue = boolValue;
            }

            public int NameHash { get; }
            public AnimatorControllerParameterType Type { get; }
            public float FloatValue { get; }
            public int IntValue { get; }
            public bool BoolValue { get; }
        }
    }
}
