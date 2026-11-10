Shader "OC Design/Comic Lighting/Mountain Flow Water"
{
    Properties
    {
        [MainColor] _BaseColor("Sunlit water", Color) = (0.30, 0.46, 0.43, 1)
        _ShadowColor("Shaded water", Color) = (0.16, 0.29, 0.34, 1)
        _FoamColor("Paper white foam", Color) = (0.89, 0.93, 0.86, 1)
        [Enum(Channel,0,Waterfall,1,Foam,2)] _FlowType("Flow surface", Float) = 0
        _FlowSpeed("Downstream speed, metres per second", Range(0,12)) = 1.6
        _Opacity("Blended foam opacity", Range(0,1)) = 1
        _FoamStrength("Foam coverage", Range(0,1)) = 0.65
        _StreakDensity("Ribbons across the width", Range(2,20)) = 8
        _FlowLength("Length in UV metres, zero disables end fade", Float) = 0
        _DetailNear("Full flow detail distance", Float) = 35
        _DetailFar("Quiet flow detail distance", Float) = 200
        _Threshold("Comic light boundary", Range(-0.5,0.9)) = 0.10
        _Feather("Comic light softness", Range(0.005,0.3)) = 0.075
        _ShadowStrength("Cast shadow influence", Range(0,1)) = 0.8
        _SoftIntersection("Blended mist depth softness, metres", Range(0,2)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend", Float) = 0
        [Toggle] _ZWrite("Write opaque depth", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+2" }
        LOD 200
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadowColor;
            half4 _FoamColor;
            float _FlowType;
            float _FlowSpeed;
            float _Opacity;
            float _FoamStrength;
            float _StreakDensity;
            float _FlowLength;
            float _DetailNear;
            float _DetailFar;
            float _Threshold;
            float _Feather;
            float _ShadowStrength;
            float _SoftIntersection;
            float _Cull;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
        CBUFFER_END

        struct WaterAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            // U crosses the ribbon from 0 to 1. V is accumulated downstream metres.
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct WaterVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        WaterVaryings WaterVertex(WaterAttributes input)
        {
            WaterVaryings output = (WaterVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = input.uv;
            return output;
        }

        float WaterHash(float2 samplePosition)
        {
            float3 value = frac(samplePosition.xyx * .1031);
            value += dot(value, value.yzx + 33.33);
            return frac((value.x + value.y) * value.z);
        }

        float WaterNoise(float2 samplePosition)
        {
            float2 cell = floor(samplePosition);
            float2 fraction = frac(samplePosition);
            fraction = fraction * fraction * (3.0 - 2.0 * fraction);
            return lerp(lerp(WaterHash(cell), WaterHash(cell + float2(1,0)), fraction.x),
                        lerp(WaterHash(cell + float2(0,1)), WaterHash(cell + float2(1,1)), fraction.x),
                        fraction.y);
        }

        float WaterBand(float phase, float halfWidth)
        {
            // Derivatives are taken before frac. Subpixel ribbons fade instead of
            // turning into a full-strength, moving one-pixel line.
            float footprint = max(fwidth(phase), .0001);
            float distance = abs(frac(phase + .5) - .5);
            float coverage = saturate((halfWidth - distance + footprint * .5) / footprint)
                           - saturate((-halfWidth - distance + footprint * .5) / footprint);
            return coverage * (1.0 - smoothstep(.30, .85, footprint));
        }

        void FlowPattern(WaterVaryings input, out float foam, out float tone, out float alpha)
        {
            float across = input.uv.x;
            // A fixed feature of this field travels toward increasing V over time.
            float downstream = input.uv.y - _Time.y * _FlowSpeed;
            float distanceToCamera = distance(_WorldSpaceCameraPos, input.positionWS);
            float detail = 1.0 - smoothstep(_DetailNear, max(_DetailNear + 1.0, _DetailFar), distanceToCamera);
            float2 pigmentUV = float2(across * 4.3, downstream * .16);
            float pigmentFilter = 1.0 - smoothstep(.35, 1.2, length(fwidth(pigmentUV)));
            float broad = lerp(.5, WaterNoise(pigmentUV), pigmentFilter);
            float bank = smoothstep(.66, .99, abs(across * 2.0 - 1.0));
            float wavering = .16 * sin(downstream * .43 + sin(across * 8.0));
            float ribbons = WaterBand(across * _StreakDensity + wavering, .11);
            float flowingPatches = lerp(.5, smoothstep(.25, .76,
                WaterNoise(float2(across * 7.0 + 9.0, downstream * .36))), detail);
            tone = 1.0 + (broad - .5) * .12 * detail;
            alpha = _Opacity;

            if (_FlowType < .5)
            {
                float ripples = WaterBand(downstream * .34 + .13 * sin(across * 10.0), .035);
                float glints = ripples * smoothstep(.40, .72, broad);
                foam = _FoamStrength * (bank * (.16 + .20 * flowingPatches)
                    + detail * (glints * .24 + ribbons * flowingPatches * .15));
            }
            else if (_FlowType < 1.5)
            {
                // Broad white pigment remains visible when narrow strands fade in
                // the distance. Long broken ribbons make the falling direction clear.
                float brokenRibbons = ribbons * (.40 + .60 * flowingPatches);
                float fallingSheets = smoothstep(.48, .80, broad);
                foam = saturate(.36 + _FoamStrength * (.28 + .66 * brokenRibbons * detail
                    + .27 * fallingSheets * detail) + bank * .10);
            }
            else
            {
                float rolledFoam = WaterBand(downstream * .56 + across * .31, .14);
                foam = saturate(.70 + (broad - .5) * .20 + rolledFoam * .22 * detail);
                float edgeFootprint = max(fwidth(across), .0001);
                float sideFade = smoothstep(0.0, .16 + edgeFootprint, across)
                               * smoothstep(0.0, .16 + edgeFootprint, 1.0 - across);
                alpha *= sideFade * lerp(.46, .90, broad);
                if (_FlowLength > .01)
                {
                    float feather = min(.65, _FlowLength * .20);
                    alpha *= smoothstep(0.0, feather, input.uv.y)
                           * smoothstep(0.0, feather, _FlowLength - input.uv.y);
                }
                if (_SoftIntersection > .001 && _ZWrite < .5)
                {
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(
                        GetNormalizedScreenSpaceUV(input.positionCS)), _ZBufferParams);
                    float waterDepth = -TransformWorldToView(input.positionWS).z;
                    alpha *= saturate((sceneDepth - waterDepth) / _SoftIntersection);
                }
            }
        }

        half4 WaterLighting(WaterVaryings input, half facing)
        {
            half3 normal = NormalizeNormalPerPixel(input.normalWS) * facing;
            Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
            float softness = max(.005, _Feather);
            float lightBand = smoothstep(_Threshold - softness, _Threshold + softness,
                dot(normal, sun.direction));
            float lit = lightBand * lerp(1.0, sun.shadowAttenuation, _ShadowStrength);
            float foam, tone, alpha;
            FlowPattern(input, foam, tone, alpha);
            half3 water = lerp(_ShadowColor.rgb, _BaseColor.rgb, lit) * tone;
            half3 whitePigment = lerp(_FoamColor.rgb * .76, _FoamColor.rgb, .30 + .70 * lit);
            half3 color = lerp(water, whitePigment, saturate(foam));
            float fog = ComputeFogFactor(TransformWorldToHClip(input.positionWS).z);
            return half4(MixFog(color, fog), alpha);
        }
        ENDHLSL

        Pass
        {
            Name "FlowForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            half4 WaterFragment(WaterVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return WaterLighting(input, IS_FRONT_VFACE(face, 1.0h, -1.0h));
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterShadowVertex
            #pragma fragment WaterShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            WaterVaryings WaterShadowVertex(WaterAttributes input)
            {
                WaterVaryings output = WaterVertex(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 direction = normalize(_LightPosition - output.positionWS);
                #else
                    float3 direction = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS,
                    normalize(output.normalWS), direction));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                return output;
            }
            half4 WaterShadowFragment(WaterVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(_ZWrite - .5);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterDepthFragment
            #pragma multi_compile_instancing
            half WaterDepthFragment(WaterVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(_ZWrite - .5);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterNormalFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 WaterNormalFragment(WaterVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(_ZWrite - .5);
                float3 normal = NormalizeNormalPerPixel(input.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 packed = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(packed * .5 + .5)), 0);
                #else
                    return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
