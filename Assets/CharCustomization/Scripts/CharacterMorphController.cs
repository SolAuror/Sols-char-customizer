using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterMorphController : MonoBehaviour
    {
        [SerializeField] private GameObject femaleRoot;
        [SerializeField] private GameObject maleRoot;
        [SerializeField] private CharacterSex activeSex = CharacterSex.Female;

        private readonly Dictionary<CharacterSex, Dictionary<string, float>> recipes = new();
        private readonly Dictionary<CharacterSex, Dictionary<string, MorphBinding>> bindings = new();
        private bool initialized;

        public CharacterSex ActiveSex => activeSex;
        public Transform ActiveCharacterRoot => activeSex == CharacterSex.Female
            ? femaleRoot != null ? femaleRoot.transform : null
            : maleRoot != null ? maleRoot.transform : null;
        public IReadOnlyList<CharacterMorphDefinition> Definitions => CharacterMorphCatalog.Definitions;
        public IReadOnlyList<StatGrowthDefinition> StatGrowthDefinitions => CharacterStatGrowthCatalog.Definitions;

        private void Awake()
        {
            Initialize();
            SetSex(CharacterSex.Female);
        }

        public void SetSex(CharacterSex sex)
        {
            Initialize();
            activeSex = sex;

            if (femaleRoot != null)
            {
                femaleRoot.SetActive(sex == CharacterSex.Female);
            }

            if (maleRoot != null)
            {
                maleRoot.SetActive(sex == CharacterSex.Male);
            }

            ApplyRecipe(sex);
        }

        public void SetMorph(string morphId, float value)
        {
            Initialize();
            if (!CharacterMorphCatalog.TryGet(morphId, out CharacterMorphDefinition definition))
            {
                Debug.LogWarning($"Unknown character morph '{morphId}'.", this);
                return;
            }

            float clampedValue = Mathf.Clamp(value, definition.MinimumValue, 1f);
            recipes[activeSex][morphId] = clampedValue;
            ApplyMorph(activeSex, definition, clampedValue);
        }

        public float GetMorph(string morphId)
        {
            Initialize();
            return recipes[activeSex].TryGetValue(morphId, out float value) ? value : 0f;
        }

        public bool SavePreset(CharacterMorphPreset preset)
        {
            Initialize();
            if (preset == null)
            {
                Debug.LogWarning("Cannot save a null character morph preset.", this);
                return false;
            }

            preset.Overwrite(activeSex, CharacterMorphCatalog.Definitions, GetMorph);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(preset);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(preset);
#endif

            return true;
        }

        public bool LoadPreset(CharacterMorphPreset preset)
        {
            Initialize();
            if (preset == null)
            {
                Debug.LogWarning("Cannot load a null character morph preset.", this);
                return false;
            }

            if (!preset.HasValidIdentifiers(out string error))
            {
                Debug.LogWarning($"Cannot load character morph preset '{preset.name}'. {error}", preset);
                return false;
            }

            foreach (CharacterMorphPresetValue presetValue in preset.Values)
            {
                if (!CharacterMorphCatalog.TryGet(presetValue.MorphId, out _))
                {
                    Debug.LogWarning(
                        $"Preset '{preset.name}' contains unknown morph '{presetValue.MorphId}', which was ignored.",
                        preset);
                }
            }

            SetSex(preset.Sex);
            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                float value = preset.TryGetValue(definition.Id, out float savedValue) ? savedValue : 0f;
                SetMorph(definition.Id, value);
            }

            return true;
        }

        public void SetStatGrowth(string statId, float normalizedValue)
        {
            if (string.IsNullOrWhiteSpace(statId) ||
                !CharacterStatGrowthCatalog.TryGet(statId, out StatGrowthDefinition growthDefinition))
            {
                Debug.LogWarning($"Unknown character growth stat '{statId}'.", this);
                return;
            }

            SetMorph(growthDefinition.MorphId, growthDefinition.Evaluate(normalizedValue));
        }

        public void ResetCurrentCharacter()
        {
            Initialize();
            Dictionary<string, float> recipe = recipes[activeSex];
            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                recipe[definition.Id] = 0f;
                ApplyMorph(activeSex, definition, 0f);
            }
        }

        public bool ResetGroup(string groupId)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(groupId))
            {
                Debug.LogWarning("Cannot reset an empty character morph group.", this);
                return false;
            }

            bool foundGroup = false;
            Dictionary<string, float> recipe = recipes[activeSex];
            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                if (!string.Equals(definition.Group, groupId, StringComparison.Ordinal))
                {
                    continue;
                }

                foundGroup = true;
                recipe[definition.Id] = 0f;
                ApplyMorph(activeSex, definition, 0f);
            }

            if (!foundGroup)
            {
                Debug.LogWarning($"Unknown character morph group '{groupId}'.", this);
            }

            return foundGroup;
        }

        public bool IsMorphAvailable(string morphId)
        {
            Initialize();
            return bindings[activeSex].TryGetValue(morphId, out MorphBinding binding) && binding.IsComplete;
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            EnsureRecipe(CharacterSex.Female);
            EnsureRecipe(CharacterSex.Male);
            bindings[CharacterSex.Female] = BuildBindings(femaleRoot, CharacterSex.Female);
            bindings[CharacterSex.Male] = BuildBindings(maleRoot, CharacterSex.Male);

            // Scene-authored test values must not disagree with the zeroed recipes.
            ApplyRecipe(CharacterSex.Female);
            ApplyRecipe(CharacterSex.Male);
        }

        private void EnsureRecipe(CharacterSex sex)
        {
            if (!recipes.TryGetValue(sex, out Dictionary<string, float> recipe))
            {
                recipe = new Dictionary<string, float>(StringComparer.Ordinal);
                recipes.Add(sex, recipe);
            }

            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                recipe.TryAdd(definition.Id, 0f);
            }
        }

        private static Dictionary<string, MorphBinding> BuildBindings(GameObject root, CharacterSex sex)
        {
            var result = new Dictionary<string, MorphBinding>(StringComparer.Ordinal);
            if (root == null)
            {
                Debug.LogWarning($"No {sex} character root is assigned to the morph controller.");
                return result;
            }

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                string positiveName = definition.GetPositiveShape(sex);
                string negativeName = definition.GetNegativeShape(sex);
                var binding = new MorphBinding(definition.RequiresNegativeShape);

                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    Mesh mesh = renderer.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    int positiveIndex = mesh.GetBlendShapeIndex(positiveName);
                    int negativeIndex = string.IsNullOrEmpty(negativeName)
                        ? -1
                        : mesh.GetBlendShapeIndex(negativeName);

                    if (positiveIndex >= 0 || negativeIndex >= 0)
                    {
                        binding.Targets.Add(new BlendShapeTarget(renderer, positiveIndex, negativeIndex));
                    }

                    binding.HasPositive |= positiveIndex >= 0;
                    binding.HasNegative |= negativeIndex >= 0;
                }

                result.Add(definition.Id, binding);
                if (!binding.IsComplete)
                {
                    string expected = definition.RequiresNegativeShape
                        ? $"'{negativeName}' and '{positiveName}'"
                        : $"'{positiveName}'";
                    Debug.LogWarning($"{sex} morph '{definition.Id}' is unavailable. Expected {expected}.", root);
                }
            }

            return result;
        }

        private void ApplyRecipe(CharacterSex sex)
        {
            if (!recipes.TryGetValue(sex, out Dictionary<string, float> recipe))
            {
                return;
            }

            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                ApplyMorph(sex, definition, recipe[definition.Id]);
            }
        }

        private void ApplyMorph(CharacterSex sex, CharacterMorphDefinition definition, float value)
        {
            if (!bindings.TryGetValue(sex, out Dictionary<string, MorphBinding> sexBindings) ||
                !sexBindings.TryGetValue(definition.Id, out MorphBinding binding))
            {
                return;
            }

            definition.CalculateWeights(value, out float positiveWeight, out float negativeWeight);

            foreach (BlendShapeTarget target in binding.Targets)
            {
                if (target.PositiveIndex >= 0)
                {
                    target.Renderer.SetBlendShapeWeight(target.PositiveIndex, positiveWeight);
                }

                if (target.NegativeIndex >= 0)
                {
                    target.Renderer.SetBlendShapeWeight(target.NegativeIndex, negativeWeight);
                }
            }
        }

        private sealed class MorphBinding
        {
            private readonly bool isBipolar;

            public MorphBinding(bool isBipolar)
            {
                this.isBipolar = isBipolar;
            }

            public readonly List<BlendShapeTarget> Targets = new();
            public bool HasPositive;
            public bool HasNegative;
            public bool IsComplete => HasPositive && (!isBipolar || HasNegative);
        }

        private readonly struct BlendShapeTarget
        {
            public BlendShapeTarget(SkinnedMeshRenderer renderer, int positiveIndex, int negativeIndex)
            {
                Renderer = renderer;
                PositiveIndex = positiveIndex;
                NegativeIndex = negativeIndex;
            }

            public SkinnedMeshRenderer Renderer { get; }
            public int PositiveIndex { get; }
            public int NegativeIndex { get; }
        }
    }
}
