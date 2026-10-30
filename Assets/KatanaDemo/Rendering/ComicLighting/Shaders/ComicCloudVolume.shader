Shader "OC Design/Comic Lighting/Cloud Volume"
{
    Properties
    {
        _DensityMap("Cloud density R / fixed paint G / height B", 3D) = "white" {}
        _ShadeColor("Deep blue shaded cloud", Color) = (0.2, 0.301961, 0.407843, 1)
        _MidColor("Smoke blue middle cloud", Color) = (0.396078, 0.482353, 0.572549, 1)
        _LitColor("Cool transmitted light", Color) = (0.647059, 0.686275, 0.725490, 1)
        // HDR defaults are scene-linear: the intended displayed swatch is #FFAF72.
        [HDR] _WarmColor("Peach orange transmitted light (linear)", Color) = (1, 0.428690, 0.168269, 1)
        _DensityScale("Density multiplier", Range(0.1, 4)) = 1.2
        _DensityEdgeSharpness("Density edge sharpness", Range(1, 4)) = 2
        _ErosionStrength("Surface-only density erosion", Range(0, 0.4)) = 0.16
        _ErosionScale("Erosion groups across cloud box", Range(6, 48)) = 24
        _Extinction("View extinction per world metre", Range(0.005, 0.6)) = 0.42
        _LightExtinction("Light extinction per world metre", Range(0.005, 0.8)) = 0.095
        _Steps("View ray samples", Range(32, 160)) = 160
        _LightSteps("Light ray samples", Range(4, 10)) = 10
        _LightYawOffset("Main light azimuth offset", Range(-180, 180)) = -100
        _LightDirection("Explicit direction toward light WS (zero uses main)", Vector) = (0, 0, 0, 0)
        _WarmStrength("Warm transmission mixture", Range(0, 1.5)) = 1.15
        _BrushStrength("Fixed paint modulation", Range(0, 0.4)) = 0.10
        _SurfaceBrushStrength("Visible surface three-tone paint", Range(0, 0.3)) = 0.08
        _BandStrength("Four-tone influence over continuous light", Range(0, 1)) = 0.22
        _Exposure("Cloud palette exposure", Range(0.25, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Front

        Pass
        {
            Name "PaintedCloudVolume"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex CloudVolumeVertex
            #pragma fragment CloudVolumeFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE3D(_DensityMap);
            SAMPLER(sampler_DensityMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _ShadeColor;
                half4 _MidColor;
                half4 _LitColor;
                half4 _WarmColor;
                float _DensityScale;
                float _DensityEdgeSharpness;
                float _ErosionStrength;
                float _ErosionScale;
                float _Extinction;
                float _LightExtinction;
                float _Steps;
                float _LightSteps;
                float _LightYawOffset;
                float4 _LightDirection;
                float _WarmStrength;
                float _BrushStrength;
                float _SurfaceBrushStrength;
                float _BandStrength;
                float _Exposure;
            CBUFFER_END

            struct CloudVolumeAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct CloudVolumeVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CloudVolumeVaryings CloudVolumeVertex(CloudVolumeAttributes input)
            {
                CloudVolumeVaryings output = (CloudVolumeVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                return output;
            }

            // Directions deliberately retain their inverse object scale. Since
            // originOS + directionOS * t equals the transformed world ray, t and
            // the integration step remain metres, including nonuniform box scale.
            float3 CloudDirectionToObject(float3 directionWS)
            {
                return mul((float3x3)GetWorldToObjectMatrix(), directionWS);
            }

            float2 CloudUnitBoxInterval(float3 originOS, float3 directionOS)
            {
                const float parallelEpsilon = 0.0000001;
                if ((abs(directionOS.x) < parallelEpsilon && abs(originOS.x) > 0.5)
                    || (abs(directionOS.y) < parallelEpsilon && abs(originOS.y) > 0.5)
                    || (abs(directionOS.z) < parallelEpsilon && abs(originOS.z) > 0.5))
                    return float2(1, -1);
                float3 directionSign = step(0.0, directionOS) * 2.0 - 1.0;
                float3 inverseDirection = directionSign / max(abs(directionOS), parallelEpsilon);
                float3 firstHit = (-0.5 - originOS) * inverseDirection;
                float3 secondHit = (0.5 - originOS) * inverseDirection;
                float3 entry = min(firstHit, secondHit);
                float3 exitHit = max(firstHit, secondHit);
                return float2(max(entry.x, max(entry.y, entry.z)),
                    min(exitHit.x, min(exitHit.y, exitHit.z)));
            }

            float4 CloudDensityData(float3 positionOS)
            {
                float3 uvw = positionOS + 0.5;
                if (any(uvw < 0.0) || any(uvw > 1.0))
                    return float4(0, 0.5, 0, 1);
                return SAMPLE_TEXTURE3D_LOD(_DensityMap, sampler_DensityMap, uvw, 0);
            }

            float CloudVolumeHash(float3 samplePos)
            {
                samplePos = frac(samplePos * 0.1031);
                samplePos += dot(samplePos, samplePos.yzx + 33.33);
                return frac((samplePos.x + samplePos.y) * samplePos.z);
            }

            float CloudVolumeNoise(float3 samplePos)
            {
                float3 cell = floor(samplePos);
                float3 f = frac(samplePos);
                f = f * f * (3.0 - 2.0 * f);
                float z0 = lerp(lerp(CloudVolumeHash(cell),
                    CloudVolumeHash(cell + float3(1,0,0)), f.x),
                    lerp(CloudVolumeHash(cell + float3(0,1,0)),
                    CloudVolumeHash(cell + float3(1,1,0)), f.x), f.y);
                float z1 = lerp(lerp(CloudVolumeHash(cell + float3(0,0,1)),
                    CloudVolumeHash(cell + float3(1,0,1)), f.x),
                    lerp(CloudVolumeHash(cell + float3(0,1,1)),
                    CloudVolumeHash(cell + float3(1,1,1)), f.x), f.y);
                return lerp(z0, z1, f.z);
            }

            float CloudDensity(float4 data, float3 positionOS)
            {
                float sharpness = max(1.0, _DensityEdgeSharpness);
                float emptyThreshold = max(0.001, 0.5 - 0.5 / sharpness);
                float rawDensity = data.r;
                // Erosion only removes density, so these samples are guaranteed
                // empty after remapping and need no procedural noise calculation.
                if (rawDensity <= emptyThreshold)
                    return 0;
                if (_ErosionStrength > 0.0001 && rawDensity < 0.8)
                {
                    // The box's authored aspect ratio is 52:79:44. Both scales
                    // are object-fixed, with modest obliqueness to avoid grid axes.
                    float3 p = (positionOS + 0.5) * float3(1.0, 1.52, 0.85) * _ErosionScale;
                    p = float3(p.x + p.y * 0.21 - p.z * 0.13,
                        p.y - p.x * 0.17 + p.z * 0.09, p.z + p.x * 0.11);
                    float mediumErosion = CloudVolumeNoise(p + float3(4.1, 9.3, 2.7));
                    float fineErosion = CloudVolumeNoise(p * 2.37 + float3(13.8, 3.6, 7.2));
                    float skin = 1.0 - smoothstep(0.34, 0.8, rawDensity);
                    rawDensity -= (mediumErosion * 0.65 + fineErosion * 0.35)
                        * skin * _ErosionStrength;
                }
                // Tighten the transition around half density while keeping the
                // dense interior. View and light rays call this exact same field.
                float density = saturate((rawDensity - 0.5) * sharpness + 0.5);
                return max(0.0, density * _DensityScale
                    * (1.0 + (data.g - 0.5) * _BrushStrength * 0.25));
            }

            float3 CloudKeyDirection()
            {
                if (dot(_LightDirection.xyz, _LightDirection.xyz) > 0.00001)
                    return normalize(_LightDirection.xyz);
                Light mainLight = GetMainLight();
                float3 directionWS = mainLight.direction;
                if (dot(directionWS, directionWS) < 0.00001)
                    directionWS = normalize(float3(-0.45, 0.70, -0.55));
                else
                    directionWS = normalize(directionWS);
                float angle = radians(_LightYawOffset);
                float cosine = cos(angle), sine = sin(angle);
                return float3(cosine * directionWS.x + sine * directionWS.z,
                    directionWS.y, -sine * directionWS.x + cosine * directionWS.z);
            }

            float CloudLightTransmission(float3 sampleOS, float3 lightDirectionOS, int lightSteps)
            {
                float lightDistance = max(0.0, CloudUnitBoxInterval(sampleOS, lightDirectionOS).y);
                float opticalDepth = 0.0;
                float inverseSteps = rcp((float)lightSteps);
                [loop]
                for (int lightIndex = 0; lightIndex < lightSteps; ++lightIndex)
                {
                    // Quadratic interval spacing concentrates the few shadow
                    // samples around the current lobe while still covering the
                    // whole path to the illuminated box boundary.
                    float a = lightIndex * inverseSteps;
                    float b = (lightIndex + 1) * inverseSteps;
                    float segmentStart = a * a * lightDistance;
                    float segmentEnd = b * b * lightDistance;
                    float sampleDistance = (segmentStart + segmentEnd) * 0.5;
                    float3 lightSampleOS = sampleOS + lightDirectionOS * sampleDistance;
                    float density = CloudDensity(CloudDensityData(lightSampleOS), lightSampleOS);
                    opticalDepth += density * (segmentEnd - segmentStart) * _LightExtinction;
                    if (opticalDepth > 6.0)
                        break;
                }
                return exp(-opticalDepth);
            }

            half4 CloudVolumeFragment(CloudVolumeVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 cameraPositionWS = GetCameraPositionWS();
                float3 rayOriginWS = cameraPositionWS;
                float3 rayDirectionWS;
                if (IsPerspectiveProjection())
                {
                    rayDirectionWS = normalize(input.positionWS - cameraPositionWS);
                }
                else
                {
                    // Every orthographic pixel starts on the camera plane at its
                    // own lateral coordinate, with the same forward direction.
                    rayDirectionWS = normalize(GetViewForwardDir());
                    rayOriginWS = input.positionWS - rayDirectionWS
                        * dot(input.positionWS - cameraPositionWS, rayDirectionWS);
                }
                float3 rayOriginOS = TransformWorldToObject(rayOriginWS);
                float3 rayDirectionOS = CloudDirectionToObject(rayDirectionWS);
                float2 interval = CloudUnitBoxInterval(rayOriginOS, rayDirectionOS);
                float rayStart = max(interval.x, 0.0);
                float rayEnd = interval.y;
                if (rayEnd <= rayStart)
                    return 0;

                int steps = clamp((int)round(_Steps), 32, 160);
                int lightSteps = clamp((int)round(_LightSteps), 4, 10);
                float stepWorld = (rayEnd - rayStart) / (float)steps;
                float3 keyDirectionWS = CloudKeyDirection();
                float3 keyDirectionOS = CloudDirectionToObject(keyDirectionWS);
                // Light behind the cloud scatters more warmly toward the viewer.
                // This is a direction-based scattering bias, never a surface rim.
                float forwardLight = smoothstep(-0.2, 0.65, dot(rayDirectionWS, keyDirectionWS));
                float viewTransmission = 1.0;
                float3 accumulatedColor = 0;
                float3 paintAnchorOS = 0;
                float strongestContribution = 0;
                bool paintAnchorFound = false;

                [loop]
                for (int sampleIndex = 0; sampleIndex < steps; ++sampleIndex)
                {
                    float sampleDistance = rayStart + (sampleIndex + 0.5) * stepWorld;
                    float3 sampleOS = rayOriginOS + rayDirectionOS * sampleDistance;
                    float4 data = CloudDensityData(sampleOS);
                    float density = CloudDensity(data, sampleOS);
                    if (density < 0.001)
                        continue;

                    float alpha = 1.0 - exp(-density * _Extinction * stepWorld);
                    if (!paintAnchorFound)
                    {
                        // Thin silhouettes may never reach 25% opacity. Their
                        // fallback paint location is the strongest visible sample.
                        float contribution = alpha * viewTransmission;
                        if (contribution > strongestContribution)
                        {
                            strongestContribution = contribution;
                            paintAnchorOS = sampleOS;
                        }
                        float nextTransmission = viewTransmission * (1.0 - alpha);
                        if (nextTransmission <= 0.75)
                        {
                            // Analytic interpolation within this constant-density
                            // step avoids snapping paint to discrete ray samples.
                            float fraction = saturate(log(viewTransmission / 0.75)
                                / max(density * _Extinction * stepWorld, 0.000001));
                            float anchorDistance = rayStart + (sampleIndex + fraction) * stepWorld;
                            paintAnchorOS = rayOriginOS + rayDirectionOS * anchorDistance;
                            paintAnchorFound = true;
                        }
                    }
                    float lightTransmission = CloudLightTransmission(sampleOS, keyDirectionOS, lightSteps);
                    float illumination = saturate(0.08 + pow(lightTransmission, 0.62) * 0.92);
                    float fourTone = floor(illumination * 3.0 + 0.5) / 3.0;
                    float paintedLight = lerp(illumination, fourTone, _BandStrength);
                    float3 sampleColor = lerp(_ShadeColor.rgb, _MidColor.rgb,
                        smoothstep(0.02, 0.62, paintedLight));
                    sampleColor = lerp(sampleColor, _LitColor.rgb,
                        smoothstep(0.56, 1.0, paintedLight));
                    sampleColor *= lerp(0.93, 1.03, data.b);

                    // A thin, light-accessible region can transmit peach light;
                    // thicker regions remain blue through actual optical depth.
                    // Continuous mixing retains light variation inside warm areas.
                    float warmMixture = saturate(_WarmStrength * pow(lightTransmission, 0.85)
                        * lerp(0.20, 1.0, forwardLight));
                    float3 transmittedWarm = _WarmColor.rgb * lerp(0.68, 1.0, lightTransmission);
                    sampleColor = lerp(sampleColor, transmittedWarm, warmMixture);
                    sampleColor *= 1.0 + (data.g - 0.5) * _BrushStrength * 0.35;

                    accumulatedColor += sampleColor * alpha * viewTransmission;
                    viewTransmission *= 1.0 - alpha;
                    if (viewTransmission < 0.012)
                        break;
                }

                float accumulatedAlpha = 1.0 - viewTransmission;
                // Internal compositing is premultiplied. The requested material
                // blend uses SrcAlpha, so return straight color exactly once.
                float3 straightColor = accumulatedColor / max(accumulatedAlpha, 0.00001);
                if (accumulatedAlpha > 0.001 && _SurfaceBrushStrength > 0.0001)
                {
                    // Sample the fixed short strokes once at the visible cloud
                    // surface, after integration, so deeper samples cannot average
                    // them away. Only brightness changes; cool/warm hue is retained.
                    float surfacePaint = CloudDensityData(paintAnchorOS).g;
                    float paintTone = smoothstep(0.36, 0.44, surfacePaint)
                        + smoothstep(0.56, 0.64, surfacePaint) - 1.0;
                    float paintVisibility = smoothstep(0.08, 0.30, accumulatedAlpha);
                    straightColor *= 1.0 + paintTone * _SurfaceBrushStrength * paintVisibility;
                }
                return half4(straightColor * _Exposure, accumulatedAlpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
