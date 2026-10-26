using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Marks one hairstyle attached to a character; Hair Setup adds it to the hair's
    // root under the character. CharacterHair lists these under the active character
    // to show one and colour it.
    public sealed class CharacterHairStyle : MonoBehaviour
    {
        [SerializeField] private string styleId;
        [SerializeField] private string displayName;
        [Tooltip("Square thumbnail for the hair menu (T_Sol_<style>_Preview from Export Hair).")]
        [SerializeField] private Sprite preview;

        public string StyleId => string.IsNullOrWhiteSpace(styleId) ? name : styleId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? StyleId : displayName;
        public Sprite Preview => preview;

        public void Configure(string id, string label, Sprite thumbnail = null)
        {
            styleId = id;
            displayName = label;
            preview = thumbnail;
        }
    }
}
