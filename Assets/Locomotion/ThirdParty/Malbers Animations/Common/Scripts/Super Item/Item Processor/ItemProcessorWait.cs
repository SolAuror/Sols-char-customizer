// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Items
{

    [System.Serializable, AddTypeMenu("Action/Wait")]
    [HelpURL(ItemDocs.Processors + "wait")]
    public class ItemProcessorWait : ItemProcessor
    {
        [Tooltip("Ability Index for Aiming")]
        public FloatReference Wait = new(1f);



        private float CurrentWait { get; set; }
        private bool WaitCompleted { get; set; }

        public override string DynamicName => $"Wait: {Wait.Value:F2}";


        public override void ActionStart(SuperItem item, ItemActionSet set, ItemAction action)
        {
            CurrentWait = 0;
            WaitCompleted = false;
        }


        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action, float delta)
        {
            if (!WaitCompleted)
            {
                CurrentWait += delta; // Increment the current wait time by the delta time
                if (CurrentWait >= Wait.Value)
                {
                    WaitCompleted = true; // Mark the wait as completed
                    ProcessorCompleted(item, set, action); // Mark the processor as completed when the wait is completed
                }
            }
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            return string.Empty;
        }
    }
}