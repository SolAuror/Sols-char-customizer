using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Chooses and colours the hairstyle on the active character. Hairstyles are the
    // CharacterHairStyle objects under each sex's character root; one is shown at a
    // time, or none. The colour reaches every hair renderer through a property block,
    // so the shared hair materials stay as authored.
    public sealed class CharacterHair : MonoBehaviour
    {
        public const string NoneStyleId = "none";
        public const float DefaultSheen = 0.25f;

        // Sheen maps to the Sol/Character Hair highlight: matte hair has a faint, broad
        // highlight, glossy hair a bright, tight one.
        private const float MaxHighlightStrength = 0.6f;
        private const float MatteSharpness = 80f;
        private const float GlossySharpness = 480f;
        private const float SecondarySharpnessScale = 0.25f;

        private static readonly int HighlightStrengthId = Shader.PropertyToID("_SpecularStrength");
        private static readonly int PrimarySharpnessId = Shader.PropertyToID("_SpecularExponent");
        private static readonly int SecondarySharpnessId = Shader.PropertyToID("_SecondaryExponent");

        [SerializeField] private CharacterMorphController controller;
        [SerializeField] private CharacterHairPalette palette;
        [Tooltip("Hairstyle to show. Empty shows the character's first one; 'none' hides hair.")]
        [SerializeField] private string styleId = string.Empty;
        [Tooltip("Hair colour (sRGB). In Play Mode, changes here apply immediately.")]
        [SerializeField] private Color color = new(0.27f, 0.19f, 0.12f, 1f);
        [Tooltip("0 matte, 1 glossy. Overrides the hair materials' highlight strength and sharpness.")]
        [SerializeField, Range(0f, 1f)] private float sheen = DefaultSheen;
        [SerializeField] private string[] colorPropertyNames = { "_BaseColor" };

        private MaterialPropertyBlock propertyBlock;
        private int[] colorPropertyIds;
        private Transform appliedRoot;

        public event Action Changed;

        public CharacterHairPalette Palette => palette;
        public string StyleId => styleId ?? string.Empty;
        public Color Color => color;
        public float Sheen => sheen;
        public Transform ActiveCharacterRoot => controller != null ? controller.ActiveCharacterRoot : null;

        // Hairstyles on the active character, including hidden ones.
        public IReadOnlyList<CharacterHairStyle> AvailableStyles
        {
            get
            {
                Transform root = ActiveCharacterRoot;
                return root != null
                    ? root.GetComponentsInChildren<CharacterHairStyle>(true)
                    : Array.Empty<CharacterHairStyle>();
            }
        }

        // The hairstyle being shown, or null for none.
        public CharacterHairStyle CurrentStyle => ResolveStyle(AvailableStyles);

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<CharacterMorphController>();
            }
        }

        private void Start()
        {
            Apply();
        }

        // The controller switches sex by activating the other character root, which has
        // its own hair; follow it.
        private void LateUpdate()
        {
            if (ActiveCharacterRoot != appliedRoot)
            {
                Apply();
            }
        }

        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled)
            {
                Apply();
            }
        }

        public void SetStyle(string id)
        {
            styleId = string.IsNullOrWhiteSpace(id) ? string.Empty : id.Trim();
            Apply();
            Changed?.Invoke();
        }

        public void SetColor(Color value)
        {
            color = new Color(value.r, value.g, value.b, 1f);
            ApplyLook(CurrentStyle);
            Changed?.Invoke();
        }

        public void SetSheen(float value)
        {
            sheen = Mathf.Clamp01(value);
            ApplyLook(CurrentStyle);
            Changed?.Invoke();
        }

        public bool SetColorOption(string colorId)
        {
            if (palette == null || !palette.TryGet(colorId, out CharacterHairColorOption option))
            {
                return false;
            }

            SetColor(option.Color);
            return true;
        }

        public void Apply()
        {
            appliedRoot = ActiveCharacterRoot;
            IReadOnlyList<CharacterHairStyle> styles = AvailableStyles;
            CharacterHairStyle shown = ResolveStyle(styles);
            foreach (CharacterHairStyle style in styles)
            {
                if (style.gameObject.activeSelf != (style == shown))
                {
                    style.gameObject.SetActive(style == shown);
                }
            }

            ApplyLook(shown);
        }

        private CharacterHairStyle ResolveStyle(IReadOnlyList<CharacterHairStyle> styles)
        {
            if (string.Equals(StyleId, NoneStyleId, StringComparison.OrdinalIgnoreCase) || styles.Count == 0)
            {
                return null;
            }

            foreach (CharacterHairStyle style in styles)
            {
                if (string.Equals(style.StyleId, StyleId, StringComparison.Ordinal))
                {
                    return style;
                }
            }

            // Empty or unknown (a style this character does not have): the first one.
            return styles[0];
        }

        private void ApplyLook(CharacterHairStyle style)
        {
            if (style == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            colorPropertyIds ??= Array.ConvertAll(colorPropertyNames ?? Array.Empty<string>(), Shader.PropertyToID);
            float sharpness = Mathf.Lerp(MatteSharpness, GlossySharpness, sheen);
            foreach (Renderer hairRenderer in style.GetComponentsInChildren<Renderer>(true))
            {
                hairRenderer.GetPropertyBlock(propertyBlock);
                foreach (int id in colorPropertyIds)
                {
                    // Like Material.SetColor, this takes sRGB and converts in linear projects.
                    propertyBlock.SetColor(id, color);
                }

                propertyBlock.SetFloat(HighlightStrengthId, sheen * MaxHighlightStrength);
                propertyBlock.SetFloat(PrimarySharpnessId, sharpness);
                propertyBlock.SetFloat(SecondarySharpnessId, sharpness * SecondarySharpnessScale);
                hairRenderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
