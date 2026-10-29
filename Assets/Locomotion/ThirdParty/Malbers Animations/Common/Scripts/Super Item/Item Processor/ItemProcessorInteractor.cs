// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Utilities;
using UnityEngine;

namespace MalbersAnimations.Items
{
    [System.Serializable, AddTypeMenu("Interaction/Owner Interactor")]
    [HelpURL(ItemDocs.Processors + "interaction-owner-interactor")]
    public class ItemProcessorInteractor : ItemProcessor
    {
        [Tooltip("If true, the Action will Stop when the Interactor interacts with an Interactable")]
        public bool StopOnInteract = true;
        public override string DynamicName => $"Interact with Owners Interactor";

        public override void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (action.Trigger == null)
            {
                Debug.LogError($"[Item: {item.name}] The Interactor Trigger is not set. Please assign a Trigger Proxy to the Action. [{action.name.Value}]", item);
                Active = false; //Disable the Processor if the Trigger is not set
                return;
            }

            if (item.User.Interactor == null)
            {
                Debug.LogError($"[Item: {item.name}] The Interactor Processor requires the User to have an Interactor component that implements the IInteractor interface. Please add an Interactor component to the User.", item);
                Active = false; //Disable the Processor if the Owner does not have an Interactor
            }
        }

        /// <summary> Makes the Owner's Interactor interact, then ends the Action when [Stop On Interact] is set </summary>
        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (item.User.Interactor != null)
            {
                item.User.Interactor.Interact();

                //MWC: was a raw [action.IsPlaying = false], which skipped the whole teardown: item.MainAction kept
                //pointing at this Action, the [On Action Exit] Reactions never fired, the Processors never got their
                //_ActionEnd, and the hands it claimed were never refreshed. Exit is the real end path (same as the
                //Mode Processor uses), and it also respects a [Can Exit] hold instead of ignoring it
                if (StopOnInteract) action.Exit(item, set);
            }
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (action.Trigger == null)
            {
                return $"Requires a Trigger Proxy to be assigned to the Action. Please assign one to the Action. [{action.name.Value}]";
            }
            return string.Empty;

        }
    }

    /// <summary>   
    /// Interaction Processor for Trigger Interactions. 
    /// This Processor will only interact with the Interactable when the Trigger is activated,
    /// it will not interact when the Action is executed. This is useful for Triggers that are set to "Trigger Only" and not "Execute on Trigger".
    /// </summary> 
    [System.Serializable, AddTypeMenu("Interaction/Trigger Interact")]
    [HelpURL(ItemDocs.Processors + "interaction-trigger-interact")]
    public class ItemProcessorInteractTrigger : ItemProcessor
    {
        public override string DynamicName => $"Interaction with Collider [Trigger only]";

        public override ExecuteProcessor InitialExecute => ExecuteProcessor.OnTriggerEnter;

        public override void EnterTrigger(GameObject go, Collider collider, TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user)
        {
            if (go.TryGetComponent<IInteractable>(out var interactable))
            {
                // Debug.Log($"Interaction with Collider [Trigger only :  ");
                item.Interact(interactable);
            }
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (action.Trigger == null)
            {
                return $"Requires a Trigger Proxy to be assigned to the Action. Please assign one to the Action. [{action.name.Value}]";
            }
            return string.Empty;
        }
    }
}