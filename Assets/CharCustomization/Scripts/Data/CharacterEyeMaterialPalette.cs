using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public sealed class CharacterEyeMaterialOption
    {
        [SerializeField] private string id;
        [SerializeField] private string label;
        [SerializeField] private Material material;

        public CharacterEyeMaterialOption(string id, string label, Material material)
        {
            this.id = id;
            this.label = label;
            this.material = material;
        }

        public string Id => id;
        public string Label => string.IsNullOrWhiteSpace(label) ? id : label;
        public Material Material => material;
    }

    [CreateAssetMenu(
        fileName = "CharacterEyeMaterialPalette",
        menuName = "Sol/Character Customization/Eye Material Palette")]
    public sealed class CharacterEyeMaterialPalette : ScriptableObject
    {
        [SerializeField] private string defaultEyeMaterialId = CharacterRecipe.DefaultEyeMaterialId;
        [SerializeField] private List<CharacterEyeMaterialOption> options = new();

        public IReadOnlyList<CharacterEyeMaterialOption> Options =>
            options ?? (IReadOnlyList<CharacterEyeMaterialOption>)Array.Empty<CharacterEyeMaterialOption>();

        public bool TryGet(string eyeMaterialId, out CharacterEyeMaterialOption option)
        {
            string normalizedId = string.IsNullOrWhiteSpace(eyeMaterialId)
                ? defaultEyeMaterialId
                : eyeMaterialId.Trim();

            foreach (CharacterEyeMaterialOption candidate in Options)
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

        public CharacterEyeMaterialOption GetDefault()
        {
            return TryGet(defaultEyeMaterialId, out CharacterEyeMaterialOption option)
                ? option
                : Options.Count > 0 ? Options[0] : null;
        }

#if UNITY_EDITOR
        public void ConfigureDefaults(IReadOnlyList<CharacterEyeMaterialOption> defaultOptions, string defaultId)
        {
            options = defaultOptions != null
                ? new List<CharacterEyeMaterialOption>(defaultOptions)
                : new List<CharacterEyeMaterialOption>();
            defaultEyeMaterialId = string.IsNullOrWhiteSpace(defaultId)
                ? CharacterRecipe.DefaultEyeMaterialId
                : defaultId.Trim();
        }
#endif
    }
}
