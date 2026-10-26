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

    // How a skin colour space reads the third skin parameter.
    public enum SkinThirdAxis
    {
        // Pheomelanin share: olive or neutral to golden or warm.
        Undertone,
        // How much fantasy pigment shows: 0 plain human skin, 1 the full colour.
        Tint
    }

    // One skin colour space. Every space is human skin (eumelanin and pheomelanin
    // over a bloodless dermis, with dermal blood), and a fantasy space adds one more
    // pigment on top. Fantasy colours therefore keep the brown depth, flush, and
    // regional detail of real skin instead of reading as paint. In a fantasy space
    // the undertone is fixed and the third parameter is tint.
    public sealed class SkinPigments
    {
        // Fantasy spaces stop short of the deepest human melanin, where every space
        // converges on the same near-black brown and loses its identity.
        public const float FantasyMaxMelanin = 0.7f;
        public const float FantasyUndertone = 0.35f;

        // Free-hue references share one lightness and chroma (CIE Lab), so every hue
        // reads as skin rather than as a saturated paint colour.
        public const float FreeHueLightness = 58f;
        public const float FreeHueChroma = 32f;

        // Where a fantasy reference colour sits: light-medium skin, typical flush, full tint.
        public static readonly CharacterSkinParameters ReferenceParameters = new(0.3f, 0.15f, 1f);

        public static readonly SkinPigments Human = new(Vector3.zero, SkinThirdAxis.Undertone, FantasyUndertone, 1f, 1f);

        public SkinPigments(
            Vector3 pigment,
            SkinThirdAxis thirdAxis,
            float fixedUndertone,
            float maxMelanin,
            float hemoglobinStrength)
        {
            Pigment = pigment;
            ThirdAxis = thirdAxis;
            FixedUndertone = Mathf.Clamp01(fixedUndertone);
            MaxMelanin = Mathf.Clamp(maxMelanin, 0.05f, 1f);
            HemoglobinStrength = Mathf.Clamp01(hemoglobinStrength);
        }

        // Optical density of the fantasy pigment per linear sRGB channel at full tint.
        public Vector3 Pigment { get; }
        public SkinThirdAxis ThirdAxis { get; }
        public float FixedUndertone { get; }
        // The melanin parameter's full range maps to 0..MaxMelanin of human melanin.
        public float MaxMelanin { get; }
        // How much blood shows: 1 as in human skin, 0 bloodless.
        public float HemoglobinStrength { get; }

        // Solves the pigment that puts referenceSrgb exactly at ReferenceParameters.
        // Pigment only absorbs, so a channel the reference has more of than the bare
        // skin beneath it gets no pigment and that channel falls short.
        public static SkinPigments FromReference(
            Color referenceSrgb,
            float hemoglobinStrength = 1f,
            float maxMelanin = FantasyMaxMelanin)
        {
            var bare = new SkinPigments(Vector3.zero, SkinThirdAxis.Tint, FantasyUndertone, maxMelanin, hemoglobinStrength);
            CharacterSkinParameters at = ReferenceParameters;
            Color bareSkin = SkinColorModel.EvaluateLinear(new CharacterSkinParameters(at.Melanin, at.Hemoglobin, 0f), bare);
            Color reference = referenceSrgb.linear;
            var pigment = new Vector3();
            for (int channel = 0; channel < 3; channel++)
            {
                float dermis = SkinColorModel.DermisReflectance[channel];
                float surface = SkinColorModel.SurfaceReflectance;
                float transmitted = (reference[channel] / dermis - surface) / Mathf.Max(bareSkin[channel] / dermis - surface, 1e-6f);
                pigment[channel] = -Mathf.Log(Mathf.Clamp(transmitted, 0.0001f, 1f)) / at.Undertone;
            }

            return new SkinPigments(pigment, SkinThirdAxis.Tint, FantasyUndertone, maxMelanin, hemoglobinStrength);
        }

        public static SkinPigments FromHue(
            float hueDegrees,
            float hemoglobinStrength = 1f,
            float maxMelanin = FantasyMaxMelanin)
        {
            return FromReference(FreeHueReference(hueDegrees), hemoglobinStrength, maxMelanin);
        }

        // The sRGB colour a free-hue space reaches at ReferenceParameters.
        public static Color FreeHueReference(float hueDegrees)
        {
            float radians = hueDegrees * Mathf.Deg2Rad;
            Color linear = SkinColorModel.LabToLinear(new Vector3(
                FreeHueLightness,
                FreeHueChroma * Mathf.Cos(radians),
                FreeHueChroma * Mathf.Sin(radians)));
            return linear.gamma;
        }
    }

    // Two-layer absorption skin model: epidermal melanin (eumelanin and pheomelanin)
    // and dermal hemoglobin attenuate a bloodless, melanin-free dermis, with a small
    // neutral surface reflection that keeps very deep skin from turning orange. The
    // constants were fitted to the ten Monk Skin Tone Scale swatches. Fantasy spaces
    // (SkinPigments) add one more absorbing pigment.
    public static class SkinColorModel
    {
        // Third-axis slices laid side by side in generated colour tables. Readers take
        // the count from the table's aspect ratio, so older three-slice tables still
        // work. Slices blend geometrically, which follows absorption closely; nine
        // keep fantasy tint within about 1 CIE Lab unit of the model.
        public const int LutSliceCount = 9;
        public const int DefaultLutSize = 64;

        internal static readonly Vector3 DermisReflectance = new(1f, 0.9543f, 0.7933f);
        private static readonly Vector3 EumelaninAbsorption = new(1f, 1.2296f, 1.5358f);
        private static readonly Vector3 PheomelaninAbsorption = new(1f, 1.558f, 2.1385f);

        // Oxyhemoglobin extinction near the sRGB primaries (611, 549, 464 nm), relative to green.
        private static readonly Vector3 HemoglobinAbsorption = new(0.035f, 1f, 0.77f);

        internal const float SurfaceReflectance = 0.0212f;
        private const float MaxMelaninDensity = 7.4194f;
        private const float MelaninDensityExponent = 2.2298f;
        private const float MaxHemoglobinDensity = 0.6f;

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

        public static float MelaninSlider(float concentration)
        {
            concentration = Mathf.Clamp01(concentration);
            int lower = MelaninBySlider.Length - 2;
            for (int index = lower - 1; index >= 0; index--)
            {
                if (concentration <= MelaninBySlider[index + 1])
                {
                    lower = index;
                }
            }

            float span = Mathf.Max(MelaninBySlider[lower + 1] - MelaninBySlider[lower], 1e-5f);
            return (lower + (concentration - MelaninBySlider[lower]) / span) / (MelaninBySlider.Length - 1);
        }

        // Region melanin scales multiply optical density, not the perceptual slider.
        // The slider is steep at the deep end, so scaling it turns deep-skin palms
        // nearly white and lips light red. Mirrored by SolScaleMelaninDensity in
        // SolSkinInput.hlsl.
        public static float ScaleMelaninDensity(float melanin, float densityScale)
        {
            float concentrationScale = Mathf.Pow(Mathf.Max(densityScale, 0f), 1f / MelaninDensityExponent);
            return MelaninSlider(MelaninConcentration(melanin) * concentrationScale);
        }

        internal static float MelaninDensity(float melanin)
        {
            return MaxMelaninDensity * Mathf.Pow(MelaninConcentration(melanin), MelaninDensityExponent);
        }

        internal static float HemoglobinDensity(float hemoglobin)
        {
            return MaxHemoglobinDensity * hemoglobin;
        }

        public static Color EvaluateLinear(CharacterSkinParameters parameters)
        {
            return EvaluateLinear(parameters, SkinPigments.Human);
        }

        public static Color EvaluateLinear(CharacterSkinParameters parameters, SkinPigments pigments)
        {
            pigments ??= SkinPigments.Human;
            bool tintAxis = pigments.ThirdAxis == SkinThirdAxis.Tint;
            float undertone = tintAxis ? pigments.FixedUndertone : parameters.Undertone;
            float tint = tintAxis ? parameters.Undertone : 0f;
            float melaninDensity = MelaninDensity(parameters.Melanin * pigments.MaxMelanin);
            float hemoglobinDensity = HemoglobinDensity(parameters.Hemoglobin) * pigments.HemoglobinStrength;
            Vector3 melaninAbsorption = Vector3.Lerp(EumelaninAbsorption, PheomelaninAbsorption, undertone);

            var color = new Color(0f, 0f, 0f, 1f);
            for (int channel = 0; channel < 3; channel++)
            {
                float transmitted = Mathf.Exp(-melaninDensity * melaninAbsorption[channel]) *
                                    Mathf.Exp(-hemoglobinDensity * HemoglobinAbsorption[channel]) *
                                    Mathf.Exp(-tint * pigments.Pigment[channel]);
                color[channel] = DermisReflectance[channel] *
                                 (SurfaceReflectance + (1f - SurfaceReflectance) * transmitted);
            }

            return color;
        }

        // Layout read by the Sol/Character Skin shader: slices side by side, each
        // size x size; x is melanin, y is hemoglobin, slices are the third parameter
        // (undertone, or tint in a fantasy space).
        public static Texture2D GenerateLut(
            int size = DefaultLutSize,
            SkinPigments pigments = null,
            int sliceCount = LutSliceCount)
        {
            size = Mathf.Max(2, size);
            sliceCount = Mathf.Max(2, sliceCount);
            var texture = new Texture2D(size * sliceCount, size, TextureFormat.RGBA32, false, false)
            {
                name = "SkinColorTable",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[size * sliceCount * size];
            for (int slice = 0; slice < sliceCount; slice++)
            {
                float third = slice / (float)(sliceCount - 1);
                for (int y = 0; y < size; y++)
                {
                    float hemoglobin = y / (float)(size - 1);
                    for (int x = 0; x < size; x++)
                    {
                        float melanin = x / (float)(size - 1);
                        Color linear = EvaluateLinear(new CharacterSkinParameters(melanin, hemoglobin, third), pigments);
                        pixels[y * size * sliceCount + slice * size + x] = linear.gamma;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        public static int SliceCount(Texture2D lut)
        {
            return Mathf.Max(2, Mathf.RoundToInt(lut.width / (float)lut.height));
        }

        // Matches the shader's sampling: half-texel inset inside each slice, bilinear
        // within a slice, geometric blend between the two nearest slices.
        public static Color SampleLutLinear(Texture2D lut, CharacterSkinParameters parameters)
        {
            int sliceCount = SliceCount(lut);
            float sliceWidth = 1f / sliceCount;
            float halfTexelU = 0.5f / lut.width;
            float halfTexelV = 0.5f / lut.height;
            float u = Mathf.Lerp(halfTexelU, sliceWidth - halfTexelU, parameters.Melanin);
            float v = Mathf.Lerp(halfTexelV, 1f - halfTexelV, parameters.Hemoglobin);
            float slicePosition = parameters.Undertone * (sliceCount - 1);
            int slice = Mathf.Min((int)slicePosition, sliceCount - 2);

            Color lower = lut.GetPixelBilinear(slice * sliceWidth + u, v).linear;
            Color upper = lut.GetPixelBilinear((slice + 1) * sliceWidth + u, v).linear;
            float blend = slicePosition - slice;
            var color = new Color(0f, 0f, 0f, 1f);
            for (int channel = 0; channel < 3; channel++)
            {
                color[channel] = Mathf.Exp(Mathf.Lerp(
                    Mathf.Log(Mathf.Max(lower[channel], 1e-5f)),
                    Mathf.Log(Mathf.Max(upper[channel], 1e-5f)),
                    blend));
            }

            return color;
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

        // Inverse of LinearToLab (D65), clamped to the displayable range.
        public static Color LabToLinear(Vector3 lab)
        {
            float fy = (lab.x + 16f) / 116f;
            float fx = fy + lab.y / 500f;
            float fz = fy - lab.z / 200f;
            float x = 0.95047f * InverseLabCurve(fx);
            float y = InverseLabCurve(fy);
            float z = 1.08883f * InverseLabCurve(fz);
            return new Color(
                Mathf.Clamp(3.2406f * x - 1.5372f * y - 0.4986f * z, 0.0005f, 1f),
                Mathf.Clamp(-0.9689f * x + 1.8758f * y + 0.0415f * z, 0.0005f, 1f),
                Mathf.Clamp(0.0557f * x - 0.2040f * y + 1.0570f * z, 0.0005f, 1f),
                1f);
        }

        private static float InverseLabCurve(float value)
        {
            float cubed = value * value * value;
            return cubed > 0.008856f ? cubed : (value - 16f / 116f) / 7.787f;
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
