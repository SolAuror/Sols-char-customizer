// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/Stance Enable-Disable")]
    [HelpURL(ReactionDocs.Animal + "stance-enable-disable")]
    public class StanceEnableReaction : MReaction
    {
        //A single Stance is named; several show the count
        override public string DynamicName =>
            $"Stance {(stances == null || stances.include ? "Enable" : "Disable")} " +
            (stances != null && stances.Length == 1 && stances[0] != null ? $"[{stances[0].name}]" : $"[{stances?.Length ?? 0}]");

        public IDListCheck<StanceID> stances;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            foreach (var id in stances.IDs)
            {
                if (animal.Stance_TryGet(id, out var st))
                {
                    st.Enable(stances.include);
                }
            }
            return true;
        }
    }
}
