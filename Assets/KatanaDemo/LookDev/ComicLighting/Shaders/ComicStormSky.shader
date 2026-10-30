Shader "OC Design/Comic Lighting/Storm Sky"
{
    Properties
    {
        [NoScaleOffset] _SkyMap("Painted sky panorama (2:1)", 2D) = "grey" {}
        _Rotation("Panorama rotation (degrees)", Range(0, 360)) = 135
        _Exposure("Exposure multiplier", Range(0.25, 2)) = 1
        _ZenithColor("Top pole indigo", Color) = (0.188235, 0.219608, 0.294118, 1)
        _SeamBlend("Back seam overlap (UV width)", Range(0, 0.02)) = 0.006
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "PaintedStormSky"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SkyVertex
            #pragma fragment SkyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SkyMap);
            SAMPLER(sampler_SkyMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                float _Rotation;
                float _Exposure;
                float _SeamBlend;
            CBUFFER_END

            struct SkyAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct SkyVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            SkyVaryings SkyVertex(SkyAttributes input)
            {
                SkyVaryings output = (SkyVaryings)0;
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

            half4 SkyFragment(SkyVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.directionWS);
                const float inversePi = 0.31830988618;
                // The image center faces world yaw = 90 - Rotation degrees.
                // Rotation 135 faces yaw -45. UV V=0.5 is the horizon, V=1 the top.
                float longitude = atan2(direction.z, direction.x);
                float latitude = asin(clamp(direction.y, -1.0, 1.0));
                float2 uv = float2(frac(0.5 - longitude * (0.5 * inversePi) + _Rotation / 360.0),
                    saturate(0.5 + latitude * inversePi));

                // Unwrap screen-space U derivatives across both atan2 and frac
                // boundaries so the panorama seam does not cause a blurred mip strip.
                float2 gradientX = ddx(uv);
                float2 gradientY = ddy(uv);
                gradientX.x -= round(gradientX.x);
                gradientY.x -= round(gradientY.x);
                half3 color = SAMPLE_TEXTURE2D_GRAD(_SkyMap, sampler_SkyMap, uv, gradientX, gradientY).rgb;

                // Blend only a narrow strip on the back of the panorama. Opposite
                // edge samples meet at their average; the main view is untouched.
                float seamDistance = min(uv.x, 1.0 - uv.x);
                if (_SeamBlend > 0.00001 && seamDistance < _SeamBlend)
                {
                    float2 otherUV = float2(1.0 - uv.x, uv.y);
                    half3 other = SAMPLE_TEXTURE2D_GRAD(_SkyMap, sampler_SkyMap, otherUV,
                        float2(-gradientX.x, gradientX.y), float2(-gradientY.x, gradientY.y)).rgb;
                    float overlap = 0.5 * (1.0 - smoothstep(0.0, _SeamBlend, seamDistance));
                    color = lerp(color, other, overlap);
                }

                // Only the final two degrees at the top pole fade to indigo. The
                // lower hemisphere and the rest of the sky retain the painted image.
                float poleFade = smoothstep(88.0 / 90.0, 1.0, latitude * (2.0 * inversePi));
                color = lerp(color, _ZenithColor.rgb, poleFade);
                return half4(color * _Exposure, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
