using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterMorphDemoUI : MonoBehaviour
    {
        [SerializeField] private CharacterMorphController controller;
        [SerializeField] private CharacterProfile profile;
        [SerializeField] private RectTransform content;
        [SerializeField] private Button femaleButton;
        [SerializeField] private Button maleButton;
        [SerializeField] private Image femaleButtonImage;
        [SerializeField] private Image maleButtonImage;
        [SerializeField] private CharacterMorphSliderRow sliderRowTemplate;
        [SerializeField] private ScrollRect tabRailScrollRect;
        [SerializeField] private RectTransform tabRailContent;
        [SerializeField] private ScrollRect mainSliderScrollRect;
        [SerializeField] private CharacterPresetLibrary presetLibrary;
        [SerializeField] private string presetSavePathOverride;
        [SerializeField] private GameObject presetPanel;
        [SerializeField] private TMP_InputField presetNameInput;
        [SerializeField] private TMP_Dropdown presetDropdown;
        [SerializeField] private Button savePresetButton;
        [SerializeField] private Button loadPresetButton;
        [SerializeField] private Button deletePresetButton;
        [SerializeField] private Button resetAllButton;
        [SerializeField] private Button resetGroupButton;
        [SerializeField] private TMP_Text resetGroupButtonLabel;
        [SerializeField] private GameObject skinPanel;
        [SerializeField] private string customSkinSavePathOverride;
        [SerializeField] private ScrollRect skinSwatchScrollRect;
        [SerializeField] private RectTransform skinSwatchContent;
        [SerializeField] private CharacterSkinSwatchButton[] skinSwatches = Array.Empty<CharacterSkinSwatchButton>();
        [SerializeField] private CharacterSkinSwatchButton savedSkinSwatchTemplate;
        [SerializeField] private Button addSkinButton;
        [SerializeField] private Button customColorToggleButton;
        [SerializeField] private GameObject customColorPanel;
        [SerializeField] private Slider hueSlider;
        [SerializeField] private Slider saturationSlider;
        [SerializeField] private Slider valueSlider;
        [SerializeField] private Image customColorPreview;
        [SerializeField] private CharacterMorphTabButton[] tabButtons = Array.Empty<CharacterMorphTabButton>();
        [SerializeField] private Color selectedTabColor = new(0.82f, 0.73f, 0.55f, 1f);
        [SerializeField] private Color inactiveTabColor = new(0.27f, 0.27f, 0.27f, 1f);

        private readonly Dictionary<string, CharacterMorphSliderRow> rows = new(StringComparer.Ordinal);
        private readonly Dictionary<CharacterMorphTabButton, UnityAction> tabListeners = new();
        private readonly Dictionary<CharacterSkinSwatchButton, UnityAction> skinListeners = new();
        private readonly Dictionary<CharacterSkinSwatchButton, UnityAction> skinDeleteListeners = new();
        private readonly Dictionary<CharacterSkinSwatchButton, RuntimeSkinColorRecord> savedSkinSwatches = new();
        private readonly List<CharacterSkinSwatchButton> runtimeSkinSwatches = new();
        private readonly List<PresetOption> availablePresets = new();
        private ICharacterPresetSaveRepository presetRepository;
        private ICharacterSkinColorSaveRepository customSkinRepository;
        private string pendingPresetOverwriteName;
        private bool initialized;
        private bool listenersRegistered;
        private bool refreshingSkin;
        private string selectedGroup = CharacterCustomizationUiConfig.DefaultMorphGroupId;

        private static readonly Color AccentColor = new(0.82f, 0.73f, 0.55f, 1f);
        private static readonly Color InactiveColor = new(0.27f, 0.27f, 0.27f, 1f);

        public event Action<RuntimeCharacterPresetRecord> RuntimePresetSaved;
        public event Action<string, CharacterRecipe> PresetLoaded;

        public ICharacterPresetSaveRepository PresetRepository => presetRepository;
        public ICharacterSkinColorSaveRepository CustomSkinRepository => customSkinRepository;

        private void Start()
        {
            Initialize();
        }

        private void OnEnable()
        {
            if (initialized)
            {
                RegisterListeners();
            }
        }

        private void OnDisable()
        {
            UnregisterListeners();
        }

        private void OnDestroy()
        {
            UnregisterListeners();

            foreach (CharacterMorphSliderRow row in rows.Values)
            {
                row.Unbind();
            }
        }

        public CharacterMorphSliderRow CreateSliderForMorph(string morphId)
        {
            if (!CharacterMorphCatalog.TryGet(morphId, out CharacterMorphDefinition definition))
            {
                Debug.LogWarning($"Cannot create UI for unknown character morph '{morphId}'.", this);
                return null;
            }

            if (rows.TryGetValue(morphId, out CharacterMorphSliderRow existingRow))
            {
                return existingRow;
            }

            if (content == null || sliderRowTemplate == null || !sliderRowTemplate.IsConfigured)
            {
                Debug.LogError($"Cannot create the missing '{morphId}' slider because no valid row template is assigned.", this);
                return null;
            }

            CharacterMorphSliderRow row = Instantiate(sliderRowTemplate, content);
            row.name = morphId;
            row.SetMorphId(morphId);
            row.gameObject.SetActive(definition.Group == selectedGroup);
            PlaceInCatalogOrder(row);

            if (!row.Bind(controller, definition))
            {
                Debug.LogError($"The fallback slider for '{morphId}' could not be bound.", row);
                row.gameObject.SetActive(false);
                return null;
            }

            rows.Add(morphId, row);
            Debug.LogWarning($"Created a runtime slider for missing prefab row '{morphId}'.", row);
            return row;
        }

        public void SetPresetRepository(ICharacterPresetSaveRepository repository)
        {
            presetRepository = repository;
            if (initialized)
            {
                RefreshPresetOptions();
            }
        }

        public void SetCustomSkinRepository(ICharacterSkinColorSaveRepository repository)
        {
            customSkinRepository = repository;
            if (initialized)
            {
                ReloadSavedSkinSwatches();
                RefreshSkinPanel();
            }
        }

        public bool TrySaveCurrentPreset(
            string presetName,
            bool overwriteExisting,
            out RuntimeCharacterPresetRecord savedRecord,
            out bool duplicateName,
            out string error)
        {
            EnsurePresetRepository();
            savedRecord = null;
            duplicateName = false;

            if (profile == null)
            {
                error = "The character profile is not assigned.";
                return false;
            }

            CharacterRecipe recipe = profile.CaptureRecipe();
            if (!presetRepository.TrySavePreset(
                    presetName,
                    recipe,
                    overwriteExisting,
                    out savedRecord,
                    out duplicateName,
                    out error))
            {
                return false;
            }

            RuntimePresetSaved?.Invoke(savedRecord);
            return true;
        }

        public bool TryApplyPresetRecipe(string presetName, CharacterRecipe recipe, out string error)
        {
            if (profile == null || recipe == null)
            {
                error = "A character profile and recipe are required before a preset can be loaded.";
                return false;
            }

            if (!profile.ApplyRecipe(recipe))
            {
                error = $"Preset '{presetName}' could not be applied.";
                return false;
            }

            if (presetNameInput != null)
            {
                presetNameInput.SetTextWithoutNotify(presetName);
            }

            RefreshPanel();
            PresetLoaded?.Invoke(presetName, recipe);
            error = null;
            return true;
        }

        public void RefreshPanel()
        {
            if (!initialized)
            {
                return;
            }

            bool isFemale = controller.ActiveSex == CharacterSex.Female;
            femaleButtonImage.color = isFemale ? AccentColor : InactiveColor;
            maleButtonImage.color = isFemale ? InactiveColor : AccentColor;

            foreach (CharacterMorphSliderRow row in rows.Values)
            {
                if (row.gameObject.activeSelf)
                {
                    row.Refresh();
                }
            }

            RefreshSkinPanel();
        }

        public void RefreshSkinPanel()
        {
            if (profile == null || hueSlider == null || saturationSlider == null ||
                valueSlider == null || customColorPreview == null)
            {
                return;
            }

            refreshingSkin = true;
            foreach (CharacterSkinSwatchButton swatch in EnumerateSkinSwatches())
            {
                bool selected = IsSkinSwatchSelected(swatch);
                swatch.transform.localScale = selected ? Vector3.one * 1.08f : Vector3.one;
            }

            Color color = profile.CurrentSkinColor;
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            hueSlider.SetValueWithoutNotify(hue);
            saturationSlider.SetValueWithoutNotify(saturation);
            valueSlider.SetValueWithoutNotify(value);
            customColorPreview.color = color;
            refreshingSkin = false;
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            TryRepairReferences();

            if (!HasRequiredReferences())
            {
                Debug.LogError("The prefab-first character morph UI has incomplete references.", this);
                return;
            }

            selectedGroup = CharacterCustomizationUiConfig.DefaultMorphGroupId;
            EnsurePresetRepository();
            EnsureCustomSkinRepository();
            skinSwatches = GetConfiguredAuthoredSkinSwatches();
            CacheAuthoredRows();

            foreach (CharacterMorphDefinition definition in controller.Definitions)
            {
                if (!rows.ContainsKey(definition.Id))
                {
                    CreateSliderForMorph(definition.Id);
                }
            }

            initialized = true;
            RefreshPresetOptions();
            LoadSavedSkinSwatches();
            customColorPanel.SetActive(false);
            RegisterListeners();
            controller.SetSex(controller.ActiveSex);
            ApplySelectedGroup(false);
            RefreshPanel();
        }

        private bool HasRequiredReferences()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            AddMissing(errors, controller == null, "controller");
            AddMissing(errors, profile == null, "profile");
            AddMissing(errors, content == null, "content");
            AddMissing(errors, femaleButton == null, "femaleButton");
            AddMissing(errors, maleButton == null, "maleButton");
            AddMissing(errors, femaleButtonImage == null, "femaleButtonImage");
            AddMissing(errors, maleButtonImage == null, "maleButtonImage");
            AddMissing(errors, sliderRowTemplate == null, "sliderRowTemplate");
            AddMissing(errors, tabRailScrollRect == null, "tabRailScrollRect");
            AddMissing(errors, tabRailContent == null, "tabRailContent");
            AddMissing(errors, mainSliderScrollRect == null, "mainSliderScrollRect");
            AddMissing(errors, presetLibrary == null, "presetLibrary");
            AddMissing(errors, presetPanel == null, "presetPanel");
            AddMissing(errors, presetDropdown == null, "presetDropdown");
            AddMissing(errors, savePresetButton == null, "savePresetButton");
            AddMissing(errors, loadPresetButton == null, "loadPresetButton");
            AddMissing(warnings, deletePresetButton == null, "deletePresetButton");
            AddMissing(warnings, resetAllButton == null, "resetAllButton");
            AddMissing(errors, resetGroupButton == null, "resetGroupButton");
            AddMissing(errors, resetGroupButtonLabel == null, "resetGroupButtonLabel");
            AddMissing(errors, skinPanel == null, "skinPanel");
            AddMissing(errors, skinSwatchScrollRect == null, "skinSwatchScrollRect");
            AddMissing(errors, skinSwatchContent == null, "skinSwatchContent");
            AddMissing(errors, addSkinButton == null, "addSkinButton");
            AddMissing(errors, customColorToggleButton == null, "customColorToggleButton");
            AddMissing(errors, customColorPanel == null, "customColorPanel");
            AddMissing(errors, hueSlider == null, "hueSlider");
            AddMissing(errors, saturationSlider == null, "saturationSlider");
            AddMissing(errors, valueSlider == null, "valueSlider");
            AddMissing(errors, customColorPreview == null, "customColorPreview");
            ValidateSkinSwatches(errors);
            ValidateTabs(errors);

            if (warnings.Count > 0)
            {
                Debug.LogWarning($"The character morph UI is missing optional references: {string.Join("; ", warnings)}", this);
            }

            if (errors.Count > 0)
            {
                Debug.LogError($"The character morph UI has incomplete references: {string.Join("; ", errors)}", this);
                return false;
            }

            return true;
        }

        private void TryRepairReferences()
        {
            RepairNamedComponent(ref femaleButton, "femaleButton", "Female Button");
            RepairNamedComponent(ref maleButton, "maleButton", "Male Button");
            RepairNamedComponent(ref femaleButtonImage, "femaleButtonImage", "Female Button");
            RepairNamedComponent(ref maleButtonImage, "maleButtonImage", "Male Button");
            RepairNamedComponent(ref sliderRowTemplate, "sliderRowTemplate", "Slider Row Template");
            RepairNamedComponent(ref tabRailScrollRect, "tabRailScrollRect", "Tab Rail");
            RepairNamedComponent(ref mainSliderScrollRect, "mainSliderScrollRect", "ScrollPanel");
            RepairNamedComponent(ref presetDropdown, "presetDropdown", "Preset Dropdown");
            RepairNamedComponent(ref savePresetButton, "savePresetButton", "Save Preset Button");
            RepairNamedComponent(ref loadPresetButton, "loadPresetButton", "Load Preset Button");
            RepairNamedComponent(ref deletePresetButton, "deletePresetButton", "Delete Preset Button", "Delete Current", "Delete Current Button");
            RepairNamedComponent(ref resetAllButton, "resetAllButton", "Reset All Button");
            RepairNamedComponent(ref resetGroupButton, "resetGroupButton", "Reset Tab Button", "Reset Group Button");
            RepairNamedComponent(ref skinSwatchScrollRect, "skinSwatchScrollRect", "Skin Scroll View");
            RepairNamedComponent(ref addSkinButton, "addSkinButton", "Add Skin");
            RepairNamedComponent(ref customColorToggleButton, "customColorToggleButton", "Custom Colour Button", "Custom Color Button");
            RepairNamedComponent(ref hueSlider, "hueSlider", "Hue Slider");
            RepairNamedComponent(ref saturationSlider, "saturationSlider", "Saturation Slider");
            RepairNamedComponent(ref valueSlider, "valueSlider", "Value Slider");
            RepairNamedComponent(ref customColorPreview, "customColorPreview", "Custom Colour Preview", "Custom Color Preview");
            RepairNamedGameObject(ref presetPanel, "presetPanel", "Preset Panel");
            RepairNamedGameObject(ref skinPanel, "skinPanel", "Skin Panel");
            RepairNamedGameObject(ref customColorPanel, "customColorPanel", "Advanced Custom Colour Panel", "Advanced Custom Color Panel");

            if (content == null && mainSliderScrollRect != null && mainSliderScrollRect.content != null)
            {
                content = mainSliderScrollRect.content;
                Debug.LogWarning($"Repaired missing content reference from authored ScrollRect '{mainSliderScrollRect.name}'.", this);
            }

            if (tabRailContent == null && tabRailScrollRect != null && tabRailScrollRect.content != null)
            {
                tabRailContent = tabRailScrollRect.content;
                Debug.LogWarning($"Repaired missing tabRailContent reference from authored ScrollRect '{tabRailScrollRect.name}'.", this);
            }

            if (skinSwatchContent == null && skinSwatchScrollRect != null && skinSwatchScrollRect.content != null)
            {
                skinSwatchContent = skinSwatchScrollRect.content;
                Debug.LogWarning($"Repaired missing skinSwatchContent reference from authored ScrollRect '{skinSwatchScrollRect.name}'.", this);
            }

            if (resetGroupButtonLabel == null && resetGroupButton != null)
            {
                resetGroupButtonLabel = resetGroupButton.GetComponentInChildren<TMP_Text>(true);
                if (resetGroupButtonLabel != null)
                {
                    Debug.LogWarning($"Repaired missing resetGroupButtonLabel reference from authored Button '{resetGroupButton.name}'.", this);
                }
            }
        }

        private void RepairNamedComponent<T>(ref T reference, string label, params string[] objectNames)
            where T : Component
        {
            if (reference != null)
            {
                return;
            }

            if (TryFindUniqueNamedComponent(objectNames, out T found))
            {
                reference = found;
                Debug.LogWarning($"Repaired missing {label} reference from authored object '{found.gameObject.name}'.", this);
            }
        }

        private void RepairNamedGameObject(ref GameObject reference, string label, params string[] objectNames)
        {
            if (reference != null)
            {
                return;
            }

            if (TryFindUniqueNamedComponent(objectNames, out RectTransform found))
            {
                reference = found.gameObject;
                Debug.LogWarning($"Repaired missing {label} reference from authored object '{found.gameObject.name}'.", this);
            }
        }

        private bool TryFindUniqueNamedComponent<T>(IReadOnlyList<string> objectNames, out T match)
            where T : Component
        {
            match = null;
            T[] candidates = GetComponentsInChildren<T>(true);
            foreach (T candidate in candidates)
            {
                if (candidate == null || !MatchesAnyName(candidate.gameObject.name, objectNames))
                {
                    continue;
                }

                if (match != null)
                {
                    return false;
                }

                match = candidate;
            }

            return match != null;
        }

        private static bool MatchesAnyName(string candidateName, IReadOnlyList<string> objectNames)
        {
            for (int index = 0; index < objectNames.Count; index++)
            {
                if (string.Equals(candidateName, objectNames[index], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsurePresetRepository()
        {
            presetRepository ??= new CharacterPresetSaveRepository(presetSavePathOverride);
        }

        private void EnsureCustomSkinRepository()
        {
            customSkinRepository ??= new CharacterSkinColorSaveRepository(customSkinSavePathOverride);
        }

        private static void AddMissing(List<string> errors, bool missing, string label)
        {
            if (missing)
            {
                errors.Add(label);
            }
        }

        private void ValidateSkinSwatches(List<string> errors)
        {
            if (skinSwatches == null || skinSwatches.Length == 0)
            {
                errors.Add("skinSwatches has no entries");
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            int configuredCount = 0;
            CharacterSkinPalette palette = profile != null ? profile.SkinPalette : null;
            for (int index = 0; index < skinSwatches.Length; index++)
            {
                CharacterSkinSwatchButton swatch = skinSwatches[index];
                if (swatch == null)
                {
                    continue;
                }

                if (IsAddSkinSwatch(swatch))
                {
                    continue;
                }

                if (!swatch.IsConfigured)
                {
                    errors.Add($"skinSwatches[{index}] '{swatch.name}' is incomplete");
                    continue;
                }

                configuredCount++;
                if (!ids.Add(swatch.SkinToneId))
                {
                    errors.Add($"skinSwatches contains duplicate tone '{swatch.SkinToneId}'");
                }

                if (palette != null && !palette.TryGet(swatch.SkinToneId, out _))
                {
                    errors.Add($"skinSwatches[{index}] '{swatch.name}' references unknown tone '{swatch.SkinToneId}'");
                }
            }

            if (configuredCount == 0)
            {
                errors.Add("skinSwatches has no configured entries");
            }
        }

        private void ValidateTabs(List<string> errors)
        {
            IReadOnlyList<string> expectedTabs = CharacterCustomizationUiConfig.TabGroups;
            if (tabButtons == null)
            {
                errors.Add("tabButtons is unassigned");
                return;
            }

            if (tabButtons.Length != expectedTabs.Count)
            {
                errors.Add($"tabButtons has {tabButtons.Length} entries but {expectedTabs.Count} are required");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < tabButtons.Length; index++)
            {
                CharacterMorphTabButton tab = tabButtons[index];
                if (tab == null)
                {
                    errors.Add($"tabButtons[{index}] is unassigned");
                    continue;
                }

                if (!tab.IsConfigured)
                {
                    errors.Add($"tabButtons[{index}] '{tab.name}' is incomplete");
                    continue;
                }

                if (!ids.Add(tab.GroupId))
                {
                    errors.Add($"tabButtons contains duplicate group '{tab.GroupId}'");
                }

                if (!CharacterCustomizationUiConfig.IsKnownGroup(tab.GroupId))
                {
                    errors.Add($"tabButtons[{index}] references unknown group '{tab.GroupId}'");
                }

                if (index < expectedTabs.Count &&
                    !string.Equals(tab.GroupId, expectedTabs[index], StringComparison.Ordinal))
                {
                    errors.Add($"tabButtons[{index}] should be '{expectedTabs[index]}' but is '{tab.GroupId}'");
                }
            }
        }

        private CharacterSkinSwatchButton[] GetConfiguredAuthoredSkinSwatches()
        {
            if (skinSwatches == null)
            {
                return Array.Empty<CharacterSkinSwatchButton>();
            }

            var swatches = new List<CharacterSkinSwatchButton>(skinSwatches.Length);
            foreach (CharacterSkinSwatchButton swatch in skinSwatches)
            {
                if (swatch != null && swatch.IsConfigured && !IsAddSkinSwatch(swatch))
                {
                    swatches.Add(swatch);
                }
            }

            return swatches.ToArray();
        }

        private void LoadSavedSkinSwatches(bool registerListenersForNewSwatches = false)
        {
            EnsureCustomSkinRepository();
            if (!customSkinRepository.TryLoad(out CharacterSkinColorSaveData data, out string error))
            {
                Debug.LogWarning(error, this);
                return;
            }

            foreach (RuntimeSkinColorRecord record in data.Colors)
            {
                if (record != null && FindSavedSkinSwatch(record.Id) == null)
                {
                    AddSavedSkinSwatch(record, registerListenersForNewSwatches, false);
                }
            }
        }

        private void ReloadSavedSkinSwatches()
        {
            foreach (CharacterSkinSwatchButton swatch in runtimeSkinSwatches)
            {
                if (swatch != null)
                {
                    if (skinListeners.TryGetValue(swatch, out UnityAction listener) && swatch.Button != null)
                    {
                        swatch.Button.onClick.RemoveListener(listener);
                        skinListeners.Remove(swatch);
                    }

                    if (skinDeleteListeners.TryGetValue(swatch, out UnityAction deleteListener) &&
                        swatch.DeleteButton != null)
                    {
                        swatch.DeleteButton.onClick.RemoveListener(deleteListener);
                        skinDeleteListeners.Remove(swatch);
                    }

                    Destroy(swatch.gameObject);
                }
            }

            runtimeSkinSwatches.Clear();
            savedSkinSwatches.Clear();
            LoadSavedSkinSwatches(listenersRegistered);
        }

        private CharacterSkinSwatchButton AddSavedSkinSwatch(
            RuntimeSkinColorRecord record,
            bool registerListener,
            bool refreshLayout)
        {
            CharacterSkinSwatchButton template = GetSavedSkinSwatchTemplate();
            if (template == null || record == null)
            {
                return null;
            }

            Transform parent = skinSwatchContent != null ? skinSwatchContent : template.transform.parent;
            CharacterSkinSwatchButton swatch = Instantiate(template, parent);
            swatch.name = $"Saved Skin {runtimeSkinSwatches.Count + 1}";
            swatch.gameObject.SetActive(true);
            swatch.transform.localScale = Vector3.one;
            swatch.ConfigureRuntime(GetSavedSkinToneId(record.Id), record.Label, record.Color);
            swatch.SetDeleteVisible(true);
            InsertBeforeAddSkin(swatch.transform);

            runtimeSkinSwatches.Add(swatch);
            savedSkinSwatches[swatch] = record;
            if (registerListener)
            {
                RegisterSkinSwatchListener(swatch);
            }

            if (refreshLayout)
            {
                RefreshSkinSwatchContentHeight();
            }

            return swatch;
        }

        private CharacterSkinSwatchButton GetSavedSkinSwatchTemplate()
        {
            if (savedSkinSwatchTemplate != null && savedSkinSwatchTemplate.IsConfigured)
            {
                return savedSkinSwatchTemplate;
            }

            if (skinSwatches != null)
            {
                for (int index = skinSwatches.Length - 1; index >= 0; index--)
                {
                    CharacterSkinSwatchButton swatch = skinSwatches[index];
                    if (swatch != null && swatch.IsConfigured && !IsAddSkinSwatch(swatch))
                    {
                        return swatch;
                    }
                }
            }

            return null;
        }

        private CharacterSkinSwatchButton FindSavedSkinSwatch(string recordId)
        {
            foreach (KeyValuePair<CharacterSkinSwatchButton, RuntimeSkinColorRecord> pair in savedSkinSwatches)
            {
                if (pair.Value != null && string.Equals(pair.Value.Id, recordId, StringComparison.Ordinal))
                {
                    return pair.Key;
                }
            }

            return null;
        }

        private void InsertBeforeAddSkin(Transform swatchTransform)
        {
            if (swatchTransform == null || addSkinButton == null)
            {
                return;
            }

            int addSkinIndex = addSkinButton.transform.GetSiblingIndex();
            if (swatchTransform.GetSiblingIndex() != addSkinIndex)
            {
                swatchTransform.SetSiblingIndex(addSkinIndex);
            }
        }

        private void RefreshSkinSwatchContentHeight()
        {
            if (skinSwatchContent == null)
            {
                return;
            }

            float requiredHeight = 0f;
            if (skinSwatchContent.TryGetComponent(out GridLayoutGroup grid))
            {
                int activeChildren = 0;
                for (int index = 0; index < skinSwatchContent.childCount; index++)
                {
                    if (skinSwatchContent.GetChild(index).gameObject.activeSelf)
                    {
                        activeChildren++;
                    }
                }

                int columns = grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount
                    ? Mathf.Max(1, grid.constraintCount)
                    : Mathf.Max(1, activeChildren);
                int rows = activeChildren == 0 ? 0 : Mathf.CeilToInt(activeChildren / (float)columns);
                requiredHeight = grid.padding.top + grid.padding.bottom +
                                 rows * grid.cellSize.y +
                                 Mathf.Max(0, rows - 1) * grid.spacing.y;
            }

            float viewportHeight = skinSwatchScrollRect != null && skinSwatchScrollRect.viewport != null
                ? skinSwatchScrollRect.viewport.rect.height
                : 0f;
            float height = Mathf.Max(viewportHeight, requiredHeight);
            skinSwatchContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            LayoutRebuilder.ForceRebuildLayoutImmediate(skinSwatchContent);
        }

        private IEnumerable<CharacterSkinSwatchButton> EnumerateSkinSwatches()
        {
            if (skinSwatches != null)
            {
                foreach (CharacterSkinSwatchButton swatch in skinSwatches)
                {
                    if (swatch != null && swatch.IsConfigured && !IsAddSkinSwatch(swatch))
                    {
                        yield return swatch;
                    }
                }
            }

            foreach (CharacterSkinSwatchButton swatch in runtimeSkinSwatches)
            {
                if (swatch != null && swatch.IsConfigured)
                {
                    yield return swatch;
                }
            }
        }

        private bool IsSkinSwatchSelected(CharacterSkinSwatchButton swatch)
        {
            if (swatch == null)
            {
                return false;
            }

            if (savedSkinSwatches.TryGetValue(swatch, out RuntimeSkinColorRecord savedRecord))
            {
                return profile.UsesCustomSkinColor &&
                       CharacterSkinColorSaveRepository.ColorsMatch(savedRecord.Color, profile.CurrentSkinColor);
            }

            return !profile.UsesCustomSkinColor &&
                   string.Equals(swatch.SkinToneId, profile.SkinToneId, StringComparison.Ordinal);
        }

        private bool IsAddSkinSwatch(CharacterSkinSwatchButton swatch)
        {
            return swatch != null && addSkinButton != null && swatch.Button == addSkinButton;
        }

        private static string GetSavedSkinToneId(string recordId)
        {
            return $"saved_skin_{recordId}";
        }

        private void CacheAuthoredRows()
        {
            rows.Clear();
            CharacterMorphSliderRow[] authoredRows = content.GetComponentsInChildren<CharacterMorphSliderRow>(true);
            foreach (CharacterMorphSliderRow row in authoredRows)
            {
                if (row == sliderRowTemplate)
                {
                    continue;
                }

                if (!row.IsConfigured || string.IsNullOrWhiteSpace(row.MorphId))
                {
                    Debug.LogWarning($"Disabled invalid morph UI row '{row.name}'.", row);
                    row.gameObject.SetActive(false);
                    continue;
                }

                if (!CharacterMorphCatalog.TryGet(row.MorphId, out CharacterMorphDefinition definition))
                {
                    Debug.LogWarning($"Disabled UI row with unknown morph ID '{row.MorphId}'.", row);
                    row.gameObject.SetActive(false);
                    continue;
                }

                if (rows.ContainsKey(row.MorphId))
                {
                    Debug.LogWarning($"Disabled duplicate morph UI row '{row.MorphId}'.", row);
                    row.gameObject.SetActive(false);
                    continue;
                }

                if (!row.Bind(controller, definition))
                {
                    Debug.LogWarning($"Disabled morph UI row '{row.MorphId}' because it could not be bound.", row);
                    row.gameObject.SetActive(false);
                    continue;
                }

                rows.Add(row.MorphId, row);
            }
        }

        private void SelectFemale()
        {
            SelectSex(CharacterSex.Female);
        }

        private void SelectMale()
        {
            SelectSex(CharacterSex.Male);
        }

        private void SelectSex(CharacterSex sex)
        {
            controller.SetSex(sex);
            profile.RefreshSkin();
            RefreshPanel();
        }

        private void ResetAll()
        {
            controller.ResetCurrentCharacter();
            RefreshPanel();
        }

        private void ResetSelectedGroup()
        {
            if (controller.ResetGroup(selectedGroup))
            {
                RefreshPanel();
            }
        }

        private void SavePreset()
        {
            TMP_InputField nameInput = GetPresetNameSource();
            string presetName = nameInput != null ? nameInput.text.Trim() : string.Empty;
            bool confirmOverwrite = string.Equals(
                pendingPresetOverwriteName,
                presetName,
                StringComparison.OrdinalIgnoreCase);

            if (!TrySaveCurrentPreset(
                    presetName,
                    confirmOverwrite,
                    out RuntimeCharacterPresetRecord savedPreset,
                    out bool duplicateName,
                    out string error))
            {
                if (duplicateName)
                {
                    pendingPresetOverwriteName = presetName;
                    SetSavePresetButtonLabel("Confirm Overwrite");
                    Debug.LogWarning(
                        $"A saved preset named '{presetName}' already exists. Select Save Preset again to overwrite it.",
                        this);
                }
                else
                {
                    ClearPresetOverwriteConfirmation();
                    Debug.LogWarning(error, this);
                }

                return;
            }

            ClearPresetOverwriteConfirmation();
            if (presetNameInput != null)
            {
                presetNameInput.SetTextWithoutNotify(savedPreset.PresetName);
            }

            RefreshPresetOptions(savedPreset.Id);
        }

        private void LoadPreset()
        {
            if (!TryGetSelectedPreset(out PresetOption preset) || preset.IsNone)
            {
                Debug.LogWarning("Select a character morph preset before loading.", this);
                return;
            }

            if (!TryApplyPresetRecipe(preset.PresetName, preset.Recipe, out string error))
            {
                Debug.LogWarning(error, this);
            }
        }

        private void DeleteSelectedPreset()
        {
            if (!TryGetSelectedPreset(out PresetOption preset) || !preset.IsRuntime)
            {
                Debug.LogWarning("Select a saved preset before deleting.", this);
                RefreshPresetControls();
                return;
            }

            EnsurePresetRepository();
            if (!presetRepository.TryDeletePreset(preset.RuntimeId, out string deletedPresetName, out string error))
            {
                Debug.LogWarning(error, this);
                RefreshPresetControls();
                return;
            }

            if (presetNameInput != null &&
                string.Equals(presetNameInput.text, deletedPresetName, StringComparison.OrdinalIgnoreCase))
            {
                presetNameInput.SetTextWithoutNotify(string.Empty);
            }

            ClearPresetOverwriteConfirmation();
            RefreshPresetOptions();
        }

        private void RefreshPresetOptions(string selectedRuntimePresetId = null)
        {
            availablePresets.Clear();
            availablePresets.Add(PresetOption.None);
            foreach (CharacterPreset preset in presetLibrary.Presets)
            {
                if (preset != null)
                {
                    availablePresets.Add(PresetOption.FromAuthored(preset));
                }
            }

            EnsurePresetRepository();
            if (presetRepository.TryLoad(out CharacterPresetSaveData savedData, out string error))
            {
                foreach (RuntimeCharacterPresetRecord preset in savedData.Presets)
                {
                    if (preset != null)
                    {
                        availablePresets.Add(PresetOption.FromRuntime(preset));
                    }
                }
            }
            else
            {
                Debug.LogWarning(error, this);
            }

            presetDropdown.ClearOptions();

            var options = new List<string>(availablePresets.Count);
            int selectedIndex = 0;
            for (int index = 0; index < availablePresets.Count; index++)
            {
                PresetOption preset = availablePresets[index];
                options.Add(preset.OptionLabel);
                if (preset.IsRuntime &&
                    string.Equals(preset.RuntimeId, selectedRuntimePresetId, StringComparison.Ordinal))
                {
                    selectedIndex = index;
                }
            }

            presetDropdown.AddOptions(options);
            presetDropdown.SetValueWithoutNotify(selectedIndex);
            presetDropdown.RefreshShownValue();
            RefreshPresetControls();
        }

        private void SelectTab(string groupId)
        {
            if (!IsKnownGroup(groupId))
            {
                Debug.LogWarning($"Cannot select unknown morph group '{groupId}'.", this);
                return;
            }

            selectedGroup = groupId;
            ApplySelectedGroup(true);
        }

        private void ApplySelectedGroup(bool resetScroll)
        {
            bool showingPresets = CharacterCustomizationUiConfig.IsPresetGroup(selectedGroup);
            bool showingSkin = CharacterCustomizationUiConfig.IsSkinGroup(selectedGroup);
            bool showingMorphs = !showingPresets && !showingSkin;
            presetPanel.SetActive(showingPresets);
            skinPanel.SetActive(showingSkin);
            mainSliderScrollRect.gameObject.SetActive(showingMorphs);
            resetGroupButton.gameObject.SetActive(showingMorphs);
            if (showingMorphs)
            {
                resetGroupButtonLabel.text = $"Reset {selectedGroup}";
            }

            foreach (KeyValuePair<string, CharacterMorphSliderRow> pair in rows)
            {
                bool visible = showingMorphs &&
                               CharacterMorphCatalog.TryGet(pair.Key, out CharacterMorphDefinition definition) &&
                               definition.Group == selectedGroup;
                pair.Value.gameObject.SetActive(visible);
                if (visible)
                {
                    pair.Value.Refresh();
                }
            }

            foreach (CharacterMorphTabButton tab in tabButtons)
            {
                if (tab != null)
                {
                    tab.SetSelected(tab.GroupId == selectedGroup, selectedTabColor, inactiveTabColor);
                }
            }

            if (resetScroll && showingMorphs)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                mainSliderScrollRect.StopMovement();
                mainSliderScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void PlaceInCatalogOrder(CharacterMorphSliderRow row)
        {
            int rowOrder = GetCatalogOrder(row.MorphId);
            int insertionIndex = sliderRowTemplate != null ? sliderRowTemplate.transform.GetSiblingIndex() : content.childCount;

            for (int index = 0; index < content.childCount; index++)
            {
                Transform child = content.GetChild(index);
                if (child == row.transform || !child.TryGetComponent(out CharacterMorphSliderRow existingRow) ||
                    existingRow == sliderRowTemplate)
                {
                    continue;
                }

                if (GetCatalogOrder(existingRow.MorphId) > rowOrder)
                {
                    insertionIndex = index;
                    break;
                }
            }

            row.transform.SetSiblingIndex(insertionIndex);
        }

        private void RegisterListeners()
        {
            if (listenersRegistered)
            {
                return;
            }

            femaleButton?.onClick.AddListener(SelectFemale);
            maleButton?.onClick.AddListener(SelectMale);
            savePresetButton?.onClick.AddListener(SavePreset);
            loadPresetButton?.onClick.AddListener(LoadPreset);
            deletePresetButton?.onClick.AddListener(DeleteSelectedPreset);
            presetDropdown?.onValueChanged.AddListener(OnPresetSelectionChanged);
            resetAllButton?.onClick.AddListener(ResetAll);
            resetGroupButton?.onClick.AddListener(ResetSelectedGroup);
            GetPresetNameSource()?.onValueChanged.AddListener(OnPresetNameChanged);
            addSkinButton?.onClick.AddListener(SaveCurrentSkinColor);
            customColorToggleButton?.onClick.AddListener(ToggleCustomColorPanel);
            hueSlider?.onValueChanged.AddListener(OnCustomColorChanged);
            saturationSlider?.onValueChanged.AddListener(OnCustomColorChanged);
            valueSlider?.onValueChanged.AddListener(OnCustomColorChanged);

            foreach (CharacterSkinSwatchButton swatch in EnumerateSkinSwatches())
            {
                RegisterSkinSwatchListener(swatch);
            }

            foreach (CharacterMorphTabButton tab in tabButtons)
            {
                if (tab == null || !tab.IsConfigured || tabListeners.ContainsKey(tab))
                {
                    continue;
                }

                CharacterMorphTabButton capturedTab = tab;
                UnityAction listener = () => SelectTab(capturedTab.GroupId);
                capturedTab.Button.onClick.AddListener(listener);
                tabListeners.Add(capturedTab, listener);
            }

            listenersRegistered = true;
        }

        private void RegisterSkinSwatchListener(CharacterSkinSwatchButton swatch)
        {
            if (swatch == null || !swatch.IsConfigured || swatch.Button == null || skinListeners.ContainsKey(swatch))
            {
                return;
            }

            CharacterSkinSwatchButton capturedSwatch = swatch;
            UnityAction listener = () => SelectSkinSwatch(capturedSwatch);
            capturedSwatch.Button.onClick.AddListener(listener);
            skinListeners.Add(capturedSwatch, listener);

            if (savedSkinSwatches.ContainsKey(swatch) &&
                swatch.DeleteButton != null &&
                !skinDeleteListeners.ContainsKey(swatch))
            {
                UnityAction deleteListener = () => DeleteSavedSkinSwatch(capturedSwatch);
                capturedSwatch.DeleteButton.onClick.AddListener(deleteListener);
                skinDeleteListeners.Add(capturedSwatch, deleteListener);
            }
        }

        private void UnregisterListeners()
        {
            if (!listenersRegistered)
            {
                return;
            }

            femaleButton?.onClick.RemoveListener(SelectFemale);
            maleButton?.onClick.RemoveListener(SelectMale);
            savePresetButton?.onClick.RemoveListener(SavePreset);
            loadPresetButton?.onClick.RemoveListener(LoadPreset);
            deletePresetButton?.onClick.RemoveListener(DeleteSelectedPreset);
            presetDropdown?.onValueChanged.RemoveListener(OnPresetSelectionChanged);
            resetAllButton?.onClick.RemoveListener(ResetAll);
            resetGroupButton?.onClick.RemoveListener(ResetSelectedGroup);
            GetPresetNameSource()?.onValueChanged.RemoveListener(OnPresetNameChanged);
            addSkinButton?.onClick.RemoveListener(SaveCurrentSkinColor);
            customColorToggleButton?.onClick.RemoveListener(ToggleCustomColorPanel);
            hueSlider?.onValueChanged.RemoveListener(OnCustomColorChanged);
            saturationSlider?.onValueChanged.RemoveListener(OnCustomColorChanged);
            valueSlider?.onValueChanged.RemoveListener(OnCustomColorChanged);

            foreach (KeyValuePair<CharacterSkinSwatchButton, UnityAction> pair in skinListeners)
            {
                if (pair.Key != null && pair.Key.Button != null)
                {
                    pair.Key.Button.onClick.RemoveListener(pair.Value);
                }
            }

            skinListeners.Clear();

            foreach (KeyValuePair<CharacterSkinSwatchButton, UnityAction> pair in skinDeleteListeners)
            {
                if (pair.Key != null && pair.Key.DeleteButton != null)
                {
                    pair.Key.DeleteButton.onClick.RemoveListener(pair.Value);
                }
            }

            skinDeleteListeners.Clear();

            foreach (KeyValuePair<CharacterMorphTabButton, UnityAction> pair in tabListeners)
            {
                if (pair.Key != null && pair.Key.Button != null)
                {
                    pair.Key.Button.onClick.RemoveListener(pair.Value);
                }
            }

            tabListeners.Clear();
            listenersRegistered = false;
        }

        private static bool IsKnownGroup(string groupId)
        {
            return CharacterCustomizationUiConfig.IsKnownGroup(groupId);
        }

        private void OnPresetNameChanged(string _)
        {
            ClearPresetOverwriteConfirmation();
        }

        private void OnPresetSelectionChanged(int _)
        {
            ClearPresetOverwriteConfirmation();
            RefreshPresetControls();
        }

        private bool TryGetSelectedPreset(out PresetOption preset)
        {
            int selectedIndex = presetDropdown != null ? presetDropdown.value : -1;
            if (selectedIndex >= 0 && selectedIndex < availablePresets.Count)
            {
                preset = availablePresets[selectedIndex];
                return true;
            }

            preset = null;
            return false;
        }

        private void RefreshPresetControls()
        {
            bool hasPresetSelection = TryGetSelectedPreset(out PresetOption preset) && !preset.IsNone;
            if (presetDropdown != null)
            {
                presetDropdown.interactable = availablePresets.Count > 1;
            }

            if (loadPresetButton != null)
            {
                loadPresetButton.interactable = hasPresetSelection;
            }

            if (deletePresetButton != null)
            {
                deletePresetButton.interactable = hasPresetSelection && preset.IsRuntime;
            }
        }

        private void ClearPresetOverwriteConfirmation()
        {
            pendingPresetOverwriteName = null;
            SetSavePresetButtonLabel("Save Preset");
        }

        private void SetSavePresetButtonLabel(string label)
        {
            TMP_Text buttonLabel = savePresetButton != null
                ? savePresetButton.GetComponentInChildren<TMP_Text>(true)
                : null;
            if (buttonLabel != null)
            {
                buttonLabel.text = label;
            }
        }

        private TMP_InputField GetPresetNameSource()
        {
            if (presetNameInput != null)
            {
                return presetNameInput;
            }

            CharacterFinalizationFlow finalizationFlow = GetComponent<CharacterFinalizationFlow>();
            return finalizationFlow != null ? finalizationFlow.CharacterNameInput : null;
        }

        private void SelectSkinSwatch(CharacterSkinSwatchButton swatch)
        {
            if (swatch == null)
            {
                return;
            }

            if (savedSkinSwatches.TryGetValue(swatch, out RuntimeSkinColorRecord savedRecord))
            {
                profile.SetCustomSkinColor(savedRecord.Color);
                RefreshSkinPanel();
                return;
            }

            SelectSkinTone(swatch.SkinToneId);
        }

        private void SaveCurrentSkinColor()
        {
            EnsureCustomSkinRepository();
            if (!customSkinRepository.TrySaveColor(
                    profile.CurrentSkinColor,
                    out RuntimeSkinColorRecord savedRecord,
                    out _,
                    out string error))
            {
                Debug.LogWarning(error, this);
                return;
            }

            CharacterSkinSwatchButton swatch = FindSavedSkinSwatch(savedRecord.Id) ??
                                               AddSavedSkinSwatch(savedRecord, listenersRegistered, true);
            if (swatch != null)
            {
                SelectSkinSwatch(swatch);
                if (skinSwatchScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    skinSwatchScrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }

        private void DeleteSavedSkinSwatch(CharacterSkinSwatchButton swatch)
        {
            if (swatch == null || !savedSkinSwatches.TryGetValue(swatch, out RuntimeSkinColorRecord record))
            {
                Debug.LogWarning("Select a saved skin color before deleting.", this);
                return;
            }

            EnsureCustomSkinRepository();
            if (!customSkinRepository.TryDeleteColor(record.Id, out _, out string error))
            {
                Debug.LogWarning(error, this);
                return;
            }

            if (skinListeners.TryGetValue(swatch, out UnityAction listener) && swatch.Button != null)
            {
                swatch.Button.onClick.RemoveListener(listener);
                skinListeners.Remove(swatch);
            }

            if (skinDeleteListeners.TryGetValue(swatch, out UnityAction deleteListener) &&
                swatch.DeleteButton != null)
            {
                swatch.DeleteButton.onClick.RemoveListener(deleteListener);
                skinDeleteListeners.Remove(swatch);
            }

            runtimeSkinSwatches.Remove(swatch);
            savedSkinSwatches.Remove(swatch);
            Destroy(swatch.gameObject);
            RefreshSkinPanel();
            RefreshSkinSwatchContentHeight();
        }

        private void SelectSkinTone(string toneId)
        {
            if (profile.SetSkinTone(toneId))
            {
                RefreshSkinPanel();
            }
        }

        private void ToggleCustomColorPanel()
        {
            customColorPanel.SetActive(!customColorPanel.activeSelf);
            if (customColorPanel.activeSelf)
            {
                RefreshSkinPanel();
            }
        }

        private void OnCustomColorChanged(float _)
        {
            if (refreshingSkin)
            {
                return;
            }

            Color color = Color.HSVToRGB(hueSlider.value, saturationSlider.value, valueSlider.value);
            color.a = 1f;
            profile.SetCustomSkinColor(color);
            customColorPreview.color = color;
            RefreshSkinPanel();
        }

        private static int GetCatalogOrder(string morphId)
        {
            for (int index = 0; index < CharacterMorphCatalog.Definitions.Count; index++)
            {
                if (CharacterMorphCatalog.Definitions[index].Id == morphId)
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        private sealed class PresetOption
        {
            public static readonly PresetOption None = new("",
                "None Selected...",
                null,
                false,
                null,
                true);

            private PresetOption(
                string presetName,
                string optionLabel,
                CharacterRecipe recipe,
                bool isRuntime,
                string runtimeId,
                bool isNone = false)
            {
                PresetName = presetName;
                OptionLabel = optionLabel;
                Recipe = recipe;
                IsRuntime = isRuntime;
                RuntimeId = runtimeId;
                IsNone = isNone;
            }

            public string PresetName { get; }
            public string OptionLabel { get; }
            public CharacterRecipe Recipe { get; }
            public bool IsRuntime { get; }
            public string RuntimeId { get; }
            public bool IsNone { get; }

            public static PresetOption FromAuthored(CharacterPreset preset)
            {
                return new PresetOption(
                    preset.DisplayName,
                    $"{preset.DisplayName} (Authored)",
                    preset.Recipe,
                    false,
                    null);
            }

            public static PresetOption FromRuntime(RuntimeCharacterPresetRecord preset)
            {
                return new PresetOption(
                    preset.PresetName,
                    $"{preset.PresetName} (Saved)",
                    preset.Recipe,
                    true,
                    preset.Id);
            }
        }
    }
}
