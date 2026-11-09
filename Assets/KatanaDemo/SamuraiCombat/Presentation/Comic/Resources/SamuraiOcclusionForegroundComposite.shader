Shader "Hidden/OC Design/Samurai Occlusion Foreground Composite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X(_SamuraiForegroundColor);

            float4 Vert(uint vertexID : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(vertexID);
            }

            half4 Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return LOAD_TEXTURE2D_X(_SamuraiForegroundColor, uint2(positionCS.xy));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
