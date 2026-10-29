// Last Change: 9/18/2026 10:30pm
using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [Serializable, AddTypeMenu("Unity/Behaviour")]
    [HelpURL(ReactionDocs.Unity + "behaviour")]
    public class BehaviourReaction : Reaction
    {
        public override string DynamicName => action switch
        {
            Behaviour_Reaction.SetEnable => $"Behaviour {(value ? "Enable" : "Disable")} {(useLocal && localTarget != null ? $"[{localTarget.name}]" : "")}",
            Behaviour_Reaction.Destroy => $"Behaviour Destroy{(time > 0 ? $" [{time}s]" : string.Empty)}",
            _ => $"Behaviour [{action}]",
        }; //Name of the Reaction

        public enum Behaviour_Reaction { SetEnable, Destroy }

        public override Type ReactionType => typeof(Behaviour);

        public Behaviour_Reaction action = Behaviour_Reaction.SetEnable;
        [Hide("action", (int)Behaviour_Reaction.SetEnable)]
        public bool value = true;
        [Hide("action", (int)Behaviour_Reaction.Destroy)]
        public float time = 0;

        protected override bool _TryReact(Component component)
        {
            var beha = component as Behaviour;

            switch (action)
            {
                case Behaviour_Reaction.SetEnable:
                    beha.enabled = value;
                    return true;
                case Behaviour_Reaction.Destroy:
                    Component.Destroy(component, time);
                    return true;
                default:
                    break;
            }
            return false;
        }
    }
}
