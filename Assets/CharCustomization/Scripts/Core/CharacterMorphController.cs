using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterMorphController : MonoBehaviour
    {
        public const string StatGrowthLayerPrefix = "stat.";

        [SerializeField] private GameObject femaleRoot;
        [SerializeField] private GameObject maleRoot;
        [SerializeField] private CharacterSex activeSex = CharacterCustomizationUiConfig.DefaultSex;
        [SerializeField] private CharacterMorphCatalogAsset catalogAsset;
        [SerializeField] private CharacterRigProportionDriver rigProportionDriver;
        [Tooltip("Adds a CharacterTwistBoneDriver to each character Animator in Play Mode, so exported twist bones follow the limbs.")]
        [SerializeField] private bool ensureTwistBoneDrivers = true;

        [Header("Joint Offsets")]
        [Tooltip("Joint offsets exported from Blender (.solrig) for the female character. With one, joints follow the reshaped mesh, and skeletal morphs that also exist as mesh shapes use the shapes.")]
        [SerializeField] private CharacterJointOffsetTable femaleJointOffsets;
        [Tooltip("Joint offsets exported from Blender (.solrig) for the male character.")]
        [SerializeField] private CharacterJointOffsetTable maleJointOffsets;

        private readonly Dictionary<CharacterSex, Dictionary<string, float>> recipes = new();
        private readonly Dictionary<CharacterSex, Dictionary<string, MorphBinding>> bindings = new();

        // Complete blendshape bindings for skeletal morphs whose shapes also exist on
        // the mesh, used instead of bone scaling when that sex has joint offsets.
        private readonly Dictionary<CharacterSex, Dictionary<string, MorphBinding>> meshShapeBindings = new();

        // Gameplay offsets keyed by layer ID, then morph ID. Layers sit on top of
        // the creator recipe and are never captured into it, so progression,
        // ageing, or injury can reshape the body without erasing the player's
        // authored appearance.
        private readonly Dictionary<string, Dictionary<string, float>> morphLayers = new(StringComparer.Ordinal);

        // Baked renderers show a mesh copy with the creator blendshapes folded in.
        // While baked, blendshape morph edits update the recipe only; rebaking
        // refreshes the meshes.
        private readonly List<BakedRenderer> bakedRenderers = new();
        private readonly Dictionary<string, float> bakedFoldWeights = new(StringComparer.Ordinal);
        private readonly HashSet<CharacterSex> releasedSexes = new();
        private CharacterSex bakedSex;
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
        public bool EnsuresTwistBoneDrivers => ensureTwistBoneDrivers;
        public bool IsBaked => bakedRenderers.Count > 0;
        public bool IsBakeStale => IsBaked && !FoldWeightsMatch(BuildFoldWeights(bakedSex), bakedFoldWeights);

        // The mesh a renderer had before BakeActiveCharacter folded its blendshapes in, or
        // its current mesh when it is not baked. Tattoos project onto this rest shape so
        // they land in the same place whether or not the character is baked.
        public Mesh GetUnbakedMesh(SkinnedMeshRenderer renderer)
        {
            foreach (BakedRenderer baked in bakedRenderers)
            {
                if (baked.Renderer == renderer)
                {
                    return baked.Source;
                }
            }

            return renderer != null ? renderer.sharedMesh : null;
        }

        private void Awake()
        {
            Initialize();
            SetSex(activeSex);
        }

        public void SetSex(CharacterSex sex)
        {
            Initialize();
            if (releasedSexes.Contains(sex))
            {
                Debug.LogWarning($"The {sex} character root was released and can no longer be shown.", this);
                return;
            }

            if (IsBaked && sex != bakedSex)
            {
                RestoreBakedMeshes();
            }

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

        public float GetEffectiveMorph(string morphId)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(morphId) ||
                !TryGetDefinition(morphId, out CharacterMorphDefinition definition))
            {
                return 0f;
            }

            return ResolveEffectiveValue(definition, GetMorph(morphId));
        }

        public void SetMorphLayerOffset(string layerId, string morphId, float offset)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(layerId))
            {
                Debug.LogWarning("Cannot set a morph offset on an empty layer ID.", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(morphId) ||
                !TryGetDefinition(morphId, out CharacterMorphDefinition definition))
            {
                Debug.LogWarning($"Unknown character morph '{morphId}'.", this);
                return;
            }

            if (!morphLayers.TryGetValue(layerId, out Dictionary<string, float> layer))
            {
                layer = new Dictionary<string, float>(StringComparer.Ordinal);
                morphLayers.Add(layerId, layer);
            }

            float range = 1f - definition.MinimumValue;
            layer[morphId] = Mathf.Clamp(offset, -range, range);
            ApplyMorph(activeSex, definition, GetMorph(morphId));
        }

        public bool TryGetMorphLayerOffset(string layerId, string morphId, out float offset)
        {
            offset = 0f;
            return !string.IsNullOrWhiteSpace(layerId) &&
                   !string.IsNullOrWhiteSpace(morphId) &&
                   morphLayers.TryGetValue(layerId, out Dictionary<string, float> layer) &&
                   layer.TryGetValue(morphId, out offset);
        }

        public bool ClearMorphLayer(string layerId)
        {
            Initialize();
            if (string.IsNullOrWhiteSpace(layerId) ||
                !morphLayers.TryGetValue(layerId, out Dictionary<string, float> layer))
            {
                return false;
            }

            morphLayers.Remove(layerId);
            foreach (string morphId in layer.Keys)
            {
                if (TryGetDefinition(morphId, out CharacterMorphDefinition definition))
                {
                    ApplyMorph(activeSex, definition, GetMorph(morphId));
                }
            }

            return true;
        }

        public void ClearAllMorphLayers()
        {
            Initialize();
            if (morphLayers.Count == 0)
            {
                return;
            }

            morphLayers.Clear();
            ApplyRecipe(activeSex);
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

            SetMorphLayerOffset(
                GetStatGrowthLayerId(growthDefinition.Id),
                growthDefinition.MorphId,
                growthDefinition.Evaluate(normalizedValue));
        }

        public bool ClearStatGrowth(string statId)
        {
            return !string.IsNullOrWhiteSpace(statId) && ClearMorphLayer(GetStatGrowthLayerId(statId));
        }

        public static string GetStatGrowthLayerId(string statId)
        {
            return StatGrowthLayerPrefix + statId;
        }

        public bool BakeActiveCharacter(out string error)
        {
            Initialize();
            Transform root = ActiveCharacterRoot;
            if (root == null)
            {
                error = $"No {activeSex} character root is assigned, so there is nothing to bake.";
                return false;
            }

            // Rebaking always starts from the source meshes.
            RestoreBakedMeshes();
            if (rigProportionDriver != null && rigProportionDriver.IsAvatarRebuildPending)
            {
                rigProportionDriver.RebuildAvatarNow();
            }

            Dictionary<string, float> foldWeights = BuildFoldWeights(activeSex);
            var baked = new List<BakedRenderer>();
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh source = renderer.sharedMesh;
                if (!CharacterMeshBaker.UsesAnyShape(source, foldWeights))
                {
                    continue;
                }

                if (!CharacterMeshBaker.TryAcquireShared(source, foldWeights, out Mesh bakedMesh, out error))
                {
                    RestoreRenderers(baked);
                    ApplyRecipe(activeSex);
                    return false;
                }

                Dictionary<string, float> keptWeights = CaptureWeightsByName(renderer);
                renderer.sharedMesh = bakedMesh;
                ApplyWeightsByName(renderer, keptWeights);

                CharacterBakedMeshHandle handle = renderer.GetComponent<CharacterBakedMeshHandle>();
                if (handle == null)
                {
                    handle = renderer.gameObject.AddComponent<CharacterBakedMeshHandle>();
                }

                handle.Track(bakedMesh);
                baked.Add(new BakedRenderer(renderer, source, handle));
            }

            if (baked.Count == 0)
            {
                error = $"No skinned renderer under '{root.name}' uses catalog blendshapes.";
                return false;
            }

            bakedRenderers.AddRange(baked);
            bakedSex = activeSex;
            foreach (KeyValuePair<string, float> entry in foldWeights)
            {
                bakedFoldWeights[entry.Key] = entry.Value;
            }

            RebindActiveAnimator();
            error = null;
            return true;
        }

        public void UnbakeActiveCharacter()
        {
            RestoreBakedMeshes();
        }

        // Destroys the hidden sex's character root so a finished NPC carries one rig.
        public bool ReleaseInactiveSexRoot()
        {
            Initialize();
            CharacterSex inactiveSex = activeSex == CharacterSex.Female ? CharacterSex.Male : CharacterSex.Female;
            GameObject inactiveRoot = inactiveSex == CharacterSex.Female ? femaleRoot : maleRoot;
            if (inactiveRoot == null)
            {
                return false;
            }

            if (inactiveSex == CharacterSex.Female)
            {
                femaleRoot = null;
            }
            else
            {
                maleRoot = null;
            }

            bindings[inactiveSex].Clear();
            meshShapeBindings[inactiveSex].Clear();
            releasedSexes.Add(inactiveSex);
            if (Application.isPlaying)
            {
                Destroy(inactiveRoot);
            }
            else
            {
                DestroyImmediate(inactiveRoot);
            }

            return true;
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

            if (TryGetMeshShapeBinding(activeSex, definition, out _))
            {
                return true;
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

            if (femaleRoot == null && !releasedSexes.Contains(CharacterSex.Female))
            {
                errors.Add("No female character root is assigned.");
            }

            if (maleRoot == null && !releasedSexes.Contains(CharacterSex.Male))
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
            bindings[CharacterSex.Female] = BuildBindings(
                femaleRoot, CharacterSex.Female, Definitions, out Dictionary<string, MorphBinding> femaleMeshShapes);
            bindings[CharacterSex.Male] = BuildBindings(
                maleRoot, CharacterSex.Male, Definitions, out Dictionary<string, MorphBinding> maleMeshShapes);
            meshShapeBindings[CharacterSex.Female] = femaleMeshShapes;
            meshShapeBindings[CharacterSex.Male] = maleMeshShapes;
            if (rigProportionDriver == null)
            {
                rigProportionDriver = GetComponent<CharacterRigProportionDriver>();
            }

            if (ensureTwistBoneDrivers && Application.isPlaying)
            {
                EnsureTwistBoneDriver(femaleRoot);
                EnsureTwistBoneDriver(maleRoot);
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
            IReadOnlyList<CharacterMorphDefinition> definitions,
            out Dictionary<string, MorphBinding> meshShapes)
        {
            var result = new Dictionary<string, MorphBinding>(StringComparer.Ordinal);
            meshShapes = new Dictionary<string, MorphBinding>(StringComparer.Ordinal);
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
                    MorphBinding meshShape = BuildBlendShapeBinding(renderers, definition, sex);
                    if (meshShape.IsComplete)
                    {
                        meshShapes.Add(definition.Id, meshShape);
                    }

                    continue;
                }

                MorphBinding binding = BuildBlendShapeBinding(renderers, definition, sex);
                result.Add(definition.Id, binding);
                if (definition.VisibleInCreator && !binding.IsComplete)
                {
                    string positiveName = definition.GetPositiveShape(sex);
                    string negativeName = definition.GetNegativeShape(sex);
                    string expected = definition.RequiresNegativeShape
                        ? $"'{negativeName}' and '{positiveName}'"
                        : $"'{positiveName}'";
                    Debug.LogWarning($"{sex} morph '{definition.Id}' is unavailable. Expected {expected}.", root);
                }
            }

            return result;
        }

        private static MorphBinding BuildBlendShapeBinding(
            SkinnedMeshRenderer[] renderers,
            CharacterMorphDefinition definition,
            CharacterSex sex)
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

            return binding;
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

        private float ResolveEffectiveValue(CharacterMorphDefinition definition, float baseValue)
        {
            float value = baseValue;
            foreach (Dictionary<string, float> layer in morphLayers.Values)
            {
                if (layer.TryGetValue(definition.Id, out float offset))
                {
                    value += offset;
                }
            }

            return Mathf.Clamp(value, definition.MinimumValue, 1f);
        }

        // Callers pass the creator recipe value; gameplay layers are folded in here
        // so every apply path (edit, reset, recipe load, sex switch) honours them.
        private void ApplyMorph(CharacterSex sex, CharacterMorphDefinition definition, float baseValue)
        {
            float value = ResolveEffectiveValue(definition, baseValue);
            bool meshShapeRoute = TryGetMeshShapeBinding(sex, definition, out MorphBinding binding);
            if (definition.IsSkeletalDriven && sex == activeSex)
            {
                BindRigProportionDriver();
                if (rigProportionDriver != null)
                {
                    // With joint offsets the mesh shape carries the change and the
                    // joints follow it, so the bone-scaling channel stays neutral.
                    rigProportionDriver.SetMorph(definition, meshShapeRoute ? 0f : value);
                }
            }

            if (definition.IsSkeletalDriven && !meshShapeRoute)
            {
                return;
            }

            if (IsBaked && sex == bakedSex)
            {
                return;
            }

            if (!meshShapeRoute &&
                (!bindings.TryGetValue(sex, out Dictionary<string, MorphBinding> sexBindings) ||
                 !sexBindings.TryGetValue(definition.Id, out binding)))
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

            if (sex == activeSex && rigProportionDriver != null)
            {
                rigProportionDriver.MarkJointOffsetsDirty();
            }
        }

        public bool UsesMeshShapes(string morphId)
        {
            Initialize();
            return TryGetDefinition(morphId, out CharacterMorphDefinition definition) &&
                   TryGetMeshShapeBinding(activeSex, definition, out _);
        }

        public CharacterJointOffsetTable GetJointOffsetTable(CharacterSex sex)
        {
            return sex == CharacterSex.Female ? femaleJointOffsets : maleJointOffsets;
        }

        // Skeletal morphs switch to their mesh shapes only when that sex has joint
        // offsets, since moving the mesh without moving the joints would tear skinning.
        private bool TryGetMeshShapeBinding(CharacterSex sex, CharacterMorphDefinition definition, out MorphBinding binding)
        {
            binding = null;
            return definition.IsSkeletalDriven &&
                   GetJointOffsetTable(sex) != null &&
                   meshShapeBindings.TryGetValue(sex, out Dictionary<string, MorphBinding> sexBindings) &&
                   sexBindings.TryGetValue(definition.Id, out binding);
        }

        // The driver initializes in Awake, which runs immediately on an active root and
        // on first activation otherwise, before that Animator has evaluated.
        private static void EnsureTwistBoneDriver(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.GetComponent<CharacterTwistBoneDriver>() != null)
            {
                return;
            }

            // An inactive Animator may not resolve its humanoid bones yet, so inactive
            // roots get the driver and detect their twist bones once shown.
            var bindings = new List<TwistBoneBinding>();
            if (!animator.gameObject.activeInHierarchy ||
                CharacterTwistBoneDriver.FindHumanoidTwistBones(animator, bindings) > 0)
            {
                animator.gameObject.AddComponent<CharacterTwistBoneDriver>();
            }
        }

        private void RestoreBakedMeshes()
        {
            if (!IsBaked)
            {
                return;
            }

            RestoreRenderers(bakedRenderers);
            bakedRenderers.Clear();
            bakedFoldWeights.Clear();
            ApplyRecipe(bakedSex);
            RebindActiveAnimator();
        }

        private static void RestoreRenderers(List<BakedRenderer> states)
        {
            foreach (BakedRenderer state in states)
            {
                if (state.Renderer != null)
                {
                    Dictionary<string, float> keptWeights = CaptureWeightsByName(state.Renderer);
                    state.Renderer.sharedMesh = state.Source;
                    ApplyWeightsByName(state.Renderer, keptWeights);
                }

                if (state.Handle != null)
                {
                    state.Handle.Release();
                }
            }
        }

        // Animated blendshape curves bind to mesh shape indices, which change when
        // meshes are swapped, so the Animator is rebound with its state carried over.
        private void RebindActiveAnimator()
        {
            if (!TryGetActiveAnimator(out Animator animator) || animator.runtimeAnimatorController == null)
            {
                return;
            }

            AnimatorStateSnapshot snapshot = AnimatorStateSnapshot.Capture(animator);
            animator.Rebind();
            if (rigProportionDriver != null)
            {
                rigProportionDriver.ApplyNow();
            }

            snapshot.Restore(animator);
        }

        private Dictionary<string, float> BuildFoldWeights(CharacterSex sex)
        {
            var weights = new Dictionary<string, float>(StringComparer.Ordinal);
            if (!recipes.TryGetValue(sex, out Dictionary<string, float> recipe))
            {
                return weights;
            }

            foreach (CharacterMorphDefinition definition in Definitions)
            {
                if (!definition.IsBlendShapeDriven && !TryGetMeshShapeBinding(sex, definition, out _))
                {
                    continue;
                }

                float baseValue = recipe.TryGetValue(definition.Id, out float value) ? value : 0f;
                definition.CalculateWeights(
                    ResolveEffectiveValue(definition, baseValue),
                    out float positiveWeight,
                    out float negativeWeight);
                AddFoldWeight(weights, definition.GetPositiveShape(sex), positiveWeight);
                AddFoldWeight(weights, definition.GetNegativeShape(sex), negativeWeight);
            }

            return weights;
        }

        private static void AddFoldWeight(Dictionary<string, float> weights, string shapeName, float weight)
        {
            if (string.IsNullOrEmpty(shapeName))
            {
                return;
            }

            weights[shapeName] = weights.TryGetValue(shapeName, out float existing) ? existing + weight : weight;
        }

        private static bool FoldWeightsMatch(Dictionary<string, float> current, Dictionary<string, float> baked)
        {
            if (current.Count != baked.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, float> entry in current)
            {
                if (!baked.TryGetValue(entry.Key, out float bakedWeight) || Mathf.Abs(bakedWeight - entry.Value) > 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, float> CaptureWeightsByName(SkinnedMeshRenderer renderer)
        {
            var weights = new Dictionary<string, float>(StringComparer.Ordinal);
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                return weights;
            }

            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                weights[mesh.GetBlendShapeName(index)] = renderer.GetBlendShapeWeight(index);
            }

            return weights;
        }

        private static void ApplyWeightsByName(SkinnedMeshRenderer renderer, Dictionary<string, float> weights)
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                return;
            }

            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                if (weights.TryGetValue(mesh.GetBlendShapeName(index), out float weight))
                {
                    renderer.SetBlendShapeWeight(index, weight);
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
            rigProportionDriver.Bind(ActiveCharacterRoot, animator, GetJointOffsetTable(activeSex));
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

        private readonly struct BakedRenderer
        {
            public BakedRenderer(SkinnedMeshRenderer renderer, Mesh source, CharacterBakedMeshHandle handle)
            {
                Renderer = renderer;
                Source = source;
                Handle = handle;
            }

            public SkinnedMeshRenderer Renderer { get; }
            public Mesh Source { get; }
            public CharacterBakedMeshHandle Handle { get; }
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
