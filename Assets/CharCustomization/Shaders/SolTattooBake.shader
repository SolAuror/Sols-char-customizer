// Composites tattoos into a character's skin UV space for CharacterTattoos. The skin
// mesh is drawn with its UVs as positions, so every texel of the layer knows where it
// sits on the bind-pose body, and each tattoo is a box projection in that space. The
// result is premultiplied ink colour and coverage, read by Sol/Character Skin.
// The projection itself is in SolTattooProjection.hlsl, shared by Stamp and Outline.
Shader "Hidden/Sol/Tattoo Bake"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct UvAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
        };

        // Places the vertex at its UV in the render target, so texel (u, v) is sampled
        // back by the skin shader at the same UV on every graphics API.
        float4 UvToClip(float2 uv)
        {
            float2 clip = uv * 2.0 - 1.0;
            #if UNITY_UV_STARTS_AT_TOP
            clip.y = -clip.y;
            #endif
            return float4(clip, 0.5, 1.0);
        }
        ENDHLSL

        // 0: marks texels inside the skin's UV islands, so dilation only fills gutters.
        Pass
        {
            Name "Islands"
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex IslandVertex
            #pragma fragment IslandFragment

            float4 IslandVertex(UvAttributes input) : SV_POSITION
            {
                return UvToClip(input.uv);
            }

            half4 IslandFragment() : SV_Target
            {
                return half4(1.0h, 1.0h, 1.0h, 1.0h);
            }
            ENDHLSL
        }

        // 1: one tattoo, laid premultiplied over those already composited.
        Pass
        {
            Name "Stamp"
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex StampVertex
            #pragma fragment StampFragment
            #include "SolTattooProjection.hlsl"

            half4 StampFragment(Varyings input) : SV_Target
            {
                float3 decal;
                half facing;
                ProjectTattoo(input, decal, facing);
                half4 design = SAMPLE_TEXTURE2D_GRAD(_TattooDesign, sampler_TattooDesign, decal.xy + 0.5,
                    ddx(decal.xy), ddy(decal.xy));
                half coverage = design.a * _TattooInk.a * smoothstep(0.15h, 0.45h, facing);
                return half4(design.rgb * _TattooInk.rgb * coverage, coverage);
            }
            ENDHLSL
        }

        // 2: grows ink one texel into the gutters around UV islands per pass, so
        // bilinear and mip sampling at seams do not show a line through a tattoo.
        Pass
        {
            Name "Dilate"
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullScreenVertex
            #pragma fragment DilateFragment

            TEXTURE2D(_TattooSource);
            TEXTURE2D(_TattooIslands);
            float4 _TattooSourceSize;

            float4 FullScreenVertex(uint vertexId : SV_VertexID) : SV_POSITION
            {
                float2 corner = float2((vertexId << 1) & 2, vertexId & 2);
                return float4(corner * 2.0 - 1.0, 0.5, 1.0);
            }

            // Texel fetches index memory rows directly, so no UV flip is needed here.
            half4 DilateFragment(float4 positionCS : SV_POSITION) : SV_Target
            {
                int2 texel = int2(positionCS.xy);
                half4 centre = LOAD_TEXTURE2D(_TattooSource, texel);
                if (LOAD_TEXTURE2D(_TattooIslands, texel).r > 0.5h || centre.a > 0.0h)
                {
                    return centre;
                }

                int2 last = int2(_TattooSourceSize.xy) - 1;
                half4 sum = 0.0h;
                half count = 0.0h;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        int2 neighbour = clamp(texel + int2(x, y), int2(0, 0), last);
                        half4 value = LOAD_TEXTURE2D(_TattooSource, neighbour);
                        half valid = (LOAD_TEXTURE2D(_TattooIslands, neighbour).r > 0.5h || value.a > 0.0h) ? 1.0h : 0.0h;
                        sum += value * valid;
                        count += valid;
                    }
                }

                return count > 0.0h ? sum / count : centre;
            }
            ENDHLSL
        }

        // 3: the selected tattoo, for the skin to show while it is edited: a frame round
        // its box and a wash over its design, in the red channel.
        Pass
        {
            Name "Outline"
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex StampVertex
            #pragma fragment OutlineFragment
            #include "SolTattooProjection.hlsl"

            // The frame's width across and up the design, in design widths and heights.
            float4 _TattooFrame;

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                float3 decal;
                half facing;
                ProjectTattoo(input, decal, facing);
                float2 inside = 0.5 - abs(decal.xy);
                float2 width = max(_TattooFrame.xy, fwidth(decal.xy) * 1.5);
                half frame = 1.0h - smoothstep(0.6h, 1.0h, min(inside.x / width.x, inside.y / width.y));
                half design = SAMPLE_TEXTURE2D_GRAD(_TattooDesign, sampler_TattooDesign, decal.xy + 0.5,
                    ddx(decal.xy), ddy(decal.xy)).a;
                half value = max(frame, design * 0.3h) * smoothstep(0.15h, 0.45h, facing);
                return half4(value, value, value, value);
            }
            ENDHLSL
        }
    }
}
