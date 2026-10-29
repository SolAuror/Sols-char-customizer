// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Events;
using UnityEngine;

namespace MalbersAnimations.Items
{

    [System.Serializable, AddTypeMenu("General/Unity Event")]
    [HelpURL(ItemDocs.Processors + "unity-unity-event")]
    public class ItemProcessorEvent : ItemProcessor
    {
        [Tooltip("If true, the event will be invoked using the Target GameObject instead of the User GameObject")]
        public bool useTarget = false;

        public GameObjectEvent m_event = new();


        public override string DynamicName => $"Event Processor [{m_event.GetPersistentEventCount()}]";

        public override ExecuteProcessor InitialExecute => ExecuteProcessor.OnActionStart;

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            m_event.Invoke(useTarget ? item.Target : item.UserGo);
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            return string.Empty;
        }
    }


    [System.Serializable, AddTypeMenu("General/GameObject [Set Active]")]
    [HelpURL(ItemDocs.Processors + "unity-gameobject-set-active")]
    public class ItemProcessorGameObjectSetActive : ItemProcessor
    {
        public IDPair<GameObject, bool>[] Targets;

        public override ExecuteProcessor InitialExecute => ExecuteProcessor.OnActionStart;

        public override string DynamicName
        {
            get
            {
                string name = "Set Active: ";

                if (Targets != null)
                {

                    foreach (var target in Targets)
                    {
                        name += $"[{(target != null && target.ID != null ? target.ID.name : "<>")}={(target.Value ? "☒" : "▢")}] ";
                    }
                }

                return name;

            }
        }

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            foreach (var target in Targets)
            {
                if (target.ID != null)
                {
                    target.ID.SetActive(target.Value);
                }
            }
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            return string.Empty;
        }
    }
}