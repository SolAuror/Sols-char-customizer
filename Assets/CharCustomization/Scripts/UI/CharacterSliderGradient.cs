using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    // Paints a slider's Background image with a horizontal gradient of what each
    // position would produce, and hides its fill. Owns its texture; dispose it with
    // the UI that created it.
    public sealed class CharacterSliderGradient : IDisposable
    {
        private const int Width = 64;

        private readonly Slider slider;
        private Texture2D texture;
        private Sprite sprite;

        public CharacterSliderGradient(Slider slider)
        {
            this.slider = slider;
        }

        public void Paint(Func<float, Color> colorAt)
        {
            Image background = FindBackground(slider);
            if (background == null)
            {
                return;
            }

            if (texture == null)
            {
                texture = new Texture2D(Width, 1, TextureFormat.RGBA32, false)
                {
                    name = $"{slider.name} Gradient",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
                sprite = Sprite.Create(texture, new Rect(0f, 0f, Width, 1f), new Vector2(0.5f, 0.5f), 1f);
                sprite.hideFlags = HideFlags.DontSave;
            }

            for (int x = 0; x < Width; x++)
            {
                Color color = colorAt(x / (float)(Width - 1));
                color.a = 1f;
                texture.SetPixel(x, 0, color);
            }

            texture.Apply(false, false);
            background.sprite = sprite;
            background.type = Image.Type.Simple;
            background.color = Color.white;
            background.raycastTarget = false;
            if (slider.fillRect != null && slider.fillRect.TryGetComponent(out Image fill))
            {
                fill.enabled = false;
            }
        }

        public void Dispose()
        {
            DestroyGenerated(sprite);
            DestroyGenerated(texture);
            sprite = null;
            texture = null;
        }

        private static Image FindBackground(Slider target)
        {
            if (target == null)
            {
                return null;
            }

            Transform background = target.transform.Find("Background");
            return background != null ? background.GetComponent<Image>() : null;
        }

        private static void DestroyGenerated(UnityEngine.Object generated)
        {
            if (generated == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(generated);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(generated);
            }
        }
    }
}
