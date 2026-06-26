using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sol.CharacterCustomization.Editor
{
    public static class CharacterMorphValidator
    {
        private const string DemoScenePath = "Assets/CharCustomization/Scenes/DemoScene.unity";
        private const string MenuPrefabPath = "Assets/CharCustomization/Prefabs/CharacterMorphMenu.prefab";
        private const string DemoPresetPath = "Assets/CharCustomization/Presets/DemoPreset.asset";
        private const string PresetLibraryPath = "Assets/CharCustomization/Presets/PresetLibrary.asset";
        private const string SkinPalettePath = "Assets/CharCustomization/Presets/DefaultSkinPalette.asset";
        private const string InputActionsPath = "Assets/CharCustomization/Scripts/InputSystem_Actions.inputactions";
        [MenuItem("Tools/Character Customization/Validate Morph Demo")]
        public static void ValidateDemo()
        {
            GameObject menuPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
            if (menuPrefab == null)
            {
                throw new InvalidOperationException($"Missing menu prefab at '{MenuPrefabPath}'.");
            }

            CharacterPreset demoPreset = AssetDatabase.LoadAssetAtPath<CharacterPreset>(DemoPresetPath);
            CharacterPresetLibrary presetLibrary =
                AssetDatabase.LoadAssetAtPath<CharacterPresetLibrary>(PresetLibraryPath);
            CharacterSkinPalette skinPalette = AssetDatabase.LoadAssetAtPath<CharacterSkinPalette>(SkinPalettePath);
            if (demoPreset == null || presetLibrary == null || skinPalette == null)
            {
                throw new InvalidOperationException("The demo preset, preset library, or skin palette is missing.");
            }

            ValidateMenu(menuPrefab, presetLibrary, skinPalette);
            ValidatePresetLibrary(presetLibrary, demoPreset, skinPalette);
            ValidateStatGrowthDefinitions();
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestoreSetup = previousSetup.Any(setup => setup.isLoaded && setup.isActive);
            try
            {
                Scene scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
                ValidateScene(scene);
            }
            finally
            {
                if (canRestoreSetup)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }

            Debug.Log("Character morph demo validation passed.");
        }

        public static void ValidateFromCommandLine()
        {
            try
            {
                ValidateDemo();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void ValidateMenu(
            GameObject menuPrefab,
            CharacterPresetLibrary presetLibrary,
            CharacterSkinPalette skinPalette)
        {
            CharacterMorphDemoUI menu = menuPrefab.GetComponent<CharacterMorphDemoUI>();
            if (menu == null)
            {
                throw new InvalidOperationException("The menu prefab has no CharacterMorphDemoUI component.");
            }

            CharacterMorphSliderRow[] rows = menuPrefab.GetComponentsInChildren<CharacterMorphSliderRow>(true);
            CharacterMorphSliderRow[] authored = rows.Where(row => !string.IsNullOrEmpty(row.MorphId)).ToArray();
            CharacterMorphSliderRow[] templates = rows.Where(row => string.IsNullOrEmpty(row.MorphId)).ToArray();
            string[] duplicates = authored.GroupBy(row => row.MorphId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            string[] missing = CharacterMorphCatalog.Definitions
                .Where(definition => authored.All(row => row.MorphId != definition.Id || !row.IsConfigured))
                .Select(definition => definition.Id).ToArray();

            if (authored.Length != CharacterMorphCatalog.Definitions.Count || templates.Length != 1 ||
                templates[0].gameObject.activeSelf ||
                duplicates.Length > 0 || missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Invalid menu bindings. Authored: {authored.Length}, templates: {templates.Length}, " +
                    $"duplicates: {string.Join(", ", duplicates)}, missing: {string.Join(", ", missing)}");
            }

            CharacterMorphSliderRow[] visibleRows = authored.Where(row => row.gameObject.activeSelf).ToArray();
            if (visibleRows.Length != 10 || visibleRows.Any(row =>
                    !CharacterMorphCatalog.TryGet(row.MorphId, out CharacterMorphDefinition definition) ||
                    definition.Group != CharacterCustomizationUiConfig.DefaultMorphGroupId))
            {
                throw new InvalidOperationException(
                    $"{CharacterCustomizationUiConfig.DefaultMorphGroupId} must be the initial tab with exactly its 10 authored rows visible.");
            }

            string[] authoredOrder = authored.OrderBy(row => row.transform.GetSiblingIndex())
                .Select(row => row.MorphId).ToArray();
            string[] catalogueOrder = CharacterMorphCatalog.Definitions.Select(definition => definition.Id).ToArray();
            if (!authoredOrder.SequenceEqual(catalogueOrder, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Authored morph rows are not in catalogue order.");
            }

            TMP_Text[] remainingHeaders = menuPrefab.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text.gameObject.name.EndsWith(" Header", StringComparison.Ordinal)).ToArray();
            if (remainingHeaders.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The menu still contains header objects: {string.Join(", ", remainingHeaders.Select(header => header.name))}.");
            }

            var serializedMenu = new SerializedObject(menu);
            foreach (string propertyName in new[]
                     {
                         "content", "femaleButton", "maleButton", "femaleButtonImage", "maleButtonImage", "sliderRowTemplate",
                         "tabRailScrollRect", "tabRailContent", "mainSliderScrollRect", "presetLibrary", "presetPanel",
                         "presetNameInput", "presetDropdown", "savePresetButton", "loadPresetButton",
                         "resetAllButton", "resetGroupButton", "resetGroupButtonLabel", "skinPanel",
                         "customColorToggleButton", "customColorPanel", "hueSlider", "saturationSlider",
                         "valueSlider", "customColorPreview"
                     })
            {
                if (serializedMenu.FindProperty(propertyName).objectReferenceValue == null)
                {
                    throw new InvalidOperationException($"Menu reference '{propertyName}' is not assigned.");
                }
            }

            ValidateTabs(menu, serializedMenu);
            ValidateScrollRect((ScrollRect)serializedMenu.FindProperty("tabRailScrollRect").objectReferenceValue, "tab rail");
            ValidateScrollRect((ScrollRect)serializedMenu.FindProperty("mainSliderScrollRect").objectReferenceValue, "slider panel");

            RectTransform tabRailContent =
                (RectTransform)serializedMenu.FindProperty("tabRailContent").objectReferenceValue;
            if (tabRailContent.GetComponent<VerticalLayoutGroup>() == null ||
                tabRailContent.GetComponent<ContentSizeFitter>() == null)
            {
                throw new InvalidOperationException("The tab rail content must be layout driven.");
            }

            CharacterPresetLibrary assignedLibrary =
                (CharacterPresetLibrary)serializedMenu.FindProperty("presetLibrary").objectReferenceValue;
            GameObject presetPanel =
                (GameObject)serializedMenu.FindProperty("presetPanel").objectReferenceValue;
            TMP_InputField nameInput =
                (TMP_InputField)serializedMenu.FindProperty("presetNameInput").objectReferenceValue;
            TMP_Dropdown dropdown =
                (TMP_Dropdown)serializedMenu.FindProperty("presetDropdown").objectReferenceValue;
            Button savePresetButton =
                (Button)serializedMenu.FindProperty("savePresetButton").objectReferenceValue;
            Button loadPresetButton =
                (Button)serializedMenu.FindProperty("loadPresetButton").objectReferenceValue;
            Button resetAllButton =
                (Button)serializedMenu.FindProperty("resetAllButton").objectReferenceValue;
            Button resetGroupButton =
                (Button)serializedMenu.FindProperty("resetGroupButton").objectReferenceValue;
            ScrollRect sliderScroll =
                (ScrollRect)serializedMenu.FindProperty("mainSliderScrollRect").objectReferenceValue;

            if (assignedLibrary != presetLibrary ||
                nameInput.transform.root != menuPrefab.transform ||
                dropdown.transform.root != menuPrefab.transform ||
                savePresetButton.transform.root != menuPrefab.transform ||
                loadPresetButton.transform.root != menuPrefab.transform ||
                resetAllButton.transform.root != menuPrefab.transform)
            {
                throw new InvalidOperationException(
                    "The preset references must stay assigned to authored controls on the menu prefab.");
            }

            if (!resetGroupButton.gameObject.activeSelf || resetGroupButton.transform.parent != sliderScroll.transform.parent ||
                resetGroupButton.transform.IsChildOf(sliderScroll.transform))
            {
                throw new InvalidOperationException(
                    "Reset Tab must remain fixed outside the morph ScrollRect.");
            }

            GameObject skinPanel =
                (GameObject)serializedMenu.FindProperty("skinPanel").objectReferenceValue;
            GameObject customColorPanel =
                (GameObject)serializedMenu.FindProperty("customColorPanel").objectReferenceValue;
            SerializedProperty swatchProperty = serializedMenu.FindProperty("skinSwatches");
            var swatches = new List<CharacterSkinSwatchButton>();
            for (int index = 0; index < swatchProperty.arraySize; index++)
            {
                if (swatchProperty.GetArrayElementAtIndex(index).objectReferenceValue is CharacterSkinSwatchButton swatch)
                {
                    swatches.Add(swatch);
                }
            }

            string[] paletteIds = skinPalette.Tones.Select(tone => tone.Id).ToArray();
            string[] swatchIds = swatches.Select(swatch => swatch.SkinToneId).ToArray();
            if (skinPanel.activeSelf || customColorPanel.activeSelf ||
                swatches.Count != paletteIds.Length || swatches.Any(swatch => !swatch.IsConfigured) ||
                !swatchIds.SequenceEqual(paletteIds, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "The inactive Skin panel must contain one configured authored swatch per palette tone and a collapsed custom colour panel.");
            }

            CharacterFinalizationFlow flow = menuPrefab.GetComponent<CharacterFinalizationFlow>();
            if (flow == null)
            {
                throw new InvalidOperationException("The menu prefab has no CharacterFinalizationFlow component.");
            }

            var serializedFlow = new SerializedObject(flow);
            foreach (string propertyName in new[]
                     {
                         "demoUI", "characterNameInput", "randomizeButton", "finalizeButton",
                         "statusLabel", "customizationInterface"
                     })
            {
                if (serializedFlow.FindProperty(propertyName).objectReferenceValue == null)
                {
                    throw new InvalidOperationException($"Finalization reference '{propertyName}' is not assigned.");
                }
            }

            TMP_InputField footerNameInput =
                serializedFlow.FindProperty("characterNameInput").objectReferenceValue as TMP_InputField;
            Button footerRandomize =
                serializedFlow.FindProperty("randomizeButton").objectReferenceValue as Button;
            Button footerFinalize =
                serializedFlow.FindProperty("finalizeButton").objectReferenceValue as Button;
            TMP_Text footerStatus =
                serializedFlow.FindProperty("statusLabel").objectReferenceValue as TMP_Text;
            if (footerNameInput == null ||
                footerRandomize == null ||
                footerFinalize == null ||
                footerStatus == null ||
                footerNameInput.transform.root != menuPrefab.transform ||
                footerRandomize.transform.root != menuPrefab.transform ||
                footerFinalize.transform.root != menuPrefab.transform ||
                footerStatus.transform.root != menuPrefab.transform)
            {
                throw new InvalidOperationException(
                    "The finalization controls must be assigned to authored controls on the menu prefab.");
            }
        }

        private static void ValidatePresetLibrary(
            CharacterPresetLibrary library,
            CharacterPreset demoPreset,
            CharacterSkinPalette skinPalette)
        {
            CharacterPreset[] presets = library.Presets.Where(preset => preset != null).ToArray();
            if (presets.Length == 0 || !presets.Contains(demoPreset) ||
                presets.Select(preset => preset.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != presets.Length)
            {
                throw new InvalidOperationException(
                    "The preset library must contain the demo preset and unique preset names.");
            }

            foreach (CharacterPreset preset in presets)
            {
                ValidatePreset(preset, skinPalette);
            }
        }

        private static void ValidatePreset(CharacterPreset preset, CharacterSkinPalette skinPalette)
        {
            if (preset.Recipe.Version != CharacterRecipe.CurrentVersion ||
                !Enum.IsDefined(typeof(CharacterSex), preset.Sex) ||
                preset.Values.Count != CharacterMorphCatalog.Definitions.Count)
            {
                throw new InvalidOperationException("The demo preset has an invalid sex or morph count.");
            }

            if (!preset.Recipe.UsesCustomSkinColor && !skinPalette.TryGet(preset.Recipe.SkinToneId, out _))
            {
                throw new InvalidOperationException($"Preset '{preset.DisplayName}' references an unknown skin tone.");
            }

            for (int index = 0; index < CharacterMorphCatalog.Definitions.Count; index++)
            {
                CharacterMorphDefinition definition = CharacterMorphCatalog.Definitions[index];
                CharacterMorphValue presetValue = preset.Values[index];
                if (!string.Equals(presetValue.MorphId, definition.Id, StringComparison.Ordinal) ||
                    presetValue.Value < definition.MinimumValue || presetValue.Value > 1f)
                {
                    throw new InvalidOperationException(
                        $"Demo preset entry {index} must be catalogue-ordered and within the range for '{definition.Id}'.");
                }
            }
        }

        private static void ValidateTabs(CharacterMorphDemoUI menu, SerializedObject serializedMenu)
        {
            SerializedProperty tabsProperty = serializedMenu.FindProperty("tabButtons");
            var tabs = new List<CharacterMorphTabButton>();
            for (int index = 0; index < tabsProperty.arraySize; index++)
            {
                CharacterMorphTabButton tab =
                    tabsProperty.GetArrayElementAtIndex(index).objectReferenceValue as CharacterMorphTabButton;
                if (tab != null)
                {
                    tabs.Add(tab);
                }
            }

            IReadOnlyList<string> expectedGroups = CharacterCustomizationUiConfig.TabGroups;
            string[] duplicateGroups = tabs.GroupBy(tab => tab.GroupId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
            string[] missingGroups = expectedGroups.Where(group => tabs.All(tab => tab.GroupId != group)).ToArray();
            string[] unexpectedGroups = tabs.Where(tab => !expectedGroups.Contains(tab.GroupId, StringComparer.Ordinal))
                .Select(tab => tab.GroupId).ToArray();

            if (tabsProperty.arraySize != expectedGroups.Count || tabs.Count != expectedGroups.Count ||
                tabs.Any(tab => !tab.IsConfigured || tab.Label.text != tab.GroupId) ||
                !tabs.Select(tab => tab.GroupId).SequenceEqual(expectedGroups, StringComparer.Ordinal) ||
                duplicateGroups.Length > 0 || missingGroups.Length > 0 ||
                unexpectedGroups.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Invalid morph tabs. Configured: {tabs.Count}/{expectedGroups.Count}, " +
                    $"duplicates: {string.Join(", ", duplicateGroups)}, missing: {string.Join(", ", missingGroups)}, " +
                    $"unexpected: {string.Join(", ", unexpectedGroups)}.");
            }

            RectTransform tabRailContent =
                (RectTransform)serializedMenu.FindProperty("tabRailContent").objectReferenceValue;
            if (tabs.Any(tab => tab.transform.parent != tabRailContent))
            {
                throw new InvalidOperationException("Every morph tab must be a direct child of the tab rail content.");
            }

            if (tabs[0].transform.GetSiblingIndex() >= tabs[1].transform.GetSiblingIndex())
            {
                throw new InvalidOperationException(
                    $"The {CharacterCustomizationUiConfig.PresetsGroupId} tab must be authored above {CharacterCustomizationUiConfig.DefaultMorphGroupId}.");
            }
        }

        private static void ValidateScrollRect(ScrollRect scrollRect, string label)
        {
            if (scrollRect == null || !scrollRect.vertical || scrollRect.horizontal || scrollRect.viewport == null ||
                scrollRect.content == null ||
                (scrollRect.viewport.GetComponent<RectMask2D>() == null &&
                 scrollRect.viewport.GetComponent<Mask>() == null))
            {
                throw new InvalidOperationException($"The {label} ScrollRect wiring is incomplete.");
            }
        }

        private static void ValidateStatGrowthDefinitions()
        {
            IReadOnlyList<StatGrowthDefinition> growthDefinitions = CharacterStatGrowthCatalog.Definitions;
            string[] requiredIds = { "muscle", "body_fat" };
            string[] configuredIds = growthDefinitions.Select(definition => definition.Id).ToArray();
            if (configuredIds.Distinct(StringComparer.Ordinal).Count() != growthDefinitions.Count ||
                requiredIds.Any(requiredId => !configuredIds.Contains(requiredId, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException(
                    "The stat-growth catalogue must contain unique muscle and body-fat definitions.");
            }

            foreach (StatGrowthDefinition growthDefinition in growthDefinitions)
            {
                if (!CharacterMorphCatalog.TryGet(
                        growthDefinition.MorphId,
                        out CharacterMorphDefinition morphDefinition) ||
                    growthDefinition.MinimumMorphValue < morphDefinition.MinimumValue ||
                    growthDefinition.MaximumMorphValue > 1f ||
                    growthDefinition.MinimumMorphValue > growthDefinition.MaximumMorphValue)
                {
                    throw new InvalidOperationException(
                        $"Stat growth '{growthDefinition.Id}' has an invalid morph mapping or value range.");
                }
            }
        }

        private static void ValidateScene(Scene scene)
        {
            CharacterMorphController controller = FindFirst<CharacterMorphController>(scene);
            CharacterProfile profile = FindFirst<CharacterProfile>(scene);
            CharacterPreviewControls preview = FindFirst<CharacterPreviewControls>(scene);
            CharacterMorphDemoUI menu = FindFirst<CharacterMorphDemoUI>(scene);
            CharacterFinalizationFlow flow = FindFirst<CharacterFinalizationFlow>(scene);
            InputSystemUIInputModule inputModule = FindFirst<InputSystemUIInputModule>(scene);
            InputActionAsset expectedActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (controller == null || profile == null || preview == null || menu == null || flow == null ||
                inputModule == null || expectedActions == null)
            {
                throw new InvalidOperationException(
                    "The demo scene is missing its controller, profile, preview, menu, finalization flow, UI input module, or input-actions asset.");
            }

            var serializedMenu = new SerializedObject(menu);
            var serializedFlow = new SerializedObject(flow);
            var missingLinks = new List<string>();
            if (serializedMenu.FindProperty("controller").objectReferenceValue != controller)
            {
                missingLinks.Add("menu.controller");
            }

            if (serializedMenu.FindProperty("profile").objectReferenceValue == null)
            {
                missingLinks.Add("menu.profile");
            }

            if (serializedFlow.FindProperty("profile").objectReferenceValue == null)
            {
                missingLinks.Add("flow.profile");
            }

            if (serializedFlow.FindProperty("previewControls").objectReferenceValue == null)
            {
                missingLinks.Add("flow.previewControls");
            }

            if (missingLinks.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The menu and finalization flow are missing scene character links: {string.Join(", ", missingLinks)}.");
            }

            var serializedProfile = new SerializedObject(profile);
            if (serializedProfile.FindProperty("controller").objectReferenceValue != controller ||
                serializedProfile.FindProperty("skinPalette").objectReferenceValue == null ||
                serializedProfile.FindProperty("femaleSkinRenderers").arraySize == 0 ||
                serializedProfile.FindProperty("maleSkinRenderers").arraySize == 0)
            {
                throw new InvalidOperationException("The scene character profile has incomplete controller, palette, or skin-renderer bindings.");
            }

            var serializedPreview = new SerializedObject(preview);
            Vector2 viewportFocus = serializedPreview.FindProperty("viewportFocus").vector2Value;
            if (viewportFocus.x < 0.6f || viewportFocus.x > 0.75f ||
                viewportFocus.y < 0.45f || viewportFocus.y > 0.55f)
            {
                throw new InvalidOperationException(
                    "The preview camera should frame the character in the open screen space beside the UI.");
            }

            ValidateInputModule(inputModule, expectedActions);
            ValidateMenuOverrides(menu);
            var serializedController = new SerializedObject(controller);
            ValidateMorphRoot(
                serializedController.FindProperty("femaleRoot").objectReferenceValue as GameObject,
                CharacterSex.Female);
            ValidateMorphRoot(
                serializedController.FindProperty("maleRoot").objectReferenceValue as GameObject,
                CharacterSex.Male);
        }

        private static void ValidateInputModule(InputSystemUIInputModule module, InputActionAsset expectedActions)
        {
            if (module.actionsAsset != expectedActions)
            {
                throw new InvalidOperationException("The UI input module references the wrong input-actions asset.");
            }

            var serializedModule = new SerializedObject(module);
            foreach (string propertyName in new[]
                     {
                         "m_PointAction", "m_MoveAction", "m_SubmitAction", "m_CancelAction", "m_LeftClickAction",
                         "m_MiddleClickAction", "m_RightClickAction", "m_ScrollWheelAction",
                         "m_TrackedDevicePositionAction", "m_TrackedDeviceOrientationAction"
                     })
            {
                UnityEngine.Object reference = serializedModule.FindProperty(propertyName).objectReferenceValue;
                if (reference == null || AssetDatabase.GetAssetPath(reference) != InputActionsPath)
                {
                    throw new InvalidOperationException($"UI input reference '{propertyName}' is invalid.");
                }
            }
        }

        private static void ValidateMenuOverrides(CharacterMorphDemoUI menu)
        {
            GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(menu.gameObject);
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instanceRoot) ?? Array.Empty<PropertyModification>();
            int controllerOverrides = modifications.Count(modification => modification.propertyPath == "controller");
            int profileOverrides = modifications.Count(modification => modification.propertyPath == "profile");
            int previewOverrides = modifications.Count(modification => modification.propertyPath == "previewControls");
            if (controllerOverrides < 1 || profileOverrides < 2 || previewOverrides < 1)
            {
                throw new InvalidOperationException(
                    $"The menu instance must keep its controller, profile, and preview-controls reference overrides; " +
                    $"found {controllerOverrides}, {profileOverrides}, and {previewOverrides}.");
            }
        }

        private static void ValidateMorphRoot(GameObject root, CharacterSex sex)
        {
            if (root == null)
            {
                throw new InvalidOperationException($"Missing {sex} character root.");
            }

            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>();
            if (modifications.Any(modification =>
                    modification.propertyPath.StartsWith("m_BlendShapeWeights", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"{sex} character still has saved blendshape-weight overrides.");
            }

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
            {
                bool hasPositive = HasBlendShape(renderers, definition.GetPositiveShape(sex));
                bool hasNegative = !definition.RequiresNegativeShape ||
                                   HasBlendShape(renderers, definition.GetNegativeShape(sex));
                if (!hasPositive || !hasNegative)
                {
                    throw new InvalidOperationException($"{sex} is missing morph '{definition.Id}'.");
                }
            }
        }

        private static bool HasBlendShape(IEnumerable<SkinnedMeshRenderer> renderers, string shapeName)
        {
            return renderers.Any(renderer =>
                renderer.sharedMesh != null && renderer.sharedMesh.GetBlendShapeIndex(shapeName) >= 0);
        }

        private static T FindFirst<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

    }
}
