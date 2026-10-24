using System;
using System.Collections.Generic;
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

        [Header("NPC Bake")]
        [Tooltip("On Start, folds body blendshapes into a shared baked mesh. For NPCs whose appearance does not change; call BakeAppearance again after later edits.")]
        [SerializeField] private bool bakeOnStart;
        [Tooltip("Destroys the hidden sex's character root when baking, so each NPC carries one rig.")]
        [SerializeField] private bool releaseInactiveSexOnBake = true;

        [Header("Skin")]
        [SerializeField] private CharacterSkinPalette skinPalette;
        [Tooltip("Skin colour space for Sol/Character Skin materials. Empty uses the built-in human model.")]
        [SerializeField] private CharacterSkinProfile skinProfile;
        [SerializeField] private string[] skinColorPropertyNames = { "_BaseColor", "_Color" };
        [SerializeField] private string[] skinRoughnessPropertyNames = { "_Smoothness", "_Glossiness" };
        [SerializeField] private bool invertRoughnessForSmoothness = true;
        [Tooltip("In Play Mode, gives each character its own copy of Sol/Character Skin materials and writes skin values to it, keeping those renderers in the SRP Batcher. Other materials use property blocks.")]
        [SerializeField] private bool instanceSkinMaterials = true;
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
        private CharacterSkinParameters skinParameters = CharacterSkinParameters.Default;
        private readonly Dictionary<Color, CharacterSkinParameters> matchedSkinParameters = new();
        private readonly Dictionary<Renderer, Material[]> skinMaterialInstances = new();
        private string eyeMaterialId = CharacterRecipe.DefaultEyeMaterialId;
        private Renderer[] resolvedFemaleEyeRenderers;
        private Renderer[] resolvedMaleEyeRenderers;

        public CharacterMorphController Controller => controller;
        public CharacterSkinPalette SkinPalette => skinPalette;
        public CharacterEyeMaterialPalette EyePalette => eyePalette;
        public string SkinToneId => skinToneId;
        public bool UsesCustomSkinColor => usesCustomSkinColor;
        public Color CurrentSkinColor => ResolveSkinColor();
        public CharacterSkinProfile SkinProfile => skinProfile;
        public CharacterSkinParameters SkinParameters => skinParameters;
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
                skinParameters = ResolveToneParameters(defaultTone);
            }
            else if (skinProfile != null)
            {
                skinParameters = skinProfile.DefaultParameters;
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

        // Start runs after every Awake, so the controller and rig driver are bound and
        // any authored preset has been applied before the bake.
        private void Start()
        {
            if (bakeOnStart && !BakeAppearance(releaseInactiveSexOnBake, out string error))
            {
                Debug.LogWarning($"Could not bake character appearance. {error}", this);
            }
        }

        public bool BakeAppearance(bool releaseInactiveSex, out string error)
        {
            if (controller == null)
            {
                error = "A character profile cannot bake without a morph controller.";
                return false;
            }

            if (!controller.BakeActiveCharacter(out error))
            {
                return false;
            }

            if (releaseInactiveSex)
            {
                controller.ReleaseInactiveSexRoot();
            }

            return true;
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
            recipe.SetSkinParameters(skinParameters);
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

            // Version 3 recipes carry exact skin parameters; older ones keep the values
            // matched from their tone or custom colour above.
            if (recipe.HasSkinParameters)
            {
                skinParameters = recipe.SkinParameters;
                ApplySkin();
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
            skinParameters = ResolveToneParameters(tone);
            ApplySkin();
            return true;
        }

        // Sets skin colour directly in the profile's colour space. The matching colour
        // is kept as the custom colour so non-skin-shader materials and colour
        // swatches stay in step.
        public void SetSkinParameters(CharacterSkinParameters parameters)
        {
            skinParameters = new CharacterSkinParameters(parameters.Melanin, parameters.Hemoglobin, parameters.Undertone);
            usesCustomSkinColor = true;
            customSkinColor = EvaluateSkinColor(skinParameters);
            ApplySkin();
        }

        // sRGB colour of a parameter set without regional variation, for UI swatches.
        public Color EvaluateSkinColor(CharacterSkinParameters parameters)
        {
            Color linear = skinProfile != null
                ? skinProfile.EvaluateLinear(parameters)
                : SkinColorModel.EvaluateLinear(parameters);
            return linear.gamma;
        }

        public void SetCustomSkinColor(Color color)
        {
            usesCustomSkinColor = true;
            customSkinColor = new Color(
                Mathf.Clamp01(color.r),
                Mathf.Clamp01(color.g),
                Mathf.Clamp01(color.b),
                Mathf.Clamp01(color.a));
            skinParameters = MatchSkinParameters(customSkinColor);
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

        private CharacterSkinParameters ResolveToneParameters(CharacterSkinTone tone)
        {
            return tone.HasParameters ? tone.Parameters : MatchSkinParameters(tone.Color);
        }

        // Colour matching searches the whole colour space, so results are cached per
        // colour; palettes and saved swatches repeat the same few colours.
        private CharacterSkinParameters MatchSkinParameters(Color srgbColor)
        {
            srgbColor.a = 1f;
            if (matchedSkinParameters.TryGetValue(srgbColor, out CharacterSkinParameters cached))
            {
                return cached;
            }

            CharacterSkinParameters matched = skinProfile != null
                ? skinProfile.FindClosest(srgbColor)
                : SkinColorModel.FindClosest(srgbColor.linear);
            matchedSkinParameters[srgbColor] = matched;
            return matched;
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

                if (TryGetSkinMaterialInstances(targetRenderer, out Material[] instances))
                {
                    foreach (Material instance in instances)
                    {
                        ApplySkinMaterial(instance, roughnessPropertyValue);
                    }

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

                // Read only by Sol/Character Skin materials; other shaders ignore them.
                propertyBlock.SetFloat(SolSkinShaderIds.Melanin, skinParameters.Melanin);
                propertyBlock.SetFloat(SolSkinShaderIds.Hemoglobin, skinParameters.Hemoglobin);
                propertyBlock.SetFloat(SolSkinShaderIds.Undertone, skinParameters.Undertone);
                if (skinProfile != null)
                {
                    skinProfile.ApplyTo(propertyBlock);
                }

                targetRenderer.SetPropertyBlock(propertyBlock);
                propertyBlock.Clear();
            }
        }

        private void ApplySkinMaterial(Material material, float roughnessPropertyValue)
        {
            material.SetFloat(SolSkinShaderIds.Melanin, skinParameters.Melanin);
            material.SetFloat(SolSkinShaderIds.Hemoglobin, skinParameters.Hemoglobin);
            material.SetFloat(SolSkinShaderIds.Undertone, skinParameters.Undertone);
            foreach (int propertyId in skinRoughnessPropertyIds)
            {
                if (material.HasProperty(propertyId))
                {
                    material.SetFloat(propertyId, roughnessPropertyValue);
                }
            }

            if (skinProfile != null)
            {
                skinProfile.ApplyTo(material);
            }
        }

        // Replaces each Sol/Character Skin material on the renderer with a
        // per-character copy, once. Renderers without one keep property blocks.
        private bool TryGetSkinMaterialInstances(Renderer targetRenderer, out Material[] instances)
        {
            if (skinMaterialInstances.TryGetValue(targetRenderer, out instances))
            {
                return instances.Length > 0;
            }

            instances = Array.Empty<Material>();
            if (!instanceSkinMaterials || !Application.isPlaying)
            {
                return false;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            var created = new List<Material>();
            for (int index = 0; index < materials.Length; index++)
            {
                if (!SolSkinShaderIds.IsSkinMaterial(materials[index]))
                {
                    continue;
                }

                materials[index] = new Material(materials[index])
                {
                    name = $"{materials[index].name} ({name})"
                };
                created.Add(materials[index]);
            }

            if (created.Count > 0)
            {
                targetRenderer.sharedMaterials = materials;
                targetRenderer.SetPropertyBlock(null);
                instances = created.ToArray();
            }

            skinMaterialInstances[targetRenderer] = instances;
            return instances.Length > 0;
        }

        private void OnDestroy()
        {
            foreach (KeyValuePair<Renderer, Material[]> entry in skinMaterialInstances)
            {
                // A character that outlives its profile, such as after the host game
                // destroys the customization manager, keeps its skin materials.
                if (entry.Key != null)
                {
                    continue;
                }

                foreach (Material instance in entry.Value)
                {
                    if (instance != null)
                    {
                        Destroy(instance);
                    }
                }
            }

            skinMaterialInstances.Clear();
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
