Shader "KatanaDemo/VFX/SamuraiFrostMoon"
{
    Properties { _Intensity ("Brightness", Range(0,3)) = 1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+28" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
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
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 style:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float2 style:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.color=input.color; output.uv=input.uv; output.style=input.style;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=input.uv*2-1;
                float shape; float3 color=input.color.rgb;
                if(input.style.x<.5)
                {
                    float r2=dot(p,p);
                    shape=exp2(-7*r2)*.45+exp2(-65*r2)*.95;
                    color=lerp(color,float3(.94,1,1),exp2(-45*r2));
                }
                else if(input.style.x<1.5)
                {
                    float skew=p.x+(input.style.y-.5)*p.y*.8;
                    float edge=max(abs(skew)*.65+abs(p.y)*.85,abs(skew*.60-p.y*.35));
                    edge+=.07*sin(p.y*9+input.style.y*19)*sin(p.x*7);
                    float cutoff=.68+input.style.y*.18;
                    shape=(1-smoothstep(cutoff-.06,cutoff,edge));
                    float rim=smoothstep(cutoff-.20,cutoff-.05,edge);
                    color=lerp(color,float3(1,1,1),rim*.6);
                    shape*=.72+rim*.28;
                }
                else
                {
                    float taper=sin(saturate(input.uv.x)*PI);
                    shape=pow(saturate(1-abs(p.y)),2.4)*taper*taper;
                    shape*=.65+.35*sin(input.uv.x*22+input.style.y);
                }
                return half4(color*max(0,_Intensity),saturate(shape*input.color.a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
