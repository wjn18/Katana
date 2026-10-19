Shader "KatanaDemo/VFX/GroundImpact"
{
    Properties
    {
        [Enum(ShockRing, 0, DustPuff, 1, Fissure, 2)] _Mode ("Impact shape", Float) = 0
        [HDR] _Tint ("Tint and opacity", Color) = (0.84, 1.12, 1.18, 0.85)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+9"
            "IgnoreProjector" = "True"
            "UniversalMaterialType" = "Unlit"
        }

        Pass
        {
            Name "GroundImpact"
            Tags { "LightMode" = "SRPDefaultUnlit" }

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
                half4 _Tint;
                float _Mode;
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
                    lerp(Hash21(cell + float2(0, 1)), Hash21(cell + float2(1, 1)), t.x), t.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Ring/dust use ordinary 0..1 quads. Fissure uses arbitrary continuous
                // UV.x along a ribbon, and UV.y from 0 at one bank to 1 at the other.
                // Runtime geometry controls the front; vertex alpha controls lifetime.
                float2 p = input.uv - 0.5;
                float radius = length(p);
                float aa = max(fwidth(radius) * 0.75, 0.0008);
                float opacity = 0.0;
                float shade = 1.0;

                // This branch is uniform across a draw: each material selects one mode.
                if (_Mode >= 1.5)
                {
                    // A matte, continuous dark opening: no bloom, radial mask, or
                    // lengthwise taper. Slightly irregular banks prevent a ruler edge.
                    float across = abs(input.uv.y - 0.5) * 2.0;
                    float bank = step(0.5, input.uv.y);
                    float outline = ValueNoise(float2(input.uv.x * 13.0 + 1.7, bank * 7.3 + 2.4));
                    float grit = ValueNoise(float2(input.uv.x * 39.0 + 4.2, input.uv.y * 31.0 + 6.8));
                    float edgeAA = clamp(fwidth(across) * 0.65, 0.001, 0.07);
                    float halfCore = 0.65 + (outline - 0.5) * 0.055;
                    float core = 1.0 - smoothstep(halfCore - edgeAA, halfCore + edgeAA, across);

                    // Only a very narrow, interrupted crushed-stone rim survives
                    // outside the core. Farther out both sides are fully transparent.
                    float outerWidth = halfCore + 0.035 + outline * 0.025;
                    float outer = 1.0 - smoothstep(outerWidth - edgeAA, outerWidth + edgeAA, across);
                    float chips = smoothstep(0.43, 0.70, grit);
                    float rim = saturate(outer - core) * (0.18 + chips * 0.72);
                    float tintAlpha = saturate(_Tint.a * input.color.a);

                    // Suggested _Tint=(.02,.018,.014,1). Vertex RGB may vary grey
                    // between segments; keep it around .6..1 to preserve rim detail.
                    // Clamp the fissure's brightness independently of the HDR ring tint.
                    float3 coreColor = min(max(_Tint.rgb * input.color.rgb, 0.0), 0.12);
                    coreColor *= 0.88 + grit * 0.12;
                    float3 rimColor = min(coreColor * 4.6
                        + float3(0.012, 0.009, 0.006) * input.color.rgb, 0.24);
                    return half4((coreColor * core + rimColor * rim) * tintAlpha,
                                 saturate((core + rim) * tintAlpha));
                }
                else if (_Mode < 0.5)
                {
                    // Sampling a direction vector instead of atan2 avoids a UV seam.
                    float2 direction = p / max(radius, 0.001);
                    float broadNoise = ValueNoise(direction * 4.8 + float2(7.2, 2.9));
                    float fineNoise = ValueNoise(direction * 15.0 + float2(1.7, 8.1));
                    float segments = smoothstep(0.27, 0.66, broadNoise * 0.72 + fineNoise * 0.28);

                    // Main radius is 0.40 UV units, i.e. 80% of quad half-width.
                    // The center remains empty; the outer quad boundary is fully clear.
                    float ringRadius = 0.400 + (fineNoise - 0.5) * 0.005;
                    float width = 0.0045 + broadNoise * 0.003;
                    float distanceToRing = abs(radius - ringRadius);
                    float core = 1.0 - smoothstep(width * 0.2, width + aa, distanceToRing);
                    float halo = 1.0 - smoothstep(width, width * 3.0 + aa, distanceToRing);

                    // A sparse, fainter curl sits on the inner side of the main rim.
                    float innerRadius = 0.369 + (broadNoise - 0.5) * 0.018;
                    float inner = 1.0 - smoothstep(0.002, 0.012 + aa, abs(radius - innerRadius));
                    float innerSegments = smoothstep(0.40, 0.75, fineNoise)
                                        * smoothstep(0.20, 0.65, broadNoise);
                    opacity = (core * 0.94 + halo * 0.075) * segments
                            + inner * innerSegments * 0.12;
                    opacity *= smoothstep(0.315, 0.345, radius)
                             * (1.0 - smoothstep(0.435, 0.470, radius));
                }
                else
                {
                    // The low-frequency warp creates irregular, soft dust lobes.
                    // Different billboard rotations/scales can reuse this one material.
                    float coarse = ValueNoise(input.uv * 4.2 + float2(3.1, 7.8));
                    float detail = ValueNoise(input.uv * 11.0 + float2(9.7, 2.3));
                    float density = coarse * 0.72 + detail * 0.28;
                    float warpedRadius = radius + (coarse - 0.5) * 0.15;
                    float cloudEdge = 1.0 - smoothstep(0.16, 0.43, warpedRadius);
                    float cloudPockets = smoothstep(0.17, 0.76, density);

                    // Keep the center translucent, with no luminous white hotspot.
                    float softCenter = lerp(0.52, 1.0, smoothstep(0.0, 0.22, radius));
                    float outerClear = 1.0 - smoothstep(0.39, 0.49, radius);
                    opacity = cloudEdge * cloudPockets * softCenter * outerClear * 0.70;
                    shade = lerp(0.72, 1.0, coarse);
                }

                // Tint alpha and vertex alpha both participate exactly once. RGB is
                // premultiplied only here; a fading quad never leaves a dark rectangle.
                half4 tint = _Tint * input.color;
                half alpha = saturate(opacity * tint.a);
                return half4(tint.rgb * shade * alpha, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
