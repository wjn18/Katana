Shader "KatanaDemo/VFX/SamuraiChargeBlade"
{
    Properties
    {
        [HDR] _CyanColor ("Cool cyan edge", Color) = (0.12, 0.76, 1.08, 1)
        [HDR] _CoreColor ("Charged white core", Color) = (1.35, 1.55, 1.60, 1)
        _Opacity ("Surface glow coverage", Range(0, 1)) = 0.55
        _NormalExpansion ("World normal expansion (metres)", Range(0, 0.002)) = 0.0003
        _RootFade ("Blade root fade (metres)", Range(0.005, 0.10)) = 0.025
        [HideInInspector] _BladeBaseWS ("Blade base in world space", Vector) = (0, 0, 0, 1)
        [HideInInspector] _BladeTipWS ("Blade tip in world space", Vector) = (0, 1, 0, 1)
        [HideInInspector] _GlowIntensity ("Charge intensity", Float) = 0
        [HideInInspector] _ChargeProgress ("Charge progress", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "IgnoreProjector" = "True"
            "UniversalMaterialType" = "Unlit"
        }
        Pass
        {
            Name "SamuraiChargeBlade"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
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
                half4 _CyanColor;
                half4 _CoreColor;
                float _Opacity;
                float _NormalExpansion;
                float _RootFade;
                float4 _BladeBaseWS;
                float4 _BladeTipWS;
                float _GlowIntensity;
                float _ChargeProgress;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // 法线变换处理非均匀缩放；扩张距离始终以世界米为单位。
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(positionWS + normalWS * _NormalExpansion);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 bladeVector = _BladeTipWS.xyz - _BladeBaseWS.xyz;
                float bladeLength = max(length(bladeVector), 0.0001);
                float along = dot(input.positionWS - _BladeBaseWS.xyz, bladeVector / bladeLength);
                // 只做轴向遮罩，保留浅弯刃的完整横向轮廓；刀柄/护手在刀根以下被裁掉。
                float rootMask = smoothstep(0.0, max(_RootFade, 0.001), along);
                float tipMask = 1.0 - smoothstep(bladeLength + 0.004, bladeLength + 0.018, along);
                float mask = rootMask * tipMask;
                clip(mask - 0.0001);

                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewWS))), 2.0);
                float charge = saturate(_ChargeProgress);
                float whiteCore = smoothstep(0.30, 1.0, charge) * (1.0 - fresnel * 0.72);
                float3 glowColor = lerp(_CyanColor.rgb, _CoreColor.rgb, whiteCore);
                float coverage = mask * saturate(_Opacity) * (0.72 + fresnel * 0.28);
                // 预乘后加色，不产生黑色轮廓；透明叠层仍受真实场景深度遮挡。
                float3 emission = glowColor * coverage * clamp(_GlowIntensity, 0.0, 8.0);
                return half4(emission, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
