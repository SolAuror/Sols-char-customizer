// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/Mode Power")]
    [HelpURL(ReactionDocs.Animal + "mode-power")]
    public class ModeSetPowerReaction : MReaction
    {
        public override string DynamicName => $"Mode Power [{ModePower?.Value ?? 0}]";

        [Tooltip("Set the Power of the Current Mode")]
        public FloatReference ModePower = new(0);

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;
            animal.Mode_SetPower(ModePower);
            return true;
        }

    }
}
