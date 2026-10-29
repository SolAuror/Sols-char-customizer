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
        // Version 3 adds skin colour parameters. Older recipes have none, and the
        // character profile derives them from the tone or custom colour on apply.
        // Version 4 adds the hairstyle and hair colour; older recipes leave hair as it is.
        // Version 5 adds tattoos; older recipes leave tattoos as they are.
        public const int CurrentVersion = 5;
        public const string DefaultSkinToneId = "fair";
        public const string DefaultEyeMaterialId = "brown";

        [SerializeField] private int version = CurrentVersion;
        [SerializeField] private CharacterSex sex = CharacterSex.Female;
        [SerializeField] private string skinToneId = DefaultSkinToneId;
        [SerializeField] private bool usesCustomSkinColor;
        [SerializeField] private Color customSkinColor = Color.white;
        [SerializeField] private bool hasSkinParameters;
        [SerializeField] private CharacterSkinParameters skinParameters = CharacterSkinParameters.Default;
        [SerializeField] private string eyeMaterialId = DefaultEyeMaterialId;
        [SerializeField] private bool hasHair;
        [SerializeField] private string hairStyleId = string.Empty;
        [SerializeField] private Color hairColor = Color.white;
        [SerializeField] private float hairSheen = CharacterHair.DefaultSheen;
        [SerializeField] private bool hasTattoos;
        [SerializeField] private List<CharacterTattooPlacement> tattoos = new();
        [SerializeField] private List<CharacterMorphValue> morphValues = new();

        public int Version => version;
        public CharacterSex Sex => sex;
        public string SkinToneId => skinToneId;
        public bool UsesCustomSkinColor => usesCustomSkinColor;
        public Color CustomSkinColor => customSkinColor;
        public bool HasSkinParameters => hasSkinParameters;
        public CharacterSkinParameters SkinParameters => skinParameters;
        public string EyeMaterialId => string.IsNullOrWhiteSpace(eyeMaterialId)
            ? DefaultEyeMaterialId
            : eyeMaterialId;
        public IReadOnlyList<CharacterMorphValue> MorphValues =>
            morphValues ?? (IReadOnlyList<CharacterMorphValue>)Array.Empty<CharacterMorphValue>();
        public bool HasHair => hasHair;
        // A CharacterHair style ID, empty for the character's first style, or
        // CharacterHair.NoneStyleId for no hair.
        public string HairStyleId => hairStyleId ?? string.Empty;
        // sRGB, like material colours in the inspector.
        public Color HairColor => hairColor;
        // 0 matte, 1 glossy.
        public float HairSheen => hairSheen;
        public bool HasTattoos => hasTattoos;
        // In the order they are layered, first underneath.
        public IReadOnlyList<CharacterTattooPlacement> Tattoos =>
            tattoos ?? (IReadOnlyList<CharacterTattooPlacement>)Array.Empty<CharacterTattooPlacement>();

        public CharacterRecipe()
        {
        }

        public CharacterRecipe(
            CharacterSex sex,
            string skinToneId,
            bool usesCustomSkinColor,
            Color customSkinColor,
            IReadOnlyList<CharacterMorphValue> morphValues)
            : this(sex, skinToneId, usesCustomSkinColor, customSkinColor, DefaultEyeMaterialId, morphValues)
        {
        }

        public CharacterRecipe(
            CharacterSex sex,
            string skinToneId,
            bool usesCustomSkinColor,
            Color customSkinColor,
            string eyeMaterialId,
            IReadOnlyList<CharacterMorphValue> morphValues)
        {
            Overwrite(sex, skinToneId, usesCustomSkinColor, customSkinColor, eyeMaterialId, morphValues);
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
            Overwrite(
                recipeSex,
                recipeSkinToneId,
                recipeUsesCustomSkinColor,
                recipeCustomSkinColor,
                DefaultEyeMaterialId,
                recipeMorphValues);
        }

        internal void Overwrite(
            CharacterSex recipeSex,
            string recipeSkinToneId,
            bool recipeUsesCustomSkinColor,
            Color recipeCustomSkinColor,
            string recipeEyeMaterialId,
            IReadOnlyList<CharacterMorphValue> recipeMorphValues)
        {
            version = CurrentVersion;
            sex = recipeSex;
            skinToneId = string.IsNullOrWhiteSpace(recipeSkinToneId)
                ? DefaultSkinToneId
                : recipeSkinToneId.Trim();
            usesCustomSkinColor = recipeUsesCustomSkinColor;
            customSkinColor = recipeCustomSkinColor;
            eyeMaterialId = string.IsNullOrWhiteSpace(recipeEyeMaterialId)
                ? DefaultEyeMaterialId
                : recipeEyeMaterialId.Trim();
            hasSkinParameters = false;
            skinParameters = CharacterSkinParameters.Default;
            hasHair = false;
            hairStyleId = string.Empty;
            hairColor = Color.white;
            hairSheen = CharacterHair.DefaultSheen;
            hasTattoos = false;
            tattoos ??= new List<CharacterTattooPlacement>();
            tattoos.Clear();
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
                    DefaultEyeMaterialId,
                    Array.Empty<CharacterMorphValue>());
                return;
            }

            Overwrite(
                source.Sex,
                source.SkinToneId,
                source.UsesCustomSkinColor,
                source.CustomSkinColor,
                source.EyeMaterialId,
                source.MorphValues);
            if (source.HasSkinParameters)
            {
                SetSkinParameters(source.SkinParameters);
            }

            if (source.HasHair)
            {
                SetHair(source.HairStyleId, source.HairColor, source.HairSheen);
            }

            if (source.HasTattoos)
            {
                SetTattoos(source.Tattoos);
            }
        }

        public void SetSkinParameters(CharacterSkinParameters parameters)
        {
            hasSkinParameters = true;
            skinParameters = new CharacterSkinParameters(parameters.Melanin, parameters.Hemoglobin, parameters.Undertone);
        }

        public void SetHair(string styleId, Color color, float sheen = CharacterHair.DefaultSheen)
        {
            hasHair = true;
            hairStyleId = string.IsNullOrWhiteSpace(styleId) ? string.Empty : styleId.Trim();
            hairColor = new Color(color.r, color.g, color.b, 1f);
            hairSheen = Mathf.Clamp01(sheen);
        }

        // Replaces the tattoos; an empty list saves a character with none, which clears
        // the tattoos of whoever it is applied to.
        public void SetTattoos(IReadOnlyList<CharacterTattooPlacement> placements)
        {
            hasTattoos = true;
            tattoos ??= new List<CharacterTattooPlacement>();
            tattoos.Clear();
            if (placements == null)
            {
                return;
            }

            for (int index = 0; index < placements.Count; index++)
            {
                tattoos.Add(placements[index]);
            }
        }

        public bool HasValidIdentifiers(out string error)
        {
            if (morphValues == null)
            {
                error = "The recipe has no morph value collection.";
                return false;
            }

            foreach (CharacterTattooPlacement tattoo in Tattoos)
            {
                if (string.IsNullOrWhiteSpace(tattoo.DesignId))
                {
                    error = "The recipe contains a tattoo without a design ID.";
                    return false;
                }
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
