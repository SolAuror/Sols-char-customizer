// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Controller;
using System;
using UnityEngine;


namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Damage/Play Combo")]
    [HelpURL(ReactionDocs.Malbers + "play-combo")]
    public class ComboReaction : Reaction
    {
        override public string DynamicName => $"Play Combo [Branch: {Branch}]";

        [Tooltip("Branch to Play on the Combo")]
        public int Branch;

        public override Type ReactionType => typeof(ComboManager);

        protected override bool _TryReact(Component component)
        {
            (component as ComboManager).Play(Branch);

            return true;
        }
    }
}
