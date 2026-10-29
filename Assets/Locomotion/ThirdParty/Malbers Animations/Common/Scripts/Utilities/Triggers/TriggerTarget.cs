// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Utilities
{
    /// <summary>Lives on a collider GameObject that is inside one or more Trigger Proxies, so they release it when it is disabled</summary>
    public class TriggerTarget : MonoBehaviour
    {
        /// <summary>Trigger Proxies currently tracking this target</summary>
        public HashSet<TriggerProxy> Proxies { get; set; } = new();
        private Collider[] _cachedCollider;

        //MWC: true when a TriggerProxy auto-added this component (vs. a user placing it manually).
        //Only auto-added targets self-destroy once no proxies reference them, so we never delete a user's component.
        public bool AutoAdded { get; internal set; }

        /// <summary>True once the auto-added component was condemned; Destroy is deferred so it is still alive until the end of the frame</summary>
        private bool _condemned;

        private void Awake()
        {
            //hideFlags = HideFlags.HideInInspector;
            _cachedCollider = GetComponents<Collider>();

#if UNITY_EDITOR 
            if (_cachedCollider == null || _cachedCollider.Length == 0)
            {
                Debug.LogWarning($"TriggerTarget on {gameObject.name} has no attached collider!", this);
                enabled = false;
            }
#endif
        }

        /// <summary>Registers this target and its colliders so Proxies can find it</summary>
        protected virtual void OnEnable()
        {
            if (_condemned) return; //a condemned component must not re-register before its Destroy lands
            TriggerRegistry.RegisterTarget(this);

            foreach (var col in _cachedCollider)
                TriggerRegistry.RegisterCollider(col, this);
        }

        /// <summary>Releases this target from every Proxy tracking it and unregisters it</summary>
        private void OnDisable()
        {
            if (Proxies != null)
            {
                foreach (var p in Proxies)
                {
                    if (!p) continue;

                    foreach (var col in _cachedCollider) p.TriggerExit(col, false);
                }
                Proxies.Clear();
            }

            Unregister();
        }

        /// <summary>Removes this target and its colliders from the registry (only entries this target owns)</summary>
        private void Unregister()
        {
            TriggerRegistry.UnregisterTarget(this);

            foreach (var col in _cachedCollider)
                TriggerRegistry.UnregisterCollider(col, this);
        }

        /// <summary>A Proxy started tracking this target</summary>
        public void AddProxy(TriggerProxy trigger) => Proxies.Add(trigger);

        /// <summary>A Proxy stopped tracking this target; an auto-added target destroys itself once nothing references it</summary>
        public void RemoveProxy(TriggerProxy trigger)
        {
            Proxies.Remove(trigger);

            //MWC: clean up the auto-added component once nothing references it, so colliders don't accumulate
            //an orphan TriggerTarget (and a registry entry) for their whole lifetime. Never touch user-added ones.
            if (AutoAdded && !_condemned && Proxies.Count == 0)
            {
                //Destroy is deferred to the end of the frame. Unregister right now, otherwise a Proxy entering
                //this collider later in the same frame is handed the condemned target and gets force-exited
                //(a spurious Exit while the collider is still inside) when the Destroy finally lands.
                _condemned = true;
                Unregister();
                Destroy(this);
            }
        }
    }
}