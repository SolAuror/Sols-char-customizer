using System;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    [Serializable]
    public struct CharacterSkinParameters
    {
        public static readonly CharacterSkinParameters Default = new(0.25f, 0.15f, 0.35f);

        [Tooltip("Perceptual melanin, 0 very fair to 1 very deep. Evenly spaced across the Monk Skin Tone Scale.")]
        [SerializeField, Range(0f, 1f)] private float melanin;
        [Tooltip("Blood in the dermis: 0 sallow or pale, 1 flushed or ruddy.")]
        [SerializeField, Range(0f, 1f)] private float hemoglobin;
        [Tooltip("Pheomelanin share: 0 olive or neutral, 1 golden or warm.")]
        [SerializeField, Range(0f, 1f)] private float undertone;

        public CharacterSkinParameters(float melanin, float hemoglobin, float undertone)
        {
            this.melanin = Mathf.Clamp01(melanin);
            this.hemoglobin = Mathf.Clamp01(hemoglobin);
            this.undertone = Mathf.Clamp01(undertone);
        }

        public float Melanin => melanin;
        public float Hemoglobin => hemoglobin;
        public float Undertone => undertone;
    }

    // Two-layer absorption skin model: epidermal melanin (eumelanin and pheomelanin)
    // and dermal hemoglobin attenuate a bloodless, melanin-free dermis, with a small
    // neutral surface reflection that keeps very deep skin from turning orange. The
    // constants were fitted to the ten Monk Skin Tone Scale swatches.
    public static class SkinColorModel
    {
        // Undertone slices laid side by side in the colour table: 0, 0.5, and 1.
        public const int LutSliceCount = 3;
        public const int DefaultLutSize = 64;

        private static readonly Vector3 DermisReflectance = new(1f, 0.9543f, 0.7933f);
        private static readonly Vector3 EumelaninAbsorption = new(1f, 1.2296f, 1.5358f);
        private static readonly Vector3 PheomelaninAbsorption = new(1f, 1.558f, 2.1385f);

        // Oxyhemoglobin extinction near the sRGB primaries (611, 549, 464 nm), relative to green.
        private static readonly Vector3 HemoglobinAbsorption = new(0.035f, 1f, 0.77f);

        private const float MaxMelaninDensity = 7.4194f;
        private const float MelaninDensityExponent = 2.2298f;
        private const float MaxHemoglobinDensity = 0.6f;
        private const float SurfaceReflectance = 0.0212f;

        // Fitted melanin concentration at slider 0, then each Monk swatch at 0.1 steps.
        private static readonly float[] MelaninBySlider =
        {
            0f, 0.0586f, 0.1149f, 0.1324f, 0.2055f, 0.2916f, 0.4419f, 0.5115f, 0.5924f, 0.722f, 1f
        };

        public static float MelaninConcentration(float slider)
        {
            float scaled = Mathf.Clamp01(slider) * (MelaninBySlider.Length - 1);
            int lower = Mathf.Min((int)scaled, MelaninBySlider.Length - 2);
            return Mathf.Lerp(MelaninBySlider[lower], MelaninBySlider[lower + 1], scaled - lower);
        }

        public static Color EvaluateLinear(CharacterSkinParameters parameters)
        {
            float melaninDensity = MaxMelaninDensity *
                                   Mathf.Pow(MelaninConcentration(parameters.Melanin), MelaninDensityExponent);
            float hemoglobinDensity = MaxHemoglobinDensity * parameters.Hemoglobin;
            Vector3 melaninAbsorption = Vector3.Lerp(EumelaninAbsorption, PheomelaninAbsorption, parameters.Undertone);

            return new Color(
                Channel(0, melaninDensity, hemoglobinDensity, melaninAbsorption),
                Channel(1, melaninDensity, hemoglobinDensity, melaninAbsorption),
                Channel(2, melaninDensity, hemoglobinDensity, melaninAbsorption),
                1f);
        }

        // Layout read by the Sol/Character Skin shader: LutSliceCount slices side by
        // side, each size x size; x is melanin, y is hemoglobin, slices are undertone.
        public static Texture2D GenerateLut(int size = DefaultLutSize)
        {
            size = Mathf.Max(2, size);
            var texture = new Texture2D(size * LutSliceCount, size, TextureFormat.RGBA32, false, false)
            {
                name = "SkinColorTable",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[size * LutSliceCount * size];
            for (int slice = 0; slice < LutSliceCount; slice++)
            {
                float undertone = slice / (float)(LutSliceCount - 1);
                for (int y = 0; y < size; y++)
                {
                    float hemoglobin = y / (float)(size - 1);
                    for (int x = 0; x < size; x++)
                    {
                        float melanin = x / (float)(size - 1);
                        Color linear = EvaluateLinear(new CharacterSkinParameters(melanin, hemoglobin, undertone));
                        pixels[y * size * LutSliceCount + slice * size + x] = linear.gamma;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        // Matches the shader's sampling: half-texel inset inside each slice, bilinear
        // within a slice, linear blend between the two nearest undertone slices.
        public static Color SampleLutLinear(Texture2D lut, CharacterSkinParameters parameters)
        {
            float sliceWidth = 1f / LutSliceCount;
            float halfTexelU = 0.5f / lut.width;
            float halfTexelV = 0.5f / lut.height;
            float u = Mathf.Lerp(halfTexelU, sliceWidth - halfTexelU, parameters.Melanin);
            float v = Mathf.Lerp(halfTexelV, 1f - halfTexelV, parameters.Hemoglobin);
            float slicePosition = parameters.Undertone * (LutSliceCount - 1);
            int slice = Mathf.Min((int)slicePosition, LutSliceCount - 2);

            Color lower = lut.GetPixelBilinear(slice * sliceWidth + u, v).linear;
            Color upper = lut.GetPixelBilinear((slice + 1) * sliceWidth + u, v).linear;
            return Color.Lerp(lower, upper, slicePosition - slice);
        }

        // Inverse lookup for converting authored or eyedropped colours. Searches a
        // coarse grid, then refines, minimising CIE Lab distance.
        public static CharacterSkinParameters FindClosest(
            Color targetLinear,
            Func<CharacterSkinParameters, Color> evaluateLinear = null)
        {
            evaluateLinear ??= EvaluateLinear;
            Vector3 target = LinearToLab(targetLinear);
            CharacterSkinParameters best = CharacterSkinParameters.Default;
            float bestDistance = float.MaxValue;

            const int melaninSteps = 32;
            const int hemoglobinSteps = 16;
            const int undertoneSteps = 4;
            for (int m = 0; m <= melaninSteps; m++)
            {
                for (int h = 0; h <= hemoglobinSteps; h++)
                {
                    for (int t = 0; t <= undertoneSteps; t++)
                    {
                        var candidate = new CharacterSkinParameters(
                            m / (float)melaninSteps,
                            h / (float)hemoglobinSteps,
                            t / (float)undertoneSteps);
                        float distance = LabDistanceSquared(target, evaluateLinear(candidate));
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = candidate;
                        }
                    }
                }
            }

            float step = 1f / melaninSteps;
            while (step > 0.001f)
            {
                bool improved = false;
                for (int axis = 0; axis < 3; axis++)
                {
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        CharacterSkinParameters candidate = Offset(best, axis, direction * step);
                        float distance = LabDistanceSquared(target, evaluateLinear(candidate));
                        if (distance < bestDistance)
                        {
                            bestDistance = distance;
                            best = candidate;
                            improved = true;
                        }
                    }
                }

                if (!improved)
                {
                    step *= 0.5f;
                }
            }

            return best;
        }

        public static Vector3 LinearToLab(Color linear)
        {
            float x = 0.4124f * linear.r + 0.3576f * linear.g + 0.1805f * linear.b;
            float y = 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
            float z = 0.0193f * linear.r + 0.1192f * linear.g + 0.9505f * linear.b;
            float fx = LabCurve(x / 0.95047f);
            float fy = LabCurve(y);
            float fz = LabCurve(z / 1.08883f);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float Channel(int index, float melaninDensity, float hemoglobinDensity, Vector3 melaninAbsorption)
        {
            float transmitted = Mathf.Exp(-melaninDensity * melaninAbsorption[index]) *
                                Mathf.Exp(-hemoglobinDensity * HemoglobinAbsorption[index]);
            return DermisReflectance[index] * (SurfaceReflectance + (1f - SurfaceReflectance) * transmitted);
        }

        private static CharacterSkinParameters Offset(CharacterSkinParameters parameters, int axis, float amount)
        {
            return new CharacterSkinParameters(
                parameters.Melanin + (axis == 0 ? amount : 0f),
                parameters.Hemoglobin + (axis == 1 ? amount : 0f),
                parameters.Undertone + (axis == 2 ? amount : 0f));
        }

        private static float LabDistanceSquared(Vector3 targetLab, Color candidateLinear)
        {
            return (LinearToLab(candidateLinear) - targetLab).sqrMagnitude;
        }

        private static float LabCurve(float value)
        {
            return value > 0.008856f ? Mathf.Pow(value, 1f / 3f) : 7.787f * value + 16f / 116f;
        }
    }
}
