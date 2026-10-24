#ifndef SOL_SKIN_DEPTH_NORMALS_PASS_INCLUDED
#define SOL_SKIN_DEPTH_NORMALS_PASS_INCLUDED

// Writes the normal-mapped normal so SSAO and other screen-space effects see the
// same surface as the forward pass.
struct SolSkinDepthNormalsAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SolSkinDepthNormalsVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half4 tangentWS : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

SolSkinDepthNormalsVaryings SolSkinDepthNormalsVertex(SolSkinDepthNormalsAttributes input)
{
    SolSkinDepthNormalsVaryings output = (SolSkinDepthNormalsVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    return output;
}

half4 SolSkinDepthNormalsFragment(SolSkinDepthNormalsVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
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
