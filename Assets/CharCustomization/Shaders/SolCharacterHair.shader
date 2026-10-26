// Hair shader for cards and scalp caps built by Tools/Blender (Build Hair Cards,
// Bake Hair Textures). Colour is a hair tint shaded by the baked root-to-tip,
// depth, and per-strand data; coverage is alpha-tested (optionally alpha to
// coverage when MSAA is on). Lighting is wrapped diffuse plus two Kajiya-Kay strand
// highlights with shifted tangents, broken up per strand. Cards run along their V;
// the cap follows its flow map. Forward-only, so it renders in Forward, Forward+,
// and Deferred.
//
// Soft edges: a second material on the same renderer, a variant with Soft Fringe on
// (Hair Setup makes it), draws only the coverage below the cutoff, blended in the
// transparent queue over the alpha-tested hair. It needs no TAA or MSAA.
Shader "Sol/Character Hair"
{
    Properties
    {
        [Header(Colour)]
        [MainColor] _BaseColor("Hair Colour", Color) = (0.27, 0.19, 0.12, 1)
        _RootDarkening("Root Darkening", Range(0, 1)) = 0.45
        _TipLightening("Tip Lightening", Range(0, 1)) = 0.1
        _DepthDarkening("Depth Darkening", Range(0, 1)) = 0.3
        _StrandVariation("Strand Variation", Range(0, 1)) = 0.5
        _CardVariation("Card Variation", Range(0, 1)) = 0.25

        [Header(Maps)]
        [Toggle(_SOL_HAIR_CAP)] _IsCap("Scalp Cap", Float) = 0
        [NoScaleOffset] _HairData("Hair Data (R root to tip, G depth, B strand random, A coverage)", 2D) = "white" {}
        [Normal][NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        [NoScaleOffset] _FlowMap("Flow Map (cap only)", 2D) = "gray" {}

        [Header(Coverage)]
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.35
        _RootFade("Card Root Fade", Range(0, 0.5)) = 0.08
        _SideFade("Card Side Fade Power", Range(1, 8)) = 3

        [Header(Highlights)]
        _SpecularStrength("Highlight Strength", Range(0, 2)) = 0.15
        _SpecularColor("Primary Highlight", Color) = (1, 1, 1, 1)
        _SpecularTint("Primary Highlight Tint (by hair colour)", Range(0, 1)) = 0.75
        _SpecularShift("Primary Shift", Range(-1, 1)) = 0.1
        _SpecularExponent("Primary Sharpness", Range(1, 512)) = 280
        _SecondarySpecularColor("Secondary Highlight (times hair colour)", Color) = (0.7, 0.7, 0.7, 1)
        _SecondaryShift("Secondary Shift", Range(-1, 1)) = -0.2
        _SecondaryExponent("Secondary Sharpness", Range(1, 512)) = 60
        _ShiftJitter("Per-Strand Shift Jitter", Range(0, 1)) = 0.3
        _HighlightBreakup("Per-Strand Highlight Breakup", Range(0, 1)) = 0.7

        [Header(Lighting)]
        _DiffuseWrap("Diffuse Wrap", Range(0, 1)) = 0.35
        _OcclusionStrength("Self Occlusion", Range(0, 1)) = 0.7

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        // Only with MSAA on: without it some GPUs dither alpha to coverage into a screen-door pattern.
        [Toggle] _AlphaToMask("Alpha To Coverage (MSAA only)", Float) = 0

        [Header(Soft Fringe)]
        [Toggle(_SOL_HAIR_FRINGE)] _Fringe("Soft Fringe (second, blended material)", Float) = 0
        _FringeOpacity("Fringe Opacity", Range(0, 2)) = 1
        [HideInInspector] _SrcBlend("Source Blend", Float) = 1
        [HideInInspector] _DstBlend("Destination Blend", Float) = 0
        [HideInInspector] _ZWrite("Depth Write", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SolHairForwardVertex
            #pragma fragment SolHairForwardFragment

            #pragma shader_feature_local _SOL_HAIR_CAP
            #pragma shader_feature_local_fragment _SOL_HAIR_FRINGE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "SolHairInput.hlsl"
            #include "SolHairForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SolHairShadowVertex
            #pragma fragment SolHairDepthFragment
            #pragma shader_feature_local _SOL_HAIR_CAP
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "SolHairInput.hlsl"
            #include "SolHairDepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SolHairDepthVertex
            #pragma fragment SolHairDepthFragment
            #pragma shader_feature_local _SOL_HAIR_CAP
            #pragma multi_compile_instancing

            #include "SolHairInput.hlsl"
            #include "SolHairDepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SolHairDepthVertex
            #pragma fragment SolHairDepthNormalsFragment
            #pragma shader_feature_local _SOL_HAIR_CAP
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "SolHairInput.hlsl"
            #include "SolHairDepthPasses.hlsl"
            ENDHLSL
        }

        // Forward-only materials need this pass name to provide normals in Deferred.
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SolHairDepthVertex
            #pragma fragment SolHairDepthNormalsFragment
            #pragma shader_feature_local _SOL_HAIR_CAP
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "SolHairInput.hlsl"
            #include "SolHairDepthPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
