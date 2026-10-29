using UnityEngine;
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;

namespace MalbersAnimations.Utilities
{
    /// <summary>
    /// This is used when the collider is in a different gameObject and you need to check the Trigger Events
    /// Create this component at runtime and subscribe to the UnityEvents
    /// </summary>
    [AddComponentMenu("Malbers/Utilities/Colliders/Trigger Exit")]
    public class TriggerExit : MonoBehaviour
    {
        public LayerReference Layer = new LayerReference(-1);
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        public ColliderEvent onTriggerExit = new ColliderEvent();

        /// <summary> Collider Component used for the Trigger Proxy </summary>
        public Collider OwnCollider { get; private set; }
        public bool Active { get => enabled; set => enabled = value; }
        public QueryTriggerInteraction TriggerInteraction { get => triggerInteraction; set => triggerInteraction = value; }


        public bool TrueConditions(Collider other)
        {
            if (!Active) return false;
            if (OwnCollider == null) return false;                                       //MWC: no trigger collider
            if (other == null) return false;                                            //MWC: collider was destroyed
            if (triggerInteraction == QueryTriggerInteraction.Ignore && other.isTrigger) return false;
            if (!MTools.Layer_in_LayerMask(other.gameObject.layer, Layer)) return false;
            if (transform.IsChildOf(other.transform)) return false; // you are

            return true;
        }

        void OnTriggerExit(Collider other) { if (TrueConditions(other)) onTriggerExit.Invoke(other); }


        //MWC: initialize in OnEnable (was Start) to match TriggerEnter, so the collider is fetched and forced
        //to isTrigger before any physics callback can arrive, and re-validated each time the component enables.
        protected virtual void OnEnable()
        {
            OwnCollider = GetComponent<Collider>();
            Active = true;

            if (OwnCollider)
            {
                OwnCollider.isTrigger = true;
            }
            else
            {
                Active = false;
                Debug.LogError("This Script requires a Collider, please add any type of collider", this);
            }
        }
    }
}