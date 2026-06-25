using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public struct CharacterMorphValue
    {
        [SerializeField] private string morphId;
        [SerializeField] private float value;

        public CharacterMorphValue(string morphId, float value)
        {
            this.morphId = morphId;
            this.value = value;
        }

        public string MorphId => morphId;
        public float Value => value;
    }

    [Serializable]
    public sealed class CharacterRecipe
    {
        public const int CurrentVersion = 1;
        public const string DefaultSkinToneId = "fair";

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private CharacterSex sex = CharacterSex.Female;
        [SerializeField] private string skinToneId = DefaultSkinToneId;
        [SerializeField] private bool usesCustomSkinColor;
        [SerializeField] private Color customSkinColor = Color.white;
        [SerializeField] private List<CharacterMorphValue> morphValues = new();

        public int Version => version;
        public CharacterSex Sex => sex;
        public string SkinToneId => skinToneId;
        public bool UsesCustomSkinColor => usesCustomSkinColor;
        public Color CustomSkinColor => customSkinColor;
        public IReadOnlyList<CharacterMorphValue> MorphValues =>
            morphValues ?? (IReadOnlyList<CharacterMorphValue>)Array.Empty<CharacterMorphValue>();

        public CharacterRecipe()
        {
        }

        public CharacterRecipe(
            CharacterSex sex,
            string skinToneId,
            bool usesCustomSkinColor,
            Color customSkinColor,
            IReadOnlyList<CharacterMorphValue> morphValues)
        {
            Overwrite(sex, skinToneId, usesCustomSkinColor, customSkinColor, morphValues);
        }

        public CharacterRecipe Copy()
        {
            var copy = new CharacterRecipe();
            copy.Overwrite(this);
            return copy;
        }

        internal void Overwrite(
            CharacterSex recipeSex,
            string recipeSkinToneId,
            bool recipeUsesCustomSkinColor,
            Color recipeCustomSkinColor,
            IReadOnlyList<CharacterMorphValue> recipeMorphValues)
        {
            version = CurrentVersion;
            sex = recipeSex;
            skinToneId = string.IsNullOrWhiteSpace(recipeSkinToneId)
                ? DefaultSkinToneId
                : recipeSkinToneId.Trim();
            usesCustomSkinColor = recipeUsesCustomSkinColor;
            customSkinColor = recipeCustomSkinColor;
            morphValues ??= new List<CharacterMorphValue>();
            morphValues.Clear();

            if (recipeMorphValues == null)
            {
                return;
            }

            for (int index = 0; index < recipeMorphValues.Count; index++)
            {
                morphValues.Add(recipeMorphValues[index]);
            }
        }

        internal void Overwrite(CharacterRecipe source)
        {
            if (source == null)
            {
                Overwrite(
                    CharacterSex.Female,
                    DefaultSkinToneId,
                    false,
                    Color.white,
                    Array.Empty<CharacterMorphValue>());
                return;
            }

            Overwrite(
                source.Sex,
                source.SkinToneId,
                source.UsesCustomSkinColor,
                source.CustomSkinColor,
                source.MorphValues);
        }

        public bool HasValidIdentifiers(out string error)
        {
            if (morphValues == null)
            {
                error = "The recipe has no morph value collection.";
                return false;
            }

            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterMorphValue morphValue in morphValues)
            {
                if (string.IsNullOrWhiteSpace(morphValue.MorphId))
                {
                    error = "The recipe contains an empty morph ID.";
                    return false;
                }

                if (!identifiers.Add(morphValue.MorphId))
                {
                    error = $"The recipe contains duplicate morph ID '{morphValue.MorphId}'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public bool TryGetValue(string morphId, out float value)
        {
            if (morphValues != null)
            {
                foreach (CharacterMorphValue morphValue in morphValues)
                {
                    if (string.Equals(morphValue.MorphId, morphId, StringComparison.Ordinal))
                    {
                        value = morphValue.Value;
                        return true;
                    }
                }
            }

            value = 0f;
            return false;
        }
    }
}
