using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization.EditorTools
{
    // Adds the Tattoos tab to the demo customizer, after Hair (or Skin), built from the
    // menu's own controls like the Hair tab: a hint, design tiles, the placed tattoos,
    // ink swatches, size, rotation, and opacity sliders copied from the skin picker, and
    // Mirror and Remove buttons, with a CharacterTattooPicker driving them; it edits the
    // CharacterTattoos of the menu's character profile. Running it again rebuilds the
    // panel with the current layout.
    public static class SolTattooMenuSetup
    {
        private const string DemoMenuPrefabPath = "Assets/CharCustomization/Resources/Prefabs/CharacterMorphMenuDemoUI.prefab";
        private const float HintHeight = 46f;
        private const float SubheadingHeight = 26f;
        // Four designs and six placed tattoos to a row.
        private const float DesignTileSize = 124f;
        private const float PlacedTileSize = 76f;
        private const float ButtonRowHeight = 42f;
        private const float ButtonSpacing = 10f;
        private const float MinimumDesignRows = 1.5f;

        private static float ContentWidth => SolHairMenuSetup.ContentWidth;

        [MenuItem("Tools/Sol/Character Customization/Tattoos/Build Tattoo Tab In Demo Menu")]
        private static void Build()
        {
            GameObject menuRoot = PrefabUtility.LoadPrefabContents(DemoMenuPrefabPath);
            try
            {
                if (!TryBuildTab(menuRoot, out string message))
                {
                    Debug.LogError(message);
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(menuRoot, DemoMenuPrefabPath);
                Debug.Log(message);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(menuRoot);
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
            Slider sourceSlider = SolHairMenuSetup.FindSkinSlider(serializedMenu);
            SerializedProperty tabs = serializedMenu.FindProperty("tabButtons");
            int anchorTabIndex = -1;
            bool hasTattooTab = false;
            for (int index = 0; index < tabs.arraySize; index++)
            {
                if (tabs.GetArrayElementAtIndex(index).objectReferenceValue is CharacterMorphTabButton tab)
                {
                    // After Hair when the menu has it, else after Skin.
                    if (CharacterCustomizationUiConfig.IsHairGroup(tab.GroupId)
                        || CharacterCustomizationUiConfig.IsSkinGroup(tab.GroupId) && anchorTabIndex < 0)
                    {
                        anchorTabIndex = index;
                    }

                    hasTattooTab |= CharacterCustomizationUiConfig.IsTattooGroup(tab.GroupId);
                }
            }

            if (skinPanel == null || skinColourPanel == null || sourceSlider == null || anchorTabIndex < 0)
            {
                message = "The menu needs its Skin tab, skin panel, custom colour panel, and a skin slider row to copy.";
                return false;
            }

            if (!hasTattooTab)
            {
                var anchorTab = (CharacterMorphTabButton)tabs.GetArrayElementAtIndex(anchorTabIndex).objectReferenceValue;
                tabs.InsertArrayElementAtIndex(anchorTabIndex + 1);
                tabs.GetArrayElementAtIndex(anchorTabIndex + 1).objectReferenceValue = CopyTab(anchorTab);
            }

            if (serializedMenu.FindProperty("tattooPanel").objectReferenceValue is GameObject oldPanel)
            {
                Object.DestroyImmediate(oldPanel);
            }

            // Like the hair panel: as tall as the skin panel, the design tiles taking what
            // the fixed rows leave.
            var skinRect = (RectTransform)skinPanel.transform;
            float bottomPadding = Mathf.Max(0f,
                SolSkinPickerSetup.LocalBottom((RectTransform)skinColourPanel.transform) - skinRect.rect.yMin);
            float placedHeight = PlacedTileSize + SolHairMenuSetup.GridInset * 2f;
            float inkHeight = SolHairMenuSetup.SwatchSize + SolHairMenuSetup.GridInset * 2f;
            float fixedHeight = SolHairMenuSetup.HeadingHeight + HintHeight + SubheadingHeight * 2f + placedHeight + inkHeight
                                + SolHairMenuSetup.RowHeight * 3f + ButtonRowHeight;
            const int sections = 10;
            float available = skinRect.rect.height - SolHairMenuSetup.PanelTopPadding - bottomPadding;
            float minimumDesigns = DesignTileSize * MinimumDesignRows + SolHairMenuSetup.SwatchSpacing + SolHairMenuSetup.GridInset;
            float designHeight = Mathf.Max(minimumDesigns, available - fixedHeight - SolHairMenuSetup.SectionSpacing * (sections - 1));

            GameObject panel = CreatePanel(skinPanel, bottomPadding);
            TMP_Text labelStyle = sourceSlider.transform.parent.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => !text.transform.IsChildOf(sourceSlider.transform));
            SolHairMenuSetup.AddLabel(panel.transform, labelStyle, "Tattoos", SolHairMenuSetup.HeadingHeight, 1.3f);
            TMP_Text hint = CreateHint(panel.transform, labelStyle);
            (RectTransform designs, Button designTemplate) = CreateTiles(
                panel.transform, labelStyle, "Tattoo Designs", designHeight, DesignTileSize, true);
            SolHairMenuSetup.AddLabel(panel.transform, labelStyle, "Your Tattoos", SubheadingHeight, 1f);
            (RectTransform placed, Button placedTemplate) = CreateTiles(
                panel.transform, labelStyle, "Placed Tattoos", placedHeight, PlacedTileSize, false);
            SolHairMenuSetup.AddLabel(panel.transform, labelStyle, "Ink", SubheadingHeight, 1f);
            (RectTransform ink, Button inkTemplate) = CreateInkSwatches(panel.transform, inkHeight);
            Slider size = SolHairMenuSetup.CopySliderRow(sourceSlider, panel.transform, "Size");
            Slider rotation = SolHairMenuSetup.CopySliderRow(sourceSlider, panel.transform, "Rotation");
            Slider opacity = SolHairMenuSetup.CopySliderRow(sourceSlider, panel.transform, "Opacity");
            Transform buttons = CreateButtonRow(panel.transform);
            Button mirror = CreateButton(buttons, labelStyle, "Mirror");
            Button remove = CreateButton(buttons, labelStyle, "Remove");

            var picker = panel.AddComponent<CharacterTattooPicker>();
            var serializedPicker = new SerializedObject(picker);
            serializedPicker.FindProperty("designContent").objectReferenceValue = designs;
            serializedPicker.FindProperty("designTileTemplate").objectReferenceValue = designTemplate;
            serializedPicker.FindProperty("placedContent").objectReferenceValue = placed;
            serializedPicker.FindProperty("placedTileTemplate").objectReferenceValue = placedTemplate;
            serializedPicker.FindProperty("inkContent").objectReferenceValue = ink;
            serializedPicker.FindProperty("inkSwatchTemplate").objectReferenceValue = inkTemplate;
            serializedPicker.FindProperty("sizeSlider").objectReferenceValue = size;
            serializedPicker.FindProperty("rotationSlider").objectReferenceValue = rotation;
            serializedPicker.FindProperty("opacitySlider").objectReferenceValue = opacity;
            serializedPicker.FindProperty("mirrorButton").objectReferenceValue = mirror;
            serializedPicker.FindProperty("removeButton").objectReferenceValue = remove;
            serializedPicker.FindProperty("hint").objectReferenceValue = hint;
            serializedPicker.ApplyModifiedPropertiesWithoutUndo();

            serializedMenu.FindProperty("tattooPanel").objectReferenceValue = panel;
            serializedMenu.FindProperty("tattooPicker").objectReferenceValue = picker;
            serializedMenu.ApplyModifiedPropertiesWithoutUndo();
            panel.SetActive(false);

            message = (hasTattooTab ? "Rebuilt the Tattoos panel" : "Built the Tattoos tab and panel") +
                      ": design tiles, your tattoos, ink swatches, size, rotation, and opacity, and Mirror and Remove.";
            return true;
        }

        private static CharacterMorphTabButton CopyTab(CharacterMorphTabButton source)
        {
            GameObject copy = Object.Instantiate(source.gameObject, source.transform.parent);
            copy.name = "Tattoos Tab";
            copy.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            var tab = copy.GetComponent<CharacterMorphTabButton>();
            var serializedTab = new SerializedObject(tab);
            serializedTab.FindProperty("groupId").stringValue = CharacterCustomizationUiConfig.TattooGroupId;
            serializedTab.ApplyModifiedPropertiesWithoutUndo();
            tab.Button.onClick = new Button.ButtonClickedEvent();
            tab.Label.text = "Tattoos";
            return tab;
        }

        private static GameObject CreatePanel(GameObject skinPanel, float bottomPadding)
        {
            var panel = new GameObject("Tattoo Panel", typeof(RectTransform), typeof(VerticalLayoutGroup))
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
            layout.padding = new RectOffset(sidePadding, sidePadding, Mathf.RoundToInt(SolHairMenuSetup.PanelTopPadding),
                Mathf.RoundToInt(bottomPadding));
            layout.spacing = SolHairMenuSetup.SectionSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return panel;
        }

        private static TMP_Text CreateHint(Transform parent, TMP_Text style)
        {
            TextMeshProUGUI hint = CreateText(parent, "Tattoo Hint", style, ContentWidth, HintHeight, 0.8f);
            hint.alignment = TextAlignmentOptions.TopLeft;
            hint.textWrappingMode = TextWrappingModes.Normal;
            hint.text = "Choose a design, then click the body to place it.";
            return hint;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, TMP_Text style, float width, float height, float scale)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            if (style != null)
            {
                text.font = style.font;
                text.fontSharedMaterial = style.fontSharedMaterial;
                text.fontSize = style.fontSize * scale;
                text.color = style.color;
            }

            text.raycastTarget = false;
            return text;
        }

        // Square tiles showing a design (white ink on the dark tile), optionally named.
        private static (RectTransform content, Button template) CreateTiles(
            Transform parent, TMP_Text labelStyle, string name, float height, float tileSize, bool named)
        {
            (RectTransform content, GridLayoutGroup grid) = SolHairMenuSetup.CreateScrollArea(parent, name, height);
            grid.cellSize = new Vector2(tileSize, tileSize);
            grid.constraintCount = SolHairMenuSetup.Columns(tileSize);

            GameObject tile = SolHairMenuSetup.CreateSquare($"{name} Template", content, SolHairMenuSetup.TileColour);
            var previewObject = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)) { layer = tile.layer };
            var previewRect = (RectTransform)previewObject.transform;
            previewRect.SetParent(tile.transform, false);
            previewRect.anchorMin = Vector2.zero;
            previewRect.anchorMax = Vector2.one;
            previewRect.offsetMin = new Vector2(4f, named ? SolHairMenuSetup.TileNameHeight : 4f);
            previewRect.offsetMax = new Vector2(-4f, -4f);
            previewObject.GetComponent<RawImage>().raycastTarget = false;

            if (named)
            {
                TextMeshProUGUI label = CreateText(tile.transform, "Name", labelStyle, 0f, SolHairMenuSetup.TileNameHeight, 1f);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = new Vector2(1f, 0f);
                labelRect.pivot = new Vector2(0.5f, 0f);
                labelRect.sizeDelta = new Vector2(0f, SolHairMenuSetup.TileNameHeight);
                label.fontSize = 14f;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
            }

            tile.SetActive(false);
            return (content, tile.GetComponent<Button>());
        }

        private static (RectTransform content, Button template) CreateInkSwatches(Transform parent, float height)
        {
            (RectTransform content, GridLayoutGroup grid) = SolHairMenuSetup.CreateScrollArea(parent, "Tattoo Ink", height);
            grid.cellSize = new Vector2(SolHairMenuSetup.SwatchSize, SolHairMenuSetup.SwatchSize);
            grid.constraintCount = SolHairMenuSetup.Columns(SolHairMenuSetup.SwatchSize);
            GameObject swatch = SolHairMenuSetup.CreateSquare("Tattoo Ink Template", content, Color.white);
            swatch.SetActive(false);
            return (content, swatch.GetComponent<Button>());
        }

        private static Transform CreateButtonRow(Transform parent)
        {
            var row = new GameObject("Tattoo Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)row.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(ContentWidth, ButtonRowHeight);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = ButtonSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return rect;
        }

        private static Button CreateButton(Transform parent, TMP_Text labelStyle, string label)
        {
            GameObject button = SolHairMenuSetup.CreateSquare($"{label} Button", parent, SolHairMenuSetup.TileColour);
            TextMeshProUGUI text = CreateText(button.transform, "Label", labelStyle, 0f, 0f, 1f);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
            text.text = label;
            return button.GetComponent<Button>();
        }
    }
}
