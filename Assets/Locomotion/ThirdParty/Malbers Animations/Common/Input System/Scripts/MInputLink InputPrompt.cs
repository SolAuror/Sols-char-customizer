// Last Change: 9/19/2026 4:05pm
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MalbersAnimations.InputSystem
{
    //IInputPrompt: the UI side of the input link. A prompt UI (e.g. Super Item Actions UI) asks "what do I press for
    //[Light Attack]?" and gets the binding of the CURRENT control scheme (Keyboard and Mouse / Gamepad), so the
    //on-screen key follows the device the player is holding.
    public partial class MInputLink : IInputPrompt
    {
        /// <summary> Resolves the display name, control path and interaction of an input row on the active control scheme </summary>
        public bool TryGetInputPrompt(string inputName, out InputPrompt prompt)
        {
            prompt = default;

            var button = FindPromptButton(inputName);
            if (button == null) return false;

            prompt.Interaction = button.interaction.ToString();
            prompt.Display = button.Name; //Fallback: the input name itself when the binding cannot be read

#if ENABLE_INPUT_SYSTEM
            //The runtime Action when it is resolved, the serialized reference before that (edit mode, not yet connected)
            var action = button.Action ?? (button.reference != null ? button.reference.action : null);
            if (action == null) return true;

            var scheme = playerInput != null ? playerInput.currentControlScheme : null;

            //Bindings of the active control scheme first; any binding when the scheme is unknown or has none
            if (!ReadBinding(action, string.IsNullOrEmpty(scheme) ? default : InputBinding.MaskByGroup(scheme), ref prompt))
                ReadBinding(action, default, ref prompt);
#endif
            return true;
        }

        /// <summary> Input row by name: the active Action Map first, then every map on the link </summary>
        private MInputAction FindPromptButton(string inputName)
        {
            if (m_MapButtons == null || string.IsNullOrEmpty(inputName)) return null;

            var key = inputName.Trim();

            if (ActiveMActionMap?.buttons != null)
            {
                foreach (var b in ActiveMActionMap.buttons)
                    if (b != null && b.Name == key) return b;
            }

            foreach (var map in m_MapButtons)
            {
                if (map?.buttons == null) continue;

                foreach (var b in map.buttons)
                    if (b != null && b.Name == key) return b;
            }
            return null;
        }

#if ENABLE_INPUT_SYSTEM
        /// <summary> Fills the prompt from the first binding matching the mask. False when no binding matches </summary>
        private static bool ReadBinding(InputAction action, InputBinding mask, ref InputPrompt prompt)
        {
            var bindings = action.bindings;

            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue; //Buttons only: a composite (Move) has no single key
                if (!mask.Matches(binding)) continue;

                var display = action.GetBindingDisplayString(i, out var device, out var control,
                    InputBinding.DisplayStringOptions.DontIncludeInteractions);

                if (string.IsNullOrEmpty(display)) continue;

                prompt.Display = display;
                prompt.Device = device;
                prompt.ControlPath = string.IsNullOrEmpty(device) ? control : $"<{device}>/{control}";
                return true;
            }
            return false;
        }
#endif
    }
}
