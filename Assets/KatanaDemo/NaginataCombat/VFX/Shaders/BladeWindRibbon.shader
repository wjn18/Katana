Shader "KatanaDemo/VFX/BladeWindRibbon"
{
    Properties
    {
        [HDR] _BodyColor ("Translucent cyan body", Color) = (0.16, 0.64, 0.78, 0.25)
        [HDR] _EdgeColor ("Pale blade edge", Color) = (0.90, 1.20, 1.26, 0.88)
        _Opacity ("Overall opacity", Range(0, 1)) = 0.90
        _EdgeWidth ("Outer edge width", Range(0.015, 0.15)) = 0.050
        _Breakup ("Tail breakup", Range(0, 1)) = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "IgnoreProjector" = "True"
            "UniversalMaterialType" = "Unlit"
        }

        Pass
        {
            Name "BladeWindRibbon"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // Premultiplied transparency keeps the fading edge clean on dark backgrounds.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BodyColor;
                half4 _EdgeColor;
                float _Opacity;
                float _EdgeWidth;
                float _Breakup;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float Hash21(float2 p)
            {
                float3 q = frac(float3(p.x, p.y, p.x) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 t = frac(p);
                t = t * t * (3.0 - 2.0 * t);
                return lerp(
                    lerp(Hash21(cell), Hash21(cell + float2(1, 0)), t.x),
                    lerp(Hash21(cell + float2(0, 1)), Hash21(cell + float2(1, 1)), t.x),
                    t.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Mesh contract: x = normalized trail age (0 newest, 1 oldest);
                // y = blade root (0) to tip (1). Vertex alpha carries lifetime fade.
                // No time scrolling: breakup follows the ribbon instead of boiling.
                float age = saturate(input.uv.x);
                float span = saturate(input.uv.y);
                float aa = max(fwidth(span) * 0.75, 0.001);
                float tipFade = 1.0 - smoothstep(0.988 - aa, 1.0, span);
                float tailFade = 1.0 - smoothstep(0.40, 1.0, age);

                // The inner silhouette retracts toward the tip as it ages. Uneven
                // wisps therefore taper into separate tips, not a square cut end.
                float noise = ValueNoise(float2(age * 4.5 - span * 0.8, span * 17.0));
                float longNoise = ValueNoise(float2(age * 2.8, span * 24.0));
                float innerCutoff = 0.05 + age * age * 0.72 + (longNoise - 0.5) * age * 0.18;
                float rootFade = smoothstep(innerCutoff, innerCutoff + 0.18, span);
                float erosion = smoothstep(0.22 + age * 0.24, 0.58 + age * 0.25, noise);
                float broken = lerp(1.0, erosion, _Breakup * smoothstep(0.02, 0.65, age));

                // A thin luminous core with a much fainter halo reads as air/light,
                // rather than a solid strip. Core width tapers along the old end.
                float edgeCenter = 0.951 - 0.006 * sin(age * 11.0);
                float edgeDistance = abs(span - edgeCenter);
                float edgeWidth = _EdgeWidth * lerp(0.48, 0.14, age);
                float edge = 1.0 - smoothstep(edgeWidth * 0.15, edgeWidth + aa, edgeDistance);
                float halo = 1.0 - smoothstep(edgeWidth, edgeWidth * 3.2 + aa, edgeDistance);
                float edgeTear = lerp(1.0, smoothstep(0.20, 0.65, longNoise),
                                      _Breakup * smoothstep(0.42, 0.95, age));

                // Wisps curve and split toward the outer edge. Their staggered
                // endings and gaps avoid evenly spaced, continuous contour lines.
                float wispCenterA = 0.70 + age * 0.13 + 0.020 * sin(age * 10.0 + 0.4);
                float wispCenterB = 0.42 + age * 0.26 + 0.030 * sin(age * 8.0 + 2.1);
                float wispCenterC = 0.82 + age * 0.055 + 0.012 * sin(age * 14.0);
                float wispA = 1.0 - smoothstep(0.002, 0.009 + aa, abs(span - wispCenterA));
                float wispB = 1.0 - smoothstep(0.001, 0.006 + aa, abs(span - wispCenterB));
                float wispC = 1.0 - smoothstep(0.001, 0.007 + aa, abs(span - wispCenterC));
                float wispMask = smoothstep(0.22, 0.66, noise) * (0.9 - age * 0.5);
                float wisps = (wispA * 0.34 + wispB * 0.20 + wispC * 0.20) * wispMask;
                wisps *= 1.0 - smoothstep(0.42 + span * 0.38, 1.0, age);

                // Most of the interior is transparent. Elongated noisy channels
                // provide a hint of wind without obscuring the character silhouette.
                float bodyShape = smoothstep(0.15, 0.55, span)
                                * (1.0 - smoothstep(0.82, 0.975, span));
                float channels = lerp(0.15, 1.0, smoothstep(0.25, 0.72, longNoise));
                float bodyAlpha = bodyShape * _BodyColor.a * 0.40 * channels
                                * (0.25 + erosion * 0.75);
                float edgeAlpha = saturate((edge * 0.97 + halo * 0.10) * edgeTear + wisps)
                                * _EdgeColor.a;
                float envelope = rootFade * tipFade * tailFade * broken
                               * saturate(input.color.a) * _Opacity;

                // Composite the edge over the faint body, then premultiply once.
                // HDR color can bloom, but the body remains visibly transparent.
                float coverage = edgeAlpha + bodyAlpha * (1.0 - edgeAlpha);
                float3 premultiplied = _EdgeColor.rgb * edgeAlpha * (1.0 + edge * 0.15)
                    + _BodyColor.rgb * bodyAlpha * (1.0 - edgeAlpha);
                return half4(premultiplied * input.color.rgb * envelope,
                             saturate(coverage * envelope));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
