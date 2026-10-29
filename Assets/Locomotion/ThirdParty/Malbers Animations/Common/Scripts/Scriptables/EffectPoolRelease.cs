// Last Change: 9/17/2026 6:05pm
using System.Collections;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary> Returns a pooled Surface Effect to its Effect Pool, on its own, independent of who spawned it </summary>
    [AddComponentMenu("")]
    public class EffectPoolRelease : MonoBehaviour
    {
        /// <summary> The Effect Pool this instance came out of, set every time it is taken from it </summary>
        private EffectType Owner;

        //MWC: returning to the pool disables this instance, which fires OnDisable again. Without this the release
        //re-enters and the same instance is handed to the pool twice, so it ends up in there two times over
        private bool released;

        /// <summary> True while this instance belongs to the pool, so a deferred park cannot kill a live effect </summary>
        public bool IsReleased => released;

        /// <summary> Binds this instance to the Effect Pool it just came from. Called on every Play </summary>
        public void Arm(EffectType effect)
        {
            Owner = effect;
            released = false;
            StopAllCoroutines(); //A pooled instance may still be carrying the timer from its previous use
        }

        /// <summary> Also returns this instance to its pool after [delay] seconds. Zero or less never times out </summary>
        public void Schedule(float delay)
        {
            if (delay <= 0) return;

            StopAllCoroutines();
            StartCoroutine(C_Release(delay));
        }

        /// <summary> Waits the delay and returns this instance to its pool </summary>
        private IEnumerator C_Release(float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToPool();
        }

        //MWC: whatever the effect was parented to got switched off - a projectile going back to its OWN pool, a
        //disabled scene object. A coroutine cannot run on a disabled GameObject, so the timer above never fires and
        //the instance is stranded: inactive, still hanging off whatever went away, and never reused. The pool then
        //builds another one on the next hit, which is the exact opposite of what a pool is for
        private void OnDisable()
        {
            //The scene itself is going away, so there is no pool left to return to (and nothing to leak into it)
            if (!gameObject.scene.isLoaded) return;

            //Unity is mid-deactivation here and refuses to reparent or disable this instance, warning on every
            //attempt, so the pool is told to leave the parking to the Holder a frame later
            ReturnToPool(parkLater: true);
        }

        /// <summary> Hands this instance back to its Effect Pool, once </summary>
        private void ReturnToPool(bool parkLater = false)
        {
            if (released || Owner == null) return;

            released = true;
            StopAllCoroutines();

            Owner.Release(gameObject, parkLater); //Parks it under the [Effect Pool - ...] holder and disables it
        }
    }
}
