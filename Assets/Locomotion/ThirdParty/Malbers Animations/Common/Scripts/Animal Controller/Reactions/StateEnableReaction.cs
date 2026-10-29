// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("Malbers/Animal/State Enable-Disable")]
    [HelpURL(ReactionDocs.Animal + "state-enable-disable")]
    public class StateEnableReaction : MReaction
    {
        //A single State is named; several show the count
        override public string DynamicName =>
            $"State {(states == null || states.include ? "Enable" : "Disable")} " +
            (states != null && states.Length == 1 && states[0] != null ? $"[{states[0].name}]" : $"[{states?.Length ?? 0}]");

        public IDListCheck<StateID> states;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            foreach (var id in states.IDs)
            {
                if (animal.State_TryGet(id.ID, out var st))
                {
                    st.Enable(states.include);
                }
            }
            return true;
        }
    }
}
