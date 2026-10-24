using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    // Skin colour picker in melanin, hemoglobin, and undertone. The plane picks
    // melanin across and hemoglobin up at the current undertone; each slider shows
    // the colours it would produce with the other two held, so every position the
    // player can reach is a plausible skin.
    public sealed class CharacterSkinParameterPicker : MonoBehaviour
    {
        private const int GradientWidth = 64;

        [SerializeField] private CharacterSkinPlaneArea plane;
        [SerializeField] private Slider melaninSlider;
        [SerializeField] private Slider hemoglobinSlider;
        [SerializeField] private Slider undertoneSlider;
        [Tooltip("Optional swatch showing the selected colour.")]
        [SerializeField] private Image preview;

        private CharacterProfile profile;
        private bool refreshing;
        private bool listening;
        private float renderedPlaneUndertone = -1f;
        private CharacterSkinProfile renderedPlaneProfile;
        private Texture2D melaninGradient;
        private Texture2D hemoglobinGradient;
        private Texture2D undertoneGradient;
        private Sprite melaninGradientSprite;
        private Sprite hemoglobinGradientSprite;
        private Sprite undertoneGradientSprite;

        public event Action<CharacterSkinParameters> ParametersChanged;

        public CharacterSkinPlaneArea Plane => plane;
        public Slider MelaninSlider => melaninSlider;
        public Slider HemoglobinSlider => hemoglobinSlider;
        public Slider UndertoneSlider => undertoneSlider;

        public void Bind(CharacterProfile target)
        {
            profile = target;
            ConfigureSlider(melaninSlider);
            ConfigureSlider(hemoglobinSlider);
            ConfigureSlider(undertoneSlider);
            Listen(true);
            renderedPlaneUndertone = -1f;
            Refresh();
        }

        public void Refresh()
        {
            if (profile == null)
            {
                return;
            }

            refreshing = true;
            CharacterSkinParameters parameters = profile.SkinParameters;
            SetSlider(melaninSlider, parameters.Melanin);
            SetSlider(hemoglobinSlider, parameters.Hemoglobin);
            SetSlider(undertoneSlider, parameters.Undertone);
            if (plane != null)
            {
                plane.SetValueWithoutNotify(new Vector2(parameters.Melanin, parameters.Hemoglobin));
            }

            UpdateVisuals(parameters);
            refreshing = false;
        }

        private void OnSliderChanged(float _)
        {
            if (refreshing || profile == null)
            {
                return;
            }

            Apply(new CharacterSkinParameters(
                ReadSlider(melaninSlider, profile.SkinParameters.Melanin),
                ReadSlider(hemoglobinSlider, profile.SkinParameters.Hemoglobin),
                ReadSlider(undertoneSlider, profile.SkinParameters.Undertone)));
        }

        private void OnPlaneChanged(Vector2 value)
        {
            if (refreshing || profile == null)
            {
                return;
            }

            Apply(new CharacterSkinParameters(value.x, value.y, profile.SkinParameters.Undertone));
        }

        private void Apply(CharacterSkinParameters parameters)
        {
            profile.SetSkinParameters(parameters);
            Refresh();
            ParametersChanged?.Invoke(profile.SkinParameters);
        }

        private void UpdateVisuals(CharacterSkinParameters parameters)
        {
            if (preview != null)
            {
                preview.color = profile.EvaluateSkinColor(parameters);
            }

            // The plane only depends on undertone and the colour space, and costs a
            // few thousand evaluations, so it is redrawn only when those change.
            if (plane != null &&
                (!Mathf.Approximately(renderedPlaneUndertone, parameters.Undertone) ||
                 renderedPlaneProfile != profile.SkinProfile))
            {
                float undertone = parameters.Undertone;
                plane.Render(point => profile.EvaluateSkinColor(new CharacterSkinParameters(point.x, point.y, undertone)));
                renderedPlaneUndertone = undertone;
                renderedPlaneProfile = profile.SkinProfile;
            }

            FillGradient(melaninSlider, ref melaninGradient, ref melaninGradientSprite, "Melanin Gradient",
                t => new CharacterSkinParameters(t, parameters.Hemoglobin, parameters.Undertone));
            FillGradient(hemoglobinSlider, ref hemoglobinGradient, ref hemoglobinGradientSprite, "Hemoglobin Gradient",
                t => new CharacterSkinParameters(parameters.Melanin, t, parameters.Undertone));
            FillGradient(undertoneSlider, ref undertoneGradient, ref undertoneGradientSprite, "Undertone Gradient",
                t => new CharacterSkinParameters(parameters.Melanin, parameters.Hemoglobin, t));
        }

        private void FillGradient(
            Slider slider,
            ref Texture2D texture,
            ref Sprite sprite,
            string textureName,
            Func<float, CharacterSkinParameters> parametersAt)
        {
            Image background = FindSliderBackground(slider);
            if (background == null)
            {
                return;
            }

            if (texture == null)
            {
                texture = new Texture2D(GradientWidth, 1, TextureFormat.RGBA32, false)
                {
                    name = textureName,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
                sprite = Sprite.Create(texture, new Rect(0f, 0f, GradientWidth, 1f), new Vector2(0.5f, 0.5f), 1f);
                sprite.hideFlags = HideFlags.DontSave;
            }

            for (int x = 0; x < GradientWidth; x++)
            {
                texture.SetPixel(x, 0, profile.EvaluateSkinColor(parametersAt(x / (float)(GradientWidth - 1))));
            }

            texture.Apply(false, false);
            background.sprite = sprite;
            background.type = Image.Type.Simple;
            background.color = Color.white;
            background.raycastTarget = false;
        }

        private static Image FindSliderBackground(Slider slider)
        {
            if (slider == null)
            {
                return null;
            }

            Transform background = slider.transform.Find("Background");
            return background != null ? background.GetComponent<Image>() : null;
        }

        private static void ConfigureSlider(Slider slider)
        {
            if (slider == null)
            {
                return;
            }

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fill))
            {
                fill.enabled = false;
            }
        }

        private static void SetSlider(Slider slider, float value)
        {
            if (slider != null)
            {
                slider.SetValueWithoutNotify(value);
            }
        }

        private static float ReadSlider(Slider slider, float fallback)
        {
            return slider != null ? slider.value : fallback;
        }

        private void Listen(bool enable)
        {
            if (listening == enable)
            {
                return;
            }

            listening = enable;
            ListenToSlider(melaninSlider, enable);
            ListenToSlider(hemoglobinSlider, enable);
            ListenToSlider(undertoneSlider, enable);
            if (plane == null)
            {
                return;
            }

            if (enable)
            {
                plane.ValueChanged += OnPlaneChanged;
            }
            else
            {
                plane.ValueChanged -= OnPlaneChanged;
            }
        }

        private void ListenToSlider(Slider slider, bool enable)
        {
            if (slider == null)
            {
                return;
            }

            if (enable)
            {
                slider.onValueChanged.AddListener(OnSliderChanged);
            }
            else
            {
                slider.onValueChanged.RemoveListener(OnSliderChanged);
            }
        }

        private void OnDestroy()
        {
            Listen(false);
            DestroyGenerated(melaninGradientSprite);
            DestroyGenerated(hemoglobinGradientSprite);
            DestroyGenerated(undertoneGradientSprite);
            DestroyGenerated(melaninGradient);
            DestroyGenerated(hemoglobinGradient);
            DestroyGenerated(undertoneGradient);
        }

        private static void DestroyGenerated(UnityEngine.Object generated)
        {
            if (generated != null)
            {
                Destroy(generated);
            }
        }
    }
}
