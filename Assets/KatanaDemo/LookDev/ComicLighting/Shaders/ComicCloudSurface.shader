Shader "OC Design/Comic Lighting/Cloud Sculpture"
{
    Properties
    {
        _ShadeColor("Deep blue shaded volume", Color) = (0.149020, 0.243137, 0.349020, 1)
        _MidColor("Blue grey middle plane", Color) = (0.325490, 0.427451, 0.525490, 1)
        _LitColor("Cool painted light plane", Color) = (0.529412, 0.596078, 0.662745, 1)
        [HDR] _HighlightColor("Orange gold grazing light", Color) = (1, 0.705882, 0.466667, 1)
        _ShadeStep("Shade to middle threshold", Range(0, 1)) = 0.30
        _LitStep("Middle to light threshold", Range(0, 1)) = 0.68
        _HighlightStep("Cool crest threshold", Range(0, 1)) = 0.84
        _BandSoftness("Paint boundary softness", Range(0.005, 0.15)) = 0.028
        _CrestHeightRange("Local upper-envelope fallback (object Y)", Vector) = (2, 7, 0, 0)
        _CloudEnvelope("Cloud half size XYZ / centre Y", Vector) = (16, 27, 14, 2)
        _VolumeVariation("Soft volume within painted planes", Range(0, 0.3)) = 0.12
        _UpInfluence("Upper surface influence", Range(0, 0.6)) = 0.04
        _LightContrast("Volume contrast", Range(0.5, 1.8)) = 1.15
        _LightYawOffset("Cloud key light azimuth offset", Range(-180, 180)) = -100
        _NormalVariation("Painted plane normal strength", Range(0, 0.35)) = 0.12
        _VariationScale("Normal variation per metre", Range(0.001, 0.1)) = 0.018
        _BrushScale("Brush groups per object metre", Range(0.1, 2)) = 0.45
        _BrushStrength("Broken paint plane strength", Range(0, 0.5)) = 0.22
        _ShadeBrushStrength("Blue shadow paint separation", Range(0, 1)) = 0.60
        _EdgeBreakup("Fine painted silhouette clipping", Range(0, 1)) = 0.35
        _AOIntensity("Sculpted cavity shading", Range(0, 1)) = 0.65
        _WarmRimStrength("Directional warm edge strength", Range(0, 3)) = 0.80
        _WarmRimWidth("Warm side-plane width", Range(0.1, 1)) = 0.70
        _WarmRimPower("Warm edge concentration", Range(0.8, 5)) = 2.4
        _WarmLightThreshold("Warm edge light-facing threshold", Range(-0.25, 0.6)) = 0.07
        _Exposure("Palette exposure", Range(0.25, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ShadeColor;
            half4 _MidColor;
            half4 _LitColor;
            half4 _HighlightColor;
            float _ShadeStep;
            float _LitStep;
            float _HighlightStep;
            float _BandSoftness;
            float4 _CrestHeightRange;
            float4 _CloudEnvelope;
            float _VolumeVariation;
            float _UpInfluence;
            float _LightContrast;
            float _LightYawOffset;
            float _NormalVariation;
            float _VariationScale;
            float _BrushScale;
            float _BrushStrength;
            float _ShadeBrushStrength;
            float _EdgeBreakup;
            float _AOIntensity;
            float _WarmRimStrength;
            float _WarmRimWidth;
            float _WarmRimPower;
            float _WarmLightThreshold;
            float _Exposure;
        CBUFFER_END

        struct CloudAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct CloudVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            float3 positionOS : TEXCOORD2;
            half4 paintData : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        CloudVaryings CloudVertex(CloudAttributes input)
        {
            CloudVaryings output = (CloudVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS;
            output.positionWS = position.positionWS;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.positionOS = input.positionOS.xyz;
            output.paintData = input.color;
            return output;
        }

        float CloudPaintHash(float3 samplePos)
        {
            samplePos = frac(samplePos * 0.1031);
            samplePos += dot(samplePos, samplePos.yzx + 33.33);
            return frac((samplePos.x + samplePos.y) * samplePos.z);
        }

        // XYZ is the analytic field gradient, W its value. Computing the gradient
        // here keeps the short painted planes glued to the object from every view.
        float4 CloudPaintNoise(float3 samplePos)
        {
            float3 cell = floor(samplePos);
            float3 f = frac(samplePos);
            float3 df = 6.0 * f * (1.0 - f);
            f = f * f * (3.0 - 2.0 * f);
            float h000 = CloudPaintHash(cell);
            float h100 = CloudPaintHash(cell + float3(1,0,0));
            float h010 = CloudPaintHash(cell + float3(0,1,0));
            float h110 = CloudPaintHash(cell + float3(1,1,0));
            float h001 = CloudPaintHash(cell + float3(0,0,1));
            float h101 = CloudPaintHash(cell + float3(1,0,1));
            float h011 = CloudPaintHash(cell + float3(0,1,1));
            float h111 = CloudPaintHash(cell + float3(1,1,1));
            float z0 = lerp(lerp(h000, h100, f.x), lerp(h010, h110, f.x), f.y);
            float z1 = lerp(lerp(h001, h101, f.x), lerp(h011, h111, f.x), f.y);
            float gx = lerp(lerp(h100-h000, h110-h010, f.y), lerp(h101-h001, h111-h011, f.y), f.z) * df.x;
            float gy = lerp(lerp(h010-h000, h110-h100, f.x), lerp(h011-h001, h111-h101, f.x), f.z) * df.y;
            float gz = (z1-z0) * df.z;
            return float4(gx, gy, gz, lerp(z0, z1, f.z));
        }

        void CloudPaintField(float3 positionOS, out float paintValue, out float3 gradientOS)
        {
            // Elongated, oblique cells create ordered short brush shapes rather
            // than isotropic speckles. Two stroke sizes share a broad domain warp.
            float3 p = positionOS * _BrushScale;
            float3 brushPos = float3(p.x + p.y*0.28 - p.z*0.16,
                -p.x*0.23 + p.y*0.96 + p.z*0.14,
                p.x*0.16 - p.y*0.12 + p.z*0.98) * float3(0.95, 2.45, 1.15);
            float4 broad = CloudPaintNoise(brushPos * 0.32 + float3(7.1, 2.8, 11.4));
            float3 warped = brushPos + broad.xyz * 0.38;
            float4 strokes = CloudPaintNoise(warped + float3(2.6, 8.3, 4.1));
            float4 smallStrokes = CloudPaintNoise(warped * 2.07 + float3(13.5, 3.2, 9.7));
            paintValue = broad.w * 0.30 + strokes.w * 0.54 + smallStrokes.w * 0.16;
            float3 gradient = broad.xyz * 0.096 + strokes.xyz * 0.54 + smallStrokes.xyz * 0.3312;
            // Chain the anisotropic paint coordinate transform back to object space.
            gradient *= float3(0.95, 2.45, 1.15);
            gradientOS = float3(gradient.x - gradient.y*0.23 + gradient.z*0.16,
                gradient.x*0.28 + gradient.y*0.96 - gradient.z*0.12,
                -gradient.x*0.16 + gradient.y*0.14 + gradient.z*0.98) * _BrushScale;
        }

        float3 CloudShapeNormal(float3 positionWS, float3 meshNormalWS, float3 paintGradientOS)
        {
            // Geometry supplies the real silhouette. Paint changes only surface
            // planes; no screen-space noise, opacity, or volume fog is involved.
            float3 phase = positionWS * _VariationScale;
            float3 variation = float3(
                sin(phase.y + 0.7) * cos(phase.z * 0.83),
                sin(phase.z + 2.1) * cos(phase.x * 0.71),
                sin(phase.x + 4.3) * cos(phase.y * 0.89));
            float3 gradientWS = TransformObjectToWorldNormal(paintGradientOS, false);
            gradientWS -= meshNormalWS * dot(gradientWS, meshNormalWS);
            return normalize(meshNormalWS - gradientWS * (_NormalVariation * 3.0)
                + variation * (_NormalVariation * 0.18));
        }

        // The same object-space paint and original shape normal drive coverage in
        // every camera pass. This only nicks the outermost grazing contour; it is
        // not opacity, screen noise, or a noisy cutout across the cloud's body.
        void CloudClipEdge(float3 positionWS, float3 meshNormalWS, float paintValue)
        {
            if (_EdgeBreakup <= 0.0001)
                return;
            float3 viewDirection = GetWorldSpaceNormalizeViewDir(positionWS);
            float viewFacing = saturate(dot(meshNormalWS, viewDirection));
            float grazing = pow(1.0 - viewFacing, 16.0);
            float borderPaint = smoothstep(0.40, 0.63, paintValue);
            // On a curved silhouette, N.V / fwidth(N.V) is roughly twice the
            // distance from its edge in pixels. This conservative cap also keeps
            // almost-flat grazing faces from being perforated over a wide area.
            float edgeDistance = viewFacing / max(fwidth(viewFacing), 0.0001);
            float narrowContour = 1.0 - smoothstep(4.0, 10.0, edgeDistance);
            clip(0.035 - _EdgeBreakup * borderPaint * grazing * narrowContour);
        }

        void CloudClipEdgeFromVaryings(CloudVaryings input)
        {
            if (_EdgeBreakup <= 0.0001)
                return;
            float paintValue;
            float3 unusedGradient;
            CloudPaintField(input.positionOS, paintValue, unusedGradient);
            CloudClipEdge(input.positionWS, normalize(input.normalWS), paintValue);
        }
        ENDHLSL

        Pass
        {
            Name "SculptedCloudForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CloudVertex
            #pragma fragment CloudFragment
            #pragma multi_compile_instancing

            half4 CloudFragment(CloudVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float paintValue;
                float3 paintGradientOS;
                CloudPaintField(input.positionOS, paintValue, paintGradientOS);
                float3 meshNormalWS = normalize(input.normalWS);
                CloudClipEdge(input.positionWS, meshNormalWS, paintValue);
                float3 normalWS = CloudShapeNormal(input.positionWS, meshNormalWS, paintGradientOS);
                Light mainLight = GetMainLight();
                float3 lightDirection = mainLight.direction;
                // Explicit palette shading remains readable even if the scene has
                // no directional main light. No scene light or global is modified.
                if (dot(lightDirection, lightDirection) < 0.0001)
                    lightDirection = normalize(float3(-0.45, 0.70, -0.55));
                else
                    lightDirection = normalize(lightDirection);
                // A local art-direction offset keeps cloud side planes visible
                // without changing the courtyard's already approved lighting.
                float keyAngle = radians(_LightYawOffset);
                float keySin = sin(keyAngle), keyCos = cos(keyAngle);
                lightDirection = float3(keyCos * lightDirection.x + keySin * lightDirection.z,
                    lightDirection.y, -keySin * lightDirection.x + keyCos * lightDirection.z);
                float facingLight = dot(normalWS, lightDirection) * 0.5 + 0.5;
                float facingUp = normalWS.y * 0.5 + 0.5;
                // The generated cloud mesh stores accessibility in R and normalized
                // object height in G; B=0 identifies that data. Older uncolored
                // meshes render with open cavities and their local-height fallback.
                float hasPaintData = 1.0 - step(0.5, input.paintData.b);
                float accessibility = lerp(1.0, saturate(input.paintData.r), hasPaintData);
                float localHeight = smoothstep(_CrestHeightRange.x,
                    max(_CrestHeightRange.y, _CrestHeightRange.x + 0.001), input.positionOS.y);
                float normalizedHeight = lerp(localHeight, saturate(input.paintData.g), hasPaintData);
                float brushAA = max(fwidth(paintValue) * 0.8, 0.016);
                // The combined noise naturally clusters around 0.5. Closer paint
                // thresholds keep both short stroke sizes present in shaded areas.
                float brushTone = smoothstep(0.44-brushAA, 0.44+brushAA, paintValue)
                    + smoothstep(0.56-brushAA, 0.56+brushAA, paintValue) - 1.0;
                float brushLevel = brushTone * 0.5 + 0.5;
                float volume = lerp(facingLight, facingUp, _UpInfluence);
                volume += (brushTone * 0.35 + (paintValue - 0.5) * 0.35) * _BrushStrength;
                volume += (normalizedHeight - 0.5) * 0.035;
                volume -= (1.0 - accessibility) * _AOIntensity * 0.36;
                volume = saturate((volume - 0.5) * _LightContrast + 0.5);
                float edge = max(_BandSoftness, fwidth(volume) * 0.8);
                float middle = smoothstep(_ShadeStep - edge, _ShadeStep + edge, volume);
                float lit = smoothstep(_LitStep - edge, _LitStep + edge, volume);
                // Paint is visible inside a dark lighting band, not only where a
                // normal happens to cross the light threshold. All three shadow
                // marks stay inside the same blue palette instead of adding dirt.
                half3 shadedPaint = lerp(_ShadeColor.rgb, _MidColor.rgb,
                    brushLevel * _ShadeBrushStrength);
                half3 middlePaint = lerp(_MidColor.rgb, _LitColor.rgb,
                    brushLevel * _BrushStrength * 0.45);
                half3 litPaint = _LitColor.rgb * lerp(0.90, 1.0, brushLevel);
                half3 color = lerp(shadedPaint, middlePaint, middle);
                color = lerp(color, litPaint, lit);
                float coolCrest = smoothstep(_HighlightStep-edge, _HighlightStep+edge, volume);
                color = lerp(color, _LitColor.rgb * lerp(0.98, 1.035, brushLevel), coolCrest);
                color *= 1.0 + (volume * 2.0 - 1.0) * _VolumeVariation * middle * 0.5;
                color *= lerp(1.0, lerp(0.72, 1.0, accessibility), _AOIntensity);

                // A broad, directional side plane replaces a thin specular-looking
                // Fresnel line. Its angular width and a little forward-facing
                // scattered color suggest illuminated cloud thickness; the main
                // blue body and the attached paint field remain view-independent.
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float viewFacing = saturate(dot(meshNormalWS, viewDirection));
                float lightDot = dot(normalWS, lightDirection);
                float edgeCoordinate = pow(1.0 - viewFacing, _WarmRimPower * 0.45);
                float widthThreshold = 1.0 - _WarmRimWidth;
                float wideEdge = smoothstep(widthThreshold - 0.08,
                    widthThreshold + 0.18, edgeCoordinate + brushTone * 0.035);
                float lightFacing = smoothstep(_WarmLightThreshold - 0.16,
                    _WarmLightThreshold + 0.18, lightDot + brushTone * 0.035);
                float sideScatter = smoothstep(0.04, 0.65, lightDot)
                    * (1.0 - smoothstep(0.35, 0.88, viewFacing)) * 0.45;
                float brushBreakup = smoothstep(0.36, 0.62, paintValue);
                float openEdge = smoothstep(0.68, 0.98, accessibility);
                float backlit = smoothstep(-0.4, 0.35, -dot(viewDirection, lightDirection));
                float warmCoverage = max(wideEdge, sideScatter) * lightFacing
                    * lerp(0.70, 1.0, brushBreakup) * lerp(0.60, 1.0, openEdge)
                    * lerp(0.55, 1.0, backlit);
                // Local lobes do not all receive the same warm outline. A smooth
                // whole-cloud envelope restricts most light to the large key-facing
                // side and top side, leaving internal creases predominantly cool.
                float3 halfSize = max(abs(_CloudEnvelope.xyz), float3(0.1, 0.1, 0.1));
                float3 fromCentreOS = input.positionOS - float3(0, _CloudEnvelope.w, 0);
                float3 macroGradientOS = fromCentreOS / (halfSize * halfSize);
                float3 macroNormalWS = TransformObjectToWorldNormal(
                    normalize(macroGradientOS + float3(0, 0.000001, 0)));
                float macroFacing = dot(macroNormalWS, lightDirection);
                float macroGate = smoothstep(-0.05, 0.45, macroFacing);
                warmCoverage *= lerp(0.08, 1.0, macroGate);
                // Retain the shaded body's information through a continuous mix.
                // Early saturation previously turned every lit seam into a flat
                // orange strip; no additional threshold is applied to coverage.
                float warmth = saturate(warmCoverage * _WarmRimStrength);
                float warmVolume = lerp(0.76, 1.0, saturate(facingLight * 0.45 + macroGate * 0.55));
                half3 warmLight = _HighlightColor.rgb * warmVolume * lerp(0.94, 1.0, brushLevel);
                color = lerp(color, warmLight, warmth);
                // This is opaque illustrated geometry, not volumetric scattering.
                // No cloud-depth plane, transparent fog, or additional light is used.
                return half4(color * _Exposure, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CloudShadowVertex
            #pragma fragment CloudShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;

            CloudVaryings CloudShadowVertex(CloudAttributes input)
            {
                CloudVaryings output = CloudVertex(input);
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
            half4 CloudShadowFragment(CloudVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                CloudClipEdgeFromVaryings(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CloudVertex
            #pragma fragment CloudDepthFragment
            #pragma multi_compile_instancing
            half CloudDepthFragment(CloudVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                CloudClipEdgeFromVaryings(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CloudVertex
            #pragma fragment CloudNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 CloudNormalsFragment(CloudVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                CloudClipEdgeFromVaryings(input);
                float3 normalWS = normalize(input.normalWS);
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
