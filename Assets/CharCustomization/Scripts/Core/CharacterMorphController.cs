using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterMorphController : MonoBehaviour
    {
        [SerializeField] private GameObject femaleRoot;
        [SerializeField] private GameObject maleRoot;
        [SerializeField] private CharacterSex activeSex = CharacterCustomizationUiConfig.DefaultSex;
        [SerializeField] private CharacterMorphCatalogAsset catalogAsset;
        [SerializeField] private CharacterRigProportionDriver rigProportionDriver;

        private readonly Dictionary<CharacterSex, Dictionary<string, float>> recipes = new();
        private readonly Dictionary<CharacterSex, Dictionary<string, MorphBinding>> bindings = new();
        private bool initialized;

        public CharacterSex ActiveSex => activeSex;
        public GameObject FemaleRoot => femaleRoot;
        public GameObject MaleRoot => maleRoot;
        public CharacterMorphCatalogAsset CatalogAsset => catalogAsset;
        public Transform ActiveCharacterRoot => activeSex == CharacterSex.Female
            ? femaleRoot != null ? femaleRoot.transform : null
            : maleRoot != null ? maleRoot.transform : null;
        public Animator ActiveAnimator => TryGetActiveAnimator(out Animator animator) ? animator : null;
        public CharacterRigProportionDriver RigProportionDriver => rigProportionDriver;
        public IReadOnlyList<CharacterMorphDefinition> Definitions => catalogAsset != null
            ? catalogAsset.Definitions
            : CharacterMorphCatalog.Definitions;
        public IReadOnlyList<StatGrowthDefinition> StatGrowthDefinitions => CharacterStatGrowthCatalog.Definitions;

        private void Awake()
        {
            Initialize();
            SetSex(activeSex);
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

            BindRigProportionDriver();
            ApplyRecipe(sex);
        }

        public void SetMorph(string morphId, float value)
        {
            Initialize();
            if (!TryGetDefinition(morphId, out CharacterMorphDefinition definition))
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

        public bool TryGetDefinition(string morphId, out CharacterMorphDefinition definition)
        {
            if (catalogAsset != null)
            {
                return catalogAsset.TryGet(morphId, out definition);
            }

            return CharacterMorphCatalog.TryGet(morphId, out definition);
        }

        public IReadOnlyList<CharacterMorphValue> CaptureMorphValues()
        {
            Initialize();
            IReadOnlyList<CharacterMorphDefinition> definitions = Definitions;
            var values = new List<CharacterMorphValue>(definitions.Count);
            foreach (CharacterMorphDefinition definition in definitions)
            {
                values.Add(new CharacterMorphValue(definition.Id, GetMorph(definition.Id)));
            }

            return values;
        }

        public bool ApplyMorphValues(IReadOnlyList<CharacterMorphValue> values)
        {
            Initialize();
            if (values == null)
            {
                Debug.LogWarning("Cannot apply a null morph value collection.", this);
                return false;
            }

            var savedValues = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (CharacterMorphValue savedValue in values)
            {
                if (string.IsNullOrWhiteSpace(savedValue.MorphId) ||
                    !savedValues.TryAdd(savedValue.MorphId, savedValue.Value))
                {
                    Debug.LogWarning($"Ignored an empty or duplicate morph ID '{savedValue.MorphId}'.", this);
                }
            }

            foreach (CharacterMorphDefinition definition in Definitions)
            {
                float value = savedValues.TryGetValue(definition.Id, out float savedValue) ? savedValue : 0f;
                SetMorph(definition.Id, value);
            }

            foreach (string savedId in savedValues.Keys)
            {
                if (!TryGetDefinition(savedId, out _))
                {
                    Debug.LogWarning($"Ignored unknown character morph '{savedId}'.", this);
                }
            }

            return true;
        }

        public void RandomizeCurrent(float rangeScale = 0.65f, System.Random random = null)
        {
            Initialize();
            random ??= new System.Random();
            float scale = Mathf.Clamp01(rangeScale);

            foreach (CharacterMorphDefinition definition in Definitions)
            {
                if (!definition.VisibleInCreator)
                {
                    continue;
                }

                float minimum = definition.MinimumValue < 0f ? definition.MinimumValue * scale : 0f;
                float maximum = scale;
                float value = Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
                SetMorph(definition.Id, value);
            }
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
            foreach (CharacterMorphDefinition definition in Definitions)
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
            foreach (CharacterMorphDefinition definition in Definitions)
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
            if (!TryGetDefinition(morphId, out CharacterMorphDefinition definition))
            {
                return false;
            }

            if (definition.IsSkeletalDriven)
            {
                return rigProportionDriver != null && rigProportionDriver.CanApply;
            }

            return bindings[activeSex].TryGetValue(morphId, out MorphBinding binding) && binding.IsComplete;
        }

        public bool TryGetActiveAnimator(out Animator animator)
        {
            animator = null;
            Transform activeRoot = ActiveCharacterRoot;
            if (activeRoot == null)
            {
                return false;
            }

            animator = activeRoot.GetComponentInChildren<Animator>(true);
            return animator != null;
        }

        public bool ValidateConfiguration(List<string> errors)
        {
            errors ??= new List<string>();
            if (catalogAsset != null && !catalogAsset.ValidateDefinitions(out string catalogError))
            {
                errors.Add(catalogError);
            }

            if (femaleRoot == null)
            {
                errors.Add("No female character root is assigned.");
            }

            if (maleRoot == null)
            {
                errors.Add("No male character root is assigned.");
            }

            if (HasAnySkeletalDefinition() && rigProportionDriver == null)
            {
                errors.Add($"Skeletal morphs require a {nameof(CharacterRigProportionDriver)} on the manager.");
            }

            ValidateBlendShapeBindings(errors, femaleRoot, CharacterSex.Female);
            ValidateBlendShapeBindings(errors, maleRoot, CharacterSex.Male);
            return errors.Count == 0;
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
            bindings[CharacterSex.Female] = BuildBindings(femaleRoot, CharacterSex.Female, Definitions);
            bindings[CharacterSex.Male] = BuildBindings(maleRoot, CharacterSex.Male, Definitions);
            if (rigProportionDriver == null)
            {
                rigProportionDriver = GetComponent<CharacterRigProportionDriver>();
            }

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

            foreach (CharacterMorphDefinition definition in Definitions)
            {
                recipe.TryAdd(definition.Id, 0f);
            }
        }

        private static Dictionary<string, MorphBinding> BuildBindings(
            GameObject root,
            CharacterSex sex,
            IReadOnlyList<CharacterMorphDefinition> definitions)
        {
            var result = new Dictionary<string, MorphBinding>(StringComparer.Ordinal);
            if (root == null)
            {
                Debug.LogWarning($"No {sex} character root is assigned to the morph controller.");
                return result;
            }

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (CharacterMorphDefinition definition in definitions)
            {
                if (definition.IsSkeletalDriven)
                {
                    result.Add(definition.Id, MorphBinding.Skeletal);
                    continue;
                }

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
                if (definition.VisibleInCreator && !binding.IsComplete)
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

            foreach (CharacterMorphDefinition definition in Definitions)
            {
                ApplyMorph(sex, definition, recipe[definition.Id]);
            }
        }

        private void ApplyMorph(CharacterSex sex, CharacterMorphDefinition definition, float value)
        {
            if (definition.IsSkeletalDriven)
            {
                if (sex != activeSex)
                {
                    return;
                }

                BindRigProportionDriver();
                rigProportionDriver?.SetMorph(definition, value);
                return;
            }

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

        private void BindRigProportionDriver()
        {
            if (rigProportionDriver == null)
            {
                return;
            }

            TryGetActiveAnimator(out Animator animator);
            rigProportionDriver.Bind(ActiveCharacterRoot, animator);
        }

        private bool HasAnySkeletalDefinition()
        {
            foreach (CharacterMorphDefinition definition in Definitions)
            {
                if (definition.IsSkeletalDriven)
                {
                    return true;
                }
            }

            return false;
        }

        private void ValidateBlendShapeBindings(List<string> errors, GameObject root, CharacterSex sex)
        {
            if (root == null)
            {
                return;
            }

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (CharacterMorphDefinition definition in Definitions)
            {
                if (!definition.IsBlendShapeDriven || !definition.VisibleInCreator)
                {
                    continue;
                }

                string positiveName = definition.GetPositiveShape(sex);
                string negativeName = definition.GetNegativeShape(sex);
                bool hasPositive = false;
                bool hasNegative = !definition.RequiresNegativeShape;
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    Mesh mesh = renderer.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    hasPositive |= mesh.GetBlendShapeIndex(positiveName) >= 0;
                    hasNegative |= !string.IsNullOrEmpty(negativeName) && mesh.GetBlendShapeIndex(negativeName) >= 0;
                }

                if (!hasPositive || !hasNegative)
                {
                    errors.Add($"{sex} morph '{definition.Id}' is missing expected blendshape binding.");
                }
            }
        }

        private sealed class MorphBinding
        {
            public static readonly MorphBinding Skeletal = new(false, true);

            private readonly bool isBipolar;
            private readonly bool isSkeletal;

            public MorphBinding(bool isBipolar)
                : this(isBipolar, false)
            {
            }

            private MorphBinding(bool isBipolar, bool isSkeletal)
            {
                this.isBipolar = isBipolar;
                this.isSkeletal = isSkeletal;
            }

            public readonly List<BlendShapeTarget> Targets = new();
            public bool HasPositive;
            public bool HasNegative;
            public bool IsComplete => isSkeletal || HasPositive && (!isBipolar || HasNegative);
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
