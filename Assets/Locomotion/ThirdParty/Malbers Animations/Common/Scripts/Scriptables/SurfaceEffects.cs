// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace MalbersAnimations
{
    [System.Serializable]
    [UnityEngine.CreateAssetMenu(menuName = "Malbers Animations/Effects/Surface Effect", fileName = "New Surface Effect", order = -1000)]
    public class SurfaceEffects : ScriptableObject
    {
        [Tooltip("Custom Hit Effects if the Damageable has a Surface ID. Default [0]")]
        public List<EffectType> surfaceEffects = new();

        /// <summary> Finds the Effect for a Surface ID. Returns the entry [0] (Default) when there's no match </summary>
        public EffectType Get(SurfaceID id)
        {
            if (surfaceEffects.Count == 0) return null;

            foreach (var effect in surfaceEffects)
            {
                if (effect.surface == id) return effect;
            }
            return surfaceEffects[0]; // Return [0] if no matching SurfaceID is found
        }

        public EffectType Default => surfaceEffects.Count > 0 ? surfaceEffects[0] : null;
    }

    [System.Serializable]
    public class EffectType
    {
        public SurfaceID surface;
        public GameObjectReference effect;

        public ObjectPool<GameObject> Pool { get; set; }

        private GameObject _PoolHolder;

        //MWC: finishes any return Unity refused because the instance was mid-deactivation. See OnReturnedToPool
        private EffectPoolHolder _Holder;

        /// <summary> Takes an Effect from the Pool and places it at the given position/rotation </summary>
        public GameObject Play(Vector3 position, Quaternion rotation, Transform parent = null, int poolSize = 10, int MaxPoolSize = 10)
        {
            if (effect.Value == null) return null;

            if (_PoolHolder == null)
            {
                //MWC — the Holder (and everything pooled inside it) died with the previous scene. Drop the stale pool
                if (Pool != null) { Pool.Clear(); Pool = null; }

                _PoolHolder = new GameObject($"Effect Pool - [{effect.Value.name}]");
                _PoolHolder.transform.SetPositionAndRotation(position, rotation);
                _Holder = _PoolHolder.AddComponent<EffectPoolHolder>();
            }

            if (Pool == null)
            {
                Pool = new ObjectPool<GameObject>
                     (CreatePooledItem, OnTakeFromPool, OnReturnedToPool, OnDestroyPoolObject, false,
                     poolSize, MaxPoolSize);
            }

            //MWC: a parked instance can still die - it was destroyed along with whatever it was parented to before it
            //could be moved to the Holder. Those leave null entries behind, so they are drained instead of failing the
            //whole Play: an empty pool always builds a fresh one, so this cannot spin
            GameObject Effect = null;

            for (int i = 0; i < 8 && Effect == null; i++) Effect = Pool.Get(); // Get an item from the pool

            if (Effect == null) return null;

            //MWC: EVERY instance carries its own release, armed here rather than only on the timed Play overload.
            //A pool exists to reuse instances, so an instance must find its way home on its own: when its duration
            //runs out, and just as importantly the moment whatever it is parented to is switched off
            if (!Effect.TryGetComponent<EffectPoolRelease>(out var release))
                release = Effect.AddComponent<EffectPoolRelease>();

            release.Arm(this);

            Effect.transform.SetPositionAndRotation(position, rotation); // Set the position and rotation of the effect
            Effect.transform.SetParent(parent, true); // Set the parent of the effect if provided

            return Effect;
        }

        /// <summary> Plays the Effect and releases it back to the Pool after [duration] seconds. Zero means no auto release </summary>
        public GameObject Play(Vector3 position, Quaternion rotation, Transform parent, float duration)
        {
            var instance = Play(position, rotation, parent);

            //The instance releases itself, so it does not depend on the Damager that spawned it (pooled/destroyed
            //projectiles). Play already armed it with the component, this only adds the timer on top
            if (instance != null && duration > 0 && instance.TryGetComponent<EffectPoolRelease>(out var release))
                release.Schedule(duration);

            return instance;
        }

        // If the pool capacity is reached then any items returned will be destroyed.
        // We can control what the destroy behavior does, here we destroy the GameObject.
        private void OnDestroyPoolObject(GameObject Go)
        {
            //GameObject.Destroy(Go); // When pool size excides max size destroy projectile
        }

        private void OnReturnedToPool(GameObject Go)
        {
            if (Go == null) return; //MWC — the instance was destroyed with its parent

            //MWC: Unity REFUSES both the reparent and the disable while the instance is inside a deactivation, and
            //WARNS on each attempt ("GameObject is already being activated or deactivated"). That is exactly the case
            //that matters here - whatever the effect was attached to has just been switched off - so the parking is
            //not even attempted: the Holder does it on the next LateUpdate, once Unity has let go of the hierarchy.
            //The instance is in the pool from this moment either way, so it can be handed straight back out
            if (m_ParkLater)
            {
                if (_Holder != null) _Holder.AdoptLater(Go);
                return;
            }

            if (_PoolHolder == null) { Go.SetActive(false); return; } //MWC — guard the Holder

            Go.transform.SetParent(_PoolHolder.transform, false); // or to Weapon
            Go.transform.ResetLocal();
            Go.SetActive(false); // Disable
        }

        private void OnTakeFromPool(GameObject Go)
        {
            if (Go == null) return; //MWC — the instance was destroyed while it was pooled

            Go.transform.SetParent(null, true); // Unparent projectile
            Go.SetActive(true); // Enable
        }

        private GameObject CreatePooledItem()
        {
            var go = GameObject.Instantiate(effect.Value);
            go.name = go.name.Replace("(Clone)", "(Pool)"); // Remove (Clone) from the name
            return go;
        }

        /// <summary> Release the effect back to the pool  </summary>
        /// <param name="parkLater">True when the caller is inside the instance's own OnDisable. Unity will not let
        /// it be reparented or disabled yet, so the Holder parks it a frame later instead. It joins the pool right now
        /// either way, so a hit on this same frame still reuses it rather than building another one</param>
        internal void Release(GameObject effect, bool parkLater = false)
        {
            if (effect == null || Pool == null) return; //MWC — nothing to release

            //MWC: ObjectPool gives OnReturnedToPool no context of its own, so it is carried across the call. Set and
            //cleared around it, and Unity runs this on one thread, so it cannot bleed into another release
            m_ParkLater = parkLater;

            Pool.Release(effect);

            m_ParkLater = false;
        }

        //MWC: see Release. True only for the duration of a single OnDisable-driven release
        private bool m_ParkLater;
    }

}
