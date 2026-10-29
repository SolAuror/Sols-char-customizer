// Last Change: 9/19/2026 4:05pm
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.UI
{
    /// <summary> Optional icon set for the input prompts: maps a binding control path (e.g. "<Gamepad>/buttonSouth") to a Sprite </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Super Item/Input Prompt Icons", fileName = "New Input Prompt Icons", order = -900)]
    public class InputPromptIcons : ScriptableObject
    {
        /// <summary> One control path → Sprite pair </summary>
        [System.Serializable]
        public class Entry
        {
            [Tooltip("Control path of the binding as the Input System reports it. E.g. <Keyboard>/e, <Mouse>/leftButton, <Gamepad>/buttonSouth, <Gamepad>/leftTrigger.\nThe angle brackets and the casing are ignored")]
            public string controlPath;
            [Tooltip("Sprite shown on the key cap instead of the text")]
            public Sprite sprite;
        }

        [Tooltip("Control path → Sprite. When a prompt's control path is found here the row shows the Sprite instead of the key text")]
        public List<Entry> icons = new();

        private Dictionary<string, Sprite> lookup;

        /// <summary> Finds the Sprite for a control path (e.g. "<Mouse>/leftButton"). Returns false when the path has no icon </summary>
        public bool TryGetSprite(string controlPath, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(controlPath) || icons == null || icons.Count == 0) return false;

            if (lookup == null || lookup.Count != icons.Count) BuildLookup();

            return lookup.TryGetValue(Normalize(controlPath), out sprite) && sprite != null;
        }

        /// <summary> Rebuilds the dictionary from the list (call it after editing the list at runtime) </summary>
        public void BuildLookup()
        {
            lookup = new Dictionary<string, Sprite>();

            foreach (var e in icons)
            {
                if (e == null || e.sprite == null || string.IsNullOrEmpty(e.controlPath)) continue;
                lookup[Normalize(e.controlPath)] = e.sprite;
            }
        }

        /// <summary> Lower case, no spaces, no angle brackets: "<Gamepad>/buttonSouth" and "gamepad/buttonsouth" are the same key </summary>
        public static string Normalize(string path) =>
            path.Replace("<", string.Empty).Replace(">", string.Empty).Replace(" ", string.Empty).Trim().ToLowerInvariant();

        private void OnValidate() => lookup = null;
    }
}
