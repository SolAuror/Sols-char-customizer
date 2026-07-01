using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterSkinSwatchButton : MonoBehaviour
    {
        [SerializeField] private string skinToneId;
        [SerializeField] private Button button;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Image swatchImage;
        [SerializeField] private TMP_Text label;

        public string SkinToneId => skinToneId;
        public Button Button => button;
        public Button DeleteButton => deleteButton;
        public Image SwatchImage => swatchImage;
        public TMP_Text Label => label;
        public Color SwatchColor => swatchImage != null ? swatchImage.color : Color.white;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(skinToneId) && button != null && swatchImage != null;

        public void ConfigureRuntime(
            string toneId,
            string displayLabel,
            Color color,
            Button targetButton = null,
            Button targetDeleteButton = null,
            Image targetImage = null,
            TMP_Text targetLabel = null)
        {
            skinToneId = toneId;
            if (targetButton != null)
            {
                button = targetButton;
            }

            if (targetDeleteButton != null)
            {
                deleteButton = targetDeleteButton;
            }

            if (targetImage != null)
            {
                swatchImage = targetImage;
            }

            if (targetLabel != null)
            {
                label = targetLabel;
            }

            SetDisplay(displayLabel, color);
        }

        public void SetDeleteVisible(bool visible)
        {
            if (deleteButton != null)
            {
                deleteButton.gameObject.SetActive(visible);
                deleteButton.interactable = visible;
            }
        }

        public void SetDisplay(string displayLabel, Color color)
        {
            if (swatchImage != null)
            {
                swatchImage.color = color;
            }

            if (label != null && !string.IsNullOrWhiteSpace(displayLabel))
            {
                label.text = displayLabel;
            }
        }

#if UNITY_EDITOR
        internal void Configure(
            string toneId,
            CharacterSkinTone tone,
            Button targetButton,
            Image targetImage,
            TMP_Text targetLabel)
        {
            skinToneId = toneId;
            button = targetButton;
            swatchImage = targetImage;
            label = targetLabel;
            if (tone != null)
            {
                SetDisplay(tone.Label, tone.Color);
            }
        }
#endif
    }
}
