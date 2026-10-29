// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("Malbers/Animal/Gravity")]
    [HelpURL(ReactionDocs.Animal + "gravity")]
    public class GravityReaction : MReaction
    {
        public override string DynamicName => type switch
        {
            Gravity_Reaction.Enable => $"Animal Gravity: {(Value ? "Enable" : "Disable")}",
            Gravity_Reaction.Reset => "Animal Gravity: Reset Direction",
            Gravity_Reaction.GroundChangesGravity => $"Animal Gravity: Ground Changes Gravity [{(Value ? "On" : "Off")}]",
            Gravity_Reaction.AlignToGravityDirection => "Animal Gravity: Align to Gravity Direction",
            _ => $"Animal Gravity [{type}]",
        };

        public Gravity_Reaction type = Gravity_Reaction.Enable;
        [Hide("type", (int)Gravity_Reaction.Enable, (int)Gravity_Reaction.GroundChangesGravity)]
        public bool Value;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;
            switch (type)
            {
                case Gravity_Reaction.Enable:
                    animal.UseGravity = Value;
                    break;
                case Gravity_Reaction.Reset:
                    animal.Gravity_ResetDirection();
                    break;
                case Gravity_Reaction.GroundChangesGravity:
                    animal.Gravity_DirectionFromGround(Value);
                    break;
                case Gravity_Reaction.AlignToGravityDirection:
                    animal.AlignToGravity();
                    break;
                default:
                    break;
            }
            return true;
        }

        public enum Gravity_Reaction
        {
            Enable,
            Reset,
            GroundChangesGravity,
            AlignToGravityDirection,
        }
    }
}
