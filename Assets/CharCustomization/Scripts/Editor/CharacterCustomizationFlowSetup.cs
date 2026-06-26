using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sol.CharacterCustomization.Editor
{
    public static class CharacterCustomizationFlowSetup
    {
        private const string MenuPrefabPath = "Assets/CharCustomization/Prefabs/CharacterMorphMenu.prefab";
        private const string ManagerPrefabPath = "Assets/CharCustomization/Prefabs/CharacterMorphManager.prefab";
        private const string DemoScenePath = "Assets/CharCustomization/Scenes/DemoScene.unity";
        private const string SkinPalettePath = "Assets/CharCustomization/Presets/DefaultSkinPalette.asset";
        private const string DemoPresetPath = "Assets/CharCustomization/Presets/DemoPreset.asset";
        private const string PresetLibraryPath = "Assets/CharCustomization/Presets/PresetLibrary.asset";

        private static readonly CharacterSkinTone[] DefaultTones =
        {
            new("porcelain", "Porcelain", Hex("F6D2B8")),
            new("fair", "Fair", Hex("EBB892")),
            new("light_brown", "Light Brown", Hex("D89B72")),
            new("tan", "Tan", Hex("BD7958")),
            new("medium", "Medium", Hex("9D5E43")),
            new("brown", "Brown", Hex("78432F")),
            new("dark", "Dark", Hex("573023")),
            new("deep", "Deep", Hex("351D18"))
        };

        private static bool running;

        [MenuItem("Tools/Character Customization/Setup Finalization Flow")]
        public static void Run()
        {
            if (running)
            {
                return;
            }

            running = true;
            try
            {
                CharacterSkinPalette palette = GetOrCreatePalette();
                MigratePresetAssets();
                ConfigureManagerPrefab(palette);
                ConfigureMenuPrefab();
                AssetDatabase.SaveAssets();
                ConfigureDemoScene();
                AssetDatabase.SaveAssets();
                Debug.Log("Character customization finalization, skin, profile, and camera flow setup completed.");
            }
            finally
            {
                running = false;
            }
        }

        private static CharacterSkinPalette GetOrCreatePalette()
        {
            CharacterSkinPalette palette = AssetDatabase.LoadAssetAtPath<CharacterSkinPalette>(SkinPalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<CharacterSkinPalette>();
                AssetDatabase.CreateAsset(palette, SkinPalettePath);
            }

            if (palette.Tones.Count == 0)
            {
                palette.ConfigureDefaults(DefaultTones, CharacterRecipe.DefaultSkinToneId);
            }

            return palette;
        }

        private static void MigratePresetAssets()
        {
            var presets = new List<CharacterPreset>();
            CharacterPreset demoPreset = AssetDatabase.LoadAssetAtPath<CharacterPreset>(DemoPresetPath);
            if (demoPreset != null)
            {
                presets.Add(demoPreset);
            }

            presets.AddRange(AssetDatabase.LoadAllAssetsAtPath(PresetLibraryPath).OfType<CharacterPreset>());
            foreach (CharacterPreset preset in presets.Distinct())
            {
                _ = preset.Recipe;
                EditorUtility.SetDirty(preset);
            }
        }

        private static void ConfigureManagerPrefab(CharacterSkinPalette palette)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ManagerPrefabPath);
            try
            {
                CharacterMorphController controller = root.GetComponent<CharacterMorphController>();
                CharacterProfile profile = root.GetComponent<CharacterProfile>();
                if (profile == null)
                {
                    profile = root.AddComponent<CharacterProfile>();
                }

                var serializedProfile = new SerializedObject(profile);
                serializedProfile.FindProperty("controller").objectReferenceValue = controller;
                serializedProfile.FindProperty("skinPalette").objectReferenceValue = palette;
                serializedProfile.ApplyModifiedPropertiesWithoutUndo();

                CharacterPreviewControls preview = root.GetComponent<CharacterPreviewControls>();
                if (preview != null)
                {
                    var serializedPreview = new SerializedObject(preview);
                    serializedPreview.FindProperty("viewportFocus").vector2Value = new Vector2(0.66f, 0.5f);
                    serializedPreview.ApplyModifiedPropertiesWithoutUndo();
                }

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ManagerPrefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException("Unity could not save the character manager prefab.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureMenuPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(MenuPrefabPath);
            try
            {
                CharacterMorphDemoUI menu = root.GetComponent<CharacterMorphDemoUI>();
                var serializedMenu = new SerializedObject(menu);
                ScrollRect slider = (ScrollRect)serializedMenu.FindProperty("mainSliderScrollRect").objectReferenceValue;
                RectTransform tabContent =
                    (RectTransform)serializedMenu.FindProperty("tabRailContent").objectReferenceValue;
                GameObject presetPanel =
                    (GameObject)serializedMenu.FindProperty("presetPanel").objectReferenceValue;
                Button buttonTemplate =
                    (Button)serializedMenu.FindProperty("resetGroupButton").objectReferenceValue;
                RectTransform resetRect = buttonTemplate.GetComponent<RectTransform>();
                if (resetRect.anchoredPosition.y < 100f)
                {
                    resetRect.anchoredPosition += Vector2.up * 100f;
                }
                CharacterMorphTabButton skinTab = EnsureSkinTab(root, tabContent);
                SkinUi skinUi = EnsureSkinPanel(root, slider, buttonTemplate);
                FooterUi footer = EnsureFooter(root, slider, presetPanel, skinUi.Panel, buttonTemplate);

                CharacterMorphTabButton[] tabs = root.GetComponentsInChildren<CharacterMorphTabButton>(true);
                IReadOnlyList<string> tabOrder = CharacterCustomizationUiConfig.TabGroups;
                var orderedTabs = new CharacterMorphTabButton[tabOrder.Count];
                for (int index = 0; index < tabOrder.Count; index++)
                {
                    string groupId = tabOrder[index];
                    orderedTabs[index] = tabs.FirstOrDefault(tab => tab.GroupId == groupId);
                    if (orderedTabs[index] == null)
                    {
                        throw new InvalidOperationException($"Missing authored tab '{groupId}'.");
                    }

                    orderedTabs[index].transform.SetSiblingIndex(index);
                }

                serializedMenu.Update();
                SetObject(serializedMenu, "skinPanel", skinUi.Panel);
                SetObject(serializedMenu, "customColorToggleButton", skinUi.CustomToggle);
                SetObject(serializedMenu, "customColorPanel", skinUi.CustomPanel);
                SetObject(serializedMenu, "hueSlider", skinUi.Hue);
                SetObject(serializedMenu, "saturationSlider", skinUi.Saturation);
                SetObject(serializedMenu, "valueSlider", skinUi.Value);
                SetObject(serializedMenu, "customColorPreview", skinUi.Preview);
                SerializedProperty swatches = serializedMenu.FindProperty("skinSwatches");
                swatches.arraySize = skinUi.Swatches.Length;
                for (int index = 0; index < skinUi.Swatches.Length; index++)
                {
                    swatches.GetArrayElementAtIndex(index).objectReferenceValue = skinUi.Swatches[index];
                }

                SerializedProperty tabProperty = serializedMenu.FindProperty("tabButtons");
                tabProperty.arraySize = orderedTabs.Length;
                for (int index = 0; index < orderedTabs.Length; index++)
                {
                    tabProperty.GetArrayElementAtIndex(index).objectReferenceValue = orderedTabs[index];
                }
                serializedMenu.ApplyModifiedPropertiesWithoutUndo();

                CharacterFinalizationFlow flow = root.GetComponent<CharacterFinalizationFlow>();
                if (flow == null)
                {
                    flow = root.AddComponent<CharacterFinalizationFlow>();
                }

                var serializedFlow = new SerializedObject(flow);
                SetObject(serializedFlow, "demoUI", menu);
                SetObject(serializedFlow, "characterNameInput", footer.NameInput);
                SetObject(serializedFlow, "randomizeButton", footer.Randomize);
                SetObject(serializedFlow, "finalizeButton", footer.Finalize);
                SetObject(serializedFlow, "finalizeButtonLabel", footer.FinalizeLabel);
                SetObject(serializedFlow, "statusLabel", footer.Status);
                SetObject(serializedFlow, "customizationInterface", root);
                serializedFlow.ApplyModifiedPropertiesWithoutUndo();

                ConfigureRaycastTargets(root);
                skinUi.Panel.SetActive(false);
                skinUi.CustomPanel.SetActive(false);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, MenuPrefabPath);
                if (saved == null)
                {
                    throw new InvalidOperationException("Unity could not save the character menu prefab.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static CharacterMorphTabButton EnsureSkinTab(GameObject root, Transform parent)
        {
            CharacterMorphTabButton existing = root.GetComponentsInChildren<CharacterMorphTabButton>(true)
                .FirstOrDefault(tab => tab.GroupId == CharacterCustomizationUiConfig.SkinGroupId);
            if (existing != null)
            {
                return existing;
            }

            CharacterMorphTabButton template = root.GetComponentsInChildren<CharacterMorphTabButton>(true)
                .First(tab => tab.GroupId == CharacterCustomizationUiConfig.DefaultMorphGroupId);
            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
            clone.name = "Skin Tab";
            var tab = clone.GetComponent<CharacterMorphTabButton>();
            var serializedTab = new SerializedObject(tab);
            serializedTab.FindProperty("groupId").stringValue = CharacterCustomizationUiConfig.SkinGroupId;
            serializedTab.ApplyModifiedPropertiesWithoutUndo();
            tab.Label.text = CharacterCustomizationUiConfig.SkinGroupId;
            return tab;
        }

        private static SkinUi EnsureSkinPanel(GameObject root, ScrollRect slider, Button buttonTemplate)
        {
            GameObject existing = FindObject(root, "Skin Panel");
            if (existing != null)
            {
                return ReadSkinUi(existing);
            }

            var panel = new GameObject("Skin Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.layer = slider.gameObject.layer;
            panel.transform.SetParent(slider.transform.parent, false);
            CopyRect(slider.GetComponent<RectTransform>(), panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.96f);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateText(panel.transform, "Skin Heading", "Skin Tone", 28f, 38f);
            var gridObject = new GameObject("Skin Swatches", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            gridObject.layer = panel.layer;
            gridObject.transform.SetParent(panel.transform, false);
            GridLayoutGroup grid = gridObject.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(138f, 58f);
            grid.spacing = new Vector2(10f, 10f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            gridObject.GetComponent<LayoutElement>().preferredHeight = 126f;

            var swatches = new CharacterSkinSwatchButton[DefaultTones.Length];
            for (int index = 0; index < DefaultTones.Length; index++)
            {
                CharacterSkinTone tone = DefaultTones[index];
                Button button = CloneButton(buttonTemplate, gridObject.transform, $"Skin {tone.Label}", tone.Label);
                Image image = button.GetComponent<Image>();
                image.color = tone.Color;
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                label.color = tone.Color.grayscale < 0.45f ? Color.white : Color.black;
                var swatch = button.gameObject.AddComponent<CharacterSkinSwatchButton>();
                var serializedSwatch = new SerializedObject(swatch);
                serializedSwatch.FindProperty("skinToneId").stringValue = tone.Id;
                serializedSwatch.FindProperty("button").objectReferenceValue = button;
                serializedSwatch.FindProperty("swatchImage").objectReferenceValue = image;
                serializedSwatch.FindProperty("label").objectReferenceValue = label;
                serializedSwatch.ApplyModifiedPropertiesWithoutUndo();
                swatches[index] = swatch;
            }

            Button customToggle = CloneButton(buttonTemplate, panel.transform, "Advanced Custom Colour Button", "Advanced Custom Colour");
            customToggle.gameObject.GetComponent<LayoutElement>().preferredHeight = 48f;
            var customPanel = new GameObject("Advanced Custom Colour Panel", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            customPanel.layer = panel.layer;
            customPanel.transform.SetParent(panel.transform, false);
            VerticalLayoutGroup customLayout = customPanel.GetComponent<VerticalLayoutGroup>();
            customLayout.spacing = 8f;
            customLayout.childControlWidth = true;
            customLayout.childControlHeight = true;
            customLayout.childForceExpandWidth = true;
            customLayout.childForceExpandHeight = false;
            customPanel.GetComponent<LayoutElement>().preferredHeight = 190f;

            var previewObject = new GameObject("Custom Colour Preview", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            previewObject.layer = panel.layer;
            previewObject.transform.SetParent(customPanel.transform, false);
            previewObject.GetComponent<LayoutElement>().preferredHeight = 38f;
            Image preview = previewObject.GetComponent<Image>();
            preview.color = DefaultTones[1].Color;

            Slider hue = CreateSliderRow(customPanel.transform, "Hue");
            Slider saturation = CreateSliderRow(customPanel.transform, "Saturation");
            Slider value = CreateSliderRow(customPanel.transform, "Value");
            hue.minValue = saturation.minValue = value.minValue = 0f;
            hue.maxValue = saturation.maxValue = value.maxValue = 1f;
            Color.RGBToHSV(DefaultTones[1].Color, out float h, out float s, out float v);
            hue.value = h;
            saturation.value = s;
            value.value = v;

            return new SkinUi(panel, customToggle, customPanel, hue, saturation, value, preview, swatches);
        }

        private static SkinUi ReadSkinUi(GameObject panel)
        {
            CharacterSkinSwatchButton[] swatches = panel.GetComponentsInChildren<CharacterSkinSwatchButton>(true);
            Button customToggle = FindComponent<Button>(panel, "Advanced Custom Colour Button");
            GameObject customPanel = FindObject(panel, "Advanced Custom Colour Panel");
            Slider hue = FindComponent<Slider>(panel, "Hue Slider");
            Slider saturation = FindComponent<Slider>(panel, "Saturation Slider");
            Slider value = FindComponent<Slider>(panel, "Value Slider");
            Image preview = FindComponent<Image>(panel, "Custom Colour Preview");
            return new SkinUi(panel, customToggle, customPanel, hue, saturation, value, preview, swatches);
        }

        private static FooterUi EnsureFooter(
            GameObject root,
            ScrollRect slider,
            GameObject presetPanel,
            GameObject skinPanel,
            Button buttonTemplate)
        {
            GameObject existing = FindObject(root, "Finalization Footer");
            if (existing != null && IsFooterComplete(existing))
            {
                return ReadFooter(existing);
            }

            FooterUi movedFooter = ReadMovedFooter(root);
            if (movedFooter.IsComplete)
            {
                return movedFooter;
            }

            RectTransform sliderRect = slider.GetComponent<RectTransform>();
            var footer = new GameObject("Finalization Footer", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            footer.layer = slider.gameObject.layer;
            footer.transform.SetParent(slider.transform.parent, false);
            RectTransform footerRect = footer.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(sliderRect.anchorMin.x, 0f);
            footerRect.anchorMax = new Vector2(sliderRect.anchorMax.x, 0f);
            footerRect.pivot = new Vector2(sliderRect.pivot.x, 0f);
            footerRect.anchoredPosition = new Vector2(sliderRect.anchoredPosition.x, 12f);
            footerRect.sizeDelta = new Vector2(sliderRect.sizeDelta.x, 88f);
            footer.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.98f);
            VerticalLayoutGroup footerLayout = footer.GetComponent<VerticalLayoutGroup>();
            footerLayout.padding = new RectOffset(12, 12, 8, 6);
            footerLayout.spacing = 4f;
            footerLayout.childControlWidth = true;
            footerLayout.childControlHeight = true;
            footerLayout.childForceExpandWidth = true;
            footerLayout.childForceExpandHeight = false;

            var row = new GameObject("Finalization Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.layer = footer.layer;
            row.transform.SetParent(footer.transform, false);
            HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;
            row.GetComponent<LayoutElement>().preferredHeight = 48f;

            GameObject inputObject = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            inputObject.name = "Character Name Input";
            inputObject.transform.SetParent(row.transform, false);
            TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
            input.characterLimit = 48;
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "Character name";
                placeholder.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            }
            input.textComponent.color = Color.white;
            inputObject.GetComponent<Image>().color = new Color(0.22f, 0.22f, 0.22f, 1f);
            inputObject.AddComponent<LayoutElement>().preferredWidth = 280f;

            Button randomize = CloneButton(buttonTemplate, row.transform, "Randomize Button", "Randomize");
            randomize.gameObject.GetComponent<LayoutElement>().preferredWidth = 150f;
            Button finalize = CloneButton(buttonTemplate, row.transform, "Finalize Button", "Finalize");
            finalize.gameObject.GetComponent<LayoutElement>().preferredWidth = 150f;
            TMP_Text finalizeLabel = finalize.GetComponentInChildren<TMP_Text>(true);
            TMP_Text status = CreateText(footer.transform, "Finalization Status", string.Empty, 17f, 24f);
            status.color = new Color(0.65f, 0.9f, 0.7f, 1f);

            return new FooterUi(input, randomize, finalize, finalizeLabel, status);
        }

        private static bool IsFooterComplete(GameObject footer)
        {
            return FindComponent<TMP_InputField>(footer, "Character Name Input") != null &&
                   FindComponent<Button>(footer, "Randomize Button") != null &&
                   FindComponent<Button>(footer, "Finalize Button") != null &&
                   FindComponent<TMP_Text>(footer, "Finalization Status") != null &&
                   FindObject(footer, "Finalization Actions") != null;
        }

        private static FooterUi ReadMovedFooter(GameObject root)
        {
            TMP_InputField input = FindComponent<TMP_InputField>(root, "Character Name Input");
            Button randomize = FindComponent<Button>(root, "Randomize Button");
            Button finalize = FindComponent<Button>(root, "Finalize Button") ??
                              FindComponent<Button>(root, "Confirm Button");
            TMP_Text status = FindComponent<TMP_Text>(root, "Finalization Status") ??
                              FindComponent<TMP_Text>(root, "SystemMessages");
            TMP_Text finalizeLabel = finalize != null ? finalize.GetComponentInChildren<TMP_Text>(true) : null;
            return new FooterUi(input, randomize, finalize, finalizeLabel, status);
        }

        private static void ConfigureRaycastTargets(GameObject root)
        {
            ScrollRect[] scrollRects = root.GetComponentsInChildren<ScrollRect>(true);
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                bool belongsToControl = graphic.GetComponentInParent<Selectable>(true) != null;
                bool belongsToScrollSurface = false;
                foreach (ScrollRect scrollRect in scrollRects)
                {
                    if (scrollRect.GetComponent<Graphic>() == graphic ||
                        (scrollRect.viewport != null && scrollRect.viewport.GetComponent<Graphic>() == graphic))
                    {
                        belongsToScrollSurface = true;
                        break;
                    }
                }

                bool belongsToMask = graphic.GetComponent<Mask>() != null || graphic.GetComponent<RectMask2D>() != null;
                graphic.raycastTarget = belongsToControl || belongsToScrollSurface || belongsToMask;
            }
        }

        private static FooterUi ReadFooter(GameObject footer)
        {
            TMP_InputField input = FindComponent<TMP_InputField>(footer, "Character Name Input");
            Button randomize = FindComponent<Button>(footer, "Randomize Button");
            Button finalize = FindComponent<Button>(footer, "Finalize Button");
            TMP_Text status = FindComponent<TMP_Text>(footer, "Finalization Status");
            return new FooterUi(input, randomize, finalize, finalize.GetComponentInChildren<TMP_Text>(true), status);
        }

        private static void ConfigureDemoScene()
        {
            SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
            bool canRestore = previousSetup.Any(setup => setup.isLoaded && setup.isActive);
            try
            {
                Scene scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
                CharacterMorphController controller = FindFirst<CharacterMorphController>(scene);
                CharacterProfile profile = FindFirst<CharacterProfile>(scene);
                CharacterMorphDemoUI menu = FindFirst<CharacterMorphDemoUI>(scene);
                CharacterFinalizationFlow flow = FindFirst<CharacterFinalizationFlow>(scene);
                CharacterPreviewControls preview = FindFirst<CharacterPreviewControls>(scene);
                if (controller == null || profile == null || menu == null || flow == null || preview == null)
                {
                    throw new InvalidOperationException("The demo scene is missing a customization flow component after prefab migration.");
                }

                var serializedController = new SerializedObject(controller);
                GameObject femaleRoot = serializedController.FindProperty("femaleRoot").objectReferenceValue as GameObject;
                GameObject maleRoot = serializedController.FindProperty("maleRoot").objectReferenceValue as GameObject;
                var serializedProfile = new SerializedObject(profile);
                AssignRenderers(serializedProfile.FindProperty("femaleSkinRenderers"), FindSkinRenderers(femaleRoot, "Human_F_Body"));
                AssignRenderers(serializedProfile.FindProperty("maleSkinRenderers"), FindSkinRenderers(maleRoot, "M_HumanMale"));
                serializedProfile.ApplyModifiedPropertiesWithoutUndo();

                var serializedMenu = new SerializedObject(menu);
                SetObject(serializedMenu, "controller", controller);
                SetObject(serializedMenu, "profile", profile);
                serializedMenu.ApplyModifiedPropertiesWithoutUndo();

                var serializedFlow = new SerializedObject(flow);
                SetObject(serializedFlow, "profile", profile);
                SetObject(serializedFlow, "demoUI", menu);
                SetObject(serializedFlow, "previewControls", preview);
                serializedFlow.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (canRestore)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                }
            }
        }

        private static Renderer[] FindSkinRenderers(GameObject root, string materialName)
        {
            if (root == null)
            {
                return Array.Empty<Renderer>();
            }

            return root.GetComponentsInChildren<Renderer>(true)
                .Where(target => target.sharedMaterials.Any(material =>
                    material != null && string.Equals(material.name, materialName, StringComparison.Ordinal)))
                .ToArray();
        }

        private static void AssignRenderers(SerializedProperty property, Renderer[] renderers)
        {
            property.arraySize = renderers.Length;
            for (int index = 0; index < renderers.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = renderers[index];
            }
        }

        private static Slider CreateSliderRow(Transform parent, string label)
        {
            var row = new GameObject($"{label} Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.layer = parent.gameObject.layer;
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            row.GetComponent<LayoutElement>().preferredHeight = 38f;
            TMP_Text text = CreateText(row.transform, $"{label} Label", label, 18f, 38f);
            text.gameObject.GetComponent<LayoutElement>().preferredWidth = 110f;

            GameObject sliderObject = DefaultControls.CreateSlider(new DefaultControls.Resources());
            sliderObject.name = $"{label} Slider";
            sliderObject.layer = parent.gameObject.layer;
            sliderObject.transform.SetParent(row.transform, false);
            sliderObject.AddComponent<LayoutElement>().preferredWidth = 430f;
            return sliderObject.GetComponent<Slider>();
        }

        private static TMP_Text CreateText(Transform parent, string objectName, string value, float size, float height)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.layer = parent.gameObject.layer;
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.text = value;
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            textObject.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

        private static Button CloneButton(Button template, Transform parent, string objectName, string label)
        {
            GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, parent);
            clone.name = objectName;
            Button button = clone.GetComponent<Button>();
            TMP_Text text = clone.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }

            LayoutElement layout = clone.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = clone.AddComponent<LayoutElement>();
            }
            layout.preferredHeight = 48f;
            return button;
        }

        private static void CopyRect(RectTransform source, RectTransform destination)
        {
            destination.anchorMin = source.anchorMin;
            destination.anchorMax = source.anchorMax;
            destination.pivot = source.pivot;
            destination.anchoredPosition = source.anchoredPosition;
            destination.sizeDelta = source.sizeDelta;
            destination.localScale = source.localScale;
        }

        private static void SetObject(SerializedObject serializedObject, string propertyName, UnityEngine.Object value)
        {
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
        }

        private static GameObject FindObject(GameObject root, string objectName)
        {
            Transform match = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.gameObject.name == objectName);
            return match != null ? match.gameObject : null;
        }

        private static T FindComponent<T>(GameObject root, string objectName) where T : Component
        {
            return root.GetComponentsInChildren<T>(true)
                .FirstOrDefault(component => component.gameObject.name == objectName);
        }

        private static T FindFirst<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T result = root.GetComponentInChildren<T>(true);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex, out Color color) ? color : Color.white;
        }

        private readonly struct SkinUi
        {
            public SkinUi(
                GameObject panel,
                Button customToggle,
                GameObject customPanel,
                Slider hue,
                Slider saturation,
                Slider value,
                Image preview,
                CharacterSkinSwatchButton[] swatches)
            {
                Panel = panel;
                CustomToggle = customToggle;
                CustomPanel = customPanel;
                Hue = hue;
                Saturation = saturation;
                Value = value;
                Preview = preview;
                Swatches = swatches;
            }

            public GameObject Panel { get; }
            public Button CustomToggle { get; }
            public GameObject CustomPanel { get; }
            public Slider Hue { get; }
            public Slider Saturation { get; }
            public Slider Value { get; }
            public Image Preview { get; }
            public CharacterSkinSwatchButton[] Swatches { get; }
        }

        private readonly struct FooterUi
        {
            public FooterUi(
                TMP_InputField nameInput,
                Button randomize,
                Button finalize,
                TMP_Text finalizeLabel,
                TMP_Text status)
            {
                NameInput = nameInput;
                Randomize = randomize;
                Finalize = finalize;
                FinalizeLabel = finalizeLabel;
                Status = status;
            }

            public TMP_InputField NameInput { get; }
            public Button Randomize { get; }
            public Button Finalize { get; }
            public TMP_Text FinalizeLabel { get; }
            public TMP_Text Status { get; }
            public bool IsComplete => NameInput != null && Randomize != null && Finalize != null && Status != null;
        }
    }
}
