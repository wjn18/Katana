Shader "OC Design/Comic Lighting/Cloud Backdrop"
{
    Properties
    {
        _ZenithColor("Deep petroleum blue upper sky", Color) = (0.090196, 0.247059, 0.349020, 1)
        _HorizonColor("Clear blue horizon", Color) = (0.364706, 0.509804, 0.600000, 1)
        _LowerColor("Cool lower sky", Color) = (0.211765, 0.345098, 0.443137, 1)
        _GoldColor("Pale gold low opening", Color) = (0.784314, 0.737255, 0.537255, 1)
        _LightAzimuth("Opening azimuth (degrees)", Range(-180, 180)) = -45
        _OpeningHeight("Opening center (direction Y)", Range(-0.05, 0.3)) = 0.08
        _OpeningSpread("Opening vertical half span", Range(0.03, 0.4)) = 0.16
        _OpeningArc("Opening horizontal arc (degrees)", Range(20, 150)) = 80
        _OpeningStrength("Opening color influence", Range(0, 1)) = 0.06
        _Exposure("Palette exposure", Range(0.25, 2)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Pass
        {
            Name "CloudSculptureBackdrop"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BackdropVertex
            #pragma fragment BackdropFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _LowerColor;
                half4 _GoldColor;
                float _LightAzimuth;
                float _OpeningHeight;
                float _OpeningSpread;
                float _OpeningArc;
                float _OpeningStrength;
                float _Exposure;
            CBUFFER_END
            struct BackdropAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct BackdropVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            BackdropVaryings BackdropVertex(BackdropAttributes input)
            {
                BackdropVaryings output = (BackdropVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                #if UNITY_REVERSED_Z
                    output.positionCS.z = 0.0;
                #else
                    output.positionCS.z = output.positionCS.w;
                #endif
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz, false);
                return output;
            }
            half4 BackdropFragment(BackdropVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.directionWS);
                half3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb,
                    smoothstep(0.015, 0.78, direction.y));
                color = lerp(_LowerColor.rgb, color, smoothstep(-0.40, 0.0, direction.y));

                float azimuth = radians(_LightAzimuth);
                float2 openingDirection = float2(sin(azimuth), cos(azimuth));
                float horizonLength = length(direction.xz);
                float alignment = dot(direction.xz / max(horizonLength, 0.0001), openingDirection);
                float arc = radians(_OpeningArc);
                float angular = smoothstep(cos(arc * 0.5), cos(arc * 0.18), alignment);
                angular *= smoothstep(0.04, 0.18, horizonLength);
                float vertical = saturate(1.0 - abs(direction.y - _OpeningHeight) / max(_OpeningSpread, 0.001));
                vertical = vertical * vertical * (3.0 - 2.0 * vertical);
                // A quiet light region behind the real cloud geometry. No cloud
                // silhouettes, fake volume, texture, noise, or atmosphere is drawn.
                color = lerp(color, _GoldColor.rgb, angular * vertical * _OpeningStrength);
                return half4(color * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
