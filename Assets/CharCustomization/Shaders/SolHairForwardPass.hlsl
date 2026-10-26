#ifndef SOL_HAIR_FORWARD_PASS_INCLUDED
#define SOL_HAIR_FORWARD_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// URP renamed the Forward+ light loop to the cluster light loop in Unity 6.1.
#if defined(USE_CLUSTER_LIGHT_LOOP)
    #define SOL_HAIR_CLUSTERED_LIGHTS USE_CLUSTER_LIGHT_LOOP
#elif defined(USE_FORWARD_PLUS)
    #define SOL_HAIR_CLUSTERED_LIGHTS USE_FORWARD_PLUS
#else
    #define SOL_HAIR_CLUSTERED_LIGHTS 0
#endif

struct SolHairAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float2 cardUV : TEXCOORD1;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SolHairVaryings
{
    float4 positionCS : SV_POSITION;
    float4 uv : TEXCOORD0;   // xy atlas or cap UV, zw card UV
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    half4 hairData : TEXCOORD4;
    half fogFactor : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

struct SolHairShadingContext
{
    half3 normalWS;
    half3 strandWS;
    half3 viewDirWS;
    half shift;
    half highlight;   // per-strand highlight scale, so highlights break into strands
};

SolHairVaryings SolHairForwardVertex(SolHairAttributes input)
{
    SolHairVaryings output = (SolHairVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = positionInputs.positionCS;
    output.positionWS = positionInputs.positionWS;
    output.uv = float4(input.texcoord, input.cardUV);
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.hairData = input.color;
    output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
    return output;
}

// Kajiya-Kay highlight along a strand, with Scheuermann's shifted tangents: the
// primary lobe is the light's colour, the secondary passes through the hair and
// takes its colour.
half SolHairStrandSpecular(half3 strand, half3 normal, half shift, half3 halfDir, half exponent)
{
    half3 shifted = normalize(strand + normal * shift);
    half TdotH = dot(shifted, halfDir);
    half sinTH = sqrt(max(1.0 - TdotH * TdotH, 0.0));
    return smoothstep(-1.0, 0.0, TdotH) * pow(sinTH, exponent);
}

half3 SolHairDirectLight(SolHairSurface surface, SolHairShadingContext context, Light light)
{
    half3 lightDir = light.direction;
    half attenuation = light.distanceAttenuation * light.shadowAttenuation;
    half NdotL = dot(context.normalWS, lightDir);
    half diffuse = saturate((NdotL + _DiffuseWrap) / (1.0 + _DiffuseWrap));

    half3 halfDir = SafeNormalize(lightDir + context.viewDirWS);
    half primary = SolHairStrandSpecular(context.strandWS, context.normalWS, _SpecularShift + context.shift,
        halfDir, _SpecularExponent);
    half secondary = SolHairStrandSpecular(context.strandWS, context.normalWS, _SecondaryShift + context.shift,
        halfDir, _SecondaryExponent);
    // The primary highlight takes the hair's hue (its colour at full brightness), so
    // dark or saturated hair gets a sheen of its own colour instead of grey streaks.
    half3 hairHue = _BaseColor.rgb / max(max(_BaseColor.r, _BaseColor.g), max(_BaseColor.b, 1e-3));
    half3 primaryColor = _SpecularColor.rgb * lerp(1.0, hairHue, _SpecularTint);
    half3 specular = (primaryColor * primary + _SecondarySpecularColor.rgb * surface.albedo * secondary)
        * _SpecularStrength * context.highlight * saturate(NdotL + 0.25);

    return light.color * attenuation * (surface.albedo * diffuse + specular);
}

half3 SolHairLighting(SolHairSurface surface, SolHairShadingContext context, float3 positionWS, float2 screenUV)
{
    half occlusion = surface.occlusion;
#if defined(_SCREEN_SPACE_OCCLUSION)
    AmbientOcclusionFactor screenOcclusion = GetScreenSpaceAmbientOcclusion(screenUV);
    occlusion = min(occlusion, screenOcclusion.indirectAmbientOcclusion);
#endif
    half3 color = SampleSH(context.normalWS) * surface.albedo * occlusion;

    // Mixed-lighting shadow masks are not used by dynamic characters.
    half4 shadowMask = half4(1.0, 1.0, 1.0, 1.0);
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    color += SolHairDirectLight(surface, context, GetMainLight(shadowCoord, positionWS, shadowMask));

#if defined(_ADDITIONAL_LIGHTS)
    // LIGHT_LOOP_BEGIN reads inputData in the clustered path.
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalizedScreenSpaceUV = screenUV;
    uint pixelLightCount = GetAdditionalLightsCount();

    #if SOL_HAIR_CLUSTERED_LIGHTS && defined(URP_FP_DIRECTIONAL_LIGHTS_COUNT)
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        color += SolHairDirectLight(surface, context, GetAdditionalLight(lightIndex, positionWS, shadowMask));
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        color += SolHairDirectLight(surface, context, GetAdditionalLight(lightIndex, positionWS, shadowMask));
    LIGHT_LOOP_END
#endif

    return color;
}

half4 SolHairForwardFragment(SolHairVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SolHairVertexData vertex;
    vertex.uv = input.uv.xy;
    vertex.cardUV = input.uv.zw;
    vertex.hairData = input.hairData;
    SolHairSurface surface = SampleSolHairSurface(vertex);
#if defined(_SOL_HAIR_FRINGE)
    // The fringe material draws after the alpha-tested hair: only the soft coverage
    // the cutoff dropped, blended, so strand tips and edges fade instead of ending hard.
    clip(_Cutoff - surface.alpha - 1e-4);
    clip(surface.alpha - 1.0 / 255.0);
#else
    clip(surface.alpha - _Cutoff);
#endif

    half3 vertexNormal = normalize(input.normalWS);
    half3 tangent = normalize(input.tangentWS.xyz);
    half3 bitangent = input.tangentWS.w * cross(vertexNormal, tangent);
    half3x3 tangentToWorld = half3x3(tangent, bitangent, vertexNormal);

    // Cards are double-sided but not flipped on the back: their normals describe the
    // hair volume (bent towards the scalp by the exporter), the same from both sides.
    SolHairShadingContext context;
    context.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(surface.normalTS, tangentToWorld));
    // Strands run along V on cards (root to tip) and along the baked flow on the cap.
    context.strandWS = normalize(tangent * surface.flow.x + bitangent * surface.flow.y);
    context.viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    context.shift = (surface.strandRandom - 0.5) * _ShiftJitter;
    // Strands catch the light unevenly, and deeper ones less: this is what keeps the
    // highlight from reading as one smooth plastic band.
    context.highlight = lerp(1.0 - _HighlightBreakup, 1.0 + _HighlightBreakup, surface.strandRandom)
        * surface.occlusion * surface.highlightMask;

    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
    half3 color = SolHairLighting(surface, context, input.positionWS, screenUV);
    color = MixFog(color, input.fogFactor);
#if defined(_SOL_HAIR_FRINGE)
    return half4(color, saturate(surface.alpha * _FringeOpacity));
#else
    return half4(color, surface.alpha);
#endif
}

#endif
