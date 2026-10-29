// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Utilities;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]

    [AddTypeMenu("Unity/Animator SetParameter")]
    [HelpURL(ReactionDocs.Unity + "animator-setparameter")]
    public class AnimatorReaction : Reaction
    {

        //A single parameter is spelled out; several show the count
        public override string DynamicName => parameters != null && parameters.Count == 1
            ? $"Animator Set [{Describe(parameters[0])}]"
            : $"Animator Set Parameters [{parameters?.Count ?? 0}]"; //Name of the Reaction

        /// <summary>"Name = Value" for one parameter, typed like Set() applies it; Triggers carry no value</summary>
        private static string Describe(MAnimatorParameter p)
        {
            float value = p.Value?.Value ?? 0f; //FloatReference is a class: null-safe

            return p.type switch
            {
                AnimatorControllerParameterType.Trigger => $"{p.param} (Trigger)",
                AnimatorControllerParameterType.Bool => $"{p.param} = {value != 0}",
                AnimatorControllerParameterType.Int => $"{p.param} = {(int)value}",
                _ => $"{p.param} = {value}",
            };
        }


        public override System.Type ReactionType => typeof(Animator);

        public List<MAnimatorParameter> parameters = new();

        public void Set(Animator anim)
        {
            foreach (var param in parameters)
                param.Set(anim);
        }

        protected override bool _TryReact(Component component)
        {
            Set(component as Animator);
            return true;
        }
    }
}
