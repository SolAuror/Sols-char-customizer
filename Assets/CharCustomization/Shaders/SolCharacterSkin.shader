// Skin shader for the character customizer. Colour comes from a melanin,
// hemoglobin, and undertone colour table (SkinColorModel) instead of a tint over a
// fixed albedo; the base map supplies detail only. Lighting adds wrapped
// per-channel diffuse for scattering, dual-lobe GGX specular, and thin-region
// transmission. Forward-only, so it renders in Forward, Forward+, and Deferred.
Shader "Sol/Character Skin"
{
    Properties
    {
        [Header(Detail)]
        [MainTexture] _BaseMap("Detail Albedo", 2D) = "white" {}
        _BaseMapMean("Detail Albedo Mean (linear RGB)", Vector) = (1, 1, 1, 0)
        _DetailChroma("Detail Chroma", Range(0, 1)) = 0.35
        [HideInInspector][MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Skin Colour)]
        [NoScaleOffset] _SkinLut("Skin Colour Table", 2D) = "white" {}
        _Melanin("Melanin", Range(0, 1)) = 0.25
        _Hemoglobin("Hemoglobin", Range(0, 1)) = 0.15
        _Undertone("Undertone", Range(0, 1)) = 0.35

        [Header(Regions)]
        [NoScaleOffset] _SkinRegionMap("Region Mask (R lips, G palms soles nails, B blush, A areolae)", 2D) = "black" {}
        _LipMelaninScale("Lip Melanin Scale", Range(0, 2)) = 0.8
        _LipHemoglobinAdd("Lip Hemoglobin Add", Range(0, 1)) = 0.35
        _PalmMelaninScale("Palm Melanin Scale", Range(0, 2)) = 0.3
        _PalmHemoglobinAdd("Palm Hemoglobin Add", Range(0, 1)) = 0.05
        _BlushHemoglobinAdd("Blush Hemoglobin Add", Range(0, 1)) = 0.15
        _AreolaMelaninScale("Areola Melanin Scale", Range(0, 2)) = 1.35

        [Header(Surface)]
        [Normal][NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        [NoScaleOffset] _SkinUtilityMap("Utility (R occlusion, G transmission, B curvature)", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.55
        _SecondLobeRoughnessScale("Second Lobe Roughness Scale", Range(1, 3)) = 1.8
        _SecondLobeWeight("Second Lobe Weight", Range(0, 1)) = 0.15
        _SpecularF0("Specular F0", Range(0, 0.08)) = 0.028

        [Header(Scattering)]
        _ScatterColor("Scatter Colour", Color) = (1, 0.35, 0.2, 1)
        _ScatterStrength("Scatter Strength", Range(0, 1)) = 0.5
        _TransmissionColor("Transmission Colour", Color) = (1, 0.3, 0.15, 1)
        _TransmissionStrength("Transmission Strength", Range(0, 2)) = 0
        _TransmissionDistortion("Transmission Distortion", Range(0, 1)) = 0.2
        _TransmissionPower("Transmission Power", Range(1, 16)) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SolSkinForwardVertex
            #pragma fragment SolSkinForwardFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "SolSkinInput.hlsl"
            #include "SolSkinForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "SolSkinInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "SolSkinInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SolSkinDepthNormalsVertex
            #pragma fragment SolSkinDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "SolSkinInput.hlsl"
            #include "SolSkinDepthNormalsPass.hlsl"
            ENDHLSL
        }

        // Forward-only materials need this pass name to provide normals in Deferred.
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SolSkinDepthNormalsVertex
            #pragma fragment SolSkinDepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            #include "SolSkinInput.hlsl"
            #include "SolSkinDepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
