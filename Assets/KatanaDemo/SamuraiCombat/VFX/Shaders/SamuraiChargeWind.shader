Shader "KatanaDemo/VFX/SamuraiChargeWind"
{
    Properties
    {
        _Intensity ("Wind and completion brightness", Range(0, 4)) = 1.4
        [HideInInspector] _FlowTime ("Charge flow time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+25" "IgnoreProjector"="True" }
        Pass
        {
            Name "ChargeWind"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _FlowTime;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 style : TEXCOORD1;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 style : TEXCOORD1;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.style = input.style;
                return output;
            }
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                    lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float u = saturate(input.uv.x);
                float across = abs(input.uv.y * 2.0 - 1.0);
                float shape;
                if (input.style.x > 1.5)
                {
                    // One local completion flash: soft halo, small white core and unequal rays.
                    float2 p = input.uv * 2.0 - 1.0;
                    float r2 = dot(p, p);
                    float halo = exp2(-7.0 * r2) * 0.28;
                    float core = exp2(-95.0 * r2) * 2.0;
                    float rayA = exp2(-abs(p.y) * 100.0 - abs(p.x) * 3.0) * 0.85;
                    float rayB = exp2(-abs(p.x) * 90.0 - abs(p.y) * 5.0) * 0.55;
                    float diagonal = exp2(-abs(p.x + p.y) * 85.0 - length(p) * 5.0) * 0.24;
                    shape = (halo + core + rayA + rayB + diagonal) * (1.0 - smoothstep(0.70, 1.0, sqrt(r2)));
                }
                else
                {
                    float taper = smoothstep(0.0, 0.09, u) * (1.0 - smoothstep(0.78, 1.0, u));
                    float body = pow(saturate(1.0 - across), 1.8);
                    float core = pow(saturate(1.0 - across), 12.0);
                    if (input.style.x > 0.5)
                        shape = taper * (body * 0.35 + core * 0.85);
                    else
                    {
                        // Advect broken wisps along each ribbon; no textures or global time dependency.
                        float seed = input.style.y * 17.0;
                        float slow = Noise(float2(u * 7.0 - _FlowTime * 2.2, seed));
                        float grain = Noise(float2(u * 36.0 - _FlowTime * 6.0, input.uv.y * 3.0 + seed));
                        float fibers = pow(saturate(0.5 + 0.5 * sin(input.uv.y * 27.0 + slow * 9.0 + u * 4.0)), 5.0);
                        shape = taper * (body * (0.65 + fibers * 0.65) + core * 0.45)
                            * (0.55 + slow * 0.45) * (0.75 + grain * 0.25);
                    }
                }
                return half4(input.color.rgb * max(0.0, input.color.a) * shape * _Intensity, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
