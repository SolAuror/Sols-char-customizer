// Last Change: 9/24/2026 12:57pm
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    // Old version. Do not show it in the AddTypeMenu anymore. Use Reactions2 instead.
    [AddTypeMenu("z_Multiple Reactions [Obsolete]")]
    [System.Serializable]
    public class ListReaction : Reaction
    {
        public override Type ReactionType => typeof(Component);

        [SerializeReference] public List<Reaction> reactions = new();

        public ListReaction() => reactions = new List<Reaction>();

        public ListReaction(List<Reaction> reactions) => this.reactions = reactions;

        public ListReaction(Reaction[] reactions) => this.reactions = reactions.ToList();


        /// <summary>Runs every reaction in the list; true only when all of them succeeded</summary>
        protected override bool _TryReact(Component component)
        {
            if (reactions != null)
            {
                var TryResult = true;

                foreach (var r in reactions)
                {
                    var verify = r.VerifyComponent(component); //Get the real component in the list

                    if (verify != null)
                    {
                        TryResult = r.TryReact(verify) && TryResult; //React first: a failed row must not skip the rows below
                    }
                }

                return TryResult;
            }
            return false;
        }
    }
}
