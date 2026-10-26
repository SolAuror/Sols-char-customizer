#ifndef SOL_HAIR_DEPTH_PASSES_INCLUDED
#define SOL_HAIR_DEPTH_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

// Depth, depth-normals, and shadow passes clip with the same coverage as the
// forward pass, so shadows, SSAO, and depth match the visible hair. URP's shared
// passes cannot be used: coverage also needs the card UVs and vertex colour.
struct SolHairDepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float2 cardUV : TEXCOORD1;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SolHairDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float4 uv : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half4 tangentWS : TEXCOORD2;
    half4 hairData : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// Set by URP for the shadow caster pass.
float3 _LightDirection;
float3 _LightPosition;

SolHairDepthVaryings SolHairDepthCommon(SolHairDepthAttributes input, out float3 positionWS)
{
    SolHairDepthVaryings output = (SolHairDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.uv = float4(input.texcoord, input.cardUV);
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.hairData = input.color;
    return output;
}

SolHairVertexData SolHairDepthVertexData(SolHairDepthVaryings input)
{
    SolHairVertexData vertex;
    vertex.uv = input.uv.xy;
    vertex.cardUV = input.uv.zw;
    vertex.hairData = input.hairData;
    return vertex;
}

SolHairDepthVaryings SolHairDepthVertex(SolHairDepthAttributes input)
{
    float3 positionWS;
    SolHairDepthVaryings output = SolHairDepthCommon(input, positionWS);
    output.positionCS = TransformWorldToHClip(positionWS);
    return output;
}

SolHairDepthVaryings SolHairShadowVertex(SolHairDepthAttributes input)
{
    float3 positionWS;
    SolHairDepthVaryings output = SolHairDepthCommon(input, positionWS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, output.normalWS, lightDirectionWS));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    output.positionCS = positionCS;
    return output;
}

half4 SolHairDepthFragment(SolHairDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    clip(SolHairAlpha(SolHairDepthVertexData(input)) - _Cutoff);
    return input.positionCS.z;
}

half4 SolHairDepthNormalsFragment(SolHairDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    SolHairVertexData vertex = SolHairDepthVertexData(input);
    clip(SolHairAlpha(vertex) - _Cutoff);

    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, vertex.uv), _BumpScale);
    half3 vertexNormal = normalize(input.normalWS);
    half3 bitangent = input.tangentWS.w * cross(vertexNormal, input.tangentWS.xyz);
    float3 normalWS = NormalizeNormalPerPixel(
        TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, vertexNormal)));

#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
#else
    return half4(normalWS, 0.0);
#endif
}

#endif
