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
        [SerializeField] private Button resetAllButton;
        [SerializeField] private Button resetGroupButton;
        [SerializeField] private TMP_Text resetGroupButtonLabel;
        [SerializeField] private GameObject skinPanel;
        [SerializeField] private CharacterSkinSwatchButton[] skinSwatches = Array.Empty<CharacterSkinSwatchButton>();
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
        private readonly List<PresetOption> availablePresets = new();
        private ICharacterPresetSaveRepository presetRepository;
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

            presetNameInput?.SetTextWithoutNotify(presetName);
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
            foreach (CharacterSkinSwatchButton swatch in skinSwatches)
            {
                if (swatch != null)
                {
                    bool selected = !profile.UsesCustomSkinColor && swatch.SkinToneId == profile.SkinToneId;
                    swatch.transform.localScale = selected ? Vector3.one * 1.08f : Vector3.one;
                }
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

            if (!HasRequiredReferences())
            {
                Debug.LogError("The prefab-first character morph UI has incomplete references.", this);
                enabled = false;
                return;
            }

            selectedGroup = CharacterCustomizationUiConfig.DefaultMorphGroupId;
            EnsurePresetRepository();
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
            customColorPanel.SetActive(false);
            RegisterListeners();
            controller.SetSex(controller.ActiveSex);
            ApplySelectedGroup(false);
            RefreshPanel();
        }

        private bool HasRequiredReferences()
        {
            var errors = new List<string>();
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
            AddMissing(errors, presetNameInput == null, "presetNameInput");
            AddMissing(errors, presetDropdown == null, "presetDropdown");
            AddMissing(errors, savePresetButton == null, "savePresetButton");
            AddMissing(errors, loadPresetButton == null, "loadPresetButton");
            AddMissing(errors, resetAllButton == null, "resetAllButton");
            AddMissing(errors, resetGroupButton == null, "resetGroupButton");
            AddMissing(errors, resetGroupButtonLabel == null, "resetGroupButtonLabel");
            AddMissing(errors, skinPanel == null, "skinPanel");
            AddMissing(errors, customColorToggleButton == null, "customColorToggleButton");
            AddMissing(errors, customColorPanel == null, "customColorPanel");
            AddMissing(errors, hueSlider == null, "hueSlider");
            AddMissing(errors, saturationSlider == null, "saturationSlider");
            AddMissing(errors, valueSlider == null, "valueSlider");
            AddMissing(errors, customColorPreview == null, "customColorPreview");
            ValidateSkinSwatches(errors);
            ValidateTabs(errors);

            if (errors.Count > 0)
            {
                Debug.LogError($"The character morph UI has incomplete references: {string.Join("; ", errors)}", this);
                return false;
            }

            return true;
        }

        private void EnsurePresetRepository()
        {
            presetRepository ??= new CharacterPresetSaveRepository(presetSavePathOverride);
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
            for (int index = 0; index < skinSwatches.Length; index++)
            {
                CharacterSkinSwatchButton swatch = skinSwatches[index];
                if (swatch == null)
                {
                    errors.Add($"skinSwatches[{index}] is unassigned");
                    continue;
                }

                if (!swatch.IsConfigured)
                {
                    errors.Add($"skinSwatches[{index}] '{swatch.name}' is incomplete");
                    continue;
                }

                if (!ids.Add(swatch.SkinToneId))
                {
                    errors.Add($"skinSwatches contains duplicate tone '{swatch.SkinToneId}'");
                }
            }

            CharacterSkinPalette palette = profile != null ? profile.SkinPalette : null;
            if (palette == null)
            {
                return;
            }

            foreach (CharacterSkinTone tone in palette.Tones)
            {
                if (tone != null && !ids.Contains(tone.Id))
                {
                    errors.Add($"skinSwatches is missing palette tone '{tone.Id}'");
                }
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
            string presetName = presetNameInput.text.Trim();
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
            presetNameInput.SetTextWithoutNotify(savedPreset.PresetName);
            RefreshPresetOptions(savedPreset.Id);
        }

        private void LoadPreset()
        {
            int selectedIndex = presetDropdown.value;
            if (selectedIndex < 0 || selectedIndex >= availablePresets.Count)
            {
                Debug.LogWarning("Select a character morph preset before loading.", this);
                return;
            }

            PresetOption preset = availablePresets[selectedIndex];
            if (!TryApplyPresetRecipe(preset.PresetName, preset.Recipe, out string error))
            {
                Debug.LogWarning(error, this);
            }
        }

        private void RefreshPresetOptions(string selectedRuntimePresetId = null)
        {
            availablePresets.Clear();
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
            if (availablePresets.Count == 0)
            {
                presetDropdown.AddOptions(new List<string> { "No presets" });
                presetDropdown.interactable = false;
                loadPresetButton.interactable = false;
                return;
            }

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
            presetDropdown.interactable = true;
            loadPresetButton.interactable = true;
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

            femaleButton.onClick.AddListener(SelectFemale);
            maleButton.onClick.AddListener(SelectMale);
            savePresetButton.onClick.AddListener(SavePreset);
            loadPresetButton.onClick.AddListener(LoadPreset);
            resetAllButton.onClick.AddListener(ResetAll);
            resetGroupButton.onClick.AddListener(ResetSelectedGroup);
            presetNameInput.onValueChanged.AddListener(OnPresetNameChanged);
            customColorToggleButton.onClick.AddListener(ToggleCustomColorPanel);
            hueSlider.onValueChanged.AddListener(OnCustomColorChanged);
            saturationSlider.onValueChanged.AddListener(OnCustomColorChanged);
            valueSlider.onValueChanged.AddListener(OnCustomColorChanged);

            foreach (CharacterSkinSwatchButton swatch in skinSwatches)
            {
                if (swatch == null || !swatch.IsConfigured || skinListeners.ContainsKey(swatch))
                {
                    continue;
                }

                CharacterSkinSwatchButton capturedSwatch = swatch;
                UnityAction listener = () => SelectSkinTone(capturedSwatch.SkinToneId);
                capturedSwatch.Button.onClick.AddListener(listener);
                skinListeners.Add(capturedSwatch, listener);
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

        private void UnregisterListeners()
        {
            if (!listenersRegistered)
            {
                return;
            }

            femaleButton.onClick.RemoveListener(SelectFemale);
            maleButton.onClick.RemoveListener(SelectMale);
            savePresetButton.onClick.RemoveListener(SavePreset);
            loadPresetButton.onClick.RemoveListener(LoadPreset);
            resetAllButton.onClick.RemoveListener(ResetAll);
            resetGroupButton.onClick.RemoveListener(ResetSelectedGroup);
            presetNameInput.onValueChanged.RemoveListener(OnPresetNameChanged);
            customColorToggleButton.onClick.RemoveListener(ToggleCustomColorPanel);
            hueSlider.onValueChanged.RemoveListener(OnCustomColorChanged);
            saturationSlider.onValueChanged.RemoveListener(OnCustomColorChanged);
            valueSlider.onValueChanged.RemoveListener(OnCustomColorChanged);

            foreach (KeyValuePair<CharacterSkinSwatchButton, UnityAction> pair in skinListeners)
            {
                if (pair.Key != null && pair.Key.Button != null)
                {
                    pair.Key.Button.onClick.RemoveListener(pair.Value);
                }
            }

            skinListeners.Clear();

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
            private PresetOption(
                string presetName,
                string optionLabel,
                CharacterRecipe recipe,
                bool isRuntime,
                string runtimeId)
            {
                PresetName = presetName;
                OptionLabel = optionLabel;
                Recipe = recipe;
                IsRuntime = isRuntime;
                RuntimeId = runtimeId;
            }

            public string PresetName { get; }
            public string OptionLabel { get; }
            public CharacterRecipe Recipe { get; }
            public bool IsRuntime { get; }
            public string RuntimeId { get; }

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
