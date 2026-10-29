using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public sealed class CharacterTattooDesign
    {
        [SerializeField] private string id;
        [SerializeField] private string label;
        [Tooltip("Alpha is the ink. A white design takes any ink colour; a coloured one is multiplied by it.")]
        [SerializeField] private Texture2D texture;
        [Tooltip("White ink masks take the chosen ink colour. Turn off for full-colour designs, which keep their own colours (use white ink).")]
        [SerializeField] private bool tintable = true;
        [Tooltip("Width and height a new tattoo of this design starts at, in metres.")]
        [SerializeField] private Vector2 defaultSize = new(0.11f, 0.11f);

        public CharacterTattooDesign(string id, string label, Texture2D texture, bool tintable = true)
        {
            this.id = id;
            this.label = label;
            this.texture = texture;
            this.tintable = tintable;
        }

        public string Id => id;
        public string Label => string.IsNullOrWhiteSpace(label) ? id : label;
        public Texture2D Texture => texture;
        public bool Tintable => tintable;
        public Vector2 DefaultSize => defaultSize;
    }

    // The tattoo designs characters can wear. Recipes refer to designs by ID, so a
    // design's texture can be replaced without breaking saved characters.
    [CreateAssetMenu(
        fileName = "CharacterTattooCatalog",
        menuName = "Sol/Character Customization/Tattoo Catalog")]
    public sealed class CharacterTattooCatalog : ScriptableObject
    {
        [SerializeField] private List<CharacterTattooDesign> designs = new();

        public IReadOnlyList<CharacterTattooDesign> Designs =>
            designs ?? (IReadOnlyList<CharacterTattooDesign>)Array.Empty<CharacterTattooDesign>();

        public bool TryGet(string designId, out CharacterTattooDesign design)
        {
            string normalizedId = designId?.Trim();
            foreach (CharacterTattooDesign candidate in Designs)
            {
                if (candidate != null && string.Equals(candidate.Id, normalizedId, StringComparison.Ordinal))
                {
                    design = candidate;
                    return true;
                }
            }

            design = null;
            return false;
        }

        // The design using this texture, for turning scene projectors into recipe tattoos.
        public bool TryFind(Texture2D texture, out CharacterTattooDesign design)
        {
            foreach (CharacterTattooDesign candidate in Designs)
            {
                if (candidate != null && texture != null && candidate.Texture == texture)
                {
                    design = candidate;
                    return true;
                }
            }

            design = null;
            return false;
        }

#if UNITY_EDITOR
        public void ConfigureDesigns(IReadOnlyList<CharacterTattooDesign> catalogDesigns)
        {
            designs = catalogDesigns != null
                ? new List<CharacterTattooDesign>(catalogDesigns)
                : new List<CharacterTattooDesign>();
        }
#endif
    }
}
