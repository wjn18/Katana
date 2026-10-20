Shader "OC Design/Samurai/Critical Screen Cracks"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI Texture", 2D) = "white" {}
        _CoreColor ("Ice White Core", Color) = (.78, .96, 1, 1)
        _GlowColor ("Blue Violet Glow", Color) = (.24, .44, 1, 1)
        _Progress ("Flash Progress", Range(0, 1)) = 0
        _Strength ("Strength", Range(0, 1.5)) = 1
        _Aspect ("Screen Aspect", Float) = 1.777778
        _Seed ("Pattern Seed", Float) = 4.17
        _EdgeDarkening ("Edge Darkening", Range(0, .15)) = .065
    }
    SubShader
    {
        Tags
        {
            "Queue"="Overlay"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="False"
        }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One OneMinusSrcAlpha
        ColorMask RGB

        Pass
        {
            Name "CriticalScreenCracks"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            float4 _CoreColor, _GlowColor;
            float _Progress, _Strength, _Aspect, _Seed, _EdgeDarkening;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash11(float p)
            {
                p = frac(p * .1031);
                p *= p + 33.33;
                return frac(p * (p + p));
            }

            float nodeOffset(float index, float seed, float amplitude)
            {
                // 固定一帧图案，整次播放不重新随机；只展开与消散，不连续频闪。
                return (hash11(index * 7.173 + seed) * 2 - 1) * amplitude;
            }

            float segmentDistance(float2 p, float2 a, float2 b)
            {
                float2 ab = b - a;
                float t = saturate(dot(p - a, ab) / max(dot(ab, ab), .000001));
                return length(p - a - t * ab);
            }

            float2 crack(float2 uv, float2 start, float2 finish, float seed,
                float amplitude, float width, float reveal)
            {
                float2 metric = float2(_Aspect, 1);
                float2 ab = (finish - start) * metric;
                float lengthAB = length(ab);
                float2 tangent = ab / max(lengthAB, .0001);
                float2 normal = float2(-tangent.y, tangent.x);
                float2 relative = (uv - start) * metric;
                float2 p = float2(dot(relative, tangent), dot(relative, normal));
                if (p.x < -.025 || p.x > lengthAB + .025 || abs(p.y) > amplitude + .035)
                    return 0;

                const float segments = 13;
                float stepLength = lengthAB / segments;
                float node = clamp(floor(p.x / stepLength), 0, segments - 1);
                float d = 10;
                // 邻段距离让折角连续；每条只求所在段及两侧，无整条逐段遍历。
                [unroll] for (int j = -1; j <= 1; j++)
                {
                    float n = clamp(node + j, 0, segments - 1);
                    float2 a = float2(n * stepLength, nodeOffset(n, seed, amplitude));
                    float2 b = float2((n + 1) * stepLength, nodeOffset(n + 1, seed, amplitude));
                    d = min(d, segmentDistance(p, a, b));
                }
                float progressAlong = saturate(p.x / max(lengthAB, .001));
                float growth = 1 - smoothstep(reveal, reveal + .08, progressAlong);
                float endpoint = smoothstep(-.015, .015, p.x)
                    * (1 - smoothstep(lengthAB - .015, lengthAB + .015, p.x));
                float aa = max(fwidth(d), .0002);
                float core = 1 - smoothstep(width, width + aa * 1.2, d);
                float halo = exp2(-d * d / max(width * width * 20, .000001));
                return float2(core, halo * .46) * growth * endpoint;
            }

            half4 frag(v2f i) : SV_Target
            {
                float t = saturate(_Progress);
                float rise = smoothstep(0, .075, t);
                float fade = 1 - smoothstep(.23, 1, t);
                float envelope = rise * fade * max(0, _Strength);
                float reveal = saturate(t / .16) * 1.12;
                float seed = _Seed;
                float2 uv = i.uv;
                float2 lines = 0;

                // 大斜裂贯穿屏幕，中间保留角色可读区域；外围碎裂与淡紫次级裂纹呼应参考。
                lines += crack(uv, float2(-.05,.27), float2(1.05,.85), seed+2, .026, .00115, reveal);
                lines += crack(uv, float2(-.07,.70), float2(1.06,.93), seed+11, .018, .00085, reveal);
                lines += crack(uv, float2(-.06,.12), float2(1.08,.34), seed+23, .033, .00110, reveal);
                lines += crack(uv, float2(.18,-.08), float2(.12,.72), seed+37, .020, .00080, reveal);
                lines += crack(uv, float2(.76,-.05), float2(1.03,.66), seed+51, .025, .00095, reveal);
                lines += crack(uv, float2(.63,1.05), float2(.92,.49), seed+71, .018, .00070, reveal);
                // 短分叉比主裂纹更细、更暗，不形成满屏密集蜘蛛网。
                lines += crack(uv, float2(.20,.39), float2(.03,.57), seed+83, .014, .00065, reveal) * .65;
                lines += crack(uv, float2(.71,.69), float2(.93,.55), seed+97, .013, .00065, reveal) * .65;
                lines += crack(uv, float2(.58,.26), float2(.40,.07), seed+109, .018, .00065, reveal) * .55;

                float2 centered = (uv - .5) * float2(_Aspect, 1);
                float centerReadability = lerp(.25, 1, smoothstep(.11, .40, length(centered)));
                // 上方两角是训练场生命/耐力HUD；即使Overlay晚于IMGUI也避免亮线盖住文字。
                float cornerX = 1 - smoothstep(.23, .38, min(uv.x, 1-uv.x));
                float topProtection = 1 - .94 * cornerX * smoothstep(.67, .83, uv.y);
                float protection = centerReadability * topProtection;
                float core = saturate(lines.x) * envelope * protection;
                float halo = saturate(lines.y) * envelope * protection;
                float kick = exp2(-t * 23) * rise * .055 * _Strength;
                float edge = smoothstep(.32, .82, length(centered));
                float darkAlpha = saturate(_EdgeDarkening) * edge * envelope * topProtection;
                float3 glowColor = lerp(_GlowColor.rgb, float3(.45,.35,1), saturate(.65-uv.y) * .5);
                float3 light = _CoreColor.rgb * core * 1.12 + glowColor * halo
                    + float3(.20,.42,.8) * kick;
                // 预乘输出：裂纹加亮，只有轻量边缘压暗；不存在全屏黑底或不透明面片。
                float alpha = saturate(core * .34 + halo * .10 + darkAlpha);
                return half4(light * i.color.rgb, alpha * i.color.a);
            }
            ENDHLSL
        }
    }
}
