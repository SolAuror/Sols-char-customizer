using UnityEngine;

namespace Sol.CharacterCustomization
{
    // One tattoo, placed by this transform: a box that projects the design along its
    // forward (blue) axis onto the skin, with the design's top along its up axis. Put
    // it under a bone of the character it marks; CharacterTattoos reads its pose
    // relative to that bone, so the tattoo stays put as the character animates.
    public sealed class CharacterTattooProjector : MonoBehaviour
    {
        [Tooltip("Tattoo design. Its alpha is the ink; its colour multiplies the ink colour, so white designs take any ink.")]
        [SerializeField] private Texture2D design;
        [Tooltip("Ink colour (sRGB). Healed black ink reads as a dark blue-grey, not pure black.")]
        [SerializeField] private Color ink = new(0.09f, 0.1f, 0.12f, 1f);
        [Tooltip("How dense the ink is where the design is solid.")]
        [SerializeField, Range(0f, 1f)] private float opacity = 0.9f;

        public Texture2D Design => design;
        public Color Ink => ink;
        public float Opacity => opacity;

        public void Configure(Texture2D tattooDesign, Color inkColour, float inkOpacity)
        {
            design = tattooDesign;
            ink = inkColour;
            opacity = Mathf.Clamp01(inkOpacity);
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.9f, 0.3f, 0.6f, 0.6f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.5f);
        }
    }
}
