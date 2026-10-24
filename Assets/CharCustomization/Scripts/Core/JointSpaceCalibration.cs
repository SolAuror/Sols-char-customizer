using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Finds how the exporter's armature axes map onto an imported skeleton. FBX
    // export and Unity import only ever swap, flip, and uniformly scale axes, so the
    // solver tries every signed axis permutation (including mirrors, since Unity
    // changes handedness) and fits scale and translation by least squares on
    // matching rest bone heads.
    public readonly struct JointSpaceCalibration
    {
        private JointSpaceCalibration(Matrix4x4 axes, float scale, Vector3 translation, float rmsError, float extent)
        {
            Axes = axes;
            Scale = scale;
            Translation = translation;
            RmsError = rmsError;
            Extent = extent;
        }

        // Signed permutation, as the upper 3x3 of the matrix.
        public Matrix4x4 Axes { get; }
        public float Scale { get; }
        public Vector3 Translation { get; }
        public float RmsError { get; }
        public float Extent { get; }

        // Fit error relative to the skeleton's size; well-matched rigs are near zero.
        public float RelativeError => Extent > 0f ? RmsError / Extent : float.PositiveInfinity;

        public Vector3 TransformPoint(Vector3 point)
        {
            return Axes.MultiplyVector(point) * Scale + Translation;
        }

        public Vector3 TransformVector(Vector3 vector)
        {
            return Axes.MultiplyVector(vector) * Scale;
        }

        public static bool TrySolve(IReadOnlyList<Vector3> from, IReadOnlyList<Vector3> to, out JointSpaceCalibration calibration)
        {
            calibration = default;
            int count = Mathf.Min(from.Count, to.Count);
            if (count < 3)
            {
                return false;
            }

            Vector3 fromCentre = Vector3.zero;
            Vector3 toCentre = Vector3.zero;
            for (int index = 0; index < count; index++)
            {
                fromCentre += from[index];
                toCentre += to[index];
            }

            fromCentre /= count;
            toCentre /= count;

            float fromSpread = 0f;
            float extent = 0f;
            for (int index = 0; index < count; index++)
            {
                fromSpread += (from[index] - fromCentre).sqrMagnitude;
                extent = Mathf.Max(extent, (to[index] - toCentre).magnitude);
            }

            if (fromSpread <= 1e-12f)
            {
                return false;
            }

            bool found = false;
            float bestError = float.PositiveInfinity;
            foreach (Matrix4x4 axes in SignedPermutations())
            {
                float correlation = 0f;
                for (int index = 0; index < count; index++)
                {
                    correlation += Vector3.Dot(axes.MultiplyVector(from[index] - fromCentre), to[index] - toCentre);
                }

                float scale = correlation / fromSpread;
                if (scale <= 0f)
                {
                    continue;
                }

                Vector3 translation = toCentre - axes.MultiplyVector(fromCentre) * scale;
                float squaredError = 0f;
                for (int index = 0; index < count; index++)
                {
                    squaredError += (axes.MultiplyVector(from[index]) * scale + translation - to[index]).sqrMagnitude;
                }

                float error = Mathf.Sqrt(squaredError / count);
                if (error < bestError)
                {
                    bestError = error;
                    calibration = new JointSpaceCalibration(axes, scale, translation, error, extent);
                    found = true;
                }
            }

            return found;
        }

        private static IEnumerable<Matrix4x4> SignedPermutations()
        {
            int[][] permutations =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
            };

            foreach (int[] permutation in permutations)
            {
                for (int signs = 0; signs < 8; signs++)
                {
                    Matrix4x4 matrix = Matrix4x4.zero;
                    for (int row = 0; row < 3; row++)
                    {
                        matrix[row, permutation[row]] = (signs & (1 << row)) != 0 ? -1f : 1f;
                    }

                    matrix[3, 3] = 1f;
                    yield return matrix;
                }
            }
        }
    }
}
