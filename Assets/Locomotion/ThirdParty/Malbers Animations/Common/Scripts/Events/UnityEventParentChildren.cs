using UnityEngine;

namespace MalbersAnimations.Events
{
    /// <summary>Simple Event Raiser On Enable</summary>
    [AddComponentMenu("Malbers/Events/Unity Event [Parent Child Change]")]
    public class UnityEventParentChildren : UnityUtils
    {
        public TransformEvent OnParentChanged = new();
        public TransformEvent OnChildrenChanged = new();


        private void OnTransformChildrenChanged()
        {
            if (enabled)
                OnChildrenChanged.Invoke(transform);
        }

        private void OnTransformParentChanged()
        {
            if (enabled)
                OnParentChanged.Invoke(transform.parent);
        }
    }

}