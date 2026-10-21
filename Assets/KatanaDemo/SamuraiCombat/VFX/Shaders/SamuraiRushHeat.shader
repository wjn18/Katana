Shader "KatanaDemo/VFX/SamuraiRushHeat"
{
    Properties
    {
        _HeatAmplitude ("Maximum heat refraction (viewport UV)", Range(0, 0.012)) = 0.0075
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "SamuraiRushHeat"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HeatAmplitude;
            CBUFFER_END
            // Per-camera property block: strength, elapsed rush seconds, aspect ratio.
            float4 _HeatParams;
            float4 _HeatCenter;
            float4 _HeatViewport;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = DYNAMIC_SCALING_REMOVE_SCALEBIAS(input.texcoord);
                float2 center = _HeatCenter.xy;
                // Feedback uses Camera.WorldToViewportPoint convention, lower left is (0,0).
                #if UNITY_UV_STARTS_AT_TOP
                    center.y = 1.0 - center.y;
                #endif
                float aspect = max(0.1, _HeatParams.z);
                float2 p = (uv - center) * float2(aspect, 1.0);
                float radius = length(p);
                float2 radial = p / max(radius, 0.0001);
                float age = max(0.0, _HeatParams.y);

                // One expanding launch wave. The player/aim point in the middle stays legible.
                float clearCenter = smoothstep(0.075, 0.19, radius);
                float outerFade = 1.0 - smoothstep(0.78, 1.12, radius);
                float waveRadius = lerp(0.10, 0.72, saturate(age / 0.34));
                float waveWidth = lerp(0.045, 0.085, saturate(age / 0.30));
                float band = (radius - waveRadius) / waveWidth;
                float waveLife = 1.0 - smoothstep(0.24, 0.44, age);
                float ring = exp2(-3.5 * band * band) * waveLife;
                float ripple = sin(band * 3.14159265 + 0.35);

                // Low-frequency coherent ripples: soft heat haze, with no texture/noise asset.
                float2 heat;
                heat.x = sin(p.y * 19.0 + age * 15.0 + sin(p.x * 11.0 - age * 5.0));
                heat.y = sin(p.x * 17.0 - age * 10.0 + sin(p.y * 13.0 + age * 4.0));
                float locality = exp2(-2.4 * radius * radius);
                float edgeStretch = smoothstep(0.30, 0.82, radius) * 0.075;
                float2 displacement = radial * (ring * ripple * 0.90 + edgeStretch)
                    + heat * locality * (0.08 + ring * 0.20);
                displacement *= clearCenter * outerFade;
                displacement /= max(1.0, length(displacement));
                displacement *= float2(1.0 / aspect, 1.0)
                    * clamp(_HeatAmplitude, 0.0, 0.012) * saturate(_HeatParams.x);

                // Stay inside the rendered source: no black edges, tint, darkening, or added overlay.
                float2 halfTexel = _HeatViewport.zw * 0.5;
                float2 warped = clamp(uv + displacement, halfTexel, 1.0 - halfTexel);
                float2 sampleUv = DYNAMIC_SCALING_APPLY_SCALEBIAS(warped);
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, sampleUv, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
