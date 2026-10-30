Shader "Hidden/OC Design/Comic Screen Outline"
{
    Properties
    {
        _BaseMap("Cutout texture", 2D) = "white" {}
        _BaseColor("Source tint", Color) = (1,1,1,1)
        _AlphaClip("Alpha clipping", Float) = 0
        _Cutoff("Alpha cutoff", Float) = 0.5
        _Cull("Source cull mode", Float) = 2
        _OutlineColor("Ink", Color) = (0.188235,0.160784,0.137255,1)
        _OutlinePixels("Minimum contour radius in render pixels", Float) = 3
        _DepthTolerance("Depth tolerance in metres", Float) = 0.0005
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D_X_FLOAT(_ComicSceneDepth);
        TEXTURE2D_X(_ComicOutlineMask);
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        float4 _ComicOutlineMask_TexelSize;
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _OutlineColor;
            float _AlphaClip;
            float _Cutoff;
            float _Cull;
            float _OutlinePixels;
            float _DepthTolerance;
        CBUFFER_END
        float ComicEyeDepth(float rawDepth)
        {
            if (unity_OrthoParams.w > 0.5)
            {
                #if UNITY_REVERSED_Z
                    return lerp(_ProjectionParams.z, _ProjectionParams.y, rawDepth);
                #else
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                #endif
            }
            return LinearEyeDepth(rawDepth, _ZBufferParams);
        }
        float ComicTolerance(float depth) { return max(0.0, _DepthTolerance) + depth * 0.000002; }
        ENDHLSL

        // Only the original visible surfaces contribute to this mask. Their
        // linear depth is retained for occlusion tests at the expanded pixels.
        Pass
        {
            Name "VisibleSourceMask"
            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend Off
            ColorMask RG
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #pragma multi_compile_instancing
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings MaskVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(world);
                output.eyeDepth = -TransformWorldToView(world).z;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            float2 MaskFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_AlphaClip > 0.5)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                // Hardware depth compares against the actual camera attachment.
                // The coverage channel makes resolved MSAA boundary samples safe.
                return float2(input.eyeDepth, 1.0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "RoundPixelFloor"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullscreenVertex
            #pragma fragment OutlineFragment
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings FullscreenVertex(uint vertexID : SV_VertexID)
            {
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return output;
            }
            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                int2 pixel = int2(input.positionCS.xy);
                float2 centerMask = LOAD_TEXTURE2D_X(_ComicOutlineMask, pixel).rg;
                // Leave source interiors intact; the existing world hull still
                // supplies internal overlaps and any wider close-up contour.
                if (centerMask.y > 0.01 || _OutlinePixels <= 0.0) return 0;
                float radius = min(_OutlinePixels, 8.0);
                int extent = (int)ceil(radius);
                int2 size = int2(_ComicOutlineMask_TexelSize.zw);
                float sceneDepth = ComicEyeDepth(LOAD_TEXTURE2D_X(_ComicSceneDepth, pixel).r);
                float coverage = 0.0;
                [loop] for (int y = -extent; y <= extent; y++)
                {
                    [loop] for (int x = -extent; x <= extent; x++)
                    {
                        float weight = saturate(radius + 0.5 - length(float2(x,y)));
                        if (weight <= coverage) continue;
                        int2 candidate = pixel + int2(x,y);
                        if (any(candidate < 0) || any(candidate >= size)) continue;
                        float2 sampleMask = LOAD_TEXTURE2D_X(_ComicOutlineMask, candidate).rg;
                        if (sampleMask.y <= 0.01) continue;
                        float sourceDepth = sampleMask.x / sampleMask.y;
                        if (sourceDepth <= sceneDepth + ComicTolerance(sceneDepth))
                            coverage = max(coverage, weight * sampleMask.y);
                    }
                }
                return half4(_OutlineColor.rgb, _OutlineColor.a * coverage);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
