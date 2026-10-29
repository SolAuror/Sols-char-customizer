using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace MalbersAnimations
{
    // [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/malbers-input")]
    [AddComponentMenu("Malbers/Input/Mouse Scroll")]
    public class MMouseScroll : MonoBehaviour
    {
        public UnityEvent OnScrollUp = new();
        public UnityEvent OnScrollDown = new();

        private float mouseDelta = 0;

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return; // no mouse attached or Input System not initialized


            // Read the scroll delta (Vector2: x for horizontal, y for vertical)
            var newDelta = mouse.scroll.ReadValue().y;


            if (newDelta != mouseDelta)
            {
                mouseDelta = newDelta;

                if (mouseDelta < 0) OnScrollDown.Invoke();
                else if (mouseDelta > 0) OnScrollUp.Invoke();
            }
        }
    }
}