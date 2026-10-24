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

#define SOL_SKIN_LUT_SLICES 3.0

struct SolSkinSurface
{
    half3 albedo;
    half3 normalTS;
    half occlusion;
    half transmission;
    half curvature;
    half perceptualRoughness;
};

// Matches SkinColorModel.SampleLutLinear: undertone slices side by side, melanin on
// x and hemoglobin on y, with a half-texel inset so slices never bleed together.
half3 SampleSkinColor(half melanin, half hemoglobin, half undertone)
{
    float sliceWidth = 1.0 / SOL_SKIN_LUT_SLICES;
    float2 halfTexel = 0.5 * _SkinLut_TexelSize.xy;
    float u = lerp(halfTexel.x, sliceWidth - halfTexel.x, saturate(melanin));
    float v = lerp(halfTexel.y, 1.0 - halfTexel.y, saturate(hemoglobin));
    float slicePosition = saturate(undertone) * (SOL_SKIN_LUT_SLICES - 1.0);
    float slice = min(floor(slicePosition), SOL_SKIN_LUT_SLICES - 2.0);

    half3 lower = SAMPLE_TEXTURE2D_LOD(_SkinLut, sampler_SkinLut, float2(slice * sliceWidth + u, v), 0).rgb;
    half3 upper = SAMPLE_TEXTURE2D_LOD(_SkinLut, sampler_SkinLut, float2((slice + 1.0) * sliceWidth + u, v), 0).rgb;
    return lerp(lower, upper, half(slicePosition - slice));
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
    half melanin = _Melanin
        * lerp(1.0, _LipMelaninScale, regions.r)
        * lerp(1.0, _PalmMelaninScale, regions.g)
        * lerp(1.0, _AreolaMelaninScale, regions.a);
    half hemoglobin = _Hemoglobin
        + regions.r * _LipHemoglobinAdd
        + regions.g * _PalmHemoglobinAdd
        + regions.b * _BlushHemoglobinAdd;
    surface.albedo = saturate(SampleSkinColor(melanin, hemoglobin, _Undertone) * detail);

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
