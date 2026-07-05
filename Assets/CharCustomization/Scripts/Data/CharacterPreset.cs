using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Sol.CharacterCustomization
{
    [CreateAssetMenu(
        fileName = "CharacterPreset",
        menuName = "Sol/Character Customization/Character Preset")]
    public sealed class CharacterPreset : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private CharacterRecipe recipe = new();

        // These fields migrate CharacterMorphPreset assets without changing their MonoScript GUID.
        [FormerlySerializedAs("sex"), SerializeField, HideInInspector]
        private CharacterSex legacySex = CharacterSex.Female;
        [FormerlySerializedAs("values"), SerializeField, HideInInspector]
        private List<CharacterMorphValue> legacyValues = new();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public CharacterRecipe Recipe
        {
            get
            {
                MigrateLegacyRecipe();
                return recipe;
            }
        }

        public CharacterSex Sex => Recipe.Sex;
        public IReadOnlyList<CharacterMorphValue> Values => Recipe.MorphValues;

        internal void SetDisplayName(string presetName)
        {
            displayName = presetName?.Trim();
            if (!string.IsNullOrEmpty(displayName))
            {
                name = displayName;
            }
        }

        internal void Overwrite(CharacterRecipe source)
        {
            recipe ??= new CharacterRecipe();
            recipe.Overwrite(source);
            legacyValues?.Clear();
        }

        private void OnEnable()
        {
            MigrateLegacyRecipe();
        }

        private void MigrateLegacyRecipe()
        {
            recipe ??= new CharacterRecipe();
            if ((recipe.MorphValues == null || recipe.MorphValues.Count == 0) &&
                legacyValues != null && legacyValues.Count > 0)
            {
                recipe.Overwrite(
                    legacySex,
                    CharacterRecipe.DefaultSkinToneId,
                    false,
                    Color.white,
                    legacyValues);
                legacyValues.Clear();
            }
        }
    }
}
