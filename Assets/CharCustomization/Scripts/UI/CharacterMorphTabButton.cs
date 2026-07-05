using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sol.CharacterCustomization
{
    public sealed class CharacterMorphTabButton : MonoBehaviour
    {
        [SerializeField] private string groupId;
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text label;

        public string GroupId => groupId;
        public Button Button => button;
        public Image Background => background;
        public TMP_Text Label => label;
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(groupId) && button != null && background != null && label != null;

        public void Configure(string configuredGroupId, Button configuredButton, Image configuredBackground, TMP_Text configuredLabel)
        {
            groupId = configuredGroupId;
            button = configuredButton;
            background = configuredBackground;
            label = configuredLabel;
        }

        public void SetSelected(bool selected, Color selectedColor, Color inactiveColor)
        {
            if (background != null)
            {
                background.color = selected ? selectedColor : inactiveColor;
            }
        }
    }
}
