using System;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    // One tattoo as saved in a recipe: a catalog design and the box that projects it,
    // in the skin mesh's own space (the bind-pose mesh as imported). That space does not
    // move with animation, joint offsets, or morphs, so the tattoo stays on the same
    // patch of skin through all of them. The box projects along its forward axis into
    // the skin, with the design's top along its up axis. Its size is width, height, and
    // how deep it reaches, in the mesh's units; a negative width mirrors the design.
    // A curved tattoo wraps around an axis instead, such as an arm's, like a label round
    // a bottle: the design is unrolled around a cylinder of the curve radius whose
    // surface touches the skin at the tattoo, so it does not stretch and fade where the
    // limb turns away. A curve radius of 0 projects flat.
    [Serializable]
    public struct CharacterTattooPlacement
    {
        public const float DefaultOpacity = 0.9f;
        // Healed black ink reads as a dark blue-grey rather than pure black.
        public static readonly Color DefaultInk = new(0.09f, 0.1f, 0.12f, 1f);

        [SerializeField] private string designId;
        [SerializeField] private Vector3 position;
        [SerializeField] private Quaternion rotation;
        [SerializeField] private Vector3 size;
        [Tooltip("Ink colour (sRGB). Multiplies the design's own colour.")]
        [SerializeField] private Color ink;
        [SerializeField, Range(0f, 1f)] private float opacity;
        [SerializeField] private Vector3 curveAxis;
        [SerializeField] private float curveRadius;

        public CharacterTattooPlacement(
            string designId,
            Vector3 position,
            Quaternion rotation,
            Vector3 size,
            Color ink,
            float opacity = DefaultOpacity,
            Vector3 curveAxis = default,
            float curveRadius = 0f)
        {
            this.designId = string.IsNullOrWhiteSpace(designId) ? string.Empty : designId.Trim();
            this.position = position;
            this.rotation = Normalize(rotation);
            this.size = new Vector3(size.x, Mathf.Abs(size.y), Mathf.Abs(size.z));
            this.ink = new Color(ink.r, ink.g, ink.b, 1f);
            this.opacity = Mathf.Clamp01(opacity);
            bool curved = curveRadius > 0f && curveAxis.sqrMagnitude > 1e-12f;
            this.curveAxis = curved ? curveAxis.normalized : Vector3.zero;
            this.curveRadius = curved ? curveRadius : 0f;
        }

        public string DesignId => designId ?? string.Empty;
        public Vector3 Position => position;
        public Quaternion Rotation => Normalize(rotation);
        public Vector3 Size => size;
        public bool IsMirrored => size.x < 0f;
        public Color Ink => ink;
        public float Opacity => opacity;
        public bool IsCurved => curveRadius > 0f && curveAxis.sqrMagnitude > 1e-12f;
        // The axis the design wraps around and the radius it wraps at, in mesh space.
        public Vector3 CurveAxis => curveAxis;
        public float CurveRadius => curveRadius;

        // Takes the projector box's unit cube into skin mesh space.
        public Matrix4x4 MeshFromBox => Matrix4x4.TRS(position, Rotation, size);

        // From a box already in skin mesh space, such as a projector's pose taken through
        // its bone's bind pose. A mirrored box keeps its mirroring as a negative width.
        public static CharacterTattooPlacement FromMeshSpace(
            string designId,
            Matrix4x4 meshFromBox,
            Color ink,
            float opacity = DefaultOpacity)
        {
            Vector3 right = meshFromBox.GetColumn(0);
            Vector3 up = meshFromBox.GetColumn(1);
            Vector3 forward = meshFromBox.GetColumn(2);
            float width = Vector3.Dot(Vector3.Cross(up, forward), right) < 0f ? -right.magnitude : right.magnitude;
            return new CharacterTattooPlacement(
                designId,
                meshFromBox.GetColumn(3),
                Quaternion.LookRotation(forward, up),
                new Vector3(width, up.magnitude, forward.magnitude),
                ink,
                opacity);
        }

        public CharacterTattooPlacement WithInk(Color value, float inkOpacity)
        {
            return new CharacterTattooPlacement(DesignId, position, Rotation, size, value, inkOpacity, curveAxis, curveRadius);
        }

        private static Quaternion Normalize(Quaternion value)
        {
            float length = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            return length > 1e-6f
                ? new Quaternion(value.x / length, value.y / length, value.z / length, value.w / length)
                : Quaternion.identity;
        }
    }
}
