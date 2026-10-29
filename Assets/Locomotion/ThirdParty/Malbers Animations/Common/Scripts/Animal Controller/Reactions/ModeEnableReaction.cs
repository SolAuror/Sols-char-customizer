// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/Mode Enable-Disable")]
    [HelpURL(ReactionDocs.Animal + "mode-enable-disable")]
    public class ModeEnableReaction : MReaction
    {
        //A single Mode is named; several show the count. Temporal is only mentioned when it is on
        override public string DynamicName =>
            $"Mode {(modes == null || modes.include ? "Enable" : "Disable")} {IDNames}{(TemporalEnable ? " (Temporal)" : "")}";

        /// <summary>[Name] of the only Mode on the list, or [Count] when there are several</summary>
        private string IDNames => modes != null && modes.Length == 1 && modes[0] != null ? $"[{modes[0].name}]" : $"[{modes?.Length ?? 0}]";

        [Tooltip("Enable or Disable the Mode temporally, this does not deactivate completely the Mode. Disables modes will remain disabled. and it wont be affected by this")]
        public bool TemporalEnable = false;
        public IDListCheck<ModeID> modes;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            foreach (var id in modes.IDs)
            {
                var mode = animal.Mode_Get(id.ID);

                if (mode != null)
                {
                    if (TemporalEnable)
                    {
                        if (modes.include)
                            mode.Enable_Temporal();
                        else
                            mode.Disable_Temporal();
                    }
                    else
                        mode.Active = modes.include;

                }
            }
            return true;
        }
    }
}
