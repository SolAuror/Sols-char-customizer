using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [CreateAssetMenu(
        fileName = "CharacterPresetLibrary",
        menuName = "Sol/Character Customization/Character Preset Library")]
    public sealed class CharacterPresetLibrary : ScriptableObject
    {
        [SerializeField] private List<CharacterPreset> presets = new();

        public IReadOnlyList<CharacterPreset> Presets =>
            presets ?? (IReadOnlyList<CharacterPreset>)Array.Empty<CharacterPreset>();

        public CharacterPreset Find(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName) || presets == null)
            {
                return null;
            }

            string trimmedName = presetName.Trim();
            return presets.Find(preset =>
                preset != null && string.Equals(preset.DisplayName, trimmedName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
