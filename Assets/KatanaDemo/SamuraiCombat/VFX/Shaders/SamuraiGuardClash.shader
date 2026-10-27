Shader "KatanaDemo/VFX/SamuraiGuardClash"
{
    Properties
    {
        _Intensity ("Metal and recovery brightness", Range(0, 4)) = 1.6
        [HideInInspector] _FlowTime ("Actor flow clock", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+35" "IgnoreProjector"="True" }
        Pass
        {
            Name "GuardClash"
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
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                    lerp(Hash(i + float2(0, 1)), Hash(i + 1), f.x), f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float across = abs(p.y);
                float shape;
                float3 tint = input.color.rgb;
                if (input.style.x > 3.5)
                {
                    // Local metal glare: thin hot center and a short warm skirt.
                    shape = pow(saturate(1.0 - abs(p.x)), 1.5)
                        * (exp2(-across * 11.0) * 1.1 + exp2(-across * 3.0) * .16);
                }
                else if (input.style.x > 2.5)
                {
                    float r2 = dot(p, p);
                    shape = (exp2(-r2 * 10.0) * 1.5 + exp2(-r2 * 3.0) * .24)
                        * (1.0 - smoothstep(.65, 1.0, sqrt(r2)));
                }
                else if (input.style.x > 1.5)
                {
                    float r2 = dot(p, p);
                    shape = (exp2(-r2 * 4.2) * .8 + exp2(-r2 * 1.8) * .18)
                        * (1.0 - smoothstep(.65, 1.0, sqrt(r2)));
                }
                else if (input.style.x > .5)
                {
                    float ends = pow(saturate(1.0 - abs(p.x)), .45);
                    float hot = pow(saturate(1.0 - across), 6.0);
                    shape = ends * (hot * 1.15 + pow(saturate(1.0 - across), 1.6) * .24);
                    tint = lerp(tint, float3(1.0, .95, .7), hot * .36);
                }
                else
                {
                    // Uneven fibers inside each broad curved ribbon suggest liquid fire.
                    float u = saturate(input.uv.x);
                    float seed = input.style.y;
                    float flow = u * 7.0 - _FlowTime * 3.2;
                    float broad = Noise(float2(flow, seed));
                    float grain = Noise(float2(u * 23.0 - _FlowTime * 7.0, input.uv.y * 5.0 + seed));
                    float bend = sin(u * 11.0 + broad * 4.0 + _FlowTime * 5.0 + seed) * .24;
                    float center = abs(p.y + bend);
                    float body = pow(saturate(1.0 - center), 1.2);
                    float hot = pow(saturate(1.0 - center), 7.0);
                    float filaments = pow(saturate(.5 + .5 * sin(p.y * 18.0 + broad * 7.0 + u * 8.0)), 4.0);
                    float taper = smoothstep(0.0, .12, u) * (1.0 - smoothstep(.93, 1.0, u));
                    shape = taper * (body * (.44 + filaments * .63) + hot * .52)
                        * (.64 + broad * .36) * (.72 + grain * .28);
                    tint = lerp(tint, float3(1.0, .78, .23), hot * .16);
                }
                return half4(tint * max(0.0, input.color.a) * shape * _Intensity, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
