using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public struct CharacterMorphPresetValue
    {
        [SerializeField] private string morphId;
        [SerializeField] private float value;

        public CharacterMorphPresetValue(string morphId, float value)
        {
            this.morphId = morphId;
            this.value = value;
        }

        public string MorphId => morphId;
        public float Value => value;
    }

    [CreateAssetMenu(
        fileName = "CharacterMorphPreset",
        menuName = "Sol/Character Customization/Morph Preset")]
    public sealed class CharacterMorphPreset : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private CharacterSex sex = CharacterSex.Female;
        [SerializeField] private List<CharacterMorphPresetValue> values = new();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public CharacterSex Sex => sex;
        public IReadOnlyList<CharacterMorphPresetValue> Values =>
            values ?? (IReadOnlyList<CharacterMorphPresetValue>)Array.Empty<CharacterMorphPresetValue>();

        internal void SetDisplayName(string presetName)
        {
            displayName = presetName?.Trim();
            if (!string.IsNullOrEmpty(displayName))
            {
                name = displayName;
            }
        }

        internal void Overwrite(
            CharacterSex presetSex,
            IReadOnlyList<CharacterMorphDefinition> definitions,
            Func<string, float> getValue)
        {
            sex = presetSex;
            values ??= new List<CharacterMorphPresetValue>();
            values.Clear();

            foreach (CharacterMorphDefinition definition in definitions)
            {
                float value = Mathf.Clamp(getValue(definition.Id), definition.MinimumValue, 1f);
                values.Add(new CharacterMorphPresetValue(definition.Id, value));
            }
        }

        internal bool HasValidIdentifiers(out string error)
        {
            if (values == null)
            {
                error = "The preset has no morph value collection.";
                return false;
            }

            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterMorphPresetValue presetValue in values)
            {
                if (string.IsNullOrWhiteSpace(presetValue.MorphId))
                {
                    error = "The preset contains an empty morph ID.";
                    return false;
                }

                if (!identifiers.Add(presetValue.MorphId))
                {
                    error = $"The preset contains duplicate morph ID '{presetValue.MorphId}'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        internal bool TryGetValue(string morphId, out float value)
        {
            if (values == null)
            {
                value = 0f;
                return false;
            }

            foreach (CharacterMorphPresetValue presetValue in values)
            {
                if (string.Equals(presetValue.MorphId, morphId, StringComparison.Ordinal))
                {
                    value = presetValue.Value;
                    return true;
                }
            }

            value = 0f;
            return false;
        }
    }
}
