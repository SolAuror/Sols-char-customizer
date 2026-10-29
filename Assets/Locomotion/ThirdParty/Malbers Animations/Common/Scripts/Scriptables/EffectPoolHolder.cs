// Last Change: 9/17/2026 6:05pm
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary> Sits on the [Effect Pool - ...] holder and finishes the returns Unity refused mid-deactivation </summary>
    [AddComponentMenu("")]
    public class EffectPoolHolder : MonoBehaviour
    {
        //MWC: instances whose reparent was rejected because they were inside a deactivation cascade at the time
        private readonly List<GameObject> pending = new();

        /// <summary> Takes an instance the pool could not park yet, and finishes the job on the next LateUpdate </summary>
        public void AdoptLater(GameObject instance)
        {
            if (instance == null || pending.Contains(instance)) return;

            pending.Add(instance);
            enabled = true; //Idle otherwise: nothing to sweep on a frame where nothing was refused
        }

        //MWC: LateUpdate, not OnDisable's own frame - by now Unity has finished with the hierarchy and lets go of it
        private void LateUpdate()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var instance = pending[i];
                pending.RemoveAt(i);

                if (instance == null) continue; //It was destroyed along with whatever it was attached to

                //It was already handed back out while it sat in this queue, so parking it now would kill a live effect
                if (instance.TryGetComponent<EffectPoolRelease>(out var release) && !release.IsReleased) continue;

                instance.transform.SetParent(transform, false);
                instance.transform.ResetLocal();
                instance.SetActive(false);
            }

            enabled = false;
        }
    }
}
