#ifndef SOL_HAIR_INPUT_INCLUDED
#define SOL_HAIR_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Every pass includes this file so the per-material constant buffer layout is
// identical across passes, which the SRP Batcher requires.
CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half _RootDarkening;
    half _TipLightening;
    half _StrandVariation;
    half _CardVariation;
    half _DepthDarkening;

    half _BumpScale;
    half _Cutoff;
    half _RootFade;
    half _SideFade;

    half4 _SpecularColor;
    half _SpecularShift;
    half _SpecularExponent;
    half4 _SecondarySpecularColor;
    half _SecondaryShift;
    half _SecondaryExponent;
    half _SpecularStrength;
    half _SpecularTint;
    half _ShiftJitter;
    half _HighlightBreakup;
    half _DiffuseWrap;
    half _OcclusionStrength;
    half _FringeOpacity;
CBUFFER_END

// Textures from Tools/Blender (Bake Hair Textures), all linear data:
// _HairData R root to tip, G depth (cards) or height above the scalp (cap),
// B a random value per strand, A coverage. _FlowMap RG is the strand direction in
// the cap's UVs; cards run along their V instead.
TEXTURE2D(_HairData);
SAMPLER(sampler_HairData);
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);
TEXTURE2D(_FlowMap);
SAMPLER(sampler_FlowMap);

// Mesh vertex colour (HairData from the exporter): R root to tip, G random per
// card, B layer, A the cap's hairline fade or a card's root fade scale. Card UV1
// runs 0 to 1 across and along.
struct SolHairVertexData
{
    float2 uv;
    float2 cardUV;
    half4 hairData;
};

struct SolHairSurface
{
    half3 albedo;
    half alpha;
    half3 normalTS;
    half2 flow;
    half strandRandom;
    half occlusion;
    half highlightMask;   // 0 where highlights would light sparse root strands
};

// HairData.A scales the root fade per card: 1 on main cards, lower (a longer fade) on
// the short hairline cards.
half SolHairRootFadeLength(SolHairVertexData vertex)
{
    return _RootFade / max(vertex.hairData.a, 0.05);
}

half SolHairCoverage(half4 data, SolHairVertexData vertex)
{
#if defined(_SOL_HAIR_CAP)
    // Solid inside the cap; towards the hairline the strand coverage breaks up the edge.
    return saturate(vertex.hairData.a * 2.0 - 1.0 + data.a);
#else
    // Fade the root end and the sides so no straight card edge shows against skin.
    half rootFade = _RootFade > 0.0 ? saturate(vertex.cardUV.y / SolHairRootFadeLength(vertex)) : 1.0;
    half sideFade = saturate(1.0 - pow(abs(vertex.cardUV.x * 2.0 - 1.0), _SideFade));
    return data.a * rootFade * sideFade;
#endif
}

half SolHairAlpha(SolHairVertexData vertex)
{
    return SolHairCoverage(SAMPLE_TEXTURE2D(_HairData, sampler_HairData, vertex.uv), vertex);
}

SolHairSurface SampleSolHairSurface(SolHairVertexData vertex)
{
    half4 data = SAMPLE_TEXTURE2D(_HairData, sampler_HairData, vertex.uv);
    SolHairSurface surface;
    surface.alpha = SolHairCoverage(data, vertex);
    surface.strandRandom = data.b;

    half strand = 1.0 + (data.b - 0.5) * _StrandVariation;
#if defined(_SOL_HAIR_CAP)
    // The cap is the under-layer: darker, brightening with height above the scalp.
    half shade = lerp(1.0 - _RootDarkening, 1.0, data.g);
    half occlusion = lerp(0.5, 1.0, data.g);
    half2 flow = SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, vertex.uv).rg * 2.0 - 1.0;
    surface.flow = normalize(flow + half2(0.0, 1e-4));
    surface.highlightMask = 1.0;
#else
    // Highlights stay off the root fade and fade in over twice its length: there the
    // card shows only a few strands over skin or cap, and a highlight on them reads as
    // a pale smear rather than sheen.
    surface.highlightMask = _RootFade > 0.0
        ? smoothstep(0.0, 1.0, saturate(vertex.cardUV.y / (SolHairRootFadeLength(vertex) * 3.0)))
        : 1.0;
    half shade = lerp(1.0 - _RootDarkening, 1.0 + _TipLightening, data.r)
        * lerp(1.0 - _DepthDarkening, 1.0, data.g)
        * (1.0 + (vertex.hairData.g - 0.5) * _CardVariation);
    half occlusion = lerp(0.4, 1.0, data.g) * lerp(0.6, 1.0, vertex.hairData.r);
    surface.flow = half2(0.0, 1.0);
#endif
    surface.albedo = _BaseColor.rgb * max(shade * strand, 0.0);
    surface.occlusion = lerp(1.0, occlusion, _OcclusionStrength);
    surface.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, vertex.uv), _BumpScale);
    return surface;
}

#endif
