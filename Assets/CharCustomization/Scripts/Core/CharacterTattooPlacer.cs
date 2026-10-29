using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Turns a point on the skin into a tattoo placement and back, in skin mesh space.
    // A tattoo is described the way someone places one: where on the skin, which way the
    // skin faces there, how far the design is turned from the character's up direction
    // as seen on that patch of skin, its size, and whether it is mirrored. Its box reaches
    // out of the skin far enough to cover skin that rises around the point, such as the
    // back either side of the spine, and inward far enough to wrap around a limb without
    // reaching through to the body behind it. Both grow with the design.
    public static class CharacterTattooPlacer
    {
        // How far the box reaches out of the skin: at least this, and this much per metre
        // of the design's larger side.
        public const float OutwardReach = 0.02f;
        public const float OutwardReachPerSize = 0.3f;
        // Inward reach per metre of the design's larger side; wraps a limb about as wide
        // as the design.
        public const float InwardReachPerSize = 0.6f;
        public const float MinimumSize = 0.02f;
        public const float MaximumSize = 0.6f;

        public static CharacterTattooPlacement Place(
            string designId,
            Vector3 point,
            Vector3 normal,
            Vector3 meshUp,
            float angleDegrees,
            Vector2 size,
            bool mirrored,
            Color ink,
            float opacity,
            Vector3 curveAxis = default,
            float curveRadius = 0f)
        {
            normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.forward;
            float width = Mathf.Clamp(Mathf.Abs(size.x), MinimumSize, MaximumSize);
            float height = Mathf.Clamp(Mathf.Abs(size.y), MinimumSize, MaximumSize);
            float inward = InwardReachPerSize * Mathf.Max(width, height);
            float outward = Mathf.Max(OutwardReach, OutwardReachPerSize * Mathf.Max(width, height));
            float depth = outward + inward;
            Vector3 forward = -normal;
            Quaternion rotation = Quaternion.LookRotation(forward, SkinUp(normal, meshUp))
                                  * Quaternion.AngleAxis(angleDegrees, Vector3.forward);
            Vector3 centre = point + normal * ((outward - inward) * 0.5f);
            return new CharacterTattooPlacement(
                designId, centre, rotation, new Vector3(mirrored ? -width : width, height, depth), ink, opacity,
                curveAxis, curveRadius);
        }

        // The skin point, normal, angle, and size a placement was made from.
        public static void Describe(
            CharacterTattooPlacement placement,
            Vector3 meshUp,
            out Vector3 point,
            out Vector3 normal,
            out float angleDegrees,
            out Vector2 size,
            out bool mirrored)
        {
            Quaternion rotation = placement.Rotation;
            Vector3 forward = rotation * Vector3.forward;
            normal = -forward;
            Vector3 boxSize = placement.Size;
            // Inward reach follows from the size; the rest of the depth reaches outward,
            // whatever outward reach the placement was made with.
            float inward = InwardReachPerSize * Mathf.Max(Mathf.Abs(boxSize.x), boxSize.y);
            float outward = boxSize.z - inward;
            point = placement.Position - normal * ((outward - inward) * 0.5f);
            angleDegrees = Vector3.SignedAngle(SkinUp(normal, meshUp), rotation * Vector3.up, forward);
            size = new Vector2(Mathf.Abs(boxSize.x), boxSize.y);
            mirrored = placement.IsMirrored;
        }

        // The same tattoo moved to another point on the skin, keeping its turn and size;
        // it wraps with the curve of the skin it moves to (none for flat).
        public static CharacterTattooPlacement Move(
            CharacterTattooPlacement placement,
            Vector3 point,
            Vector3 normal,
            Vector3 meshUp,
            Vector3 curveAxis = default,
            float curveRadius = 0f)
        {
            Describe(placement, meshUp, out _, out _, out float angle, out Vector2 size, out bool mirrored);
            return Place(placement.DesignId, point, normal, meshUp, angle, size, mirrored, placement.Ink, placement.Opacity,
                curveAxis, curveRadius);
        }

        // The same tattoo with another turn, size, or mirroring, about the same point and
        // with the same curve.
        public static CharacterTattooPlacement Reshape(
            CharacterTattooPlacement placement, Vector3 meshUp, float angleDegrees, Vector2 size, bool mirrored)
        {
            Describe(placement, meshUp, out Vector3 point, out Vector3 normal, out _, out _, out _);
            return Place(placement.DesignId, point, normal, meshUp, angleDegrees, size, mirrored, placement.Ink, placement.Opacity,
                placement.CurveAxis, placement.CurveRadius);
        }

        // The same tattoo with the box this placer makes now, for placements saved when it
        // reached less far out of the skin; boxes that already reach as far are kept.
        public static CharacterTattooPlacement Refit(CharacterTattooPlacement placement)
        {
            Describe(placement, Vector3.up, out Vector3 point, out Vector3 normal, out float angle, out Vector2 size, out bool mirrored);
            float largest = Mathf.Max(size.x, size.y);
            float outward = placement.Size.z - InwardReachPerSize * largest;
            if (outward >= Mathf.Max(OutwardReach, OutwardReachPerSize * largest) - 1e-5f)
            {
                return placement;
            }

            return Place(placement.DesignId, point, normal, Vector3.up, angle, size, mirrored, placement.Ink, placement.Opacity,
                placement.CurveAxis, placement.CurveRadius);
        }

        // The character's up direction along the skin; across the skin's normal when the
        // skin faces straight up or down, such as the top of the head or the soles.
        private static Vector3 SkinUp(Vector3 normal, Vector3 meshUp)
        {
            Vector3 up = Vector3.ProjectOnPlane(meshUp, normal);
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Vector3.ProjectOnPlane(Mathf.Abs(normal.x) < 0.9f ? Vector3.right : Vector3.forward, normal);
            }

            return up.normalized;
        }
    }
}
