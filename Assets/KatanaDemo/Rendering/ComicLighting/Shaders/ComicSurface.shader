Shader "OC Design/Comic Lighting/Surface"
{
    Properties
    {
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
        [HideInInspector] _SamuraiOcclusionFade("Samurai occluder fade", Float) = 0
        [Toggle(_SAMURAI_GARDEN_MOSS)] _GardenMoss("Moss on stone", Float) = 0
        [HideInInspector] _GardenMossBakedProjection("Moss travels with the mesh", Float) = 0
        _GardenMossAnchor("Moss center XZ and inverse radii", Vector) = (0, 0, 1, 1)
        _GardenMossColor("Sunlit moss", Color) = (0.545, 0.580, 0.435, 1)
        _GardenMossShadow("Shaded moss", Color) = (0.318, 0.424, 0.431, 1)
        [Enum(None,0,Stone,1,Cedar,2,Cloth,3,RakedGravel,4)] _TempleSurface("Temple surface detail", Float) = 0
        _TempleDetailStrength("Temple detail strength", Range(0,1)) = 0.12
        _TempleAnchor("Gravel centre XZ", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Assets/KatanaDemo/Rendering/ComicLighting/Shaders/SamuraiOcclusionAperture.hlsl"

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
            float _SamuraiOcclusionFade;
            float4 _GardenMossAnchor;
            float _GardenMossBakedProjection;
            half4 _GardenMossColor;
            half4 _GardenMossShadow;
            float _TempleSurface;
            float _TempleDetailStrength;
            float4 _TempleAnchor;
        CBUFFER_END

        struct ComicAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float3 mossProjection : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct ComicVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD2;
            float3 mossProjection : TEXCOORD3;
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
            output.mossProjection = input.mossProjection;
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

        float GardenMossMask(float3 projection, float upward)
        {
            float2 footprint = (projection.xz - _GardenMossAnchor.xy) * _GardenMossAnchor.zw;
            float broad = ComicNoise(projection * 1.05);
            float fine = ComicNoise(projection * 2.65 + 19.7);
            float radius = length(footprint);
            float rim = 1.0 - smoothstep(0.63, 0.99, radius + (broad - 0.5) * 0.28);
            float top = smoothstep(0.40, 0.79, upward);
            float rootPocket = (1.0 - smoothstep(0.08, 0.61, radius)) * 0.22;
            float pockets = smoothstep(0.45, 0.68, broad * 0.77 + fine * 0.23 + rootPocket);
            return rim * top * pockets;
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

        float TempleGroove(float phase, float width)
        {
            float footprint = max(fwidth(phase), 0.0001);
            float distance = abs(frac(phase + 0.5) - 0.5);
            float coverage = saturate((width - distance + footprint * 0.5) / footprint)
                           - saturate((-width - distance + footprint * 0.5) / footprint);
            return coverage * (1.0 - smoothstep(0.3, 0.8, footprint));
        }

        float TempleSurfaceDetail(float3 position, half3 normal)
        {
            if (_TempleSurface < 0.5) return 1.0;
            float strength = _TempleDetailStrength;
            float broad = ComicNoise(position * 3.1);
            if (_TempleSurface < 1.5)
            {
                float course = position.y / 0.36;
                float horizontal = abs(normal.x) > abs(normal.z) ? position.z : position.x;
                float joint = max(TempleGroove(course, 0.012),
                    TempleGroove(horizontal / 0.78 + floor(course) * 0.5, 0.009));
                return 1.0 + (broad - 0.5) * strength * 0.35
                    - joint * strength * (1.0 - smoothstep(0.4, 0.7, normal.y));
            }
            if (_TempleSurface < 2.5)
            {
                float horizontal = abs(normal.x) > abs(normal.z) ? position.z : position.x;
                float grain = TempleGroove(horizontal * 14.0 + ComicNoise(position * 0.5) * 1.8, 0.07);
                return 1.0 - grain * strength + (broad - 0.5) * strength * 0.6;
            }
            if (_TempleSurface < 3.5)
            {
                float horizontal = abs(normal.x) > abs(normal.z) ? position.z : position.x;
                float weave = max(TempleGroove(horizontal * 90.0, 0.1),
                    TempleGroove(position.y * 90.0, 0.1));
                return 1.0 - weave * strength * 0.7 + (broad - 0.5) * strength * 0.6;
            }
            float2 relative = position.xz - _TempleAnchor.xy;
            float radius = length(relative * float2(1.0, 0.92));
            float arcs = TempleGroove(radius / 0.095, 0.09);
            return 1.0 - arcs * strength * smoothstep(0.6, 0.94, normal.y)
                + (broad - 0.5) * strength * 0.3;
        }

        half3 ComicLighting(ComicVaryings input)
        {
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
            half3 palette = lerp(_ShadowColor.rgb, _BaseColor.rgb, weight);
            #if defined(_SAMURAI_GARDEN_MOSS)
                half3 moss = lerp(_GardenMossShadow.rgb, _GardenMossColor.rgb, weight);
                float3 projection = lerp(input.positionWS, input.mossProjection, _GardenMossBakedProjection);
                palette = lerp(palette, moss, GardenMossMask(projection, normalWS.y));
            #endif
            half3 color = textureColor * palette;
            color *= lerp(half3(1,1,1), sun.color, _LightColorInfluence * weight);
            float paper = ComicNoise(input.positionWS * _PaperScale);
            color *= 1.0 + (paper - 0.5) * _PaperStrength * 2.0;
            float paving = ComicPavingCoverage(input.positionWS.xz)
                * smoothstep(0.65, 0.95, normalWS.y) * _PavingStrength;
            color *= 1.0 - 0.12 * paving;
            color *= TempleSurfaceDetail(input.positionWS, normalWS);
            // Fog remains scene-controlled. Material colors are authored as the
            // final palette; this shader intentionally does not add PBR or GI.
            float fog = ComputeFogFactor(TransformWorldToHClip(input.positionWS).z);
            return MixFog(color, fog);
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
            #pragma shader_feature_local_fragment _SAMURAI_GARDEN_MOSS

            half4 ComicFragment(ComicVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SamuraiOcclusionClip(input.positionWS, _SamuraiOcclusionFade);
                return half4(ComicLighting(input), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SamuraiOcclusionForeground"
            Tags { "LightMode"="SamuraiOcclusionForeground" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ComicVertex
            #pragma fragment ComicForegroundFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SAMURAI_GARDEN_MOSS

            half4 ComicForegroundFragment(ComicVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float alpha = SamuraiOcclusionForegroundAlpha(input.positionWS, input.positionCS,
                    _SamuraiOcclusionFade);
                return half4(ComicLighting(input), alpha);
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
                SamuraiOcclusionClip(input.positionWS, _SamuraiOcclusionFade);
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
                SamuraiOcclusionClip(input.positionWS, _SamuraiOcclusionFade);
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
