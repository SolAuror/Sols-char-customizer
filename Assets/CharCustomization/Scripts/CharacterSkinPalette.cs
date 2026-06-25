using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public sealed class CharacterSkinTone
    {
        [SerializeField] private string id;
        [SerializeField] private string label;
        [SerializeField] private Color color = Color.white;

        public string Id => id;
        public string Label => string.IsNullOrWhiteSpace(label) ? id : label;
        public Color Color => color;

        public CharacterSkinTone(string id, string label, Color color)
        {
            this.id = id;
            this.label = label;
            this.color = color;
        }
    }

    [CreateAssetMenu(
        fileName = "CharacterSkinPalette",
        menuName = "Sol/Character Customization/Skin Palette")]
    public sealed class CharacterSkinPalette : ScriptableObject
    {
        [SerializeField] private string defaultToneId = CharacterRecipe.DefaultSkinToneId;
        [SerializeField] private List<CharacterSkinTone> tones = new();

        public string DefaultToneId => defaultToneId;
        public IReadOnlyList<CharacterSkinTone> Tones =>
            tones ?? (IReadOnlyList<CharacterSkinTone>)Array.Empty<CharacterSkinTone>();

        public bool TryGet(string toneId, out CharacterSkinTone tone)
        {
            if (tones != null && !string.IsNullOrWhiteSpace(toneId))
            {
                tone = tones.Find(candidate =>
                    candidate != null && string.Equals(candidate.Id, toneId, StringComparison.Ordinal));
                if (tone != null)
                {
                    return true;
                }
            }

            tone = null;
            return false;
        }

        public CharacterSkinTone GetDefault()
        {
            return TryGet(defaultToneId, out CharacterSkinTone tone)
                ? tone
                : tones != null && tones.Count > 0 ? tones[0] : null;
        }

#if UNITY_EDITOR
        public void ConfigureDefaults(IReadOnlyList<CharacterSkinTone> defaultTones, string defaultId)
        {
            tones = defaultTones != null
                ? new List<CharacterSkinTone>(defaultTones)
                : new List<CharacterSkinTone>();
            defaultToneId = defaultId;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
