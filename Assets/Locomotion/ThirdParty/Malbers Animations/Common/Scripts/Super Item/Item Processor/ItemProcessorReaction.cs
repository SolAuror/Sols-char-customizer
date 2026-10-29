// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Reactions;
using UnityEngine;

namespace MalbersAnimations.Items
{
    [System.Serializable, AddTypeMenu("General/Reactions")]
    [HelpURL(ItemDocs.Processors + "reactions-list")]
    public class ItemProcessorReaction : ItemProcessor
    {
        [Tooltip("If true, the Reactions will use the Target assigned in the Item for the dynamic reaction target, otherwise it will use the Item's Owner")]
        public bool UseTarget = false;

        public Reaction2 reaction;

        //MWC — show what the processor actually does instead of a fixed label: a single reaction reports its own
        //Dynamic Name, several report "Mixed". Empty and null-entry cases are named too, so a broken list is visible.
        public override string DynamicName
        {
            get
            {
                if (!reaction.IsValid) return "Reactions [Empty]";

                if (reaction.Count == 1)
                    return "Reaction: " + (reaction.reactions[0] != null ? reaction.reactions[0].DynamicName : "Reactions [Missing]");

                return $"Multiple Reactions [{reaction.Count}]";
            }
        }

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            var Target = UseTarget ? item.Target : item.UserGo;

            reaction.React(Target);
        }


        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (!reaction.IsValid)
                return $"The reaction is not valid or is empty";
            return string.Empty;
        }
    }
}