Shader "OC Design/Execution Cue"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (1,1,1,1)
        _Shape ("Body pulse (zero for lines)", Float) = 0
        _CueTime ("Cue animation time", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _DestinationBlend ("Destination blend", Float) = 10
        [Enum(UnityEngine.Rendering.CompareFunction)] _DepthTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+12" }
        Pass
        {
            Blend SrcAlpha [_DestinationBlend]
            ZWrite Off
            ZTest [_DepthTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Shape;
                float _CueTime;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
                o.color=i.color*_Tint; o.uv=i.uv; return o;
            }
            float hash21(float2 p)
            {
                p=frac(p*float2(123.34,456.21));
                p+=dot(p,p+45.32);
                return frac(p.x*p.y);
            }
            float noise21(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash21(cell),hash21(cell+float2(1,0)),f.x),
                    lerp(hash21(cell+float2(0,1)),hash21(cell+1),f.x),f.y);
            }
            half4 frag(Varyings i):SV_Target
            {
                if (_Shape < .5) return i.color;
                float2 p=i.uv*2-1;
                float t=_CueTime;
                float grain=noise21(p*8+float2(t*.27,-t*.8));
                float fine=noise21(p*19+float2(-t*.6,t*.45));
                float2 warped=p+float2(grain-.5,fine-.5)*.055;
                float radius=length(warped);
                float angle=atan2(warped.y,warped.x);
                // An incandescent, uneven centre; no circular outline or symbol.
                float core=exp(-radius*radius*180)*(1.05+.22*grain);
                float ember=exp(-radius*radius*43)*(.22+.45*grain);
                float halo=exp(-radius*radius*7)*(.075+.055*grain);
                // Fine broken rays breathe independently, instead of scaling a UI icon.
                float threads=pow(saturate(.5+.5*sin(angle*23+sin(angle*7+t*.7)*1.2)),20);
                threads*=exp(-radius*5.5)*smoothstep(.05,.16,radius);
                threads*=.2+.8*noise21(float2(cos(angle),sin(angle))*4+radius*7-t*.7);
                float motes=0;
                [unroll] for(int k=0;k<14;k++)
                {
                    float seed=hash21(float2(k,3.17));
                    float age=frac(t*(.22+seed*.18)+seed);
                    float a=k*2.39996+sin(t*.4+k)*.09;
                    float2 centre=float2(cos(a),sin(a))*(.16+age*.64);
                    centre.y+=age*age*.10;
                    float2 delta=p-centre;
                    float along=dot(delta,float2(cos(a),sin(a)));
                    float across=dot(delta,float2(-sin(a),cos(a)));
                    float fleck=exp(-(across*across*19000+along*along*2900));
                    motes+=fleck*sin(age*3.141593)*(.3+seed*.6);
                }
                float light=core+ember+halo+threads*.8+motes*.75;
                light*=1-smoothstep(.82,.98,length(p));
                half3 warmth=lerp(half3(1,.53,.12),half3(1,.95,.72),saturate(core*1.6+ember));
                return half4(i.color.rgb*warmth*1.45,i.color.a*saturate(light));
            }
            ENDHLSL
        }
    }
}
