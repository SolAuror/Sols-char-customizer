using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Sol.CharacterCustomization.EditorTools
{
    // Adds hair to the demo customizer: a default hair colour palette, a CharacterHair
    // beside the character profile, and a Hair tab in the demo menu with the hair
    // picker. The tab and its panel are built from the menu's own controls (the Skin
    // tab, the preset dropdown, the skin preview strip and slider rows) so they match.
    public static class SolHairMenuSetup
    {
        private const string DemoMenuPrefabPath = "Assets/CharCustomization/Resources/Prefabs/CharacterMorphMenuDemoUI.prefab";
        private const string ManagerPrefabPath = "Assets/CharCustomization/Resources/Prefabs/CharacterMorphManager.prefab";
        private const string PalettePath = "Assets/CharCustomization/CustomizationPresets/DefaultHairPalette.asset";
        private const float ContentWidth = 537f;
        private const float RowHeight = 40f;
        private const float SwatchSize = 48f;
        private const float SwatchSpacing = 10f;
        private const int GridInset = 4;
        private const float TileSize = 96f;
        private const float TileNameHeight = 20f;
        private static readonly Color TileColour = new(0.16f, 0.16f, 0.16f, 1f);
        // Two and a half rows of style tiles and three rows of colours show; more scroll.
        private const float StyleAreaHeight = TileSize * 2.5f + SwatchSpacing * 2f + GridInset;
        private const float SwatchAreaHeight = SwatchSize * 3f + SwatchSpacing * 2f + GridInset * 2;
        private const float PanelTopPadding = 8f;
        private const float SectionSpacing = 8f;
        private const float HeadingHeight = 34f;
        private const float PreviewHeight = 22f;
        // Matches the skin picker's spacing between preview, field, and slider rows.
        private const float PickerRowSpacing = 5f;
        private const float PickerHeight =
            PreviewHeight + SolSkinPickerSetup.PlaneHeight + RowHeight * 4f + PickerRowSpacing * 5f;

        [MenuItem("Tools/Sol/Character Customization/Hair/Build Hair Tab In Demo Menu")]
        private static void Build()
        {
            CharacterHairPalette palette = CreateOrLoadPalette();
            if (!AddHairToManager(palette, out string managerMessage))
            {
                Debug.LogError(managerMessage);
                return;
            }

            GameObject menuRoot = PrefabUtility.LoadPrefabContents(DemoMenuPrefabPath);
            try
            {
                if (!TryBuildTab(menuRoot, out string message))
                {
                    Debug.LogError(message);
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(menuRoot, DemoMenuPrefabPath);
                Debug.Log($"{managerMessage} {message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(menuRoot);
            }
        }

        // Creates the default palette, or adds any default colours an existing one lacks
        // (matched by ID), so colours edited or added in the asset are kept.
        public static CharacterHairPalette CreateOrLoadPalette()
        {
            var palette = AssetDatabase.LoadAssetAtPath<CharacterHairPalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<CharacterHairPalette>();
                palette.ConfigureDefaults(new List<CharacterHairColorOption>(), "brown");
                AssetDatabase.CreateAsset(palette, PalettePath);
            }

            var serializedPalette = new SerializedObject(palette);
            SerializedProperty options = serializedPalette.FindProperty("options");
            foreach ((string id, string label, string hex) in DefaultHairColours)
            {
                if (palette.TryGet(id, out _) || !ColorUtility.TryParseHtmlString(hex, out Color colour))
                {
                    continue;
                }

                options.InsertArrayElementAtIndex(options.arraySize);
                SerializedProperty option = options.GetArrayElementAtIndex(options.arraySize - 1);
                option.FindPropertyRelative("id").stringValue = id;
                option.FindPropertyRelative("label").stringValue = label;
                option.FindPropertyRelative("color").colorValue = colour;
            }

            // Default colours in their order; colours added by hand stay after them.
            int next = 0;
            foreach ((string id, string _, string _) in DefaultHairColours)
            {
                for (int index = next; index < options.arraySize; index++)
                {
                    if (options.GetArrayElementAtIndex(index).FindPropertyRelative("id").stringValue == id)
                    {
                        options.MoveArrayElement(index, next++);
                        break;
                    }
                }
            }

            serializedPalette.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            return palette;
        }

        // sRGB. Natural shades dark to light, then greys, then fashion colours.
        private static readonly (string id, string label, string hex)[] DefaultHairColours =
        {
            ("jet", "Jet Black", "#0A0909"),
            ("black", "Black", "#100D0D"),
            ("soft_black", "Soft Black", "#1C1716"),
            ("darkest_brown", "Darkest Brown", "#241A15"),
            ("dark_brown", "Dark Brown", "#2B1C12"),
            ("chestnut", "Chestnut", "#4A2A1C"),
            ("brown", "Brown", "#45301F"),
            ("mocha", "Mocha", "#5A3D2C"),
            ("walnut", "Walnut", "#6B4A33"),
            ("ash_brown", "Ash Brown", "#6A5D52"),
            ("light_brown", "Light Brown", "#735233"),
            ("mousy", "Mousy", "#8A7560"),
            ("honey_brown", "Honey Brown", "#8C5E33"),
            ("caramel", "Caramel", "#A0703E"),
            ("golden_brown", "Golden Brown", "#9C6B2E"),
            ("dark_blonde", "Dark Blonde", "#A88A5A"),
            ("ash_blonde", "Ash Blonde", "#B8A58A"),
            ("sandy", "Sandy", "#C7AE87"),
            ("honey_blonde", "Honey Blonde", "#C9A05A"),
            ("blonde", "Blonde", "#D1AB6B"),
            ("golden_blonde", "Golden Blonde", "#D6B26E"),
            ("light_blonde", "Light Blonde", "#E3CF9F"),
            ("pale_blonde", "Pale Blonde", "#EDE0C0"),
            ("platinum", "Platinum", "#E6DBC2"),
            ("strawberry", "Strawberry Blonde", "#CC8052"),
            ("ginger", "Ginger", "#B5562A"),
            ("copper", "Copper", "#A85426"),
            ("red_copper", "Red Copper", "#9A3B1F"),
            ("auburn", "Auburn", "#6B2B1A"),
            ("mahogany", "Mahogany", "#6E2E22"),
            ("burgundy", "Burgundy", "#5C1A24"),
            ("salt_pepper", "Salt and Pepper", "#6E6A68"),
            ("ash_grey", "Ash Grey", "#7D7D80"),
            ("grey", "Grey", "#8C8C8C"),
            ("silver", "Silver", "#B9B9BB"),
            ("white", "White", "#E0E0E0"),
            ("red", "Red", "#B31414"),
            ("scarlet", "Scarlet", "#D42A1E"),
            ("orange", "Orange", "#E0782A"),
            ("yellow", "Yellow", "#E8C62E"),
            ("lime", "Lime", "#8CC63F"),
            ("green", "Green", "#2E8040"),
            ("mint", "Mint", "#77C9A4"),
            ("teal", "Teal", "#1E7F86"),
            ("sky", "Sky Blue", "#6EA8DE"),
            ("blue", "Blue", "#264DB8"),
            ("navy", "Navy", "#1B2A5E"),
            ("violet", "Violet", "#5B2A8C"),
            ("purple", "Purple", "#6B339E"),
            ("lavender", "Lavender", "#B49BD8"),
            ("magenta", "Magenta", "#B0287A"),
            ("hot_pink", "Hot Pink", "#E8508E"),
            ("pink", "Pink", "#E673A6"),
            ("pastel_pink", "Pastel Pink", "#F2B6CB"),
        };

        private static bool AddHairToManager(CharacterHairPalette palette, out string message)
        {
            GameObject manager = PrefabUtility.LoadPrefabContents(ManagerPrefabPath);
            try
            {
                var profile = manager.GetComponentInChildren<CharacterProfile>(true);
                if (profile == null)
                {
                    message = $"No {nameof(CharacterProfile)} in {ManagerPrefabPath}.";
                    return false;
                }

                var hair = profile.GetComponent<CharacterHair>();
                if (hair == null)
                {
                    hair = profile.gameObject.AddComponent<CharacterHair>();
                }
                var serializedHair = new SerializedObject(hair);
                var serializedProfile = new SerializedObject(profile);
                serializedHair.FindProperty("controller").objectReferenceValue =
                    serializedProfile.FindProperty("controller").objectReferenceValue;
                if (serializedHair.FindProperty("palette").objectReferenceValue == null)
                {
                    serializedHair.FindProperty("palette").objectReferenceValue = palette;
                }

                serializedHair.ApplyModifiedPropertiesWithoutUndo();
                serializedProfile.FindProperty("hair").objectReferenceValue = hair;
                serializedProfile.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(manager, ManagerPrefabPath);
                message = $"Character hair is on '{profile.name}' in the manager prefab.";
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(manager);
            }
        }

        public static bool TryBuildTab(GameObject menuRoot, out string message)
        {
            CharacterMorphDemoUI menu = menuRoot.GetComponentInChildren<CharacterMorphDemoUI>(true);
            if (menu == null)
            {
                message = $"No {nameof(CharacterMorphDemoUI)} under '{menuRoot.name}'.";
                return false;
            }

            var serializedMenu = new SerializedObject(menu);
            var skinPanel = serializedMenu.FindProperty("skinPanel").objectReferenceValue as GameObject;
            var skinColourPanel = serializedMenu.FindProperty("customColorPanel").objectReferenceValue as GameObject;
            var preview = serializedMenu.FindProperty("customColorPreview").objectReferenceValue as Image;
            Slider sourceSlider = FindSkinSlider(serializedMenu);
            SerializedProperty tabs = serializedMenu.FindProperty("tabButtons");
            int skinTabIndex = -1;
            bool hasHairTab = false;
            for (int index = 0; index < tabs.arraySize; index++)
            {
                if (tabs.GetArrayElementAtIndex(index).objectReferenceValue is CharacterMorphTabButton tab)
                {
                    skinTabIndex = CharacterCustomizationUiConfig.IsSkinGroup(tab.GroupId) ? index : skinTabIndex;
                    hasHairTab |= CharacterCustomizationUiConfig.IsHairGroup(tab.GroupId);
                }
            }

            if (skinPanel == null || skinColourPanel == null || preview == null || sourceSlider == null || skinTabIndex < 0)
            {
                message = "The menu needs its Skin tab, skin panel, custom colour panel, preview, and a skin slider row to copy.";
                return false;
            }

            if (!hasHairTab)
            {
                var skinTab = (CharacterMorphTabButton)tabs.GetArrayElementAtIndex(skinTabIndex).objectReferenceValue;
                tabs.InsertArrayElementAtIndex(skinTabIndex + 1);
                tabs.GetArrayElementAtIndex(skinTabIndex + 1).objectReferenceValue = CopyTab(skinTab);
            }

            // The panel is generated, so a rerun replaces it with the current layout.
            if (serializedMenu.FindProperty("hairPanel").objectReferenceValue is GameObject oldPanel)
            {
                Object.DestroyImmediate(oldPanel);
            }

            // Like the skin tab: the colour picker sits at the bottom, level with the skin
            // one; above it, the style tiles show two and a half rows and the colours three,
            // and both scroll.
            var skinRect = (RectTransform)skinPanel.transform;
            float bottomPadding = Mathf.Max(0f,
                SolSkinPickerSetup.LocalBottom((RectTransform)skinColourPanel.transform) - skinRect.rect.yMin);
            float available = skinRect.rect.height - PanelTopPadding - bottomPadding;
            float styleHeight = StyleAreaHeight;
            float spacer = available - HeadingHeight - styleHeight - SwatchAreaHeight - PickerHeight - SectionSpacing * 4f;
            if (spacer < 0f)
            {
                // Too short for everything: the style tiles give way first, down to two rows.
                styleHeight = Mathf.Max(styleHeight + spacer, TileSize * 2f + SwatchSpacing + GridInset);
                spacer = 0f;
            }

            GameObject panel = CreatePanel(skinPanel, bottomPadding);
            TMP_Text labelStyle = sourceSlider.transform.parent.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => !text.transform.IsChildOf(sourceSlider.transform));
            AddLabel(panel.transform, labelStyle, "Hair", HeadingHeight, 1.3f);
            (RectTransform tiles, Button tileTemplate) = CreateStyleTiles(panel.transform, labelStyle, styleHeight);
            (RectTransform swatches, Button swatchTemplate) = CreateSwatchArea(panel.transform, SwatchAreaHeight);
            var gap = new GameObject("Spacer", typeof(RectTransform)) { layer = panel.layer };
            ((RectTransform)gap.transform).SetParent(panel.transform, false);
            ((RectTransform)gap.transform).sizeDelta = new Vector2(ContentWidth, spacer);

            // The colour picker, laid out like the skin one: preview, field, slider rows.
            Transform picker = CreatePickerBlock(panel.transform);
            Image hairPreview = Copy(preview.gameObject, picker, "Hair Colour Preview", PreviewHeight).GetComponent<Image>();
            CharacterSkinPlaneArea field = SolSkinPickerSetup.CreatePlane(picker, ContentWidth, "Hair Colour Field");
            Slider hue = CopySliderRow(sourceSlider, picker, "Hue");
            Slider saturation = CopySliderRow(sourceSlider, picker, "Saturation");
            Slider value = CopySliderRow(sourceSlider, picker, "Brightness");
            Slider sheen = CopySliderRow(sourceSlider, picker, "Sheen");

            var hairPicker = panel.AddComponent<CharacterHairPicker>();
            var serializedPicker = new SerializedObject(hairPicker);
            serializedPicker.FindProperty("styleTileContent").objectReferenceValue = tiles;
            serializedPicker.FindProperty("styleTileTemplate").objectReferenceValue = tileTemplate;
            serializedPicker.FindProperty("swatchContent").objectReferenceValue = swatches;
            serializedPicker.FindProperty("swatchTemplate").objectReferenceValue = swatchTemplate;
            serializedPicker.FindProperty("hueSlider").objectReferenceValue = hue;
            serializedPicker.FindProperty("saturationSlider").objectReferenceValue = saturation;
            serializedPicker.FindProperty("valueSlider").objectReferenceValue = value;
            serializedPicker.FindProperty("sheenSlider").objectReferenceValue = sheen;
            serializedPicker.FindProperty("colorField").objectReferenceValue = field;
            serializedPicker.FindProperty("preview").objectReferenceValue = hairPreview;
            serializedPicker.ApplyModifiedPropertiesWithoutUndo();

            serializedMenu.FindProperty("hairPanel").objectReferenceValue = panel;
            serializedMenu.FindProperty("hairPicker").objectReferenceValue = hairPicker;
            serializedMenu.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);

            message = (hasHairTab ? "Rebuilt the Hair panel" : "Built the Hair tab and panel") +
                      ": style tiles, scrolling colour swatches, and the colour picker at the bottom.";
            return true;
        }


        private static Slider FindSkinSlider(SerializedObject serializedMenu)
        {
            if (serializedMenu.FindProperty("skinParameterPicker").objectReferenceValue is CharacterSkinParameterPicker picker)
            {
                var slider = new SerializedObject(picker).FindProperty("melaninSlider").objectReferenceValue as Slider;
                if (slider != null)
                {
                    return slider;
                }
            }

            return serializedMenu.FindProperty("hueSlider").objectReferenceValue as Slider;
        }

        private static CharacterMorphTabButton CopyTab(CharacterMorphTabButton skinTab)
        {
            GameObject copy = Object.Instantiate(skinTab.gameObject, skinTab.transform.parent);
            copy.name = "Hair Tab";
            copy.transform.SetSiblingIndex(skinTab.transform.GetSiblingIndex() + 1);
            var tab = copy.GetComponent<CharacterMorphTabButton>();
            var serializedTab = new SerializedObject(tab);
            serializedTab.FindProperty("groupId").stringValue = CharacterCustomizationUiConfig.HairGroupId;
            serializedTab.ApplyModifiedPropertiesWithoutUndo();
            tab.Button.onClick = new Button.ButtonClickedEvent();
            tab.Label.text = "Hair";
            return tab;
        }

        private static GameObject CreatePanel(GameObject skinPanel, float bottomPadding)
        {
            var panel = new GameObject("Hair Panel", typeof(RectTransform), typeof(VerticalLayoutGroup))
            {
                layer = skinPanel.layer
            };
            var rect = (RectTransform)panel.transform;
            var skinRect = (RectTransform)skinPanel.transform;
            rect.SetParent(skinRect.parent, false);
            rect.SetSiblingIndex(skinRect.GetSiblingIndex() + 1);
            rect.anchorMin = skinRect.anchorMin;
            rect.anchorMax = skinRect.anchorMax;
            rect.pivot = skinRect.pivot;
            rect.sizeDelta = skinRect.sizeDelta;
            rect.anchoredPosition = skinRect.anchoredPosition;

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            int sidePadding = Mathf.RoundToInt((skinRect.sizeDelta.x - ContentWidth) * 0.5f);
            layout.padding = new RectOffset(sidePadding, sidePadding, Mathf.RoundToInt(PanelTopPadding),
                Mathf.RoundToInt(bottomPadding));
            layout.spacing = SectionSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return panel;
        }

        private static void AddLabel(Transform parent, TMP_Text style, string text, float height, float scale)
        {
            var labelObject = new GameObject($"{text} Label", typeof(RectTransform), typeof(TextMeshProUGUI))
            {
                layer = parent.gameObject.layer
            };
            var rect = (RectTransform)labelObject.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(ContentWidth, height);
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            if (style != null)
            {
                label.font = style.font;
                label.fontSharedMaterial = style.fontSharedMaterial;
                label.fontSize = style.fontSize * scale;
                label.color = style.color;
            }

            label.text = text;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
        }

        private static GameObject Copy(GameObject source, Transform parent, string name, float height)
        {
            GameObject copy = Object.Instantiate(source, parent);
            copy.name = name;
            copy.SetActive(true);
            var rect = (RectTransform)copy.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ContentWidth, height);
            if (copy.TryGetComponent(out LayoutElement element))
            {
                element.preferredWidth = ContentWidth;
                element.preferredHeight = height;
            }

            return copy;
        }

        // A vertical scroll view whose content is a grid that grows with its children.
        private static (RectTransform content, GridLayoutGroup grid) CreateScrollArea(Transform parent, string name, float height)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect))
            {
                layer = parent.gameObject.layer
            };
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            rootRect.sizeDelta = new Vector2(ContentWidth, height);
            // Clear, but still catches drags between swatches.
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)) { layer = root.layer };
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.SetParent(rootRect, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter))
            {
                layer = root.layer
            };
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(viewportRect, false);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            var grid = content.GetComponent<GridLayoutGroup>();
            // Room for a selected tile, which grows slightly.
            grid.padding = new RectOffset(GridInset, GridInset, GridInset, GridInset);
            grid.spacing = new Vector2(SwatchSpacing, SwatchSpacing);
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            return (contentRect, grid);
        }

        private static (RectTransform content, Button template) CreateSwatchArea(Transform parent, float height)
        {
            (RectTransform content, GridLayoutGroup grid) = CreateScrollArea(parent, "Hair Swatches", height);
            grid.cellSize = new Vector2(SwatchSize, SwatchSize);
            grid.constraintCount = Columns(SwatchSize);

            GameObject swatch = CreateSquare("Hair Swatch Template", content, Color.white);
            swatch.SetActive(false);
            return (content, swatch.GetComponent<Button>());
        }

        // Square tiles, one per hairstyle and one for None: the style's preview over its name.
        private static (RectTransform content, Button template) CreateStyleTiles(Transform parent, TMP_Text labelStyle, float height)
        {
            (RectTransform content, GridLayoutGroup grid) = CreateScrollArea(parent, "Hair Styles", height);
            grid.cellSize = new Vector2(TileSize, TileSize);
            grid.constraintCount = Columns(TileSize);

            GameObject tile = CreateSquare("Hair Style Template", content, TileColour);
            var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(Image)) { layer = tile.layer };
            var previewRect = (RectTransform)previewObject.transform;
            previewRect.SetParent(tile.transform, false);
            previewRect.anchorMin = Vector2.zero;
            previewRect.anchorMax = Vector2.one;
            previewRect.offsetMin = new Vector2(4f, TileNameHeight);
            previewRect.offsetMax = new Vector2(-4f, -4f);
            var previewImage = previewObject.GetComponent<Image>();
            previewImage.preserveAspect = true;
            previewImage.raycastTarget = false;

            var nameObject = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI)) { layer = tile.layer };
            var nameRect = (RectTransform)nameObject.transform;
            nameRect.SetParent(tile.transform, false);
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = new Vector2(1f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.sizeDelta = new Vector2(0f, TileNameHeight);
            var name = nameObject.GetComponent<TextMeshProUGUI>();
            if (labelStyle != null)
            {
                name.font = labelStyle.font;
                name.fontSharedMaterial = labelStyle.fontSharedMaterial;
                name.color = labelStyle.color;
            }

            name.fontSize = 15f;
            name.alignment = TextAlignmentOptions.Center;
            name.raycastTarget = false;

            tile.SetActive(false);
            return (content, tile.GetComponent<Button>());
        }

        // A rounded, outlined square button, styled like the skin and hair swatches.
        private static GameObject CreateSquare(string name, Transform parent, Color colour)
        {
            var square = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline))
            {
                layer = parent.gameObject.layer
            };
            square.transform.SetParent(parent, false);
            var image = square.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = colour;
            var outline = square.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            square.GetComponent<Button>().targetGraphic = image;
            return square;
        }

        private static Transform CreatePickerBlock(Transform parent)
        {
            var block = new GameObject("Hair Colour Picker", typeof(RectTransform), typeof(VerticalLayoutGroup))
            {
                layer = parent.gameObject.layer
            };
            var rect = (RectTransform)block.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(ContentWidth, PickerHeight);
            var layout = block.GetComponent<VerticalLayoutGroup>();
            layout.spacing = PickerRowSpacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return rect;
        }

        private static int Columns(float cellSize)
        {
            return Mathf.Max(1, Mathf.FloorToInt((ContentWidth - GridInset * 2 + SwatchSpacing) / (cellSize + SwatchSpacing)));
        }

        // Copies a skin slider's row (label and slider) and relabels it.
        private static Slider CopySliderRow(Slider source, Transform parent, string label)
        {
            Transform row = source.transform.parent;
            int sliderIndex = row.GetComponentsInChildren<Slider>(true).ToList().IndexOf(source);
            GameObject copy = Copy(row.gameObject, parent, $"{label} Row", RowHeight);
            Slider slider = copy.GetComponentsInChildren<Slider>(true)[Math.Max(sliderIndex, 0)];
            slider.name = $"{label} Slider";
            slider.onValueChanged = new Slider.SliderEvent();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            foreach (TMP_Text text in copy.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!text.transform.IsChildOf(slider.transform))
                {
                    text.text = label;
                    text.name = $"{label} Label";
                }
            }

            return slider;
        }
    }
}
