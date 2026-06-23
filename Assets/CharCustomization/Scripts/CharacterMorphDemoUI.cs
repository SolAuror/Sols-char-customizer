using System;
using System.Collections.Generic;
using UnityEngine;
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

        private readonly Dictionary<string, CharacterMorphSliderRow> rows = new(StringComparer.Ordinal);
        private bool initialized;

        private static readonly Color AccentColor = new(0.82f, 0.73f, 0.55f, 1f);
        private static readonly Color InactiveColor = new(0.27f, 0.27f, 0.27f, 1f);

        private void Start()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            if (femaleButton != null)
            {
                femaleButton.onClick.RemoveListener(SelectFemale);
            }

            if (maleButton != null)
            {
                maleButton.onClick.RemoveListener(SelectMale);
            }

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
            row.gameObject.SetActive(true);
            PlaceAtEndOfGroup(row.transform, definition.Group);

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
                row.Refresh();
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

            initialized = true;
            CacheAuthoredRows();

            foreach (CharacterMorphDefinition definition in controller.Definitions)
            {
                if (!rows.ContainsKey(definition.Id))
                {
                    CreateSliderForMorph(definition.Id);
                }
            }

            femaleButton.onClick.AddListener(SelectFemale);
            maleButton.onClick.AddListener(SelectMale);
            controller.SetSex(CharacterSex.Female);
            RefreshPanel();
        }

        private bool HasRequiredReferences()
        {
            return controller != null &&
                   content != null &&
                   femaleButton != null &&
                   maleButton != null &&
                   femaleButtonImage != null &&
                   maleButtonImage != null;
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

        private void PlaceAtEndOfGroup(Transform row, string group)
        {
            int insertionIndex = content.childCount;
            bool foundHeader = false;

            for (int index = 0; index < content.childCount; index++)
            {
                Transform child = content.GetChild(index);
                if (child == row)
                {
                    continue;
                }

                if (child.name == group + " Header")
                {
                    foundHeader = true;
                    insertionIndex = index + 1;
                    continue;
                }

                if (foundHeader && child.name.EndsWith(" Header", StringComparison.Ordinal))
                {
                    insertionIndex = index;
                    break;
                }

                if (foundHeader)
                {
                    insertionIndex = index + 1;
                }
            }

            row.SetSiblingIndex(insertionIndex);
        }
    }
}
