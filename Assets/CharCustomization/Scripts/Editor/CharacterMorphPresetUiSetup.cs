using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization.Editor
{
    public static class CharacterMorphPresetUiSetup
    {
        private const string MenuPrefabPath = "Assets/CharCustomization/Prefabs/CharacterMorphMenu.prefab";
        private const string PresetLibraryPath = "Assets/CharCustomization/Presets/PresetLibrary.asset";
        private static readonly string[] MorphGroups =
        {
            "Body", "Jaw / Chin", "Mouth", "Nose", "Cheeks", "Eyes", "Brows", "Neck / Ears"
        };

        [InitializeOnLoadMethod]
        private static void ScheduleSetup()
        {
            EditorApplication.delayCall += RunIfRequired;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Character Customization/Setup Preset Tab")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before setting up the preset tab.");
            }

            CharacterPresetLibrary library =
                AssetDatabase.LoadAssetAtPath<CharacterPresetLibrary>(PresetLibraryPath);
            if (library == null)
            {
                throw new InvalidOperationException($"Missing preset library at '{PresetLibraryPath}'.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(MenuPrefabPath);
            try
            {
                CharacterMorphDemoUI menu = root.GetComponent<CharacterMorphDemoUI>();
                if (menu == null)
                {
                    throw new InvalidOperationException("The menu prefab has no CharacterMorphDemoUI component.");
                }

                var serializedMenu = new SerializedObject(menu);
                if (serializedMenu.FindProperty("presetPanel").objectReferenceValue != null)
                {
                    Debug.Log("Character morph preset tab is already configured.");
                    return;
                }

                ScrollRect sliderScroll =
                    (ScrollRect)serializedMenu.FindProperty("mainSliderScrollRect").objectReferenceValue;
                Button oldResetButton = Find<Button>(root, "Reset Button");
                CharacterMorphTabButton presetTab = Find<CharacterMorphTabButton>(root, "Preset Tab");
                if (sliderScroll == null || oldResetButton == null || presetTab == null)
                {
                    throw new InvalidOperationException(
                        "The authored menu must contain its slider ScrollRect, Reset Button, and Preset Tab.");
                }

                ConfigurePresetTab(root, presetTab, serializedMenu);
                Transform panelParent = sliderScroll.transform.parent;
                Transform existingPanel = panelParent.Find("Preset Panel");
                if (existingPanel != null)
                {
                    UnityEngine.Object.DestroyImmediate(existingPanel.gameObject);
                }

                GameObject presetPanel = CreatePresetPanel(sliderScroll.GetComponent<RectTransform>());
                presetPanel.transform.SetParent(panelParent, false);
                presetPanel.transform.SetSiblingIndex(sliderScroll.transform.GetSiblingIndex() + 1);

                TMP_InputField nameInput = CreateNameInput(presetPanel.transform);
                TMP_Dropdown dropdown = CreatePresetDropdown(presetPanel.transform);
                GameObject buttonRow = CreateButtonRow(presetPanel.transform);

                Button groupResetButton = CloneButton(oldResetButton, panelParent, "Reset Tab Button", "Reset Body");
                ConfigureFixedButton(groupResetButton.GetComponent<RectTransform>());
                TMP_Text groupResetLabel = groupResetButton.GetComponentInChildren<TMP_Text>(true);

                Button saveButton = CloneButton(oldResetButton, buttonRow.transform, "Save Preset Button", "Save Preset");
                Button loadButton = CloneButton(oldResetButton, buttonRow.transform, "Load Preset Button", "Load Preset");
                oldResetButton.name = "Reset All Button";
                oldResetButton.transform.SetParent(buttonRow.transform, false);
                SetButtonLabel(oldResetButton, "Reset All");

                var finalMenu = new SerializedObject(menu);
                finalMenu.FindProperty("presetLibrary").objectReferenceValue = library;
                finalMenu.FindProperty("presetPanel").objectReferenceValue = presetPanel;
                finalMenu.FindProperty("presetNameInput").objectReferenceValue = nameInput;
                finalMenu.FindProperty("presetDropdown").objectReferenceValue = dropdown;
                finalMenu.FindProperty("savePresetButton").objectReferenceValue = saveButton;
                finalMenu.FindProperty("loadPresetButton").objectReferenceValue = loadButton;
                finalMenu.FindProperty("resetAllButton").objectReferenceValue = oldResetButton;
                finalMenu.FindProperty("resetGroupButton").objectReferenceValue = groupResetButton;
                finalMenu.FindProperty("resetGroupButtonLabel").objectReferenceValue = groupResetLabel;
                finalMenu.ApplyModifiedPropertiesWithoutUndo();

                presetPanel.SetActive(false);
                groupResetButton.gameObject.SetActive(true);
                EditorUtility.SetDirty(menu);
                EditorUtility.SetDirty(root);
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, MenuPrefabPath, out bool saved);
                if (!saved || savedPrefab == null)
                {
                    throw new InvalidOperationException("Unity could not save the wired character morph menu prefab.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Character morph preset tab setup completed.");
        }

        private static void RunIfRequired()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
            CharacterMorphDemoUI menu = prefab != null ? prefab.GetComponent<CharacterMorphDemoUI>() : null;
            if (menu == null)
            {
                return;
            }

            var serializedMenu = new SerializedObject(menu);
            SerializedProperty presetPanel = serializedMenu.FindProperty("presetPanel");
            if (presetPanel != null && presetPanel.objectReferenceValue != null)
            {
                return;
            }

            try
            {
                Run();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += RunIfRequired;
            }
        }

        private static void ConfigurePresetTab(
            GameObject root,
            CharacterMorphTabButton presetTab,
            SerializedObject serializedMenu)
        {
            var serializedTab = new SerializedObject(presetTab);
            serializedTab.FindProperty("groupId").stringValue = "Presets";
            serializedTab.ApplyModifiedPropertiesWithoutUndo();

            CharacterMorphTabButton[] allTabs = root.GetComponentsInChildren<CharacterMorphTabButton>(true);
            var orderedTabs = new CharacterMorphTabButton[MorphGroups.Length + 1];
            orderedTabs[0] = presetTab;
            for (int index = 0; index < MorphGroups.Length; index++)
            {
                string group = MorphGroups[index];
                orderedTabs[index + 1] = allTabs.FirstOrDefault(tab =>
                    tab != presetTab && string.Equals(tab.GroupId, group, StringComparison.Ordinal));
                if (orderedTabs[index + 1] == null)
                {
                    throw new InvalidOperationException($"Missing authored morph tab '{group}'.");
                }
            }

            SerializedProperty tabs = serializedMenu.FindProperty("tabButtons");
            tabs.arraySize = orderedTabs.Length;
            for (int index = 0; index < orderedTabs.Length; index++)
            {
                tabs.GetArrayElementAtIndex(index).objectReferenceValue = orderedTabs[index];
            }

            serializedMenu.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreatePresetPanel(RectTransform sliderRect)
        {
            var panel = new GameObject("Preset Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.layer = sliderRect.gameObject.layer;
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = sliderRect.anchorMin;
            rect.anchorMax = sliderRect.anchorMax;
            rect.pivot = sliderRect.pivot;
            rect.anchoredPosition = sliderRect.anchoredPosition;
            rect.sizeDelta = sliderRect.sizeDelta;

            Image image = panel.GetComponent<Image>();
            image.color = new Color(0.12f, 0.12f, 0.12f, 0.96f);

            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return panel;
        }

        private static TMP_InputField CreateNameInput(Transform parent)
        {
            GameObject inputObject = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            inputObject.name = "Preset Name Input";
            inputObject.transform.SetParent(parent, false);
            inputObject.AddComponent<LayoutElement>().preferredHeight = 48f;

            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.characterLimit = 48;
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "Preset name";
                placeholder.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            }

            input.textComponent.color = Color.white;
            inputObject.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.22f, 1f);
            return input;
        }

        private static TMP_Dropdown CreatePresetDropdown(Transform parent)
        {
            GameObject dropdownObject = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            dropdownObject.name = "Preset Dropdown";
            dropdownObject.transform.SetParent(parent, false);
            dropdownObject.AddComponent<LayoutElement>().preferredHeight = 48f;

            TMP_Dropdown dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
            dropdownObject.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.22f, 1f);
            foreach (TMP_Text text in dropdownObject.GetComponentsInChildren<TMP_Text>(true))
            {
                text.color = Color.white;
            }

            return dropdown;
        }

        private static GameObject CreateButtonRow(Transform parent)
        {
            var row = new GameObject("Preset Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.layer = parent.gameObject.layer;
            row.transform.SetParent(parent, false);

            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            row.GetComponent<LayoutElement>().preferredHeight = 52f;
            return row;
        }

        private static Button CloneButton(Button template, Transform parent, string objectName, string label)
        {
            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
            clone.name = objectName;
            SetButtonLabel(clone.GetComponent<Button>(), label);
            return clone.GetComponent<Button>();
        }

        private static void SetButtonLabel(Button button, string label)
        {
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }

        private static void ConfigureFixedButton(RectTransform rect)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-16f, 16f);
            rect.sizeDelta = new Vector2(190f, 48f);
        }

        private static T Find<T>(GameObject root, string objectName) where T : Component
        {
            return root.GetComponentsInChildren<T>(true)
                .FirstOrDefault(component => component.gameObject.name == objectName);
        }
    }
}
