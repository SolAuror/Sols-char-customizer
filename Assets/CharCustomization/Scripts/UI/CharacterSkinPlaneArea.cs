using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    // A clickable two-axis colour field. Shows a rendered colour plane and moves a
    // marker to the picked point; values are normalized 0..1 on both axes.
    [RequireComponent(typeof(RawImage))]
    public sealed class CharacterSkinPlaneArea : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        [SerializeField] private RectTransform marker;
        [Tooltip("Texels along the short side. The colour field is smooth, so bilinear filtering hides a low resolution and keeps redraws cheap while dragging.")]
        [SerializeField, Min(8)] private int resolution = 32;

        private RawImage image;
        private Texture2D texture;

        public event Action<Vector2> ValueChanged;

        public Vector2 Value { get; private set; }

        public void SetValueWithoutNotify(Vector2 value)
        {
            Value = new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
            UpdateMarker();
        }

        // sampleSrgb receives normalized (x, y) and returns the colour to show there.
        public void Render(Func<Vector2, Color> sampleSrgb)
        {
            if (sampleSrgb == null)
            {
                return;
            }

            EnsureTexture();
            int width = texture.width;
            int height = texture.height;
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var point = new Vector2(x / (float)(width - 1), y / (float)(height - 1));
                    pixels[y * width + x] = sampleSrgb(point);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Pick(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            Pick(eventData);
        }

        private void Pick(PointerEventData eventData)
        {
            var rectTransform = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            SetValueWithoutNotify(Rect.PointToNormalized(rectTransform.rect, localPoint));
            ValueChanged?.Invoke(Value);
        }

        private void UpdateMarker()
        {
            if (marker == null)
            {
                return;
            }

            marker.anchorMin = Value;
            marker.anchorMax = Value;
            marker.anchoredPosition = Vector2.zero;
        }

        private void EnsureTexture()
        {
            if (texture != null)
            {
                return;
            }

            // The plane is wider than tall; keep texels roughly square.
            var rect = ((RectTransform)transform).rect;
            float aspect = rect.height > 1f ? rect.width / rect.height : 1f;
            int height = resolution;
            int width = Mathf.Clamp(Mathf.RoundToInt(resolution * aspect), resolution, resolution * 8);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Skin Colour Plane",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            image = GetComponent<RawImage>();
            image.texture = texture;
            image.color = Color.white;
        }

        private void OnDestroy()
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }
    }
}
