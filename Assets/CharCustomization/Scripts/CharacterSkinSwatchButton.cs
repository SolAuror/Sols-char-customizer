using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterSkinSwatchButton : MonoBehaviour
    {
        [SerializeField] private string skinToneId;
        [SerializeField] private Button button;
        [SerializeField] private Image swatchImage;
        [SerializeField] private TMP_Text label;

        public string SkinToneId => skinToneId;
        public Button Button => button;
        public Image SwatchImage => swatchImage;
        public TMP_Text Label => label;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(skinToneId) && button != null && swatchImage != null;

#if UNITY_EDITOR
        internal void Configure(string toneId, CharacterSkinTone tone, Button targetButton, Image targetImage, TMP_Text targetLabel)
        {
            skinToneId = toneId;
            button = targetButton;
            swatchImage = targetImage;
            label = targetLabel;
            if (tone != null)
            {
                swatchImage.color = tone.Color;
                if (label != null)
                {
                    label.text = tone.Label;
                }
            }
        }
#endif
    }
}
