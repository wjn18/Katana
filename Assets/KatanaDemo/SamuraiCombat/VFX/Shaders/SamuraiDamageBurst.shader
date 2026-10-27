Shader "KatanaDemo/VFX/SamuraiDamageBurst"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+26" "IgnoreProjector"="True" }
        Pass
        {
            Name "DamageBurst"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
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
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float shape;
                float brightness = 1.0;
                if (input.style.x < 0.5)
                {
                    // Tapered solid blood drop, with a small wet highlight rather than a glowing orb.
                    float width = lerp(0.18, 0.82, saturate(input.uv.x));
                    float d = length(float2(p.x, p.y / width));
                    shape = 1.0 - smoothstep(0.72, 1.0, d);
                    brightness = 0.85 + 0.3 * exp2(-20.0 * dot(p - float2(.2, .1), p - float2(.2, .1)));
                }
                else if (input.style.x < 1.5)
                {
                    // Several uneven lobes form a brief translucent mist, no texture allocation.
                    float angle = atan2(p.y, p.x);
                    float edge = radius + sin(angle * 5.0 + input.style.y * 11.0) * 0.07
                        + sin(angle * 9.0 - input.style.y * 7.0) * 0.035;
                    shape = exp2(-4.0 * radius * radius) * (1.0 - smoothstep(0.5, 1.0, edge));
                }
                else if (input.style.x < 2.5)
                {
                    float width = max(0.015, (1.0 - abs(p.x)) * .7);
                    shape = (1.0 - smoothstep(0.0, width, abs(p.y))) * (1.0 - abs(p.x));
                }
                else
                {
                    float core = exp2(-75.0 * dot(p, p));
                    float rays = exp2(-abs(p.y) * 65.0 - abs(p.x) * 4.0)
                        + exp2(-abs(p.x) * 80.0 - abs(p.y) * 6.0);
                    shape = saturate(core + rays * .6) * (1.0 - smoothstep(.7, 1.0, radius));
                }
                return half4(input.color.rgb * brightness, saturate(input.color.a * shape));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
