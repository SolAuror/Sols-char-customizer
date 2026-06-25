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
        private readonly List<CharacterPreset> availablePresets = new();
        private bool initialized;
        private bool listenersRegistered;
        private bool refreshingSkin;
        private string selectedGroup = "Body";

        private static readonly Color AccentColor = new(0.82f, 0.73f, 0.55f, 1f);
        private static readonly Color InactiveColor = new(0.27f, 0.27f, 0.27f, 1f);

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

            selectedGroup = "Body";
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
            controller.SetSex(CharacterSex.Female);
            ApplySelectedGroup(false);
            RefreshPanel();
        }

        private bool HasRequiredReferences()
        {
            return controller != null &&
                   profile != null &&
                   content != null &&
                   femaleButton != null &&
                   maleButton != null &&
                   femaleButtonImage != null &&
                   maleButtonImage != null &&
                   sliderRowTemplate != null &&
                   tabRailScrollRect != null &&
                   tabRailContent != null &&
                   mainSliderScrollRect != null &&
                   presetLibrary != null &&
                   presetPanel != null &&
                   presetNameInput != null &&
                   presetDropdown != null &&
                   savePresetButton != null &&
                   loadPresetButton != null &&
                   resetAllButton != null &&
                   resetGroupButton != null &&
                   resetGroupButtonLabel != null &&
                   skinPanel != null &&
                   skinSwatches != null &&
                   skinSwatches.Length > 0 &&
                   customColorToggleButton != null &&
                   customColorPanel != null &&
                   hueSlider != null &&
                   saturationSlider != null &&
                   valueSlider != null &&
                   customColorPreview != null &&
                   tabButtons != null &&
                   tabButtons.Length == 10;
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
            if (string.IsNullOrEmpty(presetName))
            {
                Debug.LogWarning("Enter a preset name before saving.", this);
                return;
            }

            CharacterPreset preset = presetLibrary.GetOrCreate(presetName);
            CharacterRecipe recipe = profile.CaptureRecipe();
            if (preset != null && recipe != null)
            {
                preset.Overwrite(recipe);
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(preset);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(preset);
#endif
                presetLibrary.MarkDirtyAndSave();
                RefreshPresetOptions(preset);
            }
        }

        private void LoadPreset()
        {
            int selectedIndex = presetDropdown.value;
            if (selectedIndex < 0 || selectedIndex >= availablePresets.Count)
            {
                Debug.LogWarning("Select a character morph preset before loading.", this);
                return;
            }

            CharacterPreset preset = availablePresets[selectedIndex];
            if (profile.ApplyPreset(preset))
            {
                presetNameInput.SetTextWithoutNotify(preset.DisplayName);
                RefreshPanel();
            }
        }

        private void RefreshPresetOptions(CharacterPreset selectedPreset = null)
        {
            availablePresets.Clear();
            foreach (CharacterPreset preset in presetLibrary.Presets)
            {
                if (preset != null)
                {
                    availablePresets.Add(preset);
                }
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
                CharacterPreset preset = availablePresets[index];
                options.Add(preset.DisplayName);
                if (preset == selectedPreset)
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
            bool showingPresets = selectedGroup == "Presets";
            bool showingSkin = selectedGroup == "Skin";
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
            if (groupId == "Presets" || groupId == "Skin")
            {
                return true;
            }

            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                if (definition.Group == groupId)
                {
                    return true;
                }
            }

            return false;
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
    }
}
