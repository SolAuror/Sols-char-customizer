#ifndef SOL_SKIN_FORWARD_PASS_INCLUDED
#define SOL_SKIN_FORWARD_PASS_INCLUDED

#include "SolSkinLighting.hlsl"

struct SolSkinAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SolSkinVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    half fogFactor : TEXCOORD4;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

SolSkinVaryings SolSkinForwardVertex(SolSkinAttributes input)
{
    SolSkinVaryings output = (SolSkinVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = positionInputs.positionCS;
    output.positionWS = positionInputs.positionWS;
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
    return output;
}

half4 SolSkinForwardFragment(SolSkinVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SolSkinSurface surface = SampleSolSkinSurface(input.uv);

    half3 vertexNormal = normalize(input.normalWS);
    half3 bitangent = input.tangentWS.w * cross(vertexNormal, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, vertexNormal);
    half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(surface.normalTS, tangentToWorld));

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

    half3 color = SolSkinLighting(surface, input.positionWS, normalWS, viewDirWS, screenUV);
    color = MixFog(color, input.fogFactor);
    return half4(color, 1.0);
}

#endif
