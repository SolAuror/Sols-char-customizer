using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    // The hair menu: a hairstyle dropdown (with None), the palette's colour swatches,
    // hue, saturation, and value sliders for any other colour, and a sheen slider from
    // matte to glossy. Each slider's track shows the colours it would produce. It edits
    // a CharacterHair and follows its changes, including a sex switch that brings the
    // other character's hairstyles.
    public sealed class CharacterHairPicker : MonoBehaviour
    {
        // How far the sheen track lightens the hair colour at its glossy end.
        private const float SheenTrackHighlight = 0.55f;

        [SerializeField] private CharacterHair hair;
        [Tooltip("Optional: a dropdown of styles. Tiles below are used when set.")]
        [SerializeField] private TMP_Dropdown styleDropdown;
        [SerializeField] private RectTransform styleTileContent;
        [Tooltip("Inactive template cloned per style, plus one for None: a Button with a 'Preview' Image and a 'Name' label as children.")]
        [SerializeField] private Button styleTileTemplate;
        [SerializeField] private RectTransform swatchContent;
        [Tooltip("Inactive template cloned once per palette colour.")]
        [SerializeField] private Button swatchTemplate;
        [SerializeField] private Slider hueSlider;
        [SerializeField] private Slider saturationSlider;
        [SerializeField] private Slider valueSlider;
        [SerializeField] private Slider sheenSlider;
        [Tooltip("Saturation across, brightness up, at the current hue.")]
        [SerializeField] private CharacterSkinPlaneArea colorField;
        [SerializeField] private Image preview;

        private readonly List<CharacterHairStyle> styles = new();
        private readonly List<Button> swatches = new();
        // Tile per entry of 'styles', after the None tile at index 0.
        private readonly List<Button> styleTiles = new();
        private Transform listedRoot;
        private bool listening;
        private bool sliding;
        private CharacterSliderGradient hueGradient;
        private CharacterSliderGradient saturationGradient;
        private CharacterSliderGradient valueGradient;
        private CharacterSliderGradient sheenGradient;
        private float renderedFieldHue = -1f;

        public CharacterHair Hair => hair;

        private void OnEnable()
        {
            if (hair != null)
            {
                Listen(true);
                Rebuild();
            }
        }

        private void OnDisable()
        {
            Listen(false);
        }

        private void OnDestroy()
        {
            hueGradient?.Dispose();
            saturationGradient?.Dispose();
            valueGradient?.Dispose();
            sheenGradient?.Dispose();
        }

        private void LateUpdate()
        {
            if (hair != null && hair.ActiveCharacterRoot != listedRoot)
            {
                RebuildStyles();
                Refresh();
            }
        }

        public void Bind(CharacterHair target)
        {
            Listen(false);
            hair = target;
            if (isActiveAndEnabled && hair != null)
            {
                Listen(true);
                Rebuild();
            }
        }

        // The character's first hairstyle in the palette's default colour and sheen.
        public void ResetToDefault()
        {
            if (hair == null)
            {
                return;
            }

            hair.SetStyle(string.Empty);
            hair.SetSheen(CharacterHair.DefaultSheen);
            CharacterHairColorOption option = hair.Palette != null ? hair.Palette.GetDefault() : null;
            if (option != null)
            {
                hair.SetColor(option.Color);
            }
        }

        private void Listen(bool listen)
        {
            if (listen == listening || hair == null)
            {
                return;
            }

            listening = listen;
            if (listen)
            {
                hair.Changed += Refresh;
                styleDropdown?.onValueChanged.AddListener(OnStyleChosen);
                hueSlider?.onValueChanged.AddListener(OnSliderChanged);
                saturationSlider?.onValueChanged.AddListener(OnSliderChanged);
                valueSlider?.onValueChanged.AddListener(OnSliderChanged);
                sheenSlider?.onValueChanged.AddListener(OnSheenChanged);
                if (colorField != null)
                {
                    colorField.ValueChanged += OnFieldChanged;
                }
            }
            else
            {
                hair.Changed -= Refresh;
                styleDropdown?.onValueChanged.RemoveListener(OnStyleChosen);
                hueSlider?.onValueChanged.RemoveListener(OnSliderChanged);
                saturationSlider?.onValueChanged.RemoveListener(OnSliderChanged);
                valueSlider?.onValueChanged.RemoveListener(OnSliderChanged);
                sheenSlider?.onValueChanged.RemoveListener(OnSheenChanged);
                if (colorField != null)
                {
                    colorField.ValueChanged -= OnFieldChanged;
                }
            }
        }

        private void Rebuild()
        {
            RebuildStyles();
            RebuildSwatches();
            Refresh();
        }

        private void RebuildStyles()
        {
            listedRoot = hair.ActiveCharacterRoot;
            styles.Clear();
            styles.AddRange(hair.AvailableStyles);
            RebuildStyleTiles();
            if (styleDropdown == null)
            {
                return;
            }

            var options = new List<string> { "None" };
            foreach (CharacterHairStyle style in styles)
            {
                options.Add(style.DisplayName);
            }

            styleDropdown.ClearOptions();
            styleDropdown.AddOptions(options);
        }

        private void RebuildStyleTiles()
        {
            foreach (Button tile in styleTiles)
            {
                if (tile != null)
                {
                    Destroy(tile.gameObject);
                }
            }

            styleTiles.Clear();
            if (styleTileContent == null || styleTileTemplate == null)
            {
                return;
            }

            styleTiles.Add(CreateStyleTile("None", null, CharacterHair.NoneStyleId));
            foreach (CharacterHairStyle style in styles)
            {
                styleTiles.Add(CreateStyleTile(style.DisplayName, style.Preview, style.StyleId));
            }
        }

        private Button CreateStyleTile(string label, Sprite thumbnail, string styleId)
        {
            Button tile = Instantiate(styleTileTemplate, styleTileContent);
            tile.name = $"Hair Style {label}";
            tile.gameObject.SetActive(true);
            Transform previewChild = tile.transform.Find("Preview");
            if (previewChild != null && previewChild.TryGetComponent(out Image previewImage))
            {
                previewImage.sprite = thumbnail;
                previewImage.preserveAspect = true;
                previewImage.enabled = thumbnail != null;
            }

            Transform nameChild = tile.transform.Find("Name");
            if (nameChild != null && nameChild.TryGetComponent(out TMP_Text nameText))
            {
                nameText.text = label;
            }

            tile.onClick.AddListener(() => hair.SetStyle(styleId));
            return tile;
        }

        private void RebuildSwatches()
        {
            foreach (Button swatch in swatches)
            {
                if (swatch != null)
                {
                    Destroy(swatch.gameObject);
                }
            }

            swatches.Clear();
            if (swatchContent == null || swatchTemplate == null || hair.Palette == null)
            {
                return;
            }

            foreach (CharacterHairColorOption option in hair.Palette.Options)
            {
                if (option == null)
                {
                    continue;
                }

                Button swatch = Instantiate(swatchTemplate, swatchContent);
                swatch.name = $"Hair Swatch {option.Label}";
                swatch.gameObject.SetActive(true);
                if (swatch.targetGraphic != null)
                {
                    swatch.targetGraphic.color = option.Color;
                }

                Color color = option.Color;
                swatch.onClick.AddListener(() => hair.SetColor(color));
                swatches.Add(swatch);
            }
        }

        // Shows the hair's current style and colour without feeding changes back.
        private void Refresh()
        {
            if (hair == null)
            {
                return;
            }

            CharacterHairStyle current = hair.CurrentStyle;
            int selected = current == null ? 0 : styles.IndexOf(current) + 1;
            styleDropdown?.SetValueWithoutNotify(selected);
            for (int index = 0; index < styleTiles.Count; index++)
            {
                if (styleTiles[index] != null)
                {
                    // Matches how the colour swatches show the selection.
                    styleTiles[index].transform.localScale = index == selected ? Vector3.one * 1.08f : Vector3.one;
                }
            }

            // While a slider drives the colour, leave the sliders alone: converting back
            // loses the hue at zero saturation and would snap it.
            if (!sliding)
            {
                Color.RGBToHSV(hair.Color, out float hue, out float saturation, out float value);
                hueSlider?.SetValueWithoutNotify(hue);
                saturationSlider?.SetValueWithoutNotify(saturation);
                valueSlider?.SetValueWithoutNotify(value);
            }

            sheenSlider?.SetValueWithoutNotify(hair.Sheen);
            if (preview != null)
            {
                preview.color = hair.Color;
            }

            PaintGradients();
        }

        // Each track shows the colours its slider would give with the others held.
        private void PaintGradients()
        {
            float hue = hueSlider != null ? hueSlider.value : 0f;
            float saturation = saturationSlider != null ? saturationSlider.value : 0f;
            float value = valueSlider != null ? valueSlider.value : 1f;
            // A grey or black colour would make the hue track flat; show hues at least this vivid.
            float trackSaturation = Mathf.Max(saturation, 0.6f);
            float trackValue = Mathf.Max(value, 0.6f);
            if (colorField != null)
            {
                colorField.SetValueWithoutNotify(new Vector2(saturation, value));
                // Only the hue changes the field, so it is redrawn only then.
                if (!Mathf.Approximately(renderedFieldHue, hue))
                {
                    colorField.Render(point => Color.HSVToRGB(hue, point.x, point.y));
                    renderedFieldHue = hue;
                }
            }

            Gradient(ref hueGradient, hueSlider)?.Paint(t => Color.HSVToRGB(t, trackSaturation, trackValue));
            Gradient(ref saturationGradient, saturationSlider)?.Paint(t => Color.HSVToRGB(hue, t, value));
            Gradient(ref valueGradient, valueSlider)?.Paint(t => Color.HSVToRGB(hue, saturation, t));
            Color color = hair.Color;
            Gradient(ref sheenGradient, sheenSlider)?.Paint(t => Color.Lerp(color, Color.white, t * t * SheenTrackHighlight));
        }

        private static CharacterSliderGradient Gradient(ref CharacterSliderGradient gradient, Slider slider)
        {
            if (slider == null)
            {
                return null;
            }

            return gradient ??= new CharacterSliderGradient(slider);
        }

        private void OnSheenChanged(float value)
        {
            hair.SetSheen(value);
        }

        // The field sets saturation and brightness; the hue stays on its slider.
        private void OnFieldChanged(Vector2 point)
        {
            saturationSlider?.SetValueWithoutNotify(point.x);
            valueSlider?.SetValueWithoutNotify(point.y);
            OnSliderChanged(0f);
        }

        private void OnStyleChosen(int index)
        {
            hair.SetStyle(index <= 0 || index > styles.Count ? CharacterHair.NoneStyleId : styles[index - 1].StyleId);
        }

        private void OnSliderChanged(float _)
        {
            sliding = true;
            try
            {
                hair.SetColor(Color.HSVToRGB(
                    hueSlider != null ? hueSlider.value : 0f,
                    saturationSlider != null ? saturationSlider.value : 0f,
                    valueSlider != null ? valueSlider.value : 1f));
            }
            finally
            {
                sliding = false;
            }
        }
    }
}
