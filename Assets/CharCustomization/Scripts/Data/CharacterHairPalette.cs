using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public sealed class CharacterHairColorOption
    {
        [SerializeField] private string id;
        [SerializeField] private string label;
        [Tooltip("sRGB, like material colours in the inspector.")]
        [SerializeField] private Color color = Color.white;

        public CharacterHairColorOption(string id, string label, Color color)
        {
            this.id = id;
            this.label = label;
            this.color = color;
        }

        public string Id => id;
        public string Label => string.IsNullOrWhiteSpace(label) ? id : label;
        public Color Color => color;
    }

    // Named hair colour swatches for the hair menu. Any colour can still be chosen
    // freely; these are starting points.
    [CreateAssetMenu(
        fileName = "CharacterHairPalette",
        menuName = "Sol/Character Customization/Hair Palette")]
    public sealed class CharacterHairPalette : ScriptableObject
    {
        [SerializeField] private string defaultColorId;
        [SerializeField] private List<CharacterHairColorOption> options = new();

        public IReadOnlyList<CharacterHairColorOption> Options =>
            options ?? (IReadOnlyList<CharacterHairColorOption>)Array.Empty<CharacterHairColorOption>();

        public bool TryGet(string colorId, out CharacterHairColorOption option)
        {
            string normalizedId = string.IsNullOrWhiteSpace(colorId) ? defaultColorId : colorId.Trim();
            foreach (CharacterHairColorOption candidate in Options)
            {
                if (candidate != null && string.Equals(candidate.Id, normalizedId, StringComparison.Ordinal))
                {
                    option = candidate;
                    return true;
                }
            }

            option = null;
            return false;
        }

        public CharacterHairColorOption GetDefault()
        {
            return TryGet(defaultColorId, out CharacterHairColorOption option)
                ? option
                : Options.Count > 0 ? Options[0] : null;
        }

#if UNITY_EDITOR
        public void ConfigureDefaults(IReadOnlyList<CharacterHairColorOption> defaultOptions, string defaultId)
        {
            options = defaultOptions != null
                ? new List<CharacterHairColorOption>(defaultOptions)
                : new List<CharacterHairColorOption>();
            defaultColorId = defaultId?.Trim();
        }
#endif
    }
}
