using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization.EditorTools
{
    // Converts the demo menu's HSV custom-colour panel into the skin parameter
    // picker. The panel keeps its size: a shorter preview strip, a melanin by
    // hemoglobin plane, and the three existing slider rows relabelled. Also restyles
    // the skin swatches to match the hair colour swatches.
    public static class SolSkinPickerSetup
    {
        private const string DemoMenuPrefabPath = "Assets/CharCustomization/Resources/Prefabs/CharacterMorphMenuDemoUI.prefab";
        private const float PreviewHeight = 22f;
        internal const float PlaneHeight = 96f;
        private const float RowSpacing = 5f;
        private const float MarkerSize = 16f;
        // Matches the hair colour swatches (SolHairMenuSetup).
        private const float SwatchSize = 48f;
        private const float SwatchSpacing = 10f;
        private const float DeleteSize = 18f;
        private const float DefaultSwatchAreaWidth = 537f;
        private const float SectionGap = 14f;
        private const float HeadingGap = 6f;
        private const string SkinPalettePath = "Assets/CharCustomization/CustomizationPresets/DefaultSkinPalette.asset";

        // Added to the default palette by the restyle step. Human tones (sRGB) are matched
        // into the human skin model; the rest are outside human hues and get a fantasy
        // profile from the fantasy profile tool.
        private static readonly (string id, string label, string hex)[] ExtraSkinTones =
        {
            ("ivory", "Ivory", "#F2E1D9"),
            ("rose", "Rose", "#EBC8B9"),
            ("peach", "Peach", "#F0CBAA"),
            ("golden_fair", "Golden Fair", "#E8C190"),
            ("olive_light", "Light Olive", "#D2B48C"),
            ("olive", "Olive", "#B89A6E"),
            ("honey", "Honey", "#C98F5E"),
            ("sienna", "Sienna", "#B5674A"),
            ("caramel", "Caramel", "#A86B45"),
            ("rosy_brown", "Rosy Brown", "#9C5F4E"),
            ("cinnamon", "Cinnamon", "#8D5235"),
            ("bronze", "Bronze", "#7A4A2E"),
            ("mahogany", "Mahogany", "#6B3324"),
            ("umber", "Umber", "#5E3A25"),
            ("espresso", "Espresso", "#4A2C21"),
            ("ebony", "Ebony", "#2E1D17"),
            ("goblin", "Goblin", "#8DAA4B"),
            ("orc", "Orc", "#5B7D3A"),
            ("teal", "Teal", "#3E8C86"),
            ("frost", "Frost", "#9EC4DE"),
            ("abyssal", "Abyssal", "#2E3A6E"),
            ("violet", "Violet", "#6B4A9A"),
            ("lilac", "Lilac", "#C7A8DE"),
            ("fae", "Fae", "#D88AB4"),
            ("blood", "Blood", "#8E2A2A"),
            ("infernal", "Infernal", "#4E1616"),
            ("ash", "Ash", "#9A9A9A"),
            ("slate", "Slate", "#5A5F66"),
        };
        private static readonly Color AddButtonColour = new(0.22f, 0.22f, 0.22f, 1f);

        [MenuItem("Tools/Sol/Character Customization/Skin/Build Skin Picker In Demo Menu")]
        private static void BuildInDemoMenu()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(DemoMenuPrefabPath);
            try
            {
                if (!TryBuild(root, out string message))
                {
                    Debug.LogError(message);
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, DemoMenuPrefabPath);
                Debug.Log(message);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/Sol/Character Customization/Skin/Restyle Skin Swatches In Demo Menu")]
        private static void RestyleSwatchesInDemoMenu()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(DemoMenuPrefabPath);
            try
            {
                if (!TryRestyleSwatches(root, out string message))
                {
                    Debug.LogError(message);
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, DemoMenuPrefabPath);
                Debug.Log(message);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Plain colour squares like the hair swatches: no names or frames, an outline, the
        // add button a dark square with a +, and the saved colours' red x in the corner.
        // Saved colours are cloned from the add button, so they pick up the same style.
        public static bool TryRestyleSwatches(GameObject menuRoot, out string message)
        {
            CharacterMorphDemoUI menu = menuRoot.GetComponentInChildren<CharacterMorphDemoUI>(true);
            var addSkin = menu != null
                ? new SerializedObject(menu).FindProperty("addSkinButton").objectReferenceValue as Button
                : null;
            GridLayoutGroup grid = addSkin != null ? addSkin.GetComponentInParent<GridLayoutGroup>(true) : null;
            if (grid == null)
            {
                message = "The demo menu needs its Add Skin button inside the skin swatch grid.";
                return false;
            }

            float width = ((RectTransform)grid.transform).rect.width;
            width = width > SwatchSize ? width - grid.padding.left - grid.padding.right : DefaultSwatchAreaWidth;
            grid.cellSize = new Vector2(SwatchSize, SwatchSize);
            grid.spacing = new Vector2(SwatchSpacing, SwatchSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, Mathf.FloorToInt((width + SwatchSpacing) / (SwatchSize + SwatchSpacing)));
            // Room for the corner x, which sits half outside its swatch.
            grid.padding = new RectOffset(
                Mathf.Max(grid.padding.left, 8), grid.padding.right, Mathf.Max(grid.padding.top, 10), grid.padding.bottom);

            int added = AddSkinTones(menu, addSkin);
            Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            int restyled = 0;
            foreach (CharacterSkinSwatchButton swatch in grid.GetComponentsInChildren<CharacterSkinSwatchButton>(true))
            {
                bool isAdd = swatch.Button == addSkin;
                Image image = swatch.SwatchImage != null ? swatch.SwatchImage : swatch.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = rounded;
                    image.type = Image.Type.Sliced;
                    if (isAdd)
                    {
                        image.color = AddButtonColour;
                    }
                }

                if (!swatch.TryGetComponent(out Outline outline))
                {
                    outline = swatch.gameObject.AddComponent<Outline>();
                }

                outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
                if (swatch.TryGetComponent(out LayoutElement element))
                {
                    element.preferredWidth = SwatchSize;
                    element.preferredHeight = SwatchSize;
                }

                foreach (Transform child in swatch.transform)
                {
                    if (swatch.DeleteButton != null && child == swatch.DeleteButton.transform)
                    {
                        var rect = (RectTransform)child;
                        rect.anchorMin = rect.anchorMax = Vector2.one;
                        rect.pivot = new Vector2(0.5f, 0.5f);
                        rect.sizeDelta = new Vector2(DeleteSize, DeleteSize);
                        rect.anchoredPosition = new Vector2(-2f, -2f);
                        if (child.TryGetComponent(out LayoutElement deleteElement))
                        {
                            deleteElement.ignoreLayout = true;
                        }

                        continue;
                    }

                    TMP_Text text = child.GetComponent<TMP_Text>();
                    if (text != null && text != swatch.Label && text.text.Trim() == "+")
                    {
                        text.fontSize = 32f;
                        text.alignment = TextAlignmentOptions.Center;
                        var rect = (RectTransform)child;
                        rect.anchorMin = Vector2.zero;
                        rect.anchorMax = Vector2.one;
                        rect.offsetMin = rect.offsetMax = Vector2.zero;
                        continue;
                    }

                    // Names and the old frame image.
                    child.gameObject.SetActive(false);
                }

                restyled++;
            }

            string layout = CompactSkinPanel(menu);
            message = $"Added {added} skin tones. Restyled {restyled} skin swatches ({grid.constraintCount} per row). {layout}";
            return true;
        }

        // Adds the extra tones to the default palette (fantasy ones get a profile from the
        // fantasy profile tool) and a swatch for each tone the menu has no swatch for yet.
        private static int AddSkinTones(CharacterMorphDemoUI menu, Button addSkin)
        {
            var palette = AssetDatabase.LoadAssetAtPath<CharacterSkinPalette>(SkinPalettePath);
            if (palette == null)
            {
                return 0;
            }

            var serializedPalette = new SerializedObject(palette);
            SerializedProperty tones = serializedPalette.FindProperty("tones");
            foreach ((string id, string label, string hex) in ExtraSkinTones)
            {
                if (palette.TryGet(id, out _) || !ColorUtility.TryParseHtmlString(hex, out Color colour))
                {
                    continue;
                }

                tones.InsertArrayElementAtIndex(tones.arraySize);
                SerializedProperty tone = tones.GetArrayElementAtIndex(tones.arraySize - 1);
                tone.FindPropertyRelative("id").stringValue = id;
                tone.FindPropertyRelative("label").stringValue = label;
                tone.FindPropertyRelative("color").colorValue = colour;
                tone.FindPropertyRelative("hasParameters").boolValue = false;
                tone.FindPropertyRelative("profile").objectReferenceValue = null;
            }

            serializedPalette.ApplyModifiedPropertiesWithoutUndo();
            SolSkinMaterialTools.CreateFantasySkinProfiles(palette, out _);
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();

            var serializedMenu = new SerializedObject(menu);
            SerializedProperty swatches = serializedMenu.FindProperty("skinSwatches");
            var existing = new HashSet<string>();
            CharacterSkinSwatchButton source = null;
            for (int index = 0; index < swatches.arraySize; index++)
            {
                if (swatches.GetArrayElementAtIndex(index).objectReferenceValue is CharacterSkinSwatchButton swatch &&
                    swatch.Button != addSkin)
                {
                    existing.Add(swatch.SkinToneId);
                    source ??= swatch;
                }
            }

            if (source == null)
            {
                return 0;
            }

            int added = 0;
            foreach (CharacterSkinTone tone in palette.Tones)
            {
                if (tone == null || existing.Contains(tone.Id))
                {
                    continue;
                }

                CharacterSkinSwatchButton swatch = Object.Instantiate(source, source.transform.parent);
                swatch.name = $"Skin {tone.Label}";
                swatch.transform.SetSiblingIndex(addSkin.transform.GetSiblingIndex());
                swatch.ConfigureRuntime(tone.Id, tone.Label, tone.Color);
                swatches.InsertArrayElementAtIndex(swatches.arraySize);
                swatches.GetArrayElementAtIndex(swatches.arraySize - 1).objectReferenceValue = swatch;
                added++;
            }

            serializedMenu.ApplyModifiedPropertiesWithoutUndo();
            return added;
        }

        // The custom colour picker stays open at the bottom of the panel with Roughness as
        // its last row, so the gradient field sits above all four sliders. The swatch
        // scroll view fills everything from the heading down to the picker.
        private static string CompactSkinPanel(CharacterMorphDemoUI menu)
        {
            var serializedMenu = new SerializedObject(menu);
            var scroll = serializedMenu.FindProperty("skinSwatchScrollRect").objectReferenceValue as ScrollRect;
            var roughness = serializedMenu.FindProperty("roughnessSlider").objectReferenceValue as Slider;
            var toggle = serializedMenu.FindProperty("customColorToggleButton").objectReferenceValue as Button;
            var panel = serializedMenu.FindProperty("customColorPanel").objectReferenceValue as GameObject;
            if (scroll == null || panel == null)
            {
                return "The swatch scroll view or custom colour panel is missing, so the layout was left as it was.";
            }

            var scrollRect = (RectTransform)scroll.transform;
            var skinPanel = (RectTransform)scrollRect.parent;
            var panelRect = (RectTransform)panel.transform;
            if (toggle != null)
            {
                toggle.gameObject.SetActive(false);
            }

            // Roughness becomes the picker's last row; the picker grows upward to fit it.
            float panelBottom = LocalBottom(panelRect);
            if (roughness != null && roughness.transform.parent is RectTransform roughnessRow)
            {
                if (roughnessRow.parent != panelRect)
                {
                    roughnessRow.SetParent(panelRect, false);
                    roughnessRow.SetAsLastSibling();
                }

                // Its old preferred height was left over from its earlier place.
                if (roughnessRow.TryGetComponent(out LayoutElement rowElement))
                {
                    rowElement.preferredHeight = roughnessRow.rect.height;
                }
            }

            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, StackHeight(panelRect));
            MoveBottomTo(panelRect, panelBottom);

            // Below the heading, or where the swatches start now.
            Transform heading = skinPanel.Find("Skin Heading");
            float top = heading is RectTransform headingRect ? LocalBottom(headingRect) - HeadingGap : LocalTop(scrollRect);
            float bottom = LocalTop(panelRect) + SectionGap;
            scrollRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(top - bottom, SwatchSize));
            MoveTopTo(scrollRect, top);

            panel.SetActive(true);
            serializedMenu.FindProperty("customColorAlwaysOpen").boolValue = true;
            serializedMenu.ApplyModifiedPropertiesWithoutUndo();
            return $"The swatches scroll in the top {Mathf.RoundToInt(top - bottom)} px; the colour picker, with Roughness, " +
                   "is always open at the bottom.";
        }

        // Height of a vertical layout group's active children, spacing, and padding.
        private static float StackHeight(RectTransform stack)
        {
            var layout = stack.GetComponent<VerticalLayoutGroup>();
            float height = layout != null ? layout.padding.top + layout.padding.bottom : 0f;
            int shown = 0;
            foreach (RectTransform child in stack)
            {
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                // The picker's layout does not control its rows' heights, so their own
                // heights are what it stacks.
                height += child.rect.height;
                shown++;
            }

            return height + (layout != null ? layout.spacing * Mathf.Max(0, shown - 1) : 0f);
        }

        // Edges in the parent's local space. World corners are no use in prefab editing,
        // where the menu canvas has zero scale.
        internal static float LocalTop(RectTransform rect)
        {
            return rect.localPosition.y + rect.rect.yMax;
        }

        internal static float LocalBottom(RectTransform rect)
        {
            return rect.localPosition.y + rect.rect.yMin;
        }

        private static void MoveTopTo(RectTransform rect, float top)
        {
            rect.anchoredPosition += new Vector2(0f, top - LocalTop(rect));
        }

        private static void MoveBottomTo(RectTransform rect, float bottom)
        {
            rect.anchoredPosition += new Vector2(0f, bottom - LocalBottom(rect));
        }

        public static bool TryBuild(GameObject menuRoot, out string message)
        {
            CharacterMorphDemoUI menu = menuRoot.GetComponentInChildren<CharacterMorphDemoUI>(true);
            if (menu == null)
            {
                message = $"No {nameof(CharacterMorphDemoUI)} found under '{menuRoot.name}'.";
                return false;
            }

            if (menuRoot.GetComponentInChildren<CharacterSkinParameterPicker>(true) != null)
            {
                message = $"'{menuRoot.name}' already has a {nameof(CharacterSkinParameterPicker)}.";
                return false;
            }

            var serializedMenu = new SerializedObject(menu);
            var panel = serializedMenu.FindProperty("customColorPanel").objectReferenceValue as GameObject;
            var melanin = serializedMenu.FindProperty("hueSlider").objectReferenceValue as Slider;
            var hemoglobin = serializedMenu.FindProperty("saturationSlider").objectReferenceValue as Slider;
            var undertone = serializedMenu.FindProperty("valueSlider").objectReferenceValue as Slider;
            var preview = serializedMenu.FindProperty("customColorPreview").objectReferenceValue as Image;
            if (panel == null || melanin == null || hemoglobin == null || undertone == null || preview == null)
            {
                message = "The menu's custom colour panel, HSV sliders, and preview must all be assigned before building the picker.";
                return false;
            }

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            SetHeight(preview.rectTransform, PreviewHeight);
            float width = ((RectTransform)panel.transform).rect.width;
            CharacterSkinPlaneArea plane = CreatePlane(panel.transform, width);
            plane.transform.SetSiblingIndex(preview.transform.GetSiblingIndex() + 1);

            Relabel(melanin, "Melanin");
            Relabel(hemoglobin, "Hemoglobin");
            Relabel(undertone, "Undertone");

            var picker = panel.AddComponent<CharacterSkinParameterPicker>();
            var serializedPicker = new SerializedObject(picker);
            serializedPicker.FindProperty("plane").objectReferenceValue = plane;
            serializedPicker.FindProperty("melaninSlider").objectReferenceValue = melanin;
            serializedPicker.FindProperty("hemoglobinSlider").objectReferenceValue = hemoglobin;
            serializedPicker.FindProperty("undertoneSlider").objectReferenceValue = undertone;
            serializedPicker.FindProperty("preview").objectReferenceValue = preview;
            serializedPicker.ApplyModifiedPropertiesWithoutUndo();

            serializedMenu.FindProperty("skinParameterPicker").objectReferenceValue = picker;
            serializedMenu.ApplyModifiedPropertiesWithoutUndo();

            message = $"Built the skin parameter picker in '{panel.name}'.";
            return true;
        }

        // A colour field with a marker; the hair panel builds its field with this too.
        internal static CharacterSkinPlaneArea CreatePlane(Transform parent, float width, string name = "Skin Colour Plane")
        {
            var planeObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(RawImage),
                typeof(LayoutElement),
                typeof(CharacterSkinPlaneArea));
            planeObject.layer = parent.gameObject.layer;
            var planeRect = (RectTransform)planeObject.transform;
            planeRect.SetParent(parent, false);
            planeRect.anchorMin = new Vector2(0f, 1f);
            planeRect.anchorMax = new Vector2(0f, 1f);
            planeRect.pivot = new Vector2(0.5f, 0.5f);
            planeRect.sizeDelta = new Vector2(width, PlaneHeight);
            var layoutElement = planeObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.preferredHeight = PlaneHeight;

            var markerObject = new GameObject("Marker", typeof(RectTransform), typeof(Image), typeof(Outline));
            markerObject.layer = planeObject.layer;
            var markerRect = (RectTransform)markerObject.transform;
            markerRect.SetParent(planeRect, false);
            markerRect.sizeDelta = new Vector2(MarkerSize, MarkerSize);
            var markerImage = markerObject.GetComponent<Image>();
            markerImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            markerImage.raycastTarget = false;
            var outline = markerObject.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var plane = planeObject.GetComponent<CharacterSkinPlaneArea>();
            var serializedPlane = new SerializedObject(plane);
            serializedPlane.FindProperty("marker").objectReferenceValue = markerRect;
            serializedPlane.ApplyModifiedPropertiesWithoutUndo();
            return plane;
        }

        private static void SetHeight(RectTransform rect, float height)
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            if (rect.TryGetComponent(out LayoutElement layoutElement))
            {
                layoutElement.preferredHeight = height;
            }
        }

        // Renames the slider, its row, and the row's label so the hierarchy reads as
        // the parameter it now controls.
        private static void Relabel(Slider slider, string label)
        {
            slider.gameObject.name = $"{label} Slider";
            Transform row = slider.transform.parent;
            if (row == null)
            {
                return;
            }

            row.gameObject.name = $"{label} Row";
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.transform.IsChildOf(slider.transform))
                {
                    continue;
                }

                text.text = label;
                text.gameObject.name = $"{label} Label";
            }
        }
    }
}
