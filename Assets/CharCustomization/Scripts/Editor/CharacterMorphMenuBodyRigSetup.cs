using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization.EditorTools
{
    public static class CharacterMorphMenuBodyRigSetup
    {
        private const string MenuPrefabPath = "Assets/CharCustomization/Prefabs/CharacterMorphMenuDemoUI.prefab";
        private const string DeprecatedBodyRigGroupId = "Body Rig";

        [MenuItem("Tools/Sol/Character Customization/Ensure Consolidated Body Menu Controls")]
        public static void EnsureBodyRigMenuControlsFromMenu()
        {
            EnsureConsolidatedBodyMenuControls();
        }

        public static void EnsureBodyRigMenuControlsFromCommandLine()
        {
            EnsureConsolidatedBodyMenuControls();
        }

        private static void EnsureConsolidatedBodyMenuControls()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(MenuPrefabPath);
            try
            {
                CharacterMorphDemoUI demoUi = prefabRoot.GetComponent<CharacterMorphDemoUI>();
                if (demoUi == null)
                {
                    Debug.LogError($"'{MenuPrefabPath}' has no {nameof(CharacterMorphDemoUI)} component.");
                    return;
                }

                var serializedUi = new SerializedObject(demoUi);
                RectTransform content = GetObjectReference<RectTransform>(serializedUi, "content");
                RectTransform tabRailContent = GetObjectReference<RectTransform>(serializedUi, "tabRailContent");
                CharacterMorphSliderRow rowTemplate = GetObjectReference<CharacterMorphSliderRow>(serializedUi, "sliderRowTemplate");
                CharacterMorphTabButton[] tabs = prefabRoot.GetComponentsInChildren<CharacterMorphTabButton>(true);
                CharacterMorphTabButton bodyTab = tabs.FirstOrDefault(tab => tab.GroupId == CharacterCustomizationUiConfig.DefaultMorphGroupId);

                if (content == null || tabRailContent == null || rowTemplate == null || bodyTab == null)
                {
                    Debug.LogError("Consolidated Body setup could not find the menu content, tab rail, slider template, or Body tab.");
                    return;
                }

                RemoveDeprecatedBodyRigTabs(tabRailContent);
                int rowCount = EnsureBodyRows(content, rowTemplate);
                AssignTabButtons(serializedUi);

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, MenuPrefabPath);
                Debug.Log($"Ensured consolidated Body tab and {rowCount} authored Body rows in {MenuPrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static T GetObjectReference<T>(SerializedObject serializedObject, string propertyName)
            where T : Object
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            return property != null ? property.objectReferenceValue as T : null;
        }

        private static void RemoveDeprecatedBodyRigTabs(RectTransform tabRailContent)
        {
            CharacterMorphTabButton[] deprecatedTabs = tabRailContent
                .GetComponentsInChildren<CharacterMorphTabButton>(true)
                .Where(tab => tab.GroupId == DeprecatedBodyRigGroupId)
                .ToArray();

            foreach (CharacterMorphTabButton tab in deprecatedTabs)
            {
                Object.DestroyImmediate(tab.gameObject);
            }
        }

        private static int EnsureBodyRows(RectTransform content, CharacterMorphSliderRow rowTemplate)
        {
            CharacterMorphDefinition[] bodyDefinitions = CharacterMorphCatalog.Definitions
                .Where(definition => definition.VisibleInCreator && definition.Group == CharacterMorphCatalog.BodyGroup)
                .ToArray();
            var visibleBodyIds = new HashSet<string>(bodyDefinitions.Select(definition => definition.Id), System.StringComparer.Ordinal);

            var existingRows = content.GetComponentsInChildren<CharacterMorphSliderRow>(true)
                .Where(row => row != rowTemplate && !string.IsNullOrWhiteSpace(row.MorphId))
                .ToDictionary(row => row.MorphId, row => row);

            foreach (CharacterMorphSliderRow staleRow in existingRows.Values
                         .Where(row => IsHiddenOrDeprecatedBodyRow(row.MorphId, visibleBodyIds))
                         .ToArray())
            {
                existingRows.Remove(staleRow.MorphId);
                Object.DestroyImmediate(staleRow.gameObject);
            }

            foreach (CharacterMorphDefinition definition in bodyDefinitions)
            {
                if (existingRows.TryGetValue(definition.Id, out CharacterMorphSliderRow existing))
                {
                    ConfigureRow(existing, definition);
                    continue;
                }

                GameObject rowObject = Object.Instantiate(rowTemplate.gameObject, content);
                rowObject.name = definition.Id;
                rowObject.SetActive(true);

                var row = rowObject.GetComponent<CharacterMorphSliderRow>();
                ConfigureRow(row, definition);
                row.transform.SetAsLastSibling();
                existingRows.Add(definition.Id, row);
            }

            return bodyDefinitions.Length;
        }

        private static bool IsHiddenOrDeprecatedBodyRow(string morphId, HashSet<string> visibleBodyIds)
        {
            return CharacterMorphCatalog.TryGet(morphId, out CharacterMorphDefinition definition)
                   && (definition.Group == CharacterMorphCatalog.BodyGroup || definition.Group == DeprecatedBodyRigGroupId)
                   && !visibleBodyIds.Contains(morphId);
        }

        private static void ConfigureRow(CharacterMorphSliderRow row, CharacterMorphDefinition definition)
        {
            row.SetMorphId(definition.Id);
            if (row.Label != null)
            {
                row.Label.text = definition.Label;
            }

            if (row.ValueText != null)
            {
                row.ValueText.text = "0.00";
            }

            if (row.Slider != null)
            {
                row.Slider.minValue = definition.MinimumValue;
                row.Slider.maxValue = 1f;
                row.Slider.SetValueWithoutNotify(0f);
            }
        }

        private static void AssignTabButtons(SerializedObject serializedUi)
        {
            var demoUi = (CharacterMorphDemoUI)serializedUi.targetObject;
            CharacterMorphTabButton[] orderedTabs = demoUi
                .GetComponentsInChildren<CharacterMorphTabButton>(true)
                .Where(tab => tab != null && tab.IsConfigured)
                .OrderBy(tab => tab.transform.GetSiblingIndex())
                .ToArray();

            SerializedProperty tabButtons = serializedUi.FindProperty("tabButtons");
            tabButtons.arraySize = orderedTabs.Length;
            for (int index = 0; index < orderedTabs.Length; index++)
            {
                tabButtons.GetArrayElementAtIndex(index).objectReferenceValue = orderedTabs[index];
            }

            serializedUi.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
