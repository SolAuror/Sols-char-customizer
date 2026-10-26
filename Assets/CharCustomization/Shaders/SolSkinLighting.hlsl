#ifndef SOL_SKIN_LIGHTING_INCLUDED
#define SOL_SKIN_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// URP renamed the Forward+ light loop to the cluster light loop in Unity 6.1.
#if defined(USE_CLUSTER_LIGHT_LOOP)
    #define SOL_SKIN_CLUSTERED_LIGHTS USE_CLUSTER_LIGHT_LOOP
#elif defined(USE_FORWARD_PLUS)
    #define SOL_SKIN_CLUSTERED_LIGHTS USE_FORWARD_PLUS
#else
    #define SOL_SKIN_CLUSTERED_LIGHTS 0
#endif

struct SolSkinShadingContext
{
    float3 positionWS;
    half3 normalWS;
    half3 viewDirWS;
    half NdotV;
    half directOcclusion;
};

// Wrapped diffuse with a per-channel wrap width, approximating pre-integrated
// subsurface scattering. Scattered light has passed through the skin's own
// melanin, so the wrap takes the albedo's hue rather than a fixed red: a fixed red
// paints red rims around eyes and fingers on deep skin. Curvature scales the wrap,
// so thin, curved features scatter most.
half3 SolSkinDiffuse(half NdotL, half curvature, half3 albedo)
{
    half3 hue = albedo / max(max(albedo.r, max(albedo.g, albedo.b)), 0.0001);
    half3 wrap = _ScatterColor.rgb * hue * (_ScatterStrength * curvature);
    return saturate((NdotL + wrap) / (1.0 + wrap));
}

float SolGGX(float NdotH, float roughness)
{
    float a2 = roughness * roughness;
    float d = (NdotH * a2 - NdotH) * NdotH + 1.0;
    return a2 / (PI * d * d + 1e-7);
}

float SolSmithJointVisibility(float NdotL, float NdotV, float roughness)
{
    float lambdaV = NdotL * (NdotV * (1.0 - roughness) + roughness);
    float lambdaL = NdotV * (NdotL * (1.0 - roughness) + roughness);
    return 0.5 / (lambdaV + lambdaL + 1e-5);
}

// Two GGX lobes: a tight lobe for the oily surface sheen and a broad one for the
// rougher skin underneath.
float SolDualLobeSpecular(float NdotH, float NdotL, float NdotV, half perceptualRoughness)
{
    float roughnessA = max(perceptualRoughness * perceptualRoughness, 0.002);
    float perceptualB = saturate(perceptualRoughness * _SecondLobeRoughnessScale);
    float roughnessB = max(perceptualB * perceptualB, 0.002);
    float lobeA = SolGGX(NdotH, roughnessA) * SolSmithJointVisibility(NdotL, NdotV, roughnessA);
    float lobeB = SolGGX(NdotH, roughnessB) * SolSmithJointVisibility(NdotL, NdotV, roughnessB);
    return lerp(lobeA, lobeB, _SecondLobeWeight);
}

// URP Lit's environment BRDF. Karis' mobile fit adds a roughness bias of about
// 0.06 at normal incidence, three times skin's F0, which spreads a grey sheen
// over deep skin and reads as ashy.
half3 SolEnvironmentBRDF(half3 f0, half perceptualRoughness, half NdotV)
{
    half roughness = max(perceptualRoughness * perceptualRoughness, HALF_MIN_SQRT);
    half surfaceReduction = 1.0 / (roughness * roughness + 1.0);
    half3 grazingTerm = saturate((1.0 - perceptualRoughness) + f0);
    half fresnelTerm = Pow4(1.0 - NdotV);
    return surfaceReduction * lerp(f0, grazingTerm, fresnelTerm);
}

half3 SolSkinDirectLight(SolSkinSurface surface, SolSkinShadingContext context, Light light)
{
    half3 lightDir = light.direction;
    half NdotL = dot(context.normalWS, lightDir);
    half3 radiance = light.color * light.distanceAttenuation;
    half shadow = light.shadowAttenuation * context.directOcclusion;

    half3 f0 = _SpecularF0.xxx;
    half3 diffuse = SolSkinDiffuse(NdotL, surface.curvature, surface.albedo) * surface.albedo * (1.0 - _SpecularF0);

    float3 halfDir = SafeNormalize(float3(lightDir) + float3(context.viewDirWS));
    float clampedNdotL = saturate(NdotL);
    float NdotH = saturate(dot(context.normalWS, halfDir));
    half VdotH = saturate(dot(context.viewDirWS, halfDir));
    half3 fresnel = f0 + (1.0 - f0) * pow(1.0 - VdotH, 5.0);
    half3 specular = fresnel * (SolDualLobeSpecular(NdotH, clampedNdotL, context.NdotV, surface.perceptualRoughness) * clampedNdotL);

    // Back-lit transmission through thin regions such as ears and nostrils. It
    // ignores shadowing, since the occluder is usually the thin feature itself.
    half3 transmittedDir = lightDir + context.normalWS * _TransmissionDistortion;
    half transmission = pow(saturate(dot(context.viewDirWS, -transmittedDir)), _TransmissionPower)
        * surface.transmission * _TransmissionStrength;
    half3 transmitted = _TransmissionColor.rgb * surface.albedo * transmission;

    return (diffuse + specular) * radiance * shadow + transmitted * radiance;
}

half3 SolSkinLighting(SolSkinSurface surface, float3 positionWS, half3 normalWS, half3 viewDirWS, float2 screenUV)
{
    SolSkinShadingContext context;
    context.positionWS = positionWS;
    context.normalWS = normalWS;
    context.viewDirWS = viewDirWS;
    context.NdotV = max(saturate(dot(normalWS, viewDirWS)), 0.0001);
    context.directOcclusion = 1.0;

    half occlusion = surface.occlusion;
#if defined(_SCREEN_SPACE_OCCLUSION)
    AmbientOcclusionFactor screenOcclusion = GetScreenSpaceAmbientOcclusion(screenUV);
    occlusion = min(occlusion, screenOcclusion.indirectAmbientOcclusion);
    context.directOcclusion = screenOcclusion.directAmbientOcclusion;
#endif

    half3 f0 = _SpecularF0.xxx;
    half3 reflectDir = reflect(-viewDirWS, normalWS);
    half3 color = SampleSH(normalWS) * surface.albedo * occlusion;
    color += GlossyEnvironmentReflection(reflectDir, positionWS, surface.perceptualRoughness, occlusion, screenUV)
        * SolEnvironmentBRDF(f0, surface.perceptualRoughness, context.NdotV);

    // Mixed-lighting shadow masks are not used by dynamic characters.
    half4 shadowMask = half4(1.0, 1.0, 1.0, 1.0);
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord, positionWS, shadowMask);
    color += SolSkinDirectLight(surface, context, mainLight);

#if defined(_ADDITIONAL_LIGHTS)
    // LIGHT_LOOP_BEGIN reads inputData in the clustered path.
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = screenUV;
    uint pixelLightCount = GetAdditionalLightsCount();

    #if SOL_SKIN_CLUSTERED_LIGHTS && defined(URP_FP_DIRECTIONAL_LIGHTS_COUNT)
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        Light directionalLight = GetAdditionalLight(lightIndex, positionWS, shadowMask);
        color += SolSkinDirectLight(surface, context, directionalLight);
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS, shadowMask);
        color += SolSkinDirectLight(surface, context, light);
    LIGHT_LOOP_END
#endif

    return color;
}

#endif
