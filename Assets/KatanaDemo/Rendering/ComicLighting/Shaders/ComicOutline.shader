Shader "OC Design/Comic Lighting/Outline"
{
    Properties
    {
        _OutlineColor("Ink color", Color) = (0.055, 0.036, 0.027, 1)
        _OutlineWidth("World-space width (metres)", Range(0, 0.08)) = 0.012
        _MinOutlinePixels("Minimum projected width (render pixels)", Range(0, 8)) = 3
        [Toggle] _BoxOutline("Centered box edge-aware width", Float) = 0
        [MainTexture] _BaseMap("Cutout texture", 2D) = "white" {}
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha clipping", Float) = 0
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _SamuraiOcclusionFade("Samurai occluder fade", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "SamuraiOcclusionAperture.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _OutlineColor;
            float _OutlineWidth;
            float _MinOutlinePixels;
            float _BoxOutline;
            float _AlphaClip;
            float _Cutoff;
            float _SamuraiOcclusionFade;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float fog : TEXCOORD1;
            float3 positionWS : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        bool ComicBoxEdgeNormal(float3 neighborOS, float2 basePixel, float2 centerPixel,
            float2 resolution, float minimumW, out float2 outward)
        {
            outward = 0;
            float4 neighborCS = TransformObjectToHClip(neighborOS);
            if (neighborCS.w <= minimumW) return false;
            float2 edgeDelta = neighborCS.xy / neighborCS.w * (0.5 * resolution) - basePixel;
            float edgeLength = length(edgeDelta);
            if (edgeLength < 0.001) return false;
            outward = float2(-edgeDelta.y, edgeDelta.x) / edgeLength;
            // The projected center lies inside a convex box silhouette.
            if (dot(outward, basePixel - centerPixel) < 0.0) outward = -outward;
            return true;
        }

        bool ComicBoxPixelOffset(float3 vertexOS, float3 positionWS, float4 baseCS,
            float2 resolution, float2 worldPixelDelta, out float2 offsetPixels)
        {
            offsetPixels = worldPixelDelta;
            // This branch is only for the centered unit box hull. Each corner's
            // three neighboring corners are obtained by flipping one coordinate.
            if (min(abs(vertexOS.x), min(abs(vertexOS.y), abs(vertexOS.z))) < 0.00001) return false;
            float4 centerCS = TransformObjectToHClip(float3(0, 0, 0));
            float minimumW = IsPerspectiveProjection() ? max(_ProjectionParams.y * 1.01, 0.0001) : 0.0001;
            if (centerCS.w <= minimumW || baseCS.w <= minimumW) return false;
            // View-depth bounds also work for orthographic cameras, whose clip
            // w stays constant even when a box crosses the near clipping plane.
            float centerViewZ = TransformWorldToView(TransformObjectToWorld(float3(0, 0, 0))).z;
            float axisXViewZ = TransformWorldToView(TransformObjectToWorld(float3(abs(vertexOS.x), 0, 0))).z;
            float axisYViewZ = TransformWorldToView(TransformObjectToWorld(float3(0, abs(vertexOS.y), 0))).z;
            float axisZViewZ = TransformWorldToView(TransformObjectToWorld(float3(0, 0, abs(vertexOS.z)))).z;
            float minimumBoxDepth = -centerViewZ - abs(axisXViewZ - centerViewZ)
                - abs(axisYViewZ - centerViewZ) - abs(axisZViewZ - centerViewZ) - abs(_OutlineWidth);
            if (minimumBoxDepth <= _ProjectionParams.y * 1.01) return false;

            float3 cornerSign = sign(vertexOS);
            float3 towardCamera = GetWorldSpaceViewDir(positionWS);
            // Face normals, not the smoothed corner normal, determine the box
            // silhouette. Inverse-transpose transforms handle nonuniform scale.
            float3 facing = float3(
                dot(TransformObjectToWorldNormal(float3(cornerSign.x, 0, 0)), towardCamera),
                dot(TransformObjectToWorldNormal(float3(0, cornerSign.y, 0)), towardCamera),
                dot(TransformObjectToWorldNormal(float3(0, 0, cornerSign.z)), towardCamera));
            bool edgeX = (facing.y > 0.0) != (facing.z > 0.0);
            bool edgeY = (facing.x > 0.0) != (facing.z > 0.0);
            bool edgeZ = (facing.x > 0.0) != (facing.y > 0.0);
            int edgeCount = (edgeX ? 1 : 0) + (edgeY ? 1 : 0) + (edgeZ ? 1 : 0);
            if (edgeCount != 2) return false;

            float2 basePixel = baseCS.xy / baseCS.w * (0.5 * resolution);
            float2 centerPixel = centerCS.xy / centerCS.w * (0.5 * resolution);
            float2 normal0 = 0;
            float2 normal1 = 0;
            float2 edgeNormal = 0;
            int collected = 0;
            if (edgeX)
            {
                if (!ComicBoxEdgeNormal(vertexOS * float3(-1, 1, 1), basePixel, centerPixel,
                    resolution, minimumW, edgeNormal)) return false;
                normal0 = edgeNormal;
                collected = 1;
            }
            if (edgeY)
            {
                if (!ComicBoxEdgeNormal(vertexOS * float3(1, -1, 1), basePixel, centerPixel,
                    resolution, minimumW, edgeNormal)) return false;
                if (collected == 0) normal0 = edgeNormal; else normal1 = edgeNormal;
                collected++;
            }
            if (edgeZ)
            {
                if (!ComicBoxEdgeNormal(vertexOS * float3(1, 1, -1), basePixel, centerPixel,
                    resolution, minimumW, edgeNormal)) return false;
                if (collected == 0) normal0 = edgeNormal; else normal1 = edgeNormal;
            }

            float distance0 = dot(worldPixelDelta, normal0);
            float distance1 = dot(worldPixelDelta, normal1);
            if (distance0 >= _MinOutlinePixels && distance1 >= _MinOutlinePixels) return false;
            // Retain the world shell's thickness wherever it already exceeds
            // the floor. Solve each edge's perpendicular distance separately.
            distance0 = max(distance0, _MinOutlinePixels);
            distance1 = max(distance1, _MinOutlinePixels);
            float determinant = normal0.x * normal1.y - normal0.y * normal1.x;
            if (abs(determinant) > 0.0001)
            {
                offsetPixels = float2(distance0 * normal1.y - normal0.y * distance1,
                    normal0.x * distance1 - distance0 * normal1.x) / determinant;
            }
            else
            {
                // Nearly collinear edges on the same side share an offset line.
                // Opposing normals at an infinitesimal tip have no stable miter.
                float2 bisector = normal0 + normal1;
                float bisectorLength = length(bisector);
                if (bisectorLength < 0.001) return false;
                bisector /= bisectorLength;
                float agreement0 = dot(bisector, normal0);
                float agreement1 = dot(bisector, normal1);
                if (min(agreement0, agreement1) < 0.001) return false;
                offsetPixels = bisector * max(distance0 / agreement0, distance1 / agreement1);
            }
            // Bound extreme acute-corner spikes. At this safety cap the exact
            // edge floor can be relaxed; ordinary box corners use the exact solve.
            float miterLength = length(offsetPixels);
            float miterLimit = max(max(length(worldPixelDelta), max(distance0, distance1)) * 6.0, 1.0);
            if (miterLength > miterLimit) offsetPixels *= miterLimit / miterLength;
            return true;
        }

        Varyings OutlineVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            float4 baseCS = TransformWorldToHClip(positionWS);
            output.positionWS = positionWS + normalWS * _OutlineWidth;
            float4 expandedCS = TransformWorldToHClip(output.positionWS);
            output.positionCS = expandedCS;
            // Keep the original metre-based shell at close range. Only enlarge
            // its projected XY displacement when it falls below the pixel floor.
            // Separate perspective divides matter: normal extrusion changes w.
            if (_MinOutlinePixels > 0.0 && baseCS.w > 0.0001 && expandedCS.w > 0.0001)
            {
                float2 resolution = max(_ScaledScreenParams.xy, float2(1.0, 1.0));
                float2 projectedDelta = expandedCS.xy / expandedCS.w - baseCS.xy / baseCS.w;
                float2 pixelDelta = projectedDelta * (0.5 * resolution);
                float projectedPixels = length(pixelDelta);
                float2 boxOffset = pixelDelta;
                bool boxSolved = _BoxOutline > 0.5 && ComicBoxPixelOffset(input.positionOS.xyz,
                    positionWS, baseCS, resolution, pixelDelta, boxOffset);
                if (boxSolved)
                {
                    output.positionCS.xy = (baseCS.xy / baseCS.w + 2.0 * boxOffset / resolution) * expandedCS.w;
                }
                else if (projectedPixels < _MinOutlinePixels)
                {
                    float2 pixelDirection = pixelDelta;
                    float directionLength = projectedPixels;
                    if (directionLength < 0.00001)
                    {
                        // Differential of clip.xy / clip.w along the world
                        // normal; robust for zero/tiny metre widths and ortho.
                        float4 normalCS = mul(GetWorldToHClipMatrix(), float4(normalWS, 0.0));
                        pixelDirection = (normalCS.xy * baseCS.w - baseCS.xy * normalCS.w) * resolution;
                        directionLength = length(pixelDirection);
                    }
                    if (directionLength > 0.00001)
                    {
                        pixelDirection /= directionLength;
                        float extraPixels = _MinOutlinePixels - projectedPixels;
                        output.positionCS.xy += pixelDirection * (2.0 * extraPixels / resolution) * expandedCS.w;
                    }
                }
            }
            // Preserve the original shell z/w, so the pixel floor does not
            // pull distant contours in front of unrelated foreground geometry.
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.fog = ComputeFogFactor(output.positionCS.z);
            return output;
        }

        half4 OutlineColor(Varyings input)
        {
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
            #endif
            return half4(MixFog(_OutlineColor.rgb, input.fog), _OutlineColor.a);
        }
        ENDHLSL

        Pass
        {
            Name "InkHull"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SamuraiOcclusionClip(input.positionWS, _SamuraiOcclusionFade);
                return OutlineColor(input);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SamuraiOcclusionForeground"
            Tags { "LightMode"="SamuraiOcclusionForeground" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVertex
            #pragma fragment OutlineForegroundFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            half4 OutlineForegroundFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float alpha = SamuraiOcclusionForegroundAlpha(input.positionWS, input.positionCS,
                    _SamuraiOcclusionFade);
                return half4(OutlineColor(input).rgb, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
