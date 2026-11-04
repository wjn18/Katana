Shader "OC Design/Comic Lighting/Stratified Mountain Rock"
{
    Properties
    {
        [Header(Stratified rock)]
        _MossColor("Vegetation sunlit", Color) = (0.44, 0.53, 0.43, 1)
        _MossShadowColor("Vegetation shadow", Color) = (0.28, 0.39, 0.36, 1)
        _MossAmount("Vegetation coverage", Range(0, 1)) = 0.72
        _MossSlope("Vegetation slope start", Range(0.3, 0.95)) = 0.74
        _MossFade("Vegetation slope softness", Range(0.02, 0.4)) = 0.18
        _StrataScale("Rock beds per metre", Range(0.01, 0.5)) = 0.11
        _StrataStrength("Rock bed tonal variation", Range(0, 0.2)) = 0.045
        _SeamStrength("Fine bed seam darkness", Range(0, 0.3)) = 0.12
        _ErosionStrength("Vertical weathering", Range(0, 0.15)) = 0.045
        [MainTexture] _BaseMap("Base texture", 2D) = "white" {}
        [MainColor] _BaseColor("Sunlit palette", Color) = (0.91, 0.86, 0.76, 1)
        _ShadowColor("Shadow palette", Color) = (0.17, 0.29, 0.43, 1)
        _TextureStrength("Original texture influence", Range(0, 1)) = 0
        _Threshold("Light boundary", Range(-0.5, 0.9)) = 0.15
        _Feather("Light boundary softness", Range(0.001, 0.2)) = 0.025
        _ShadowStrength("Cast shadow influence", Range(0, 1)) = 1
        _LightColorInfluence("Sun color influence", Range(0, 1)) = 0
        _PaperStrength("Broad surface variation", Range(0, 0.2)) = 0.025
        _PaperScale("Variation scale per metre", Range(0.1, 8)) = 1
        _PavingStrength("Broad stone paving seams", Range(0, 1)) = 0
        _PavingWobble("Paving line variation (metres)", Range(0, 0.02)) = 0.008
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha clipping", Float) = 0
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _ShadowColor;
            float _TextureStrength;
            float _Threshold;
            float _Feather;
            float _ShadowStrength;
            float _LightColorInfluence;
            float _PaperStrength;
            float _PaperScale;
            float _PavingStrength;
            float _PavingWobble;
            float _AlphaClip;
            float _Cutoff;
            float _Cull;
            half4 _MossColor;
            half4 _MossShadowColor;
            float _MossAmount;
            float _MossSlope;
            float _MossFade;
            float _StrataScale;
            float _StrataStrength;
            float _SeamStrength;
            float _ErosionStrength;

        CBUFFER_END

        struct ComicAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct ComicVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        ComicVaryings ComicVertex(ComicAttributes input)
        {
            ComicVaryings output = (ComicVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return output;
        }

        half4 ComicSample(float2 uv)
        {
            half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            #if defined(_ALPHATEST_ON)
                clip(texel.a * _BaseColor.a - _Cutoff);
            #endif
            return texel;
        }

        float ComicHash(float3 samplePos)
        {
            samplePos = frac(samplePos * 0.1031);
            samplePos += dot(samplePos, samplePos.yzx + 33.33);
            return frac((samplePos.x + samplePos.y) * samplePos.z);
        }

        // Low frequency object-independent variation, deliberately no fine noise.
        float ComicNoise(float3 samplePos)
        {
            float3 cell = floor(samplePos);
            float3 f = frac(samplePos);
            f = f * f * (3.0 - 2.0 * f);
            float z0 = lerp(lerp(ComicHash(cell), ComicHash(cell + float3(1,0,0)), f.x),
                            lerp(ComicHash(cell + float3(0,1,0)), ComicHash(cell + float3(1,1,0)), f.x), f.y);
            float z1 = lerp(lerp(ComicHash(cell + float3(0,0,1)), ComicHash(cell + float3(1,0,1)), f.x),
                            lerp(ComicHash(cell + float3(0,1,1)), ComicHash(cell + float3(1,1,1)), f.x), f.y);
            return lerp(z0, z1, f.z);
        }

        // Continuous world-space geology. Submesh borders have no influence on the palette.
        void MountainPalette(float3 world, half3 normal, out half3 sunlit, out half3 shaded, out float surfaceTone)
        {
            float vegetation = smoothstep(_MossSlope, _MossSlope + _MossFade, saturate(normal.y)) * _MossAmount;
            sunlit = lerp(_BaseColor.rgb, _MossColor.rgb, vegetation);
            shaded = lerp(_ShadowColor.rgb, _MossShadowColor.rgb, vegetation);
            float fade = 1.0 - smoothstep(110.0, 320.0, distance(_WorldSpaceCameraPos, world));
            surfaceTone = 1.0;
            if (fade > 0.005)
            {
                // Slow warping bends the beds; narrow upright streaks suggest water erosion.
                float warp = (ComicNoise(float3(world.x * 0.018, world.z * 0.018, 17.0)) - 0.5) * 0.7;
                float beds = world.y * _StrataScale + world.x * 0.012 + world.z * 0.007 + warp;
                float footprint = max(fwidth(beds), 0.001);
                float seam = 1.0 - smoothstep(0.014, 0.014 + footprint, abs(frac(beds) - 0.5));
                seam *= 1.0 - smoothstep(0.10, 0.42, footprint);
                float layeredTone = cos(beds * 6.2831853) * _StrataStrength;
                float streak = ComicNoise(world * float3(0.24, 0.012, 0.24)) - 0.5;
                // Weathering interrupts the bedding instead of drawing repeated closed rings.
                seam *= smoothstep(-0.08, 0.20, streak);
                float weatheredCrease = 1.0 - smoothstep(0.012, 0.075, abs(streak));
                surfaceTone = 1.0 + fade * (1.0 - vegetation * 0.85)
                    * (layeredTone + (streak - weatheredCrease * 0.45) * _ErosionStrength - seam * _SeamStrength);
            }
        }

        float ComicPavingCoverage(float2 worldXZ)
        {
            // A few slow bends give the large slabs a drawn edge without grain.
            float2 pavingXZ = worldXZ;
            pavingXZ.x += _PavingWobble * sin(worldXZ.y * 1.13 + sin(worldXZ.x * 0.37));
            pavingXZ.y += _PavingWobble * sin(worldXZ.x * 0.91 + sin(worldXZ.y * 0.43));
            float row = floor(pavingXZ.y / 1.8);
            float stagger = frac(row * 0.5) * 2.4;
            float2 cellPosition = frac((pavingXZ + float2(stagger, 0)) / float2(2.4, 1.8));
            float2 distanceToSeam = min(cellPosition, 1.0 - cellPosition) * float2(2.4, 1.8);
            // Use the continuous coordinates for derivatives, avoiding false wide
            // lines at frac boundaries and at the alternating row offsets.
            float2 footprint = max(fwidth(pavingXZ), float2(0.0001, 0.0001));
            const float halfWidth = 0.01;
            // Box-filter a 2 cm interval: subpixel lines become proportionally faint
            // instead of retaining a full-strength flickering one-pixel contour.
            float2 coverage = saturate((halfWidth - distanceToSeam + footprint * 0.5) / footprint)
                            - saturate((-halfWidth - distanceToSeam + footprint * 0.5) / footprint);
            coverage *= 1.0 - smoothstep(float2(0.18, 0.18), float2(0.7, 0.7), footprint);
            return max(coverage.x, coverage.y);
        }
        ENDHLSL

        Pass
        {
            Name "ComicForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ComicVertex
            #pragma fragment ComicFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            half4 ComicFragment(ComicVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 texel = ComicSample(input.uv);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                // Evaluate shadow coordinates per pixel: correct across cascades and
                // the URP screen-space shadow variant, including large ground meshes.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light sun = GetMainLight(shadowCoord);
                float edge = max(_Feather, 0.001);
                float formLight = smoothstep(_Threshold - edge, _Threshold + edge,
                                             dot(normalWS, sun.direction));
                float shadow = lerp(1.0, sun.shadowAttenuation, _ShadowStrength);
                float weight = formLight * shadow;
                half3 textureColor = lerp(half3(1,1,1), texel.rgb, _TextureStrength);
                half3 mountainLit, mountainShade;
                float surfaceTone;
                MountainPalette(input.positionWS, normalWS, mountainLit, mountainShade, surfaceTone);
                half3 palette = lerp(mountainShade, mountainLit, weight);
                half3 color = textureColor * palette * surfaceTone;
                color *= lerp(half3(1,1,1), sun.color, _LightColorInfluence * weight);
                float paper = ComicNoise(input.positionWS * _PaperScale);
                color *= 1.0 + (paper - 0.5) * _PaperStrength * 2.0;
                float paving = ComicPavingCoverage(input.positionWS.xz)
                    * smoothstep(0.65, 0.95, normalWS.y) * _PavingStrength;
                color *= 1.0 - 0.12 * paving;
                // Fog remains scene-controlled. Material colors are authored as the
                // final palette; this shader intentionally does not add PBR or GI.
                float fog = ComputeFogFactor(TransformWorldToHClip(input.positionWS).z);
                return half4(MixFog(color, fog), 1);
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
            #pragma vertex ComicShadowVertex
            #pragma fragment ComicShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            float3 _LightDirection;
            float3 _LightPosition;

            ComicVaryings ComicShadowVertex(ComicAttributes input)
            {
                ComicVaryings output = ComicVertex(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 directionWS = normalize(_LightPosition - output.positionWS);
                #else
                    float3 directionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS,
                    normalize(output.normalWS), directionWS));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                return output;
            }
            half4 ComicShadowFragment(ComicVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ComicSample(input.uv);
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
            #pragma vertex ComicVertex
            #pragma fragment ComicDepthFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            half ComicDepthFragment(ComicVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ComicSample(input.uv);
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
            #pragma vertex ComicVertex
            #pragma fragment ComicNormalFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            half4 ComicNormalFragment(ComicVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ComicSample(input.uv);
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    return half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0);
                #else
                    return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
