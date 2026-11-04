Shader "OC Design/Execution Blood"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+5" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            Varyings vert(Attributes i){Varyings o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.color=i.color;o.uv=i.uv;return o;}
            half4 frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;
                p.x+=sin(i.uv.y*9.42)*.12;
                float shape=1-smoothstep(.55,1,dot(p,p));
                return half4(i.color.rgb,i.color.a*shape);
            }
            ENDHLSL
        }
    }
}
