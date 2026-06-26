using System;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterProfile : MonoBehaviour
    {
        [Header("Recipe")]
        [SerializeField] private CharacterMorphController controller;
        [SerializeField] private CharacterPreset authoredPreset;
        [SerializeField] private bool applyOnAwake;

        [Header("Skin")]
        [SerializeField] private CharacterSkinPalette skinPalette;
        [SerializeField] private string[] skinColorPropertyNames = { "_BaseColor", "_Color" };
        [SerializeField] private Renderer[] femaleSkinRenderers = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] maleSkinRenderers = Array.Empty<Renderer>();

        private MaterialPropertyBlock propertyBlock;
        private int[] skinColorPropertyIds;
        private string skinToneId = CharacterRecipe.DefaultSkinToneId;
        private bool usesCustomSkinColor;
        private Color customSkinColor = Color.white;

        public CharacterMorphController Controller => controller;
        public CharacterSkinPalette SkinPalette => skinPalette;
        public string SkinToneId => skinToneId;
        public bool UsesCustomSkinColor => usesCustomSkinColor;
        public Color CurrentSkinColor => ResolveSkinColor();

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (controller == null)
            {
                controller = GetComponent<CharacterMorphController>();
            }

            CacheSkinPropertyIds();
            CharacterSkinTone defaultTone = skinPalette != null ? skinPalette.GetDefault() : null;
            if (defaultTone != null)
            {
                skinToneId = defaultTone.Id;
            }

            if (applyOnAwake && authoredPreset != null)
            {
                ApplyPreset(authoredPreset);
            }
            else
            {
                ApplySkin();
            }
        }

        public CharacterRecipe CaptureRecipe()
        {
            if (controller == null)
            {
                Debug.LogError("A character profile cannot capture a recipe without a morph controller.", this);
                return null;
            }

            var recipe = new CharacterRecipe();
            recipe.Overwrite(
                controller.ActiveSex,
                skinToneId,
                usesCustomSkinColor,
                customSkinColor,
                controller.CaptureMorphValues());
            return recipe;
        }

        public bool ApplyRecipe(CharacterRecipe recipe)
        {
            if (controller == null || recipe == null)
            {
                Debug.LogWarning("A character profile requires a controller and recipe before it can apply one.", this);
                return false;
            }

            if (!recipe.HasValidIdentifiers(out string error))
            {
                Debug.LogWarning($"Cannot apply character recipe. {error}", this);
                return false;
            }

            controller.SetSex(recipe.Sex);
            controller.ApplyMorphValues(recipe.MorphValues);

            if (recipe.UsesCustomSkinColor)
            {
                SetCustomSkinColor(recipe.CustomSkinColor);
            }
            else
            {
                SetSkinTone(recipe.SkinToneId);
            }

            return true;
        }

        public bool ApplyPreset(CharacterPreset preset)
        {
            if (preset == null)
            {
                Debug.LogWarning("Cannot apply a null character preset.", this);
                return false;
            }

            return ApplyRecipe(preset.Recipe);
        }

        public bool SetSkinTone(string toneId)
        {
            if (skinPalette == null || !skinPalette.TryGet(toneId, out CharacterSkinTone tone))
            {
                Debug.LogWarning($"Unknown skin tone '{toneId}'.", this);
                return false;
            }

            skinToneId = tone.Id;
            usesCustomSkinColor = false;
            customSkinColor = tone.Color;
            ApplySkin();
            return true;
        }

        public void SetCustomSkinColor(Color color)
        {
            usesCustomSkinColor = true;
            customSkinColor = new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a));
            ApplySkin();
        }

        public void RandomizeCurrent(float rangeScale = 0.65f, System.Random random = null)
        {
            if (controller == null)
            {
                return;
            }

            random ??= new System.Random();
            controller.RandomizeCurrent(rangeScale, random);
            if (skinPalette != null && skinPalette.Tones.Count > 0)
            {
                CharacterSkinTone tone = skinPalette.Tones[random.Next(0, skinPalette.Tones.Count)];
                SetSkinTone(tone.Id);
            }
        }

        public void RefreshSkin()
        {
            ApplySkin();
        }

        private Color ResolveSkinColor()
        {
            if (usesCustomSkinColor)
            {
                return customSkinColor;
            }

            if (skinPalette != null && skinPalette.TryGet(skinToneId, out CharacterSkinTone tone))
            {
                return tone.Color;
            }

            CharacterSkinTone fallback = skinPalette != null ? skinPalette.GetDefault() : null;
            return fallback != null ? fallback.Color : Color.white;
        }

        private void ApplySkin()
        {
            Color color = ResolveSkinColor();
            ApplySkin(femaleSkinRenderers, color);
            ApplySkin(maleSkinRenderers, color);
        }

        private void ApplySkin(Renderer[] renderers, Color color)
        {
            if (renderers == null)
            {
                return;
            }

            CacheSkinPropertyIds();
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer == null)
                {
                    continue;
                }

                propertyBlock ??= new MaterialPropertyBlock();
                targetRenderer.GetPropertyBlock(propertyBlock);
                foreach (int propertyId in skinColorPropertyIds)
                {
                    propertyBlock.SetColor(propertyId, color);
                }

                targetRenderer.SetPropertyBlock(propertyBlock);
                propertyBlock.Clear();
            }
        }

        private void CacheSkinPropertyIds()
        {
            if (skinColorPropertyIds != null)
            {
                return;
            }

            if (skinColorPropertyNames == null || skinColorPropertyNames.Length == 0)
            {
                skinColorPropertyNames = new[] { "_BaseColor", "_Color" };
            }

            var ids = new System.Collections.Generic.List<int>(skinColorPropertyNames.Length);
            foreach (string propertyName in skinColorPropertyNames)
            {
                if (!string.IsNullOrWhiteSpace(propertyName))
                {
                    ids.Add(Shader.PropertyToID(propertyName.Trim()));
                }
            }

            if (ids.Count == 0)
            {
                ids.Add(Shader.PropertyToID("_BaseColor"));
                ids.Add(Shader.PropertyToID("_Color"));
            }

            skinColorPropertyIds = ids.ToArray();
        }
    }
}
