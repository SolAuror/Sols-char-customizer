#ifndef SOL_SKIN_INPUT_INCLUDED
#define SOL_SKIN_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

// Every pass includes this file so the per-material constant buffer layout is
// identical across passes, which the SRP Batcher requires. _BaseColor and _Cutoff
// are only read by URP's shared shadow and depth passes.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Cutoff;
    float4 _BaseMapMean;
    half _DetailChroma;

    float4 _SkinLut_TexelSize;
    half _Melanin;
    half _Hemoglobin;
    half _Undertone;
    half _MelaninMax;

    half _LipMelaninScale;
    half _LipHemoglobinAdd;
    half _PalmMelaninScale;
    half _PalmHemoglobinAdd;
    half _BlushHemoglobinAdd;
    half _AreolaMelaninScale;

    half _BumpScale;
    half _OcclusionStrength;
    half _Smoothness;
    half _SecondLobeRoughnessScale;
    half _SecondLobeWeight;
    half _SpecularF0;

    half4 _ScatterColor;
    half _ScatterStrength;
    half4 _TransmissionColor;
    half _TransmissionStrength;
    half _TransmissionDistortion;
    half _TransmissionPower;
CBUFFER_END

TEXTURE2D(_SkinLut);
SAMPLER(sampler_SkinLut);
TEXTURE2D(_SkinRegionMap);
SAMPLER(sampler_SkinRegionMap);
TEXTURE2D(_SkinUtilityMap);
SAMPLER(sampler_SkinUtilityMap);
// Premultiplied ink colour and coverage, composited per character by CharacterTattoos.
TEXTURE2D(_TattooMap);
SAMPLER(sampler_TattooMap);
// The tattoo being edited, outlined by CharacterTattoos; black otherwise.
TEXTURE2D(_TattooHighlight);
SAMPLER(sampler_TattooHighlight);
static const half3 SolTattooHighlightColor = half3(0.1h, 0.75h, 1.0h);


struct SolSkinSurface
{
    half3 albedo;
    half3 normalTS;
    half occlusion;
    half transmission;
    half curvature;
    half perceptualRoughness;
};

// Matches SkinColorModel.SampleLutLinear: third-parameter slices (undertone, or tint
// in a fantasy space) side by side, melanin on x and hemoglobin on y, with a
// half-texel inset so slices never bleed together. The slice count comes from the
// table's aspect ratio, and slices blend geometrically, which follows absorption.
half3 SampleSkinColor(half melanin, half hemoglobin, half third)
{
    float sliceCount = max(2.0, round(_SkinLut_TexelSize.z / _SkinLut_TexelSize.w));
    float sliceWidth = 1.0 / sliceCount;
    float2 halfTexel = 0.5 * _SkinLut_TexelSize.xy;
    float u = lerp(halfTexel.x, sliceWidth - halfTexel.x, saturate(melanin));
    float v = lerp(halfTexel.y, 1.0 - halfTexel.y, saturate(hemoglobin));
    float slicePosition = saturate(third) * (sliceCount - 1.0);
    float slice = min(floor(slicePosition), sliceCount - 2.0);

    float3 lower = SAMPLE_TEXTURE2D_LOD(_SkinLut, sampler_SkinLut, float2(slice * sliceWidth + u, v), 0).rgb;
    float3 upper = SAMPLE_TEXTURE2D_LOD(_SkinLut, sampler_SkinLut, float2((slice + 1.0) * sliceWidth + u, v), 0).rgb;
    return half3(exp2(lerp(log2(max(lower, 1e-5)), log2(max(upper, 1e-5)), slicePosition - slice)));
}

// Mirrors SkinColorModel: melanin concentration at each tenth of the perceptual
// slider, and the exponent from concentration to optical density.
static const float SolMelaninBySlider[11] =
{
    0.0, 0.0586, 0.1149, 0.1324, 0.2055, 0.2916, 0.4419, 0.5115, 0.5924, 0.722, 1.0
};
#define SOL_MELANIN_DENSITY_EXPONENT 2.2298

float SolMelaninConcentration(float slider)
{
    float scaled = saturate(slider) * 10.0;
    int lower = min((int)scaled, 9);
    return lerp(SolMelaninBySlider[lower], SolMelaninBySlider[lower + 1], scaled - lower);
}

