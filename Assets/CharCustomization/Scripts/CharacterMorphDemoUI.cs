using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterMorphDemoUI : MonoBehaviour
    {
        [SerializeField] private CharacterMorphController controller;
        [SerializeField] private RectTransform content;
        [SerializeField] private Button femaleButton;
        [SerializeField] private Button maleButton;
        [SerializeField] private Image femaleButtonImage;
        [SerializeField] private Image maleButtonImage;
        [SerializeField] private CharacterMorphSliderRow sliderRowTemplate;
        [SerializeField] private ScrollRect tabRailScrollRect;
        [SerializeField] private RectTransform tabRailContent;
        [SerializeField] private ScrollRect mainSliderScrollRect;
        [SerializeField] private Button resetButton;
        [SerializeField] private CharacterMorphTabButton[] tabButtons = Array.Empty<CharacterMorphTabButton>();
        [SerializeField] private Color selectedTabColor = new(0.82f, 0.73f, 0.55f, 1f);
        [SerializeField] private Color inactiveTabColor = new(0.27f, 0.27f, 0.27f, 1f);

        private readonly Dictionary<string, CharacterMorphSliderRow> rows = new(StringComparer.Ordinal);
        private readonly Dictionary<CharacterMorphTabButton, UnityAction> tabListeners = new();
        private bool initialized;
        private bool listenersRegistered;
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
            RegisterListeners();
            controller.SetSex(CharacterSex.Female);
            ApplySelectedGroup(false);
            RefreshPanel();
        }

        private bool HasRequiredReferences()
        {
            return controller != null &&
                   content != null &&
                   femaleButton != null &&
                   maleButton != null &&
                   femaleButtonImage != null &&
                   maleButtonImage != null &&
                   sliderRowTemplate != null &&
                   tabRailScrollRect != null &&
                   tabRailContent != null &&
                   mainSliderScrollRect != null &&
                   resetButton != null &&
                   tabButtons != null &&
                   tabButtons.Length == 8;
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
            RefreshPanel();
        }

        private void ResetCurrentCharacter()
        {
            controller.ResetCurrentCharacter();
            RefreshPanel();
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
            foreach (KeyValuePair<string, CharacterMorphSliderRow> pair in rows)
            {
                bool visible = CharacterMorphCatalog.TryGet(pair.Key, out CharacterMorphDefinition definition) &&
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

            if (resetScroll)
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
            resetButton.onClick.AddListener(ResetCurrentCharacter);

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
            resetButton.onClick.RemoveListener(ResetCurrentCharacter);

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
            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                if (definition.Group == groupId)
                {
                    return true;
                }
            }

            return false;
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
