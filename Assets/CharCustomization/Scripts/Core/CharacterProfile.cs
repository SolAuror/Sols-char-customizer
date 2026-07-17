using System;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterProfile : MonoBehaviour
    {
        private const string FemaleEyeRendererName = "F_eyes";
        private const string MaleEyeRendererName = "M_Eyes";

        [Header("Recipe")]
        [SerializeField] private CharacterMorphController controller;
        [SerializeField] private CharacterPreset authoredPreset;
        [SerializeField] private bool applyOnAwake;

        [Header("Skin")]
        [SerializeField] private CharacterSkinPalette skinPalette;
        [SerializeField] private string[] skinColorPropertyNames = { "_BaseColor", "_Color" };
        [SerializeField] private string[] skinRoughnessPropertyNames = { "_Smoothness", "_Glossiness" };
        [SerializeField] private bool invertRoughnessForSmoothness = true;
        [SerializeField, Range(0f, 1f)] private float skinRoughness = 0.5f;
        [SerializeField] private Renderer[] femaleSkinRenderers = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] maleSkinRenderers = Array.Empty<Renderer>();

        [Header("Eyes")]
        [SerializeField] private CharacterEyeMaterialPalette eyePalette;
        [SerializeField] private Renderer[] femaleEyeRenderers = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] maleEyeRenderers = Array.Empty<Renderer>();

        private MaterialPropertyBlock propertyBlock;
        private int[] skinColorPropertyIds;
        private int[] skinRoughnessPropertyIds;
        private string skinToneId = CharacterRecipe.DefaultSkinToneId;
        private bool usesCustomSkinColor;
        private Color customSkinColor = Color.white;
        private string eyeMaterialId = CharacterRecipe.DefaultEyeMaterialId;
        private Renderer[] resolvedFemaleEyeRenderers;
        private Renderer[] resolvedMaleEyeRenderers;

        public CharacterMorphController Controller => controller;
        public CharacterSkinPalette SkinPalette => skinPalette;
        public CharacterEyeMaterialPalette EyePalette => eyePalette;
        public string SkinToneId => skinToneId;
        public bool UsesCustomSkinColor => usesCustomSkinColor;
        public Color CurrentSkinColor => ResolveSkinColor();
        public float SkinRoughness => skinRoughness;
        public string EyeMaterialId => string.IsNullOrWhiteSpace(eyeMaterialId)
            ? CharacterRecipe.DefaultEyeMaterialId
            : eyeMaterialId;

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

            CharacterEyeMaterialOption defaultEye = eyePalette != null ? eyePalette.GetDefault() : null;
            if (defaultEye != null)
            {
                eyeMaterialId = defaultEye.Id;
            }

            if (applyOnAwake && authoredPreset != null)
            {
                ApplyPreset(authoredPreset);
            }
            else
            {
                ApplySkin();
                ApplyEyeMaterial();
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
                EyeMaterialId,
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

            SetEyeMaterial(recipe.EyeMaterialId);
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

        public void SetSkinRoughness(float roughness)
        {
            skinRoughness = Mathf.Clamp01(roughness);
            ApplySkin();
        }

        public bool SetEyeMaterial(string materialId)
        {
            if (eyePalette == null || !eyePalette.TryGet(materialId, out CharacterEyeMaterialOption option))
            {
                Debug.LogWarning($"Unknown eye material '{materialId}'.", this);
                return false;
            }

            eyeMaterialId = option.Id;
            ApplyEyeMaterial();
            return true;
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

            if (eyePalette != null && eyePalette.Options.Count > 0)
            {
                CharacterEyeMaterialOption option = eyePalette.Options[random.Next(0, eyePalette.Options.Count)];
                if (option != null)
                {
                    SetEyeMaterial(option.Id);
                }
            }
        }

        public void RefreshSkin()
        {
            ApplySkin();
            ApplyEyeMaterial();
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
            float roughnessPropertyValue = invertRoughnessForSmoothness ? 1f - skinRoughness : skinRoughness;
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

                foreach (int propertyId in skinRoughnessPropertyIds)
                {
                    propertyBlock.SetFloat(propertyId, roughnessPropertyValue);
                }

                targetRenderer.SetPropertyBlock(propertyBlock);
                propertyBlock.Clear();
            }
        }

        private void ApplyEyeMaterial()
        {
            if (eyePalette == null || !eyePalette.TryGet(EyeMaterialId, out CharacterEyeMaterialOption option))
            {
                return;
            }

            Renderer[] femaleRenderers = ResolveEyeRenderers(
                femaleEyeRenderers,
                controller != null ? controller.FemaleRoot : null,
                FemaleEyeRendererName,
                ref resolvedFemaleEyeRenderers);
            Renderer[] maleRenderers = ResolveEyeRenderers(
                maleEyeRenderers,
                controller != null ? controller.MaleRoot : null,
                MaleEyeRendererName,
                ref resolvedMaleEyeRenderers);

            ApplyEyeMaterial(femaleRenderers, option.Material);
            ApplyEyeMaterial(maleRenderers, option.Material);
        }

        private static void ApplyEyeMaterial(Renderer[] renderers, Material material)
        {
            if (renderers == null || material == null)
            {
                return;
            }

            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer == null)
                {
                    continue;
                }

                Material[] materials = targetRenderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                materials[0] = material;
                targetRenderer.sharedMaterials = materials;
            }
        }

        private static Renderer[] ResolveEyeRenderers(
            Renderer[] authoredRenderers,
            GameObject root,
            string rendererName,
            ref Renderer[] resolvedRenderers)
        {
            if (HasAssignedRenderer(authoredRenderers))
            {
                return authoredRenderers;
            }

            if (HasAssignedRenderer(resolvedRenderers))
            {
                return resolvedRenderers;
            }

            if (root == null)
            {
                return Array.Empty<Renderer>();
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer candidate in renderers)
            {
                if (candidate != null && string.Equals(candidate.name, rendererName, StringComparison.Ordinal))
                {
                    resolvedRenderers = new[] { candidate };
                    return resolvedRenderers;
                }
            }

            resolvedRenderers = Array.Empty<Renderer>();
            return resolvedRenderers;
        }

        private static bool HasAssignedRenderer(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return false;
            }

            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer != null)
                {
                    return true;
                }
            }

            return false;
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

            if (skinRoughnessPropertyNames == null || skinRoughnessPropertyNames.Length == 0)
            {
                skinRoughnessPropertyNames = new[] { "_Smoothness", "_Glossiness" };
            }

            ids.Clear();
            foreach (string propertyName in skinRoughnessPropertyNames)
            {
                if (!string.IsNullOrWhiteSpace(propertyName))
                {
                    ids.Add(Shader.PropertyToID(propertyName.Trim()));
                }
            }

            if (ids.Count == 0)
            {
                ids.Add(Shader.PropertyToID("_Smoothness"));
            }

            skinRoughnessPropertyIds = ids.ToArray();
        }
    }
}