float SolMelaninSlider(float concentration)
{
    concentration = saturate(concentration);
    int lower = 9;
    [unroll]
    for (int index = 8; index >= 0; index--)
    {
        lower = concentration <= SolMelaninBySlider[index + 1] ? index : lower;
    }
    float span = max(SolMelaninBySlider[lower + 1] - SolMelaninBySlider[lower], 1e-5);
    return (lower + (concentration - SolMelaninBySlider[lower]) / span) / 10.0;
}

// Region scales multiply melanin density, not the perceptual slider. The slider
// is steep at the deep end, so a slider scale turns deep-skin palms nearly white
// and lips light red; a density scale keeps every region in proportion to the
// skin around it, as melanin does in real skin.
float SolScaleMelaninDensity(float slider, float densityScale)
{
    float concentrationScale = pow(max(densityScale, 0.0), 1.0 / SOL_MELANIN_DENSITY_EXPONENT);
    return SolMelaninSlider(SolMelaninConcentration(slider) * concentrationScale);
}

SolSkinSurface SampleSolSkinSurface(float2 uv)
{
    SolSkinSurface surface;

    // The base map is used as detail only: dividing by its mean colour removes the
    // skin tone baked into it, and _DetailChroma keeps some of its colour variation.
    half3 ratio = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb / max(_BaseMapMean.rgb, half3(0.001, 0.001, 0.001));
    half luminanceRatio = dot(ratio, half3(0.2126, 0.7152, 0.0722));
    half3 detail = lerp(luminanceRatio.xxx, ratio, _DetailChroma);

    // R lips, G palms, soles and nails, B blush zones, A areolae.
    half4 regions = SAMPLE_TEXTURE2D(_SkinRegionMap, sampler_SkinRegionMap, uv);
    half densityScale = lerp(1.0, _LipMelaninScale, regions.r)
        * lerp(1.0, _PalmMelaninScale, regions.g)
        * lerp(1.0, _AreolaMelaninScale, regions.a);
    // The table's melanin axis spans 0.._MelaninMax of human melanin (fantasy spaces
    // stop short of the deepest), so density scaling works on that human value.
    float melaninRange = max(_MelaninMax, 0.05);
    half melanin = saturate(SolScaleMelaninDensity(_Melanin * melaninRange, densityScale) / melaninRange);
    half hemoglobin = _Hemoglobin
        + regions.r * _LipHemoglobinAdd
        + regions.g * _PalmHemoglobinAdd
        + regions.b * _BlushHemoglobinAdd;
    surface.albedo = saturate(SampleSkinColor(melanin, hemoglobin, _Undertone) * detail);

    // Tattoo ink sits in the dermis, under the epidermal melanin, so it is seen
    // through the same melanin as the skin around it: the ink colour is filtered by
    // this skin's melanin-only colour over unpigmented skin, which darkens and mutes
    // it on deep skin as real ink is. Pores still show through it.
    half4 ink = SAMPLE_TEXTURE2D(_TattooMap, sampler_TattooMap, uv);
    UNITY_BRANCH
    if (ink.a > 0.002h)
    {
        half3 unpigmented = max(SampleSkinColor(0.0h, 0.0h, _Undertone), half3(0.001h, 0.001h, 0.001h));
        half3 epidermis = SampleSkinColor(melanin, 0.0h, _Undertone) / unpigmented;
        half3 inkColor = ink.rgb / ink.a;
        surface.albedo = lerp(surface.albedo, saturate(inkColor * epidermis * luminanceRatio), ink.a);
    }

    // Pulses, so the selection stands out from ink of any colour.
    half highlight = SAMPLE_TEXTURE2D(_TattooHighlight, sampler_TattooHighlight, uv).r;
    UNITY_BRANCH
    if (highlight > 0.002h)
    {
        half pulse = 0.75h + 0.25h * sin(_Time.y * 5.0);
        surface.albedo = lerp(surface.albedo, SolTattooHighlightColor, highlight * pulse);
    }

    surface.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);

    // R occlusion, G transmission (1 where thin), B curvature.
    half4 utility = SAMPLE_TEXTURE2D(_SkinUtilityMap, sampler_SkinUtilityMap, uv);
    surface.occlusion = lerp(1.0, utility.r, _OcclusionStrength);
    surface.transmission = utility.g;
    surface.curvature = utility.b;
    surface.perceptualRoughness = 1.0 - _Smoothness;
    return surface;
}

#endif
